import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';
import { automaticDeletionWorker } from './automaticDeletionWorker';

for (const width of [1280, 390]) for (const offline of [false, true]) {
  test(`PRD-03/18-TC-05/07/09/10/11/12: Internal member recovers real terminal deletion ${offline ? 'after disconnect' : 'while connected'} at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000);
    await page.setViewportSize({ width, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${offline}-${Date.now()}`;
    const ownerCredentials = { email: `member-lifecycle-owner-${suffix}@example.test`, password: 'member-lifecycle-correct-horse', displayName: 'Deletion Owner' };
    const memberCredentials = { ...ownerCredentials, email: `member-lifecycle-member-${suffix}@example.test`, displayName: 'Lifecycle Member' };
    let socket: WebSocketRoute | undefined;
    let disconnected = false;
    let documents = 0;
    let restoreWorker = () => {};
    const frames: { state: string; events: Record<string, unknown>[] }[] = [];
    await page.routeWebSocket('**/organizations/live/lifecycle*', route => {
      if (disconnected) { route.close(); return; }
      socket = route; route.connectToServer();
    });
    page.on('request', request => { if (request.isNavigationRequest()) documents++; });
    page.on('websocket', transport => {
      if (!new URL(transport.url()).pathname.endsWith('/organizations/live/lifecycle')) return;
      transport.on('framereceived', frame => {
        for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
          const message = JSON.parse(raw);
          if (message.type === 2 && message.item?.page) frames.push(message.item.page);
        }
      });
    });
    try {
      const registered = await owner.request.post('/auth/register', { headers, data: ownerCredentials });
      expect(registered.status()).toBe(201); const ownerId = (await registered.json()).user.id;
      expect((await owner.request.post('/auth/login', { headers, data: ownerCredentials })).status()).toBe(200);
      const member = await context.request.post('/auth/register', { headers, data: memberCredentials });
      expect(member.status()).toBe(201); const memberId = (await member.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: memberCredentials })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode)
        .toBe(process.env.STRATAAI_E2E_RUNTIME_MODE === 'demo' ? 'demo' : 'production');
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Member lifecycle council' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const invitation = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: memberCredentials.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const board = await owner.request.post('/boards', { headers, data: { organizationId: org, name: 'Member lifecycle Board', visibility: 'ORGANIZATION' } });
      expect(board.status()).toBe(201); const boardId = (await board.json()).id;
      const list = await owner.request.post(`/boards/${boardId}/lists`, { headers, data: { name: 'Lifecycle List' } });
      expect(list.status()).toBe(201);
      const card = await owner.request.post(`/lists/${(await list.json()).id}/cards`, { headers, data: { title: 'Lifecycle Card' } });
      expect(card.status()).toBe(201); const cardId = (await card.json()).id;
      await page.goto(`/app/${org}`);
      await expect(page.getByRole('heading', { name: 'Member lifecycle council', exact: true })).toBeVisible();
      await expect(page.getByText('Member lifecycle Board', { exact: true })).toBeVisible();
      await expect.poll(() => frames.some(frame => frame.state === 'ACTIVE')).toBe(true);
      if (offline) {
        disconnected = true; await context.setOffline(true); socket?.close();
        await expect(page.getByText('Member lifecycle council', { exact: true })).toHaveCount(0);
        await expect(page.getByText('Member lifecycle Board', { exact: true })).toHaveCount(0);
      }
      const key = crypto.randomUUID();
      restoreWorker = automaticDeletionWorker();
      const path = `/organizations/${org}/deletion-requests/${key}?expectedActorId=${ownerId}`;
      const remove = () => owner.request.delete(`/organizations/${org}?version=1&expectedActorId=${ownerId}`,
        { headers: { ...headers, 'Idempotency-Key': key }, data: {} });
      const accepted = await remove(); expect(accepted.status()).toBe(202); expect(await accepted.json()).toEqual({ requestId: key });
      await expect.poll(async () => {
        const response = await owner.request.get(path); expect(response.status()).toBe(200);
        return (await response.json()).state;
      }, { timeout: 60_000 }).toBe('COMPLETED');
      const terminal = await owner.request.get(path); const snapshot = await terminal.json();
      expect(snapshot.version).toBe(3); expect(snapshot.eventId).toMatch(/^[0-9a-f-]{36}$/);
      const retried = await remove(); expect(retried.status()).toBe(202); expect(await retried.json()).toEqual({ requestId: key });
      expect(await (await owner.request.get(path)).json()).toEqual(snapshot);
      if (offline) { disconnected = false; await context.setOffline(false); }
      await expect(page.getByRole('status')).toHaveText('Organization deletion confirmed complete.', { timeout: 30_000 });
      await expect(page.getByText('Member lifecycle council', { exact: true })).toHaveCount(0);
      await expect(page.getByText('Member lifecycle Board', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Create board', exact: true })).toHaveCount(0);
      const expected = { state: 'COMPLETED', events: [{ eventId: snapshot.eventId, eventType: 'ORGANIZATION_DELETED', actorId: ownerId,
        organizationId: org, boardId: null, entityType: 'Organization', entityId: org, version: 3, metadata: {}, createdAt: snapshot.completedAt }] };
      await expect.poll(() => frames.find(frame => frame.state === 'COMPLETED' && frame.events.length > 0)).toEqual(expected);
      const lifecycle = await context.request.get(`/organizations/${org}/lifecycle-events?expectedActorId=${memberId}`);
      expect(lifecycle.status()).toBe(200); expect(lifecycle.headers()['cache-control']).toContain('no-store');
      expect(await lifecycle.json()).toEqual(expected);
      for (const target of [`/organizations/${org}`, `/boards/${boardId}`, `/cards/${cardId}/labels`, path.replace(ownerId, memberId)])
        expect((await context.request.get(target)).status()).toBe(404);
      expect((await context.request.get(`/organizations/${org}/lifecycle-events?expectedActorId=${ownerId}`)).status()).toBe(401);
      expect((await context.request.get('/me')).status()).toBe(200);
      expect(documents).toBe(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
      expect((await context.request.post('/auth/logout', { headers, data: {} })).status()).toBe(204);
      await expect(page).toHaveURL(/\/login(?:\?|$)/, { timeout: 30_000 });
      await expect(page.getByText('Organization deletion confirmed complete.', { exact: true })).toHaveCount(0);
      expect((await context.request.get(`/organizations/${org}/lifecycle-events?expectedActorId=${memberId}`)).status()).toBe(401);
    } finally { try { restoreWorker(); } finally { await context.setOffline(false); await owner.close(); } }
  });
}
