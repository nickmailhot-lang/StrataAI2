import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Actual account/Board commands and domain history; no feed/authorization reply
// is mocked. Each viewport has two distinct accounts and issuing sessions.
for (const width of [1280, 390]) {
  test(`PRD-15 activity keyboard pages, two-client reconnect and revoked access at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const caption = 'Historical <script>🙂';
    const peer = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const peerEmail = `activity-peer-${width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, peer].entries()) {
        const credentials = { email: index ? peerEmail : `activity-owner-${width}-${Date.now()}@example.test`,
          password: 'activity-browser-correct-horse', displayName: index ? 'Activity reader' : caption };
        expect((await client.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      }
      const member = (await (await peer.request.get('/me')).json()).id;
      const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Activity Organization' } });
      expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: peerEmail, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await peer.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Activity Board', visibility: 'PRIVATE' } });
      expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
      expect((await context.request.patch(`/boards/${board}/members/${member}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const listResult = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Activity List' } });
      expect(listResult.status()).toBe(201); const list = (await listResult.json()).id;
      const cardResult = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Activity Card' } });
      expect(cardResult.status()).toBe(201); const card = (await cardResult.json()).id;
      const authored = await peer.request.patch(`/cards/${card}`, { headers, data: { title: 'Peer authored history', version: 1 } });
      expect(authored.status()).toBe(200);
      let version = 2;
      for (let index = 1; index <= 65; index++) {
        const changed = await context.request.patch(`/cards/${card}`, { headers, data: { title: `Activity Card ${index}`, version } });
        expect(changed.status()).toBe(200); version++;
      }
      const first = await peer.request.get(`/cards/${card}/activity`); expect(first.status()).toBe(200);
      const firstPage = await first.json(); expect(firstPage.items).toHaveLength(50); expect(firstPage.nextCursor).not.toBeNull();
      expect(firstPage.items.every((item: { actorLabel: string; metadata: object }) => item.actorLabel === caption && Object.keys(item.metadata).length === 0)).toBe(true);
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const boardPath = `/app/${org}/boards/${board}`;
      const boardReads = trackBoardReads(page, board, boardPath); await page.goto(boardPath); await expect.poll(boardReads).toBeGreaterThanOrEqual(2);
      const boardOpen = page.getByRole('button', { name: 'Review Board activity', exact: true });
      await expect(boardOpen).toBeEnabled(); await boardOpen.press('Enter');
      const boardHistory = page.getByRole('region', { name: 'Board activity', exact: true });
      await expect(boardHistory.getByRole('listitem')).toHaveCount(50);
      await expect(boardHistory.getByRole('button', { name: 'Older activity', exact: true })).toBeEnabled();
      await boardHistory.getByRole('button', { name: 'Close activity', exact: true }).press('Enter');
      await expect(boardOpen).toBeFocused();
      await page.getByRole('link', { name: 'Activity Card 65', exact: true }).press('Enter');
      const cardHistory = page.getByRole('region', { name: 'Card activity', exact: true });
      const open = page.getByRole('button', { name: 'Review Card activity', exact: true });
      await expect(open).toBeEnabled(); await open.press('Enter');
      await expect(cardHistory.getByRole('listitem')).toHaveCount(50);
      expect(await cardHistory.locator('script').count()).toBe(0);
      await expect(cardHistory.getByText(`${caption} updated a Card.`, { exact: true })).toHaveCount(50);
      const older = cardHistory.getByRole('button', { name: 'Older activity', exact: true }); await expect(older).toBeFocused();
      await older.press('Enter'); await expect(cardHistory.getByRole('listitem')).toHaveCount(17);
      await expect(cardHistory.getByText('Activity reader updated a Card.', { exact: true })).toHaveCount(1);
      const newer = cardHistory.getByRole('button', { name: 'Newer activity', exact: true }); await expect(newer).toBeFocused();
      await newer.press('Enter'); await expect(cardHistory.getByRole('listitem')).toHaveCount(50); await expect(older).toBeFocused();
      const peerPage = await peer.newPage(); const cardPath = `${boardPath}/cards/${card}`;
      let disconnected = false; let socket: WebSocketRoute | undefined;
      await peer.routeWebSocket('**/boards/live*', route => {
        if (disconnected) { route.close({ code: 1013 }); return; }
        socket = route; route.connectToServer();
      });
      const peerReads = trackBoardReads(peerPage, board, cardPath); await peerPage.goto(cardPath); await expect.poll(peerReads).toBeGreaterThanOrEqual(2);
      const peerOpen = peerPage.getByRole('button', { name: 'Review Card activity', exact: true }); await expect(peerOpen).toBeEnabled(); await peerOpen.press('Enter');
      const peerHistory = peerPage.getByRole('region', { name: 'Card activity', exact: true }); await expect(peerHistory.getByRole('listitem')).toHaveCount(50);
      const added = await context.request.post(`/cards/${card}/comments`, { headers, data: { content: 'Private body excluded from activity', cardVersion: version } });
      expect(added.status()).toBe(200); expect((await added.json()).cardVersion).toBe(version + 1); version++;
      for (const history of [cardHistory, peerHistory]) {
        await expect(history.getByText(`${caption} added a comment.`, { exact: true })).toHaveCount(1, { timeout: 30_000 });
        await expect(history.getByText('Private body excluded from activity', { exact: true })).toHaveCount(0);
      }
      await expect(peerPage.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 30_000 });
      expect(socket).toBeDefined(); disconnected = true; socket!.close({ code: 1013 });
      await expect(peerPage.getByText('Live updates connected.', { exact: true })).toHaveCount(0);
      const missed = await context.request.post(`/cards/${card}/comments`, { headers, data: { content: 'Recovery body excluded from activity', cardVersion: version } });
      expect(missed.status()).toBe(200); expect((await missed.json()).cardVersion).toBe(version + 1); version++;
      await waitForBoardDelivery(context.request, board); disconnected = false;
      await expect(peerPage.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 30_000 });
      await expect(peerHistory.getByText(`${caption} added a comment.`, { exact: true })).toHaveCount(2, { timeout: 30_000 });
      await expect(peerHistory.getByText('Recovery body excluded from activity', { exact: true })).toHaveCount(0);
      await expect(peerHistory.getByRole('listitem')).toHaveCount(50);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      expect((await context.request.delete(`/boards/${board}/members/${member}`, { headers })).status()).toBe(204);
      await expect(peerHistory).toHaveCount(0, { timeout: 15_000 });
      await expect(peerPage.getByText(`${caption} added a comment.`, { exact: true })).toHaveCount(0);
      const denied = await peer.request.get(`/cards/${card}/activity?after=${encodeURIComponent(firstPage.nextCursor)}`);
      expect(denied.status()).toBe(404); expect(await denied.text()).not.toContain(caption);
      const archived = await context.request.post(`/cards/${card}/archive`, { headers, data: { version } });
      expect(archived.status()).toBe(200); version++;
      const archivedHistory = await context.request.get(`/cards/${card}/activity`); expect(archivedHistory.status()).toBe(200);
      expect((await archivedHistory.json()).items.some((item: { eventType: string }) => item.eventType === 'CARD_ARCHIVED')).toBe(true);
      await page.goto(`${boardPath}/cards/${card}`);
      await expect(page.getByText('This Card or its List is archived. Details are read-only.', { exact: true })).toBeVisible();
      await expect(open).toBeEnabled(); await open.press('Enter');
      await expect(cardHistory.getByText(`${caption} archived a Card.`, { exact: true })).toHaveCount(1);
      await expect(page.getByRole('textbox', { name: 'Card title', exact: true })).toHaveCount(0);
      const archivedComments = page.getByRole('button', { name: 'Review Card comments', exact: true });
      await expect(archivedComments).toBeEnabled(); await archivedComments.press('Enter');
      await expect(page.getByText('Recovery body excluded from activity', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Add comment', exact: true })).toBeDisabled();
      await page.goto(boardPath); await expect(boardOpen).toBeEnabled(); await boardOpen.press('Enter');
      await expect(boardHistory.getByText(`${caption} archived a Card.`, { exact: true })).toHaveCount(1);
      const deleted = await context.request.delete(`/cards/${card}?version=${version}&confirmed=true`, { headers });
      expect(deleted.status()).toBe(200);
      await expect(boardHistory.getByText(`${caption} deleted a Card.`, { exact: true })).toHaveCount(1, { timeout: 30_000 });
      expect((await context.request.get(`/cards/${card}/activity`)).status()).toBe(200);
      const peerProfile = await (await peer.request.get('/me')).json();
      expect((await peer.request.patch('/me', { headers, data: { displayName: 'Renamed activity reader', version: peerProfile.version } })).status()).toBe(200);
      expect((await peer.request.post('/me/deactivate', { headers, data: {} })).status()).toBe(204);
      expect((await peer.request.get(`/cards/${card}/activity`)).status()).toBe(401);
      const boardOlder = boardHistory.getByRole('button', { name: 'Older activity', exact: true });
      await expect(boardOlder).toBeEnabled(); await boardOlder.press('Enter');
      await expect(boardHistory.getByText('Activity reader updated a Card.', { exact: true })).toHaveCount(1);
      await expect(boardHistory.getByText('Renamed activity reader updated a Card.', { exact: true })).toHaveCount(0);
    } finally { try { restoreWorker(); } finally { await peer.close(); } }
  });
}
