import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import { expect, test, type Page, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

test('PRD-05 AC-PERM-05-03: two administrators recover member changes and cancel visibility consent on live updates', async ({ page, context, browser }) => {
  test.setTimeout(180_000);
  const headers = { 'X-StrataAI-Request': '1' };
  const phone = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width: 390, height: 844 } });
  const other = await phone.newPage();
  let restoreWorker = () => {};
  let unavailable = false; let socket: WebSocketRoute | undefined;
  await phone.routeWebSocket('**/boards/live*', route => {
    if (unavailable) { route.close({ code: 1013 }); return; }
    socket = route; route.connectToServer();
  });
  try {
    const email = `board-admin-live-${Date.now()}@example.test`;
    for (const [index, client] of [context, phone].entries()) {
      const data = { email: index ? email : `board-admin-owner-${Date.now()}@example.test`,
        password: 'board-admin-live-correct-horse', displayName: index ? 'Live administrator' : 'Live owner' };
      await registerVerifiedAccountFixture(client.request, data);
    }
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Live administration' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await phone.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const owner = (await (await context.request.get('/me')).json()).id;
    const administrator = (await (await phone.request.get('/me')).json()).id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private live administration', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = (await created.json()).id;
    expect((await context.request.patch(`/boards/${board}/members/${administrator}`, { headers, data: { role: 'ADMIN' } })).status()).toBe(200);
    restoreWorker = scopedBoardWorker(org);
    await waitForBoardDelivery(context.request, board);

    // The initial durable event page also invalidates the snapshot. Observe that
    // second directory read before reviewing anything; do not race bootstrap.
    const directoryReads = new Map<Page, number>([[page, 0], [other, 0]]);
    for (const target of [page, other]) target.on('response', response => {
      if (new URL(response.url()).pathname === `/boards/${board}/members` && response.request().method() === 'GET' && response.status() === 200)
        directoryReads.set(target, directoryReads.get(target)! + 1);
    });
    for (const target of [page, other]) {
      await target.goto(`/app/${org}/boards/${board}/members`);
      await expect(target.getByText('Live member updates connected.')).toBeVisible();
      await expect.poll(() => directoryReads.get(target)).toBeGreaterThanOrEqual(2);
      await expect(target.getByRole('progressbar', { name: 'Loading Board members' })).toHaveCount(0);
      await expect(target.getByRole('button', { name: 'Make member: Live owner' })).toBeVisible();
    }
    const setOwnerRole = async (role: 'ADMIN' | 'MEMBER') => {
      const changed = await context.request.patch(`/boards/${board}/members/${owner}`, { headers, data: { role } });
      expect(changed.status()).toBe(200); expect((await changed.json()).role).toBe(role);
      for (const target of [page, other]) await expect(target.getByRole('button', {
        name: `${role === 'ADMIN' ? 'Make member' : 'Make administrator'}: Live owner`,
      })).toBeVisible({ timeout: 20_000 });
    };
    await setOwnerRole('MEMBER');
    // Organization ownership keeps both clients authorized independently of
    // this explicit Board role. The phone misses the next event in its socket.
    expect(socket).toBeDefined(); unavailable = true; await socket!.close({ code: 1012 });
    await expect(other.getByText('Member updates are reconnecting or checking periodically.')).toBeVisible();
    await setOwnerRole('ADMIN');
    unavailable = false;
    await expect(other.getByText('Live member updates connected.')).toBeVisible({ timeout: 45_000 });
    await setOwnerRole('MEMBER');

    const visibilityReads = new Map([page, other].map(target => [target,
      trackBoardReads(target, board, `/app/${org}/boards/${board}/visibility`)]));
    for (const target of [page, other]) {
      await target.goto(`/app/${org}/boards/${board}/visibility`);
      await expect(target.getByText('Live visibility updates connected.')).toBeVisible();
      await expect.poll(visibilityReads.get(target)!).toBeGreaterThanOrEqual(2);
      await expect(target.getByRole('progressbar', { name: 'Checking Board visibility' })).toHaveCount(0);
    }
    let phoneWrites = 0;
    other.on('request', request => { if (request.method() === 'PATCH' && new URL(request.url()).pathname === `/boards/${board}/visibility`) phoneWrites++; });
    const visibility = other.getByRole('combobox', { name: 'Board visibility' });
    await expect(visibility).toBeEnabled(); await visibility.press('ArrowDown');
    await expect(other.getByRole('listbox', { name: 'Board visibility' })).toBeVisible();
    await other.getByRole('option', { name: 'Public', exact: true }).focus(); await other.keyboard.press('Enter');
    await other.getByRole('button', { name: 'Review visibility change' }).focus(); await other.keyboard.press('Enter');
    await expect(other.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
    const changed = await context.request.patch(`/boards/${board}/visibility`, { headers,
      data: { visibility: 'ORGANIZATION', version: (await current.json()).board.version } });
    expect(changed.status()).toBe(200); expect((await changed.json()).visibility).toBe('ORGANIZATION');
    for (const target of [page, other]) await expect(target.getByRole('combobox', { name: 'Board visibility' })).toHaveText('Organization', { timeout: 15_000 });
    await expect(other.getByRole('dialog')).toHaveCount(0);
    await expect(other.getByRole('button', { name: 'Check current visibility' })).toBeFocused(); expect(phoneWrites).toBe(0);
    // A Board demotion does not change Organization membership or grant edit
    // rights through visibility. The open administration screen must clear.
    expect((await context.request.patch(`/boards/${board}/members/${administrator}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    await expect(other.getByText('Board visibility administration is unavailable.')).toBeVisible({ timeout: 15_000 });
    await expect(other.getByRole('heading', { name: 'Private live administration', exact: true })).toHaveCount(0);
    await expect(other.getByRole('combobox')).toHaveCount(0);
    expect((await phone.request.get(`/boards/${board}/members`)).status()).toBe(404);
    expect((await phone.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility: 'PUBLIC', version: (await changed.json()).version } })).status()).toBe(404);
    for (const target of [page, other]) expect(await target.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  } finally { try { await phone.close(); } finally { restoreWorker(); } }
});
