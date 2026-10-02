import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test('PRD-17: recipient inbox recovers real assignment and read changes across desktop and phone', async ({ page, context, browser }) => {
  test.setTimeout(150_000);
  await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
  let phone: typeof owner | undefined; let restoreWorker = () => {};
  try {
    const email = `notification-recipient-${Date.now()}@example.test`;
    for (const [index, client] of [owner, context].entries()) {
      const account = { email: index ? email : `notification-owner-${Date.now()}@example.test`,
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
    phone = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
    const other = await phone.newPage();
    await page.goto(`/app/${org}/notifications`); await other.goto(`/app/${org}/notifications`);
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    async function assign(title: string) {
      const reply = await owner.request.post(`/lists/${list}/cards`, { headers, data: { title } });
      expect(reply.status()).toBe(201); const card = (await reply.json()).id;
      expect((await owner.request.put(`/cards/${card}/members/${recipient}?version=1`, { headers, data: {} })).status()).toBe(200);
      return card;
    }
    const card = await assign('First inbox Card');
    // Both views recover through the bounded HTTP refresh without navigation.
    await expect(page.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await expect(other.getByText('1 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const link = page.getByRole('link', { name: 'Open Card', exact: true });
    await expect(link).toHaveAttribute('href', `/app/${org}/boards/${board}/cards/${card}`);
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
    await read.focus(); await other.keyboard.press('Enter');
    await expect(other.getByRole('button', { name: 'Retry mark read' })).toBeEnabled();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const retry = other.getByRole('button', { name: 'Retry mark read' }); await retry.focus(); await other.keyboard.press('Enter');
    await expect(retry).toHaveCount(0);
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible(); expect(writes).toBe(2);
    await expect(other.getByRole('button', { name: 'Refresh notifications' })).toBeFocused();
    await other.unroute(`**/organizations/${org}/notifications/read`);
    await assign('Second inbox Card'); await assign('Third inbox Card');
    await expect(other.getByText('2 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await other.getByRole('button', { name: 'Select unread on this page' }).focus(); await other.keyboard.press('Enter');
    await other.getByRole('button', { name: 'Mark selected read' }).focus(); await other.keyboard.press('Enter');
    await expect(other.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const inbox = await context.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200);
    const notifications = (await inbox.json()).items; expect(notifications).toHaveLength(3);
    expect(notifications.every((n: { recipientId: string; readAt: string | null }) => n.recipientId === recipient && n.readAt !== null)).toBe(true);
    expect((await (await owner.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await owner.request.delete(`/boards/${board}/members/${recipient}`, { headers })).status()).toBe(204);
    await expect(other.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
    await expect(page.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
    await expect(other.getByText('No notifications on this page. Card assignments from other people will appear here.', { exact: true })).toBeVisible();
    await expect(page.getByText('No notifications on this page. Card assignments from other people will appear here.', { exact: true })).toBeVisible();
    const hidden = await context.request.get(`/organizations/${org}/notifications`); expect(hidden.status()).toBe(200); expect((await hidden.json()).items).toEqual([]);
  } finally { restoreWorker(); await phone?.close(); await owner.close(); }
});
