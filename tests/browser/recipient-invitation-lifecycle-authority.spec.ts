import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';

for (const width of [1280, 390]) for (const offline of [false, true]) {
  test(`PRD-03-TC-05/07/09/11/12, PRD-60-TC-04/09/11/12: Portal invitation withdraws on Organization deletion ${offline ? 'after disconnect' : 'while connected'} at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${offline}-${Date.now()}`;
    const owner = { email: `lifecycle-owner-${suffix}@example.test`, password: 'lifecycle-browser-correct-horse', displayName: 'Lifecycle Owner' };
    const recipient = { ...owner, email: `lifecycle-recipient-${suffix}@example.test`, displayName: 'Lifecycle Recipient' };
    const envelopes: { cursor: string; resetRequired: boolean; hasMore: boolean; events: unknown[] }[] = [];
    let documents = 0;
    let disconnected = false;
    let liveSocket: WebSocketRoute | undefined;
    let releaseRead: (() => void) | undefined;
    await page.routeWebSocket('**/invitations/live*', socket => {
      if (disconnected) { socket.close(); return; }
      socket.connectToServer(); liveSocket = socket;
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    page.on('websocket', socket => {
      if (!new URL(socket.url()).pathname.startsWith('/invitations/live')) return;
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          const value = JSON.parse(part);
          if (value.type === 2) envelopes.push(value.item);
        }
      });
    });
    try {
      expect((await issuer.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await issuer.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const actor = (await (await issuer.request.get('/me')).json()).id as string;
      expect((await context.request.post('/auth/register', { headers, data: recipient })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe(process.env.STRATAAI_E2E_RUNTIME_MODE ?? 'production');
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Invitation lifecycle parent' } });
      expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id as string;
      const invited = await issuer.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: recipient.email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(invited.status()).toBe(201);
      const invitationId = (await invited.json()).id as string;
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: 'Accept invitation to Invitation lifecycle parent', exact: true });
      await expect(accept).toBeEnabled();
      await expect.poll(() => envelopes.some(e => e.resetRequired && !e.events.length)).toBe(true);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      await refresh.focus();
      if (offline) {
        disconnected = true; await context.setOffline(true); liveSocket!.close();
        await expect(page.getByRole('status')).toContainText('interrupted', { timeout: 15_000 });
      }
      // Delay only the actual recovery read, preserving genuine socket frames,
      // command publication and protected recipient discovery.
      let reads = 0;
      const held = new Promise<void>(resolve => { releaseRead = resolve; });
      await page.route('**/me/invitations?**', async route => { reads++; await held; await route.continue(); });
      const key = crypto.randomUUID();
      const path = `/organizations/${org}?version=1&expectedActorId=${actor}`;
      const requestHeaders = { ...headers, 'Idempotency-Key': key };
      expect((await issuer.request.delete(path, { headers: requestHeaders, data: {} })).status()).toBe(202);
      expect((await issuer.request.delete(path, { headers: requestHeaders, data: {} })).status()).toBe(202);
      if (offline) { disconnected = false; await context.setOffline(false); }
      await expect.poll(() => reads, { timeout: 45_000 }).toBeGreaterThan(0);
      await expect(page.getByText('Invitation lifecycle parent', { exact: true })).toHaveCount(0);
      await expect(accept).toHaveCount(0);
      await expect(refresh).toBeFocused();
      releaseRead!();
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible({ timeout: 45_000 });
      await page.unrouteAll({ behavior: 'wait' });
      const denied = await context.request.post(`/me/invitations/${invitationId}/accept`, { headers });
      expect(denied.status()).toBe(400); expect((await denied.json()).code).toBe('invalid_or_expired_invitation');
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await context.request.get('/me')).status()).toBe(200);
      expect(documents).toBe(1); expect(envelopes.flatMap(e => e.events)).toEqual([]);
      for (const envelope of envelopes)
        expect(Object.keys(envelope).sort()).toEqual(['cursor', 'events', 'hasMore', 'resetRequired']);
      const wire = JSON.stringify(envelopes);
      for (const privateValue of [recipient.email, actor, org, invitationId, 'Invitation lifecycle parent'])
        expect(wire).not.toContain(privateValue);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { releaseRead?.(); await context.setOffline(false); await issuer.close(); }
  });
}
