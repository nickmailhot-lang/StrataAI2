import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-06/08: keyboard ${viewport.width === 390 ? 'relative' : 'append'} move recovers a lost acknowledgment and updates another client at ${viewport.width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `card-move-${viewport.width}-${Date.now()}@example.test`, password: 'card-move-correct-horse', displayName: 'Card mover' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Card move recovery' } });
    expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
    const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Move Board', visibility: 'PRIVATE' } });
    expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
    const lists: string[] = [];
    for (const name of ['Planning', 'Complete']) {
      const result = await context.request.post(`/boards/${board}/lists`, { headers, data: { name } });
      expect(result.status()).toBe(201); lists.push((await result.json()).id);
    }
    const created = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Move this card' } });
    expect(created.status()).toBe(201); const card = (await created.json()).id;
    let anchor: { id: string; rank: string } | undefined;
    if (viewport.width === 390) {
      const result = await context.request.post(`/lists/${lists[1]}/cards`, { headers, data: { title: 'Position anchor' } });
      expect(result.status()).toBe(201); anchor = await result.json();
      // Compare persisted records with persisted records. The creation response
      // can contain .NET ticks that PostgreSQL rounds to microseconds.
      const baseline = await context.request.get(`/boards/${board}`);
      expect(baseline.status()).toBe(200);
      const persistedAnchor = (await baseline.json()).lists
        .find((column: { list: { id: string } }) => column.list.id === lists[1]).cards
        .find((value: { id: string }) => value.id === anchor!.id);
      expect(persistedAnchor).toBeDefined(); anchor = persistedAnchor;
    }
    const otherContext = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport });
    let restoreWorker = () => {};
    try {
      expect((await otherContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const other = await otherContext.newPage();
      for (const target of [page, other]) {
        const reads = trackBoardReads(target, board, `/app/${org}/boards/${board}`);
        await target.goto(`/app/${org}/boards/${board}`);
        await expect(target.getByRole('region', { name: 'Planning', exact: true }).getByRole('link', { name: 'Move this card' })).toBeVisible();
        await expect.poll(reads).toBeGreaterThanOrEqual(2);
      }
      let writes = 0; let key = ''; let body = '';
      await page.route(`**/cards/${card}/move`, async route => {
        writes++;
        expect(route.request().postDataJSON()).toEqual({ destinationListId: lists[1], expectedVersion: 1,
          ...(anchor ? { beforeCardId: anchor.id } : {}) });
        if (writes === 1) {
          key = route.request().headers()['idempotency-key']; body = route.request().postData()!;
          expect(key).toMatch(/^[0-9a-f-]{36}$/);
          const response = await route.fetch(); expect(response.status()).toBe(200);
          expect((await response.json()).listId).toBe(lists[1]); await route.abort('timedout');
        } else {
          expect(writes).toBe(2); expect(route.request().headers()['idempotency-key']).toBe(key);
          expect(route.request().postData()).toBe(body); await route.continue();
        }
      });
      await page.getByRole('link', { name: 'Move this card', exact: true }).focus(); await page.keyboard.press('Enter');
      const move = page.getByRole('button', { name: 'Move card', exact: true }); await expect(move).toBeEnabled();
      await move.focus(); await page.keyboard.press('Enter');
      const destination = page.getByRole('combobox', { name: 'Destination list' }); await destination.press('ArrowDown');
      await expect(page.getByRole('listbox', { name: 'Destination list' })).toBeVisible();
      await page.getByRole('option', { name: 'Complete', exact: true }).focus(); await page.keyboard.press('Enter');
      if (anchor) {
        const position = page.getByRole('combobox', { name: 'Card position' }); await position.press('ArrowDown');
        await expect(page.getByRole('listbox', { name: 'Card position' })).toBeVisible();
        await page.getByRole('option', { name: 'Before Position anchor', exact: true }).focus(); await page.keyboard.press('Enter');
      }
      await page.getByRole('button', { name: 'Confirm card move' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/The move could not be confirmed/)).toBeVisible();
      await expect(other.getByRole('region', { name: 'Complete', exact: true }).getByRole('link', { name: 'Move this card' })).toBeVisible({ timeout: 20_000 });
      expect(writes).toBe(1);
      const retry = page.getByRole('button', { name: 'Retry this move' }); await expect(retry).toBeEnabled();
      await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Move acknowledged. Current placement is being checked.')).toBeVisible();
      expect(writes).toBe(2);
      const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
      const persisted = (await current.json()).lists.find((column: { list: { id: string } }) => column.list.id === lists[1]).cards;
      expect(persisted).toHaveLength(anchor ? 2 : 1); expect(persisted[0]).toMatchObject({ id: card, version: 2 });
      if (anchor) { expect(persisted[1]).toMatchObject(anchor); expect(persisted[0].rank < anchor.rank).toBe(true); }
      await page.getByRole('button', { name: 'Close', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('region', { name: 'Complete', exact: true }).getByRole('link', { name: 'Move this card', exact: true })).toBeFocused();
      if (viewport.width === 390) {
        await page.unroute(`**/cards/${card}/move`);
        const dragHandle = page.getByRole('button', { name: 'Drag Move this card card', exact: true });
        await expect(dragHandle).toBeEnabled();
        const beforeDrag = await (await context.request.get(`/boards/${board}`)).json();
        let dragWrites = 0;
        page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname === `/cards/${card}/move`) dragWrites++; });
        await dragHandle.focus(); await page.keyboard.press('Space');
        await expect(dragHandle).toHaveAttribute('aria-pressed', 'true');
        await page.keyboard.press('ArrowLeft');
        await expect(page.getByText('Move this card card can be dropped at the end of Planning.', { exact: true })).toBeAttached();
        await page.keyboard.press('Escape');
        await expect(dragHandle).not.toHaveAttribute('aria-pressed', 'true');
        expect(dragWrites).toBe(0);
        expect((await (await context.request.get(`/boards/${board}`)).json()).lists).toEqual(beforeDrag.lists);
        await dragHandle.focus(); await page.keyboard.press('Space');
        await expect(dragHandle).toHaveAttribute('aria-pressed', 'true');
        await page.keyboard.press('ArrowLeft');
        await expect(page.getByText('Move this card card can be dropped at the end of Planning.', { exact: true })).toBeAttached();
        await page.keyboard.press('Space');
        await expect(page.getByText('Move acknowledged. Current placement is being checked.')).toBeVisible();
        await expect(page.getByRole('region', { name: 'Planning', exact: true }).getByRole('link', { name: 'Move this card', exact: true })).toBeFocused();
        expect(dragWrites).toBe(1);
        const afterDrag = await (await context.request.get(`/boards/${board}`)).json();
        expect(afterDrag.lists.find((column: { list: { id: string } }) => column.list.id === lists[0]).cards[0]).toMatchObject({ id: card, version: 3 });
        expect(afterDrag.lists.find((column: { list: { id: string } }) => column.list.id === lists[1]).cards).toEqual([anchor]);
        await page.reload();
        await expect(page.getByRole('region', { name: 'Planning', exact: true }).getByRole('link', { name: 'Move this card', exact: true })).toBeVisible();
      }
      if (viewport.width === 1280) {
        async function dragBefore(handleName: string, target: import('@playwright/test').Locator) {
          const handle = page.getByRole('button', { name: handleName, exact: true }); await expect(handle).toBeEnabled();
          const source = await handle.boundingBox(); const destination = await target.boundingBox();
          expect(source).not.toBeNull(); expect(destination).not.toBeNull();
          await page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2); await page.mouse.down();
          await page.mouse.move(destination!.x + destination!.width / 2, destination!.y + destination!.height / 2, { steps: 12 }); await page.mouse.up();
          await expect(page.getByText('Move acknowledged. Current placement is being checked.')).toBeVisible();
          await expect(handle).toBeEnabled();
          await expect(page.getByRole('link', { name: handleName.slice(5, -5), exact: true })).toBeFocused();
        }
        await page.unroute(`**/cards/${card}/move`);
        await dragBefore('Drag Move this card card', page.getByText('Drop card at end of Planning', { exact: true }));
        let persistedBoard = await (await context.request.get(`/boards/${board}`)).json();
        expect(persistedBoard.lists.find((column: { list: { id: string } }) => column.list.id === lists[0]).cards[0]).toMatchObject({ id: card, version: 3 });
        expect(persistedBoard.lists.find((column: { list: { id: string } }) => column.list.id === lists[1]).cards).toHaveLength(0);
        const extra = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Reorder anchor' } });
        expect(extra.status()).toBe(201); const extraId = (await extra.json()).id;
        await page.reload();
        await dragBefore('Drag Reorder anchor card', page.getByRole('link', { name: 'Move this card', exact: true }));
        persistedBoard = await (await context.request.get(`/boards/${board}`)).json();
        const planning = persistedBoard.lists.find((column: { list: { id: string } }) => column.list.id === lists[0]).cards;
        expect(planning.map((value: { id: string }) => value.id)).toEqual([extraId, card]);
        expect(planning.map((value: { version: number }) => value.version)).toEqual([2, 3]);
        await page.reload();
        await expect(page.getByRole('region', { name: 'Planning', exact: true }).getByRole('link').first()).toHaveAccessibleName('Reorder anchor');
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { try { await otherContext.close(); } finally { restoreWorker(); } }
  });
}
