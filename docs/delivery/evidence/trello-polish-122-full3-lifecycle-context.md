# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: organization-operational-lifecycle-live.spec.ts >> PRD-27: operational lifecycle withdraws work and replays across distinct accounts at 390px
- Location: tests/browser/organization-operational-lifecycle-live.spec.ts:11:7

# Error details

```
Error: expect(received).toBe(expected) // Object.is equality

Expected: true
Received: false

Call Log:
- Timeout 30000ms exceeded while waiting on the predicate
```

# Page snapshot

```yaml
- generic [ref=e2]:
  - main [ref=e3]:
    - generic [ref=e4]:
      - heading "Organization lifecycle" [level=1] [ref=e5]
      - link "Back to organizations" [ref=e6] [cursor=pointer]:
        - /url: /app
      - link "Organization lifecycle management" [ref=e7] [cursor=pointer]:
        - /url: /app/lifecycle
      - status [active] [ref=e8]:
        - generic [ref=e12]: "Original change acknowledged: Suspended, revision 2. This acknowledgment may precede a later change. Load the current lifecycle before another action."
      - button "Load current lifecycle" [ref=e13] [cursor=pointer]
  - contentinfo "Application version" [ref=e14]:
    - generic [ref=e15]: Version 0.1.0-1968 · Build 30fcdb2104b9aaec913cf1c31ba3b0a920f9e53d
```

# Test source

