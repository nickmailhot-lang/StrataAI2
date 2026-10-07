import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test('PRD-17: recipient inbox recovers real assignment and read changes across desktop and phone', async ({ page, context, browser, baseURL }) => {
  test.setTimeout(150_000);
  await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const owner = await browser.newContext({ baseURL });
  let phone: typeof owner | undefined; let preferences: typeof owner | undefined; let restoreWorker = () => {};
  try {
    const email = `notification-recipient-${Date.now()}@example.test`;
    for (const [index, client] of [owner, context].entries()) {
      const account = { email: index ? email : `notification-owner-1280-${Date.now()}@example.test`,
        password: 'notification-center-correct-horse', displayName: index ? 'Notification recipient' : 'Assignment issuer' };
      expect((await client.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
      expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    }
    const organization = await owner.request.post('/organizations', { headers, data: { name: 'Recipient inbox' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const invitation = await owner.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const recipient = (await (await context.request.get('/me')).json()).id;
    const createdBoard = await owner.request.post('/boards', { headers, data: { organizationId: org, name: 'Inbox assignments', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    expect((await owner.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    const createdList = await owner.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Inbox planning' } });
    expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
    restoreWorker = scopedBoardWorker(org);
    await waitForBoardDelivery(owner.request, board);
    phone = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
    preferences = await browser.newContext({ baseURL });
    expect((await preferences.request.post('/auth/login', { headers, data: { email, password: 'notification-center-correct-horse' } })).status()).toBe(200);
    let notificationSocket: WebSocketRoute | undefined; let liveOffline = false;
    await phone.routeWebSocket('**/notifications/live*', route => {
      if (liveOffline) { route.close({ code: 1013 }); return; }
      notificationSocket = route; route.connectToServer();
    });
    const other = await phone.newPage();
    function observeLive(client: typeof page) {
      const events: string[] = []; const cursors: (string | null)[] = []; let snapshots = 0;
      client.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/notifications/live') return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text);
            if (message.type !== 2 || !message.item) continue;
            const item = message.item;
            expect(item.organizationId).toBe(org); expect(item.recipientId).toBe(recipient);
            snapshots++;
            for (const event of item.events) {
              expect(event.organizationId).toBe(org); expect(event.recipientId).toBe(recipient);
              expect(event.entityType).toBe('Notification'); expect(event.metadata).toEqual({});
              events.push(event.eventType);
            }
          }
        });
        socket.on('framesent', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text);
            if (message.type === 4 && message.target === 'Watch') {
              expect(message.arguments[0]).toBe(org); cursors.push(message.arguments[1]);
            }
          }
        });
      });
      return { events, cursors, snapshots: () => snapshots };
    }
    const desktopLive = observeLive(page); const phoneLive = observeLive(other);
    await page.goto(`/app/${org}/notifications`); await other.goto(`/app/${org}/notifications`);
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect.poll(desktopLive.snapshots).toBeGreaterThan(0);
    await expect.poll(phoneLive.snapshots).toBeGreaterThan(0);
    async function assign(title: string) {
      const reply = await owner.request.post(`/lists/${list}/cards`, { headers, data: { title } });
      expect(reply.status()).toBe(201); const card = (await reply.json()).id;
      expect((await owner.request.put(`/cards/${card}/members/${recipient}?version=1`, { headers, data: {} })).status()).toBe(200);
      return card;
    }
    const card = await assign('First inbox Card');
    // Observe actual private live delivery, then fresh HTTP content in both views.
    await expect.poll(() => desktopLive.events.filter(type => type === 'NOTIFICATION_CREATED').length).toBe(1);
    await expect.poll(() => phoneLive.events.filter(type => type === 'NOTIFICATION_CREATED').length).toBe(1);
    await expect(page.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await expect(other.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const link = page.getByRole('link', { name: 'Open Card', exact: true });
    await expect(link).toHaveAttribute('href', `/app/${org}/boards/${board}/cards/${card}`);
    // AUTH-FR-010 / AC-AUTH-02-03: another authenticated session changes the
    // viewing preferences. Both open views must recover without a manual read,
    // while persisted notification timestamps and read state remain unchanged.
    async function recoverPreferences(timezone: string) {
      const before = await context.request.get(`/organizations/${org}/notifications`);
      expect(before.status()).toBe(200); const source = await before.json();
      const me = await preferences!.request.get('/me'); expect(me.status()).toBe(200);
      const profile = await me.json();
      expect((await preferences!.request.patch('/me', { headers, data: { version: profile.version, locale: 'en-US', timezone } })).status()).toBe(200);
      const createdAt = source.items.find((n: { entityId: string }) => n.entityId === card).createdAt;
      const caption = new Intl.DateTimeFormat('en-US', { timeZone: timezone, year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short' }).format(new Date(createdAt));
      for (const client of [page, other]) {
        const stamp = client.locator('time').filter({ hasText: caption });
        await expect(stamp).toBeVisible({ timeout: 25_000 });
        await expect(stamp).toHaveAttribute('datetime', createdAt);
      }
      const after = await context.request.get(`/organizations/${org}/notifications`);
      expect(after.status()).toBe(200); expect(await after.json()).toEqual(source);
    }
    await recoverPreferences('Asia/Tokyo');
    let originalKey: string | undefined; let originalBody: string | null = null; let writes = 0;
    await other.route(`**/organizations/${org}/notifications/read`, async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      const request = route.request(); const key = request.headers()['idempotency-key'];
      if (++writes === 1) {
        originalKey = key; originalBody = request.postData();
        const committed = await route.fetch(); expect(committed.status()).toBe(200); await route.abort('failed');
      } else {
        expect(key).toBe(originalKey); expect(request.postData()).toBe(originalBody); await route.continue();
      }
    });
    const read = other.getByRole('button', { name: 'Mark read', exact: true });
    await expect(read).toBeEnabled(); await read.press('Enter');
    await expect(other.getByRole('button', { name: 'Retry mark read' })).toBeEnabled();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await expect.poll(() => desktopLive.events.filter(type => type === 'NOTIFICATION_READ').length).toBe(1);
    const retry = other.getByRole('button', { name: 'Retry mark read' }); await expect(retry).toBeEnabled(); await retry.press('Enter');
    await expect(retry).toHaveCount(0);
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible(); expect(writes).toBe(2);
    await expect(other.getByRole('button', { name: 'Refresh notifications' })).toBeFocused();
    await other.unroute(`**/organizations/${org}/notifications/read`);
    await recoverPreferences('UTC');
    await expect(other.getByRole('button', { name: 'Refresh notifications' })).toBeFocused();
    await assign('Second inbox Card'); await assign('Third inbox Card');
    await expect(other.getByText('2 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const select = other.getByRole('button', { name: 'Select unread on this page' }); await expect(select).toBeEnabled(); await select.press('Enter');
    const markSelected = other.getByRole('button', { name: 'Mark selected read' }); await expect(markSelected).toBeEnabled(); await markSelected.press('Enter');
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    expect(notificationSocket).toBeDefined(); liveOffline = true;
    await phone.setOffline(true); await notificationSocket!.close({ code: 1012 });
    await other.getByRole('button', { name: 'Refresh notifications', exact: true }).press('Enter');
    await expect(other.getByText('Unable to load current notifications. Try again.', { exact: true })).toBeVisible();
    await expect(other.getByRole('article')).toHaveCount(0);
    await assign('Assignment during recipient disconnect');
    await expect(page.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    liveOffline = false; await phone.setOffline(false);
    await expect(other.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await expect.poll(() => phoneLive.cursors.some(cursor => typeof cursor === 'string' && /^[0-9]+$/.test(cursor)), { timeout: 25_000 }).toBe(true);
    await expect.poll(() => phoneLive.events.filter(type => type === 'NOTIFICATION_CREATED').length, { timeout: 25_000 }).toBe(4);
    await other.getByRole('button', { name: 'Mark read', exact: true }).press('Enter');
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    for (const client of [page, other]) {
      expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    }
    const inbox = await context.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200);
    const notifications = (await inbox.json()).items; expect(notifications).toHaveLength(4);
    expect(notifications.every((n: { recipientId: string; readAt: string | null }) => n.recipientId === recipient && n.readAt !== null)).toBe(true);
    expect((await (await owner.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await owner.request.delete(`/boards/${board}/members/${recipient}`, { headers })).status()).toBe(204);
    await expect(other.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
    await expect(page.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
    await expect(other.getByText('No notifications on this page. Card assignments from other people will appear here.', { exact: true })).toBeVisible();
    await expect(page.getByText('No notifications on this page. Card assignments from other people will appear here.', { exact: true })).toBeVisible();
    const hidden = await context.request.get(`/organizations/${org}/notifications`); expect(hidden.status()).toBe(200); expect((await hidden.json()).items).toEqual([]);
  } finally { restoreWorker(); await preferences?.close(); await phone?.close(); await owner.close(); }
});
