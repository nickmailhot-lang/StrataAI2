import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-04: personal star isolation, lost acknowledgment and later-state recovery at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `board-star-${width}-${Date.now()}@example.test`, password: 'star-correct-horse-battery', displayName: 'Star owner' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Personal star browser fixture' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Independent stars', visibility: 'PUBLIC' } });
    expect(created.status()).toBe(201); const board = await created.json();
    const outsider = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    const otherAccount = { ...account, email: `board-star-other-${width}-${Date.now()}@example.test`, displayName: 'Other star account' };
    expect((await outsider.request.post('/auth/register', { headers, data: otherAccount })).status()).toBe(201);
    expect((await outsider.request.post('/auth/login', { headers, data: otherAccount })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board.id);
      const path = `/app/${org}/boards/${board.id}`; const other = await outsider.newPage();
      const mirror = await context.newPage(); let initialPrivateFrame = false; let liveRevision = 0;
      mirror.on('websocket', socket => {
        if (!socket.url().includes('/boards/live/stars')) return;
        socket.on('framereceived', frame => {
          for (const value of frame.payload.toString().split('\x1e').filter(Boolean)) {
            try {
              const message = JSON.parse(value); const item = message.item;
              if (message.type !== 2 || item?.organizationId !== org || item?.boardId !== board.id || !Array.isArray(item.events)) continue;
              initialPrivateFrame = true;
              for (const event of item.events) if (event.eventType === 'BOARD_STARRED' && event.actorId === item.userId)
                liveRevision = Math.max(liveRevision, event.version);
            } catch { /* SignalR handshake/control frames contain no star page. */ }
          }
        });
      });
      await page.goto(path); await other.goto(path); await mirror.goto(path);
      for (const client of [page, other, mirror]) {
        await expect(client.getByRole('button', { name: 'Board starring', exact: true })).toBeEnabled();
        await client.getByRole('button', { name: 'Board starring', exact: true }).focus(); await client.keyboard.press('Enter');
        await expect(client.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      }
      await expect.poll(() => initialPrivateFrame).toBe(true);
      const before = await context.request.get(`/boards/${board.id}`); expect(before.status()).toBe(200);
      const beforeState = await before.json();
      const keys: (string | undefined)[] = [];
      await page.route(`**/boards/${board.id}/star?*`, async route => {
        if (route.request().method() !== 'PUT') { await route.continue(); return; }
        keys.push(route.request().headers()['idempotency-key']);
        const acknowledgment = await route.fetch(); expect(acknowledgment.status()).toBe(204);
        if (keys.length === 1) {
          // A later authorized change commits after the first star, before its
          // acknowledgment reaches the original client.
          const unstar = await context.request.delete(`/boards/${board.id}/star?version=1`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } });
          expect(unstar.status()).toBe(204); await route.abort('failed');
        } else await route.fulfill({ response: acknowledgment });
      });
      await expect(page.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
      await page.getByRole('button', { name: 'Star Board', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry same star change', exact: true })).toBeEnabled();
      await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Done', exact: true })).toHaveCount(0);
      // Prove actual private WebSocket delivery to a second client for this
      // account, independently of the fallback HTTP polling timer.
      await expect.poll(() => liveRevision).toBe(2);
      await expect(other.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
      await other.getByRole('button', { name: 'Star Board', exact: true }).focus(); await other.keyboard.press('Enter');
      await expect(other.getByText('You have starred this Board.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Retry same star change', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
      await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      expect(keys).toHaveLength(2); expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/); expect(keys[1]).toBe(keys[0]);
      for (const [client, revisions] of [[page, 2], [other, 1], [mirror, 2]] as const) {
        const review = client.getByRole('button', { name: 'Review your star history', exact: true });
        await expect(review).toBeEnabled(); await review.focus(); await client.keyboard.press('Enter');
        const history = client.getByRole('region', { name: 'Your star history', exact: true });
        await expect(history.getByRole('listitem')).toHaveCount(revisions);
        await expect(history.getByText(`You changed your Board star (revision ${revisions}).`, { exact: true })).toBeVisible();
        const close = client.getByRole('button', { name: 'Close star history', exact: true });
        await close.focus(); await client.keyboard.press('Enter'); await expect(review).toBeFocused();
      }
      for (const [client, starred] of [[context.request, false], [outsider.request, true]] as const) {
        const result = await client.get(`/boards/${board.id}/star`); expect(result.status()).toBe(200);
        expect((await result.json()).starred).toBe(starred);
      }
      const after = await context.request.get(`/boards/${board.id}`); expect(after.status()).toBe(200);
      const afterState = await after.json(); expect(afterState.board).toEqual(beforeState.board); expect(afterState.lists).toEqual(beforeState.lists);
      await expect(other.getByRole('button', { name: 'Unstar Board', exact: true })).toBeEnabled();
      await other.getByRole('button', { name: 'Unstar Board', exact: true }).focus(); await other.keyboard.press('Enter');
      await expect(other.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      for (const client of [page, other, mirror]) {
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        await expect(client.getByRole('button', { name: 'Done', exact: true })).toBeEnabled();
        await client.getByRole('button', { name: 'Done', exact: true }).focus(); await client.keyboard.press('Enter');
        await expect(client.getByRole('button', { name: 'Board starring', exact: true })).toBeFocused();
      }
      await mirror.close();
      await page.reload();
      await expect(page.getByRole('button', { name: 'Board starring', exact: true })).toBeEnabled();
      await page.getByRole('button', { name: 'Board starring', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
    } finally { await outsider.close(); restoreWorker(); }
  });
}
