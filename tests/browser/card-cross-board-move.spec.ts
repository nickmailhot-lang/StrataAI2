import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-08/15 cross-Board keyboard move recovers after source removal and updates both streams at ${viewport.width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000); await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `cross-move-${viewport.width}-${Date.now()}@example.test`, password: 'cross-move-correct-horse', displayName: 'Cross Board mover' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Cross Board keyboard move' } });
    expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
    const boards: string[] = []; const lists: string[] = [];
    for (const name of ['Original Board', 'Destination Board']) {
      const b = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(b.status()).toBe(201); const board = (await b.json()).id; boards.push(board);
      const l = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `${name} List` } });
      expect(l.status()).toBe(201); lists.push((await l.json()).id);
    }
    const created = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Stable cross Board Card' } });
    expect(created.status()).toBe(201); const card = (await created.json()).id;
    const peerContext = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport });
    let restore = () => {};
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      restore = scopedBoardWorker(org);
      for (const board of boards) await waitForBoardDelivery(context.request, board);
      const sourcePeer = await peerContext.newPage(); const destinationPeer = await peerContext.newPage();
      for (const [p, board] of [[page, boards[0]], [sourcePeer, boards[0]], [destinationPeer, boards[1]]] as const) {
        const reads = trackBoardReads(p, board, `/app/${org}/boards/${board}`);
        await p.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      }
      await expect(sourcePeer.getByRole('link', { name: 'Stable cross Board Card', exact: true })).toBeVisible();
      let writes = 0; let originalBody = ''; let originalKey = '';
      await page.route(`**/cards/${card}/move`, async route => {
        writes++; expect(route.request().postDataJSON()).toEqual({ sourceBoardId: boards[0], destinationListId: lists[1], expectedVersion: 1 });
        if (writes === 1) {
          originalBody = route.request().postData()!; originalKey = route.request().headers()['idempotency-key'];
          const response = await route.fetch(); expect(response.status()).toBe(200);
          expect(await response.json()).toMatchObject({ id: card, boardId: boards[1], listId: lists[1], version: 2 });
          await route.abort('timedout');
        } else {
          expect(writes).toBe(2); expect(route.request().postData()).toBe(originalBody);
          expect(route.request().headers()['idempotency-key']).toBe(originalKey); await route.continue();
        }
      });
      await page.getByRole('link', { name: 'Stable cross Board Card', exact: true }).focus(); await page.keyboard.press('Enter');
      const start = page.getByRole('button', { name: 'Move to another Board', exact: true }); await expect(start).toBeEnabled();
      await start.focus(); await page.keyboard.press('Enter');
      const boardChoice = page.getByRole('combobox', { name: 'Destination Board', exact: true }); await expect(boardChoice).toBeEnabled();
      await boardChoice.press('ArrowDown'); await page.getByRole('option', { name: 'Destination Board', exact: true }).focus(); await page.keyboard.press('Enter');
      const listChoice = page.getByRole('combobox', { name: 'Destination List on another Board', exact: true }); await expect(listChoice).toBeEnabled();
      await listChoice.press('ArrowDown'); await page.getByRole('option', { name: 'Destination Board List', exact: true }).focus(); await page.keyboard.press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm move to another Board', exact: true }); await expect(confirm).toBeEnabled();
      await confirm.focus(); await page.keyboard.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry this cross-Board move', exact: true }); await expect(retry).toBeEnabled();
      await expect(sourcePeer.getByRole('link', { name: 'Stable cross Board Card', exact: true })).toHaveCount(0);
      await expect(destinationPeer.getByRole('link', { name: 'Stable cross Board Card', exact: true })).toBeVisible({ timeout: 20_000 });
      const canonicalSource = await context.request.get(`/boards/${boards[0]}`);
      expect((await canonicalSource.json()).lists.flatMap((c: { cards: { id: string }[] }) => c.cards).some((c: { id: string }) => c.id === card)).toBe(false);
      expect(writes).toBe(1);
      await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Move acknowledged. Check the destination Board for current placement.')).toBeVisible(); expect(writes).toBe(2);
      await page.getByRole('button', { name: 'Close', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Refresh board', exact: true })).toBeFocused();
      await destinationPeer.getByRole('link', { name: 'Stable cross Board Card', exact: true }).click();
      await destinationPeer.getByRole('button', { name: 'Review Card activity', exact: true }).click();
      await expect(destinationPeer.getByRole('region', { name: 'Card activity' }).getByText('Cross Board mover moved a Card.', { exact: true })).toHaveCount(2);
      const history = await context.request.get(`/cards/${card}/activity`); expect(history.status()).toBe(200);
      const moves = (await history.json()).items.filter((e: { eventType: string }) => e.eventType === 'CARD_MOVED');
      expect(moves).toHaveLength(2); expect(moves.every((e: { currentBoardId: string }) => e.currentBoardId === boards[1])).toBe(true);
      await destinationPeer.reload(); await expect(destinationPeer.getByRole('heading', { name: 'Card details', exact: true })).toBeVisible();
      await destinationPeer.getByRole('button', { name: 'Review Card activity', exact: true }).click();
      await expect(destinationPeer.getByRole('region', { name: 'Card activity' }).getByText('Cross Board mover moved a Card.', { exact: true })).toHaveCount(2);
    } finally { restore(); await peerContext.close(); }
  });
}