```ts
  1   | import { expect, test, type WebSocketRoute } from './releaseTest';
  2   | import { registerNotificationAccount } from './notificationAccountFixture';
  3   | import { scopedBoardWorker } from './scopedBoardWorker';
  4   | 
  5   | type WirePage = { organizationId: string; actorId: string; status: string; version: number; afterVersion: number;
  6   |   nextVersion: number | null; pending: boolean; events: Array<Record<string, unknown>> };
  7   | 
  8   | // PRD-27 TC-06/09/11/12: actual distinct accounts, admitted MUI updates and
  9   | // missed-source recovery through the release edge. Production uses the Worker.
  10  | for (const width of [1280, 390]) {
  11  |   test(`PRD-27: operational lifecycle withdraws work and replays across distinct accounts at ${width}px`, async ({ page, context, browser }) => {
  12  |     test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
  13  |     const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
  14  |     const nonce = crypto.randomUUID();
  15  |     const owner = await registerNotificationAccount(context.request, { email: `lifecycle-owner-${nonce}@example.test`,
  16  |       password: 'lifecycle-owner-correct-horse', displayName: 'Lifecycle release owner' }, production);
  17  |     const peer = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
  18  |     let restoreWorker = () => {};
  19  |     try {
  20  |       const email = `lifecycle-admin-${nonce}@example.test`;
  21  |       const recipient = await registerNotificationAccount(peer.request, { email,
  22  |         password: 'lifecycle-admin-correct-horse', displayName: 'Lifecycle release administrator' }, production);
  23  |       expect(owner.user.id).not.toBe(recipient.user.id);
  24  |       const headers = { 'X-StrataAI-Request': '1' };
  25  |       const name = `Lifecycle collaboration ${width}`;
  26  |       const created = await context.request.post('/organizations', { headers, data: { name } });
  27  |       expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
  28  |       const invitation = await context.request.post(`/organizations/${organization}/invitations`, {
  29  |         headers, data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
  30  |       expect(invitation.status()).toBe(201); const invited = await invitation.json();
  31  |       // Inbox acceptance works in Production without exposing bearer tokens.
  32  |       const accepted = await peer.request.post(`/me/invitations/${invited.id}/accept`, { headers });
  33  |       expect(accepted.status()).toBe(200);
  34  |       restoreWorker = scopedBoardWorker(organization);
  35  |       const review = await peer.newPage(), home = await peer.newPage();
  36  |       const sockets = new Set<WebSocketRoute>(); const frames: WirePage[] = [];
  37  |       const reports: { events: { action: string; kind: string; count: number; durationMs?: number }[] }[] = [];
  38  |       const reportStatuses: number[] = [];
  39  |       review.on('request', request => {
  40  |         if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') reports.push(request.postDataJSON());
  41  |       });
  42  |       review.on('response', response => {
  43  |         if (new URL(response.url()).pathname === '/me/activity-client-events') reportStatuses.push(response.status());
  44  |       });
  45  |       await review.routeWebSocket('**/organizations/live/operational-lifecycle*', route => {
  46  |         sockets.add(route); route.connectToServer();
  47  |       });
  48  |       review.on('websocket', socket => {
  49  |         if (new URL(socket.url()).pathname !== '/organizations/live/operational-lifecycle') return;
  50  |         socket.on('framereceived', received => {
  51  |           if (typeof received.payload !== 'string') return;
  52  |           for (const part of received.payload.split('\x1e').filter(Boolean)) {
  53  |             try {
  54  |               const frame = JSON.parse(part) as { type?: number; item?: { organizationId: string; userId: string; page: WirePage } };
  55  |               if (frame.type === 2 && frame.item && frame.item.organizationId === organization && frame.item.userId === recipient.user.id)
  56  |                 frames.push(frame.item.page);
  57  |             } catch { /* Ignore handshake fragments, never use them as source evidence. */ }
  58  |           }
  59  |         });
  60  |       });
  61  |       const path = `/organizations/${organization}/operational-lifecycle`;
  62  |       await page.goto(`/app/${organization}/operational-lifecycle`);
  63  |       await review.goto(`/app/${organization}/operational-lifecycle`); await home.goto(`/app/${organization}`);
  64  |       const launch = page.getByRole('button', { name: 'Review lifecycle change', exact: true });
  65  |       const peerLaunch = review.getByRole('button', { name: 'Review lifecycle change', exact: true });
  66  |       await expect(launch).toBeVisible(); await expect(peerLaunch).toBeVisible();
  67  |       await expect(home.getByRole('heading', { name, exact: true })).toBeVisible();
  68  |       await expect.poll(() => frames.some(frame => frame.status === 'ACTIVE'), { timeout: 10_000 }).toBe(true);
  69  |       await peerLaunch.click(); const dialog = review.getByRole('dialog', { name: 'Confirm lifecycle change', exact: true });
  70  |       await expect(dialog).toBeVisible();
  71  |       await launch.focus(); await page.keyboard.press('Enter');
  72  |       await expect(page.getByRole('button', { name: 'Cancel lifecycle change', exact: true })).toBeFocused();
  73  |       await page.keyboard.press('Tab'); await page.keyboard.press('Enter');
  74  |       await expect(page.getByRole('status').filter({ hasText: 'Original change acknowledged: Suspended' })).toBeVisible();
  75  |       await expect(review.getByText(/Current lifecycle: Suspended/)).toBeVisible({ timeout: 10_000 });
  76  |       await expect(dialog).not.toBeVisible();
  77  |       await expect(home.getByRole('heading', { name, exact: true })).toHaveCount(0, { timeout: 10_000 });
  78  |       await expect.poll(() => frames.some(frame => frame.status === 'SUSPENDED' && frame.events.length === 1), { timeout: 10_000 }).toBe(true);
  79  |       expect(frames.flatMap(frame => frame.events).every(event => Object.keys(event).sort().join(',')
  80  |         === 'actingCapacity,actorId,createdAt,entityId,entityType,eventId,eventType,organizationId,previousStatus,status,version')).toBe(true);
  81  |       expect(JSON.stringify(frames)).not.toContain(name);
  82  |       expect(JSON.stringify(frames)).not.toContain('Lifecycle release owner');
  83  |       await peer.setOffline(true); await Promise.all([...sockets].map(socket => socket.close()));
  84  |       const current = await context.request.get(path); expect(current.status()).toBe(200); const head = await current.json();
  85  |       const reactivated = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID(),
  86  |         'X-StrataAI-Expected-Actor': owner.user.id }, data: { version: head.version, status: 'ACTIVE' } });
  87  |       expect(reactivated.status()).toBe(200);
  88  |       await peer.setOffline(false);
  89  |       await expect(review.getByText(/Current lifecycle: Active/)).toBeVisible({ timeout: 30_000 });
  90  |       await expect.poll(() => frames.some(frame => frame.status === 'ACTIVE' && frame.afterVersion === 2
> 91  |         && frame.events.length === 1 && frame.events[0].version === 3), { timeout: 30_000 }).toBe(true);
      |                                                                                              ^ Error: expect(received).toBe(expected) // Object.is equality
  92  |       await expect(home.getByRole('heading', { name, exact: true })).toBeVisible({ timeout: 30_000 });
  93  |       expect(await review.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  94  |       await expect.poll(() => reports.flatMap(report => report.events).some(row => row.action === 'organization_lifecycle_read' && row.kind === 'reconnect'), { timeout: 15_000 }).toBe(true);
  95  |       await expect.poll(() => reportStatuses.length).toBe(reports.length); expect(reportStatuses.every(status => status === 204)).toBe(true);
  96  |       const serializedReports = JSON.stringify(reports);
  97  |       for (const secret of [organization, owner.user.id, recipient.user.id, name, email, 'SUSPENDED', 'ACTIVE', 'ORGANIZATION_ADMIN', path])
  98  |         expect(serializedReports).not.toContain(secret);
  99  |       for (const row of reports.flatMap(report => report.events)) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
  100 |         ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
  101 |       const logout = await peer.request.post('/auth/logout', { headers }); expect(logout.status()).toBe(204);
  102 |       await expect(review.getByText(name, { exact: true })).toHaveCount(0, { timeout: 10_000 });
  103 |       await expect(peerLaunch).toHaveCount(0);
  104 |     } finally { await peer.setOffline(false); await peer.close(); restoreWorker(); }
  105 |   });
  106 | }
  107 | 
```