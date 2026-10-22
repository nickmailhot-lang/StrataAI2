import { expect, test, type WebSocketRoute } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

type WirePage = { organizationId: string; actorId: string; status: string; version: number; afterVersion: number;
  nextVersion: number | null; pending: boolean; events: Array<Record<string, unknown>> };

// PRD-27 TC-06/09/11/12: actual distinct accounts, admitted MUI updates and
// missed-source recovery through the release edge. Production uses the Worker.
for (const width of [1280, 390]) {
  test(`PRD-27: operational lifecycle withdraws work and replays across distinct accounts at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
    const nonce = crypto.randomUUID();
    const owner = await registerNotificationAccount(context.request, { email: `lifecycle-owner-${nonce}@example.test`,
      password: 'lifecycle-owner-correct-horse', displayName: 'Lifecycle release owner' }, production);
    const peer = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const email = `lifecycle-admin-${nonce}@example.test`;
      const recipient = await registerNotificationAccount(peer.request, { email,
        password: 'lifecycle-admin-correct-horse', displayName: 'Lifecycle release administrator' }, production);
      expect(owner.user.id).not.toBe(recipient.user.id);
      const headers = { 'X-StrataAI-Request': '1' };
      const name = `Lifecycle collaboration ${width}`;
      const created = await context.request.post('/organizations', { headers, data: { name } });
      expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${organization}/invitations`, {
        headers, data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201); const invited = await invitation.json();
      // Inbox acceptance works in Production without exposing bearer tokens.
      const accepted = await peer.request.post(`/me/invitations/${invited.id}/accept`, { headers });
      expect(accepted.status()).toBe(200);
      restoreWorker = scopedBoardWorker(organization);
      const review = await peer.newPage(), home = await peer.newPage();
      const sockets = new Set<WebSocketRoute>(); const frames: WirePage[] = [];
      const reports: { events: { action: string; kind: string; count: number; durationMs?: number }[] }[] = [];
      const reportStatuses: number[] = [];
      review.on('request', request => {
        if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') reports.push(request.postDataJSON());
      });
      review.on('response', response => {
        if (new URL(response.url()).pathname === '/me/activity-client-events') reportStatuses.push(response.status());
      });
      await review.routeWebSocket('**/organizations/live/operational-lifecycle*', route => {
        sockets.add(route); route.connectToServer();
      });
      review.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/operational-lifecycle') return;
        socket.on('framereceived', received => {
          if (typeof received.payload !== 'string') return;
          for (const part of received.payload.split('\x1e').filter(Boolean)) {
            try {
              const frame = JSON.parse(part) as { type?: number; item?: { organizationId: string; userId: string; page: WirePage } };
              if (frame.type === 2 && frame.item && frame.item.organizationId === organization && frame.item.userId === recipient.user.id)
                frames.push(frame.item.page);
            } catch { /* Ignore handshake fragments, never use them as source evidence. */ }
          }
        });
      });
      const path = `/organizations/${organization}/operational-lifecycle`;
      await page.goto(`/app/${organization}/operational-lifecycle`);
      await review.goto(`/app/${organization}/operational-lifecycle`); await home.goto(`/app/${organization}`);
      const launch = page.getByRole('button', { name: 'Review lifecycle change', exact: true });
      const peerLaunch = review.getByRole('button', { name: 'Review lifecycle change', exact: true });
      await expect(launch).toBeVisible(); await expect(peerLaunch).toBeVisible();
      await expect(home.getByRole('heading', { name, exact: true })).toBeVisible();
      await expect.poll(() => frames.some(frame => frame.status === 'ACTIVE'), { timeout: 10_000 }).toBe(true);
      await peerLaunch.click(); const dialog = review.getByRole('dialog', { name: 'Confirm lifecycle change', exact: true });
      await expect(dialog).toBeVisible();
      await launch.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel lifecycle change', exact: true })).toBeFocused();
      await page.keyboard.press('Tab'); await page.keyboard.press('Enter');
      await expect(page.getByRole('status').filter({ hasText: 'Original change acknowledged: Suspended' })).toBeVisible();
      await expect(review.getByText(/Current lifecycle: Suspended/)).toBeVisible({ timeout: 10_000 });
      await expect(dialog).not.toBeVisible();
      await expect(home.getByRole('heading', { name, exact: true })).toHaveCount(0, { timeout: 10_000 });
      await expect.poll(() => frames.some(frame => frame.status === 'SUSPENDED' && frame.events.length === 1), { timeout: 10_000 }).toBe(true);
      expect(frames.flatMap(frame => frame.events).every(event => Object.keys(event).sort().join(',')
        === 'actingCapacity,actorId,createdAt,entityId,entityType,eventId,eventType,organizationId,previousStatus,status,version')).toBe(true);
      expect(JSON.stringify(frames)).not.toContain(name);
      expect(JSON.stringify(frames)).not.toContain('Lifecycle release owner');
      // A frame's arrival is not consumer admission: a parent-only update can
      // already render this revision while its published fact is still being
      // checked. Disconnect only after the visible consumer confirms delivery.
      await expect(review.getByRole('status', { name: 'Live update status', exact: true })).toHaveText('Live updates are current.');
      await peer.setOffline(true); await Promise.all([...sockets].map(socket => socket.close()));
      const current = await context.request.get(path); expect(current.status()).toBe(200); const head = await current.json();
      const reactivated = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID(),
        'X-StrataAI-Expected-Actor': owner.user.id }, data: { version: head.version, status: 'ACTIVE' } });
      expect(reactivated.status()).toBe(200);
      await peer.setOffline(false);
      await expect(review.getByText(/Current lifecycle: Active/)).toBeVisible({ timeout: 30_000 });
      await expect.poll(() => frames.some(frame => frame.status === 'ACTIVE' && frame.afterVersion === 2
        && frame.events.length === 1 && frame.events[0].version === 3), { timeout: 30_000 }).toBe(true);
      await expect(home.getByRole('heading', { name, exact: true })).toBeVisible({ timeout: 30_000 });
      expect(await review.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await expect.poll(() => reports.flatMap(report => report.events).some(row => row.action === 'organization_lifecycle_read' && row.kind === 'reconnect'), { timeout: 15_000 }).toBe(true);
      await expect.poll(() => reportStatuses.length).toBe(reports.length); expect(reportStatuses.every(status => status === 204)).toBe(true);
      const serializedReports = JSON.stringify(reports);
      for (const secret of [organization, owner.user.id, recipient.user.id, name, email, 'SUSPENDED', 'ACTIVE', 'ORGANIZATION_ADMIN', path])
        expect(serializedReports).not.toContain(secret);
      for (const row of reports.flatMap(report => report.events)) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
        ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      const logout = await peer.request.post('/auth/logout', { headers }); expect(logout.status()).toBe(204);
      await expect(review.getByText(name, { exact: true })).toHaveCount(0, { timeout: 10_000 });
      await expect(peerLaunch).toHaveCount(0);
    } finally { await peer.setOffline(false); await peer.close(); restoreWorker(); }
  });
}
