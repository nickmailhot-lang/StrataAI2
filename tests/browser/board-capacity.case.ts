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
    const snapshot = await result.json() as { lists: { list: { id: string }; cards: { id: string; version: number }[] }[] };
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
    await cards.evaluate(node => { node.scrollTop = node.scrollHeight; });
    const last = cards.locator(`a[href$="/cards/${column.cards.at(-1)!.id}"]`);
    await expect(last).toBeVisible(); await last.press('Enter');
    const close = page.getByRole('button', { name: 'Close', exact: true }); await expect(close).toBeEnabled(); await close.press('Enter');
    await expect(last).toBeFocused(); await expect(last).toBeInViewport();
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
