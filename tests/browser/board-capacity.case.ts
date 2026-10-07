import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { performance } from 'node:perf_hooks';
import { expect, test } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// The existing restricted PostgreSQL rank fixture supplies actual persisted
// 200-List/5000-active-Card data. No Board responses or live events are mocked.
for (const width of [1280, 390]) {
  test(`PRD-04/06 large Board windowing, keyboard and detail focus at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000); expect(process.env.CI).toBe('true');
    const fixturePath = process.env.STRATAAI_BOARD_CAPACITY_FIXTURE; expect(fixturePath).toBeTruthy();
    const fixture = JSON.parse(readFileSync(fixturePath!, 'utf8')) as { email: string; password: string; organizationId: string; boardId: string; listId: string };
    for (const id of [fixture.organizationId, fixture.boardId, fixture.listId]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
    await page.setViewportSize({ width, height: 844 });
    expect((await context.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' }, data: { email: fixture.email, password: fixture.password } })).status()).toBe(200);
    const result = await context.request.get(`/boards/${fixture.boardId}`); expect(result.status()).toBe(200);
    const snapshot = await result.json() as { lists: { list: { id: string; name: string; version: number }; cards: { id: string; version: number }[] }[] };
    expect(snapshot.lists).toHaveLength(200);
    const index = snapshot.lists.findIndex(column => column.list.id === fixture.listId);
    expect(index).toBeGreaterThanOrEqual(0); const column = snapshot.lists[index];
    expect(column.cards.length).toBeGreaterThanOrEqual(5000);
    await waitForBoardDelivery(context.request, fixture.boardId);
    await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
    const path = `/app/${fixture.organizationId}/boards/${fixture.boardId}`;
    const reads = trackBoardReads(page, fixture.boardId, path); const started = performance.now();
    await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    await expect(page.getByRole('button', { name: /^Drag .* list$/ }).first()).toBeEnabled();
    const usableMs = performance.now() - started;
    const canvas = page.getByLabel('Kanban board', { exact: true });
    expect(await canvas.locator('[data-board-window-axis="lists"]').count()).toBeLessThan(15);
    await canvas.evaluate((node, index) => {
      const row = node.querySelector('[data-board-window-axis="lists"]')!;
      node.scrollLeft = index * (row.getBoundingClientRect().width + 16);
    }, index);
    const list = canvas.locator(`[data-board-window-id="${fixture.listId}"]`);
    await expect(list).toBeVisible();
    const cards = list.getByLabel('Cards', { exact: true });
    expect(await cards.locator('[data-board-window-axis="cards"]').count()).toBeLessThan(40);
    await cards.evaluate(node => { node.scrollTop = node.scrollHeight / 2; });
    const mounted = cards.locator('a[href*="/cards/"]');
    // Native scrolling delivers its event after the evaluate call. Require a
    // canonical middle identity before capturing the keyboard starting point.
    await expect.poll(async () => {
      const hrefs = await mounted.evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
      return hrefs.some(href => column.cards.findIndex(card => href?.endsWith('/' + card.id)) >= column.cards.length / 4);
    }).toBe(true);
    const href = await mounted.last().getAttribute('href');
    const cardIndex = column.cards.findIndex(card => href?.endsWith('/' + card.id)); expect(cardIndex).toBeGreaterThan(0);
    expect(cardIndex + 1).toBeLessThan(column.cards.length);
    // Measurements can change the mounted range. Activate the captured
    // identity, rather than a positional locator that could select another row.
    const middle = cards.locator(`a[href$="/cards/${column.cards[cardIndex].id}"]`);
    await middle.press('Tab');
    const next = cards.locator(`a[href$="/cards/${column.cards[cardIndex + 1].id}"]`);
    await expect(next.locator('..').getByRole('button', { name: /^Drag .* card$/ })).toBeFocused();
    await page.keyboard.press('Shift+Tab'); await expect(cards.locator(`a[href$="/cards/${column.cards[cardIndex].id}"]`)).toBeFocused();
    await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    const retainedScroll = await cards.evaluate(node => node.scrollTop);
    const section = list.getByRole('region', { name: column.list.name, exact: true });
    const retainedSection = await section.evaluate(node => node.scrollTop);
    const awayIndex = index < 100 ? 199 : 0;
    const scrollToColumn = async (target: number) => canvas.evaluate((node, target) => {
      const row = node.querySelector('[data-board-window-axis="lists"]')!;
      node.scrollLeft = target * (row.getBoundingClientRect().width + 16);
    }, target);
    await scrollToColumn(awayIndex);
    const awayList = canvas.locator(`[data-board-window-id="${snapshot.lists[awayIndex].list.id}"]`);
    await expect(awayList).toBeVisible(); await awayList.getByRole('button', { name: /^Drag .* list$/ }).focus();
    // Focus the destination to release the old List's focus retention, proving
    // an actual unmount rather than a still-mounted offscreen source.
    await expect(list).toHaveCount(0);
    await scrollToColumn(index); await expect(list).toBeVisible();
    await expect.poll(() => cards.evaluate(node => node.scrollTop)).toBe(retainedScroll);
    await expect.poll(() => section.evaluate(node => node.scrollTop)).toBe(retainedSection);
    await expect(middle).toBeVisible();
    const moving = column.cards[cardIndex];
    const handle = middle.locator('..').getByRole('button', { name: /^Drag .* card$/ });
    await expect(handle).toBeEnabled(); await handle.press('Space'); await expect(handle).toHaveAttribute('aria-pressed', 'true');
    const settleDrag = () => page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
    await settleDrag();
    const initialMounted = await mounted.evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
    const lastInitialIndex = Math.max(...initialMounted.map(href => column.cards.findIndex(card => href?.endsWith('/' + card.id))));
    // Twelve adjacent keyboard targets exceed the initial mounted buffer.
    // Real keyboard scrolling must mount later targets while retaining source.
    for (let step = 0; step < 12; step++) {
      await page.keyboard.press('ArrowDown');
      const target = cards.locator(`a[href$="/cards/${column.cards[cardIndex + step + 1].id}"]`).locator('..');
      // The adopted KeyboardSensor scrolls smoothly. Require its real dragged
      // rectangle to reach each adjacent canonical target before the next key.
      await expect.poll(async () => {
        const [sourceBox, targetBox] = await Promise.all([handle.locator('..').boundingBox(), target.boundingBox()]);
        return !!sourceBox && !!targetBox
          && Math.abs(sourceBox.y + sourceBox.height / 2 - targetBox.y - targetBox.height / 2) < 2;
      }).toBe(true);
    }
    await expect(handle).toHaveAttribute('aria-pressed', 'true');
    await expect.poll(async () => {
      const hrefs = await mounted.evaluateAll(nodes => nodes.map(node => node.getAttribute('href')));
      return hrefs.some(href => column.cards.findIndex(card => href?.endsWith('/' + card.id)) > lastInitialIndex);
    }).toBe(true);
    const moveReply = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/cards/${moving.id}/move`);
    await page.keyboard.press('Space'); expect((await moveReply).status()).toBe(200);
    await expect(page.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await waitForBoardDelivery(context.request, fixture.boardId);
    const movedResponse = await context.request.get(`/boards/${fixture.boardId}`); expect(movedResponse.status()).toBe(200);
    const movedSnapshot = await movedResponse.json() as typeof snapshot;
    const movedColumn = movedSnapshot.lists.find(value => value.list.id === fixture.listId)!;
    expect(movedColumn.cards[cardIndex + 11]).toMatchObject({ id: moving.id, version: moving.version + 1 });
    expect(movedColumn.cards.filter(card => card.id !== moving.id)).toEqual(column.cards.filter(card => card.id !== moving.id));
    expect(movedSnapshot.lists.filter(value => value.list.id !== fixture.listId)).toEqual(snapshot.lists.filter(value => value.list.id !== fixture.listId));
    await expect(middle).toBeFocused(); await expect(middle).toBeInViewport();
    let pointerWrites = 0;
    page.on('request', request => { if (request.method() === 'POST'
      && new URL(request.url()).pathname === `/cards/${moving.id}/move`) pointerWrites++; });
    const currentOrder = new Map(movedColumn.cards.map((card, index) => [card.id, index]));
    async function pointerAcrossBuffer(cancel: boolean): Promise<string | null> {
      await expect(handle).toBeEnabled(); await handle.focus();
      // Reveal the actual nested Card scroll surface before using its edge.
      // A focused source alone can leave the viewport bottom below the window,
      // where a pointer at the browser edge cannot reach the container edge.
      await cards.evaluate(node => node.scrollIntoView({ block: 'end', inline: 'nearest', behavior: 'instant' }));
      await settleDrag();
      // Worker-delivered reconciliation can disable the handle after it was
      // first focused. Bind pointer geometry only after current admission.
      const initial = await mounted.evaluateAll(nodes => nodes.map(node => node.getAttribute('href')?.split('/').at(-1)));
      const minimum = Math.max(...initial.map(id => currentOrder.get(id ?? '') ?? -1));
      const offset = await cards.evaluate(node => node.scrollTop);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await expect(handle).toBeEnabled();
      const [start, viewport, outer] = await Promise.all([handle.boundingBox(), cards.boundingBox(), section.boundingBox()]);
      expect(start).not.toBeNull(); expect(viewport).not.toBeNull(); expect(outer).not.toBeNull();
      const x = start!.x + start!.width / 2;
      const top = Math.max(0, viewport!.y, outer!.y);
      const bottom = Math.min(844, viewport!.y + viewport!.height, outer!.y + outer!.height);
      expect(bottom - top).toBeGreaterThan(100);
      await page.mouse.move(x, start!.y + start!.height / 2);
      await expect(handle).toBeEnabled();
      await page.mouse.down();
      await page.mouse.move(x + 12, start!.y + start!.height / 2);
      await page.mouse.move(x, bottom - 12, { steps: 12 });
      await expect(handle).toHaveAttribute('aria-pressed', 'true');
      const laterVisible = async () => {
        const candidates = await mounted.evaluateAll((nodes, sourceId) => {
          const root = nodes[0]?.closest('[aria-label="Cards"]'); const section = root?.closest('section[aria-labelledby]');
          if (!root || !section) return [];
          const inner = root.getBoundingClientRect(), outer = section.getBoundingClientRect();
          const top = Math.max(0, inner.top, outer.top), bottom = Math.min(window.innerHeight, inner.bottom, outer.bottom);
          return nodes.flatMap(node => {
            const id = node.getAttribute('href')?.split('/').at(-1), rect = node.parentElement!.getBoundingClientRect();
            return id && id !== sourceId && rect.top >= top && rect.bottom <= bottom
              ? [{ id, distance: Math.abs(rect.top + rect.height / 2 - (top + bottom) / 2) }] : [];
          }).sort((a, b) => a.distance - b.distance);
        }, moving.id);
        return candidates.find(value => (currentOrder.get(value.id) ?? -1) > minimum)?.id ?? null;
      };
      await expect.poll(() => cards.evaluate(node => node.scrollTop)).toBeGreaterThan(offset);
      await expect.poll(laterVisible).not.toBeNull();
      await expect(middle).toBeAttached();
      expect(await cards.locator('[data-board-window-axis="cards"]').count()).toBeLessThan(40);
      if (cancel) {
        await page.keyboard.press('Escape'); await page.mouse.up();
        await expect(handle).not.toHaveAttribute('aria-pressed', 'true'); return null;
      }
      // Move away from the edge to stop auto-scroll before binding a real,
      // fully visible destination. No application responses are intercepted.
      const currentViewport = await cards.boundingBox(), currentOuter = await section.boundingBox();
      expect(currentViewport).not.toBeNull(); expect(currentOuter).not.toBeNull();
      const centerTop = Math.max(0, currentViewport!.y, currentOuter!.y);
      const centerBottom = Math.min(844, currentViewport!.y + currentViewport!.height, currentOuter!.y + currentOuter!.height);
      await page.mouse.move(x, (centerTop + centerBottom) / 2); await settleDrag();
      const id = await laterVisible(); expect(id).not.toBeNull();
      const target = cards.locator(`a[href$="/cards/${id}"]`).locator('..'); const box = await target.boundingBox(); expect(box).not.toBeNull();
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
      const reply = page.waitForResponse(response => response.request().method() === 'POST'
        && new URL(response.url()).pathname === `/cards/${moving.id}/move`);
      await page.mouse.up(); expect((await reply).status()).toBe(200); return id;
    }
    await pointerAcrossBuffer(true); expect(pointerWrites).toBe(0);
    const cancelled = await context.request.get(`/boards/${fixture.boardId}`); expect(cancelled.status()).toBe(200);
    expect((await cancelled.json()).lists).toEqual(movedSnapshot.lists);
    const pointerTarget = await pointerAcrossBuffer(false);
    expect(pointerWrites).toBe(1);
    await expect(page.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await waitForBoardDelivery(context.request, fixture.boardId);
    const pointerResult = await context.request.get(`/boards/${fixture.boardId}`); expect(pointerResult.status()).toBe(200);
    const pointerSnapshot = await pointerResult.json() as typeof snapshot;
    const pointerColumn = pointerSnapshot.lists.find(value => value.list.id === fixture.listId)!;
    const targetIndex = pointerColumn.cards.findIndex(card => card.id === pointerTarget);
    expect(targetIndex).toBeGreaterThan(0);
    expect(pointerColumn.cards[targetIndex - 1]).toMatchObject({ id: moving.id, version: moving.version + 2 });
    expect(pointerColumn.cards.filter(card => card.id !== moving.id)).toEqual(movedColumn.cards.filter(card => card.id !== moving.id));
    expect(pointerSnapshot.lists.filter(value => value.list.id !== fixture.listId)).toEqual(movedSnapshot.lists.filter(value => value.list.id !== fixture.listId));
    await expect(middle).toBeFocused(); await expect(middle).toBeInViewport();
    // Cross-List movement must still work when horizontal auto-scroll mounts
    // an empty destination outside the initial List buffer. Source retention
    // must not confine a real pointer drag to the originally mounted columns.
    await expect(handle).toBeEnabled(); await handle.focus(); await settleDrag();
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    await expect(handle).toBeEnabled();
    const initialColumns = await canvas.locator('[data-board-window-axis="lists"]').evaluateAll(nodes => nodes.map(node => (node as HTMLElement).dataset.boardWindowId));
    const initialColumnIndices = initialColumns.map(id => pointerSnapshot.lists.findIndex(value => value.list.id === id));
    const initialColumnFirst = Math.min(...initialColumnIndices), initialColumnLast = Math.max(...initialColumnIndices);
    // Earlier real rank commands can place the populated List near either end.
    // Travel toward the half with available canonical destination columns.
    const goRight = index < 100;
    const emptyColumns = new Set(pointerSnapshot.lists.filter(value => value.cards.length === 0).map(value => value.list.id));
    const horizontalOffset = await canvas.evaluate(node => node.scrollLeft);
    // Window inventory and scroll reads above can overlap a live refresh.
    // Bind the native input after those reads, with freshly admitted geometry.
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    await expect(handle).toBeEnabled();
    await handle.hover();
    await expect.poll(() => handle.evaluate(node => {
      const rect = node.getBoundingClientRect();
      const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
      return !!hit && node.contains(hit);
    })).toBe(true);
    const [sourceBox, canvasBox] = await Promise.all([handle.boundingBox(), canvas.boundingBox()]);
    expect(sourceBox).not.toBeNull(); expect(canvasBox).not.toBeNull();
    const left = Math.max(0, canvasBox!.x), right = Math.min(width, canvasBox!.x + canvasBox!.width);
    const dragY = sourceBox!.y + sourceBox!.height / 2;
    await page.mouse.move(sourceBox!.x + sourceBox!.width / 2, dragY);
    await expect(handle).toBeEnabled();
    await page.mouse.down();
    await page.mouse.move(sourceBox!.x + sourceBox!.width / 2 + 12, dragY);
    await expect(handle).toHaveAttribute('aria-pressed', 'true');
    await page.mouse.move(goRight ? right - 12 : left + 12, dragY, { steps: 12 });
    await expect(handle).toHaveAttribute('aria-pressed', 'true');
    const laterEmptyIds = pointerSnapshot.lists.flatMap((value, ordinal) =>
      emptyColumns.has(value.list.id) && (goRight ? ordinal > initialColumnLast : ordinal < initialColumnFirst) ? [value.list.id] : []);
    const laterEmptyColumn = () => canvas.evaluate((canvas, ids) => new Promise<string | null>(resolve => {
      const allowed = new Set(ids); let frame = 0;
      const finish = (id: string | null) => { clearTimeout(timeout); cancelAnimationFrame(frame); resolve(id); };
      const timeout = setTimeout(() => finish(null), 5_000);
      const observe = () => {
        const viewport = canvas.getBoundingClientRect();
        const left = Math.max(0, viewport.left), right = Math.min(window.innerWidth, viewport.right), quarter = (right - left) / 4;
        for (const node of Array.from(canvas.querySelectorAll<HTMLElement>('[data-board-window-axis="lists"]'))) {
          const id = node.dataset.boardWindowId;
          if (!id || !allowed.has(id)) continue;
          const target = node.querySelector<HTMLElement>('[data-card-list-end]');
          if (!target || target.dataset.cardListEnd !== id) continue;
          const rect = target.getBoundingClientRect(), center = rect.left + rect.width / 2;
          // Keep the same middle-half/drop-surface requirement, observing every
          // animation frame instead of missing moving phone targets between
          // Playwright's progressively spaced predicate samples.
          if (rect.width > 0 && rect.height > 0 && center > left + quarter && center < right - quarter
            && Math.min(844, viewport.bottom, rect.bottom) > Math.max(0, viewport.top, rect.top)) { finish(id); return; }
        }
        frame = requestAnimationFrame(observe);
      };
      observe();
    }), laterEmptyIds);
    if (goRight) await expect.poll(() => canvas.evaluate(node => node.scrollLeft)).toBeGreaterThan(horizontalOffset);
    else await expect.poll(() => canvas.evaluate(node => node.scrollLeft)).toBeLessThan(horizontalOffset);
    // Retain the observed later drop target while edge scrolling is active. Moving
    // back to the middle stops scrolling, but the final animation frame can
    // leave that column partially visible on a one-column phone viewport.
    const destinationId = await laterEmptyColumn();
    expect(destinationId).not.toBeNull();
    await expect(middle).toBeAttached();
    expect(await canvas.locator('[data-board-window-axis="lists"]').count()).toBeLessThan(15);
    await page.mouse.move((left + right) / 2, dragY); await settleDrag();
    expect(destinationId).not.toBeNull();
    const destination = pointerSnapshot.lists.find(value => value.list.id === destinationId)!;
    const destinationRow = canvas.locator(`[data-board-window-id="${destinationId}"]`);
    const destinationDrop = destinationRow.getByText(`Drop card at end of ${destination.list.name}`, { exact: true });
    await expect(destinationDrop).toBeInViewport();
    const destinationBox = await destinationDrop.boundingBox();
    expect(destinationBox).not.toBeNull();
    const destinationX = destinationBox!.x + destinationBox!.width / 2;
    expect(destinationX).toBeGreaterThan(left); expect(destinationX).toBeLessThan(right);
    const destinationY = (Math.max(0, destinationBox!.y) + Math.min(844, destinationBox!.y + destinationBox!.height)) / 2;
    await page.mouse.move(destinationX, destinationY);
    const transferReply = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/cards/${moving.id}/move`);
    await page.mouse.up(); expect((await transferReply).status()).toBe(200);
    expect(pointerWrites).toBe(2);
    await expect(page.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await waitForBoardDelivery(context.request, fixture.boardId);
    const transferResult = await context.request.get(`/boards/${fixture.boardId}`); expect(transferResult.status()).toBe(200);
    const settledSnapshot = await transferResult.json() as typeof snapshot;
    const transferredSource = settledSnapshot.lists.find(value => value.list.id === fixture.listId)!;
    const transferredDestination = settledSnapshot.lists.find(value => value.list.id === destinationId)!;
    expect(transferredSource.list).toEqual(pointerColumn.list);
    expect(transferredSource.cards).toEqual(pointerColumn.cards.filter(card => card.id !== moving.id));
    expect(transferredDestination.list).toEqual(destination.list);
    expect(transferredDestination.cards).toHaveLength(1);
    expect(transferredDestination.cards[0]).toMatchObject({ id: moving.id, version: moving.version + 3,
      listId: destinationId, boardId: fixture.boardId, organizationId: fixture.organizationId });
    expect(settledSnapshot.lists.filter(value => ![fixture.listId, destinationId].includes(value.list.id)))
      .toEqual(pointerSnapshot.lists.filter(value => ![fixture.listId, destinationId].includes(value.list.id)));
    const transferredLink = destinationRow.locator(`a[href$="/cards/${moving.id}"]`);
    await expect(transferredLink).toBeFocused(); await expect(transferredLink).toBeInViewport();
    await scrollToColumn(index); await expect(list).toBeVisible();
    await cards.evaluate(node => { node.scrollTop = node.scrollHeight; });
    const last = cards.locator(`a[href$="/cards/${column.cards.at(-1)!.id}"]`);
    await expect(last).toBeVisible(); await last.press('Enter');
    const close = page.getByRole('button', { name: 'Close', exact: true }); await expect(close).toBeEnabled(); await close.press('Enter');
    await expect(last).toBeFocused(); await expect(last).toBeInViewport();
    const listSourceIndex = settledSnapshot.lists.findIndex((value, index) => index + 8 < 200 && value.cards.length === 0);
    expect(listSourceIndex).toBeGreaterThanOrEqual(0);
    const sourceList = settledSnapshot.lists[listSourceIndex];
    await scrollToColumn(listSourceIndex);
    const listRow = canvas.locator(`[data-board-window-id="${sourceList.list.id}"]`);
    const listHandle = listRow.getByRole('button', { name: /^Drag .* list$/ });
    await expect(listHandle).toBeEnabled(); await listHandle.press('Space');
    await expect(listHandle).toHaveAttribute('aria-pressed', 'true'); await settleDrag();
    const initialListIds = await canvas.locator('[data-board-window-axis="lists"]').evaluateAll(nodes => nodes.map(node => (node as HTMLElement).dataset.boardWindowId));
    // Focus/detail retention can pin a distant column outside the viewport.
    // Bind the contiguous initial buffer around the source, rather than letting
    // that unrelated retained column become the window's far boundary.
    expect(initialListIds).toContain(sourceList.list.id);
    let initialListLast = listSourceIndex;
    while (initialListLast + 1 < settledSnapshot.lists.length
      && initialListIds.includes(settledSnapshot.lists[initialListLast + 1].list.id)) initialListLast++;
    expect(initialListIds).not.toContain(settledSnapshot.lists[listSourceIndex + 8].list.id);
    for (let step = 0; step < 8; step++) {
      // A current Board refresh withdraws all drop targets until admission
      // settles. Send the key against enabled, committed geometry.
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await expect(listHandle).toBeEnabled();
      await page.keyboard.press('ArrowRight');
      const neighbor = settledSnapshot.lists[listSourceIndex + step + 1].list;
      const target = canvas.locator(`[data-board-window-id="${neighbor.id}"]`).getByRole('region', { name: neighbor.name, exact: true });
      await expect.poll(async () => {
        const [sourceBox, targetBox] = await Promise.all([
          listRow.getByRole('region', { name: sourceList.list.name, exact: true }).boundingBox(), target.boundingBox(),
        ]);
        return !!sourceBox && !!targetBox
          && Math.abs(sourceBox.x + sourceBox.width / 2 - targetBox.x - targetBox.width / 2) < 2
          && Math.abs(sourceBox.y + sourceBox.height / 2 - targetBox.y - targetBox.height / 2) < 2;
      }).toBe(true);
    }
    const mountedListIds = await canvas.locator('[data-board-window-axis="lists"]').evaluateAll(nodes => nodes.map(node => (node as HTMLElement).dataset.boardWindowId));
    expect(mountedListIds.some(id => !initialListIds.includes(id)
      && settledSnapshot.lists.findIndex(value => value.list.id === id) > initialListLast)).toBe(true);
    await expect(listHandle).toHaveAttribute('aria-pressed', 'true');
    let listWrites = 0;
    page.on('request', request => { if (request.method() === 'PATCH' && new URL(request.url()).pathname === `/lists/${sourceList.list.id}`) listWrites++; });
    const listReply = page.waitForResponse(response => response.request().method() === 'PATCH'
      && new URL(response.url()).pathname === `/lists/${sourceList.list.id}`);
    await page.keyboard.press('Space'); expect((await listReply).status()).toBe(200); expect(listWrites).toBe(1);
    await expect(listRow.getByText('List move acknowledged. Current ordering is being checked.', { exact: true })).toBeVisible();
    await waitForBoardDelivery(context.request, fixture.boardId);
    const listResult = await context.request.get(`/boards/${fixture.boardId}`); expect(listResult.status()).toBe(200);
    const listSnapshot = await listResult.json() as typeof snapshot;
    expect(listSnapshot.lists[listSourceIndex + 7].list).toMatchObject({ id: sourceList.list.id, version: sourceList.list.version + 1 });
    expect(listSnapshot.lists[listSourceIndex + 7].cards).toEqual(sourceList.cards);
    expect(listSnapshot.lists.filter(value => value.list.id !== sourceList.list.id)).toEqual(settledSnapshot.lists.filter(value => value.list.id !== sourceList.list.id));
    const listFocus = listRow.getByRole('button', { name: `Move ${sourceList.list.name} list`, exact: true });
    await expect(listFocus).toBeFocused(); await expect(listFocus).toBeInViewport();
    expect(await canvas.locator('[data-board-window-axis="lists"]').count()).toBeLessThan(15);
    expect(await cards.locator('[data-board-window-axis="cards"]').count()).toBeLessThan(40);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    await test.info().attach('board-capacity.json', { contentType: 'application/json', body: JSON.stringify({ fixture: 'restricted-postgres', width, lists: 200, activeCards: column.cards.length, usableMs }) });
    // Capacity timings are retained observations, not a replacement for the
    // separate unchanged normal-condition <1500/<100/<500/<200 budgets.
    expect(Number.isFinite(usableMs) && usableMs > 0).toBe(true);
  });
}
