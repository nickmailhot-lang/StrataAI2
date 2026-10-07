import AxeBuilder from '@axe-core/playwright';
import { expect, test, type WebSocketRoute } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-05/08/09/11/12, PRD-60-TC-04/09/11/12: nonmember Portal recipient withdraws and recovers changed Organization authority at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${Date.now()}`;
    const owner = { email: `authority-issuer-${suffix}@example.test`, password: 'authority-browser-correct-horse', displayName: 'Authority issuer' };
    const recipient = { ...owner, email: `authority-recipient-${suffix}@example.test`, displayName: 'Authority recipient' };
    const envelopes: { cursor: string; resetRequired: boolean; hasMore: boolean; events: unknown[] }[] = [];
    let documents = 0;
    let releaseRead: (() => void) | undefined;
    let liveSocket: WebSocketRoute | undefined;
    let disconnected = false;
    // Forward every frame to the real server. Chromium's offline emulation
    // leaves established sockets alive, so close this transport explicitly too.
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
      expect((await context.request.post('/auth/register', { headers, data: recipient })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: recipient })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe(process.env.STRATAAI_E2E_RUNTIME_MODE ?? 'production');
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Portal authority before' } });
      expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id as string;
      const invitation = await issuer.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: recipient.email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(invitation.status()).toBe(201);
      const invitationId = (await invitation.json()).id as string;
      await page.goto('/app/invitations');
      await expect(page.getByRole('button', { name: 'Accept invitation to Portal authority before', exact: true })).toBeEnabled();
      await expect.poll(() => envelopes.some(e => e.resetRequired && !e.events.length)).toBe(true);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      await refresh.focus();

      // Hold the actual protected recovery read to observe withdrawal before
      // its new response arrives. Neither socket frames nor source rows are mocked.
      let reads = 0;
      const held = new Promise<void>(resolve => { releaseRead = resolve; });
      await page.route('**/me/invitations?**', async route => { reads++; await held; await route.continue(); });
      expect((await issuer.request.patch(`/organizations/${org}`, { headers,
        data: { name: 'Portal authority after', version: 1 } })).status()).toBe(200);
      await expect.poll(() => reads, { timeout: 45_000 }).toBeGreaterThan(0);
      await expect(page.getByText('Portal authority before', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: /^Accept invitation to/ })).toHaveCount(0);
      releaseRead!();
      await expect(page.getByRole('button', { name: 'Accept invitation to Portal authority after', exact: true })).toBeEnabled({ timeout: 45_000 });
      await page.unrouteAll({ behavior: 'wait' });
      await expect(refresh).toBeFocused();

      disconnected = true; await context.setOffline(true); liveSocket!.close();
      await expect(page.getByRole('status')).toContainText('interrupted', { timeout: 15_000 });
      expect((await issuer.request.patch(`/organizations/${org}`, { headers,
        data: { name: 'Portal authority recovered', version: 2 } })).status()).toBe(200);
      disconnected = false; await context.setOffline(false);
      const accept = page.getByRole('button', { name: 'Accept invitation to Portal authority recovered', exact: true });
      await expect(accept).toBeEnabled({ timeout: 45_000 });
      await expect(page.getByText('Portal authority after', { exact: true })).toHaveCount(0);
      await expect(refresh).toBeFocused();
      expect(documents).toBe(1);
      // Parent authority changes invalidate cursors without inventing invitation transitions.
      expect(envelopes.flatMap(e => e.events)).toEqual([]);
      for (const envelope of envelopes)
        expect(Object.keys(envelope).sort()).toEqual(['cursor', 'events', 'hasMore', 'resetRequired']);
      const wire = JSON.stringify(envelopes);
      for (const privateValue of [recipient.email, org, invitationId, 'Portal authority before', 'Portal authority after', 'Portal authority recovered'])
        expect(wire).not.toContain(privateValue);
      await accept.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('link', { name: 'Open Owner Portal', exact: true })).toBeVisible({ timeout: 15_000 });
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect(documents).toBe(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { releaseRead?.(); await context.setOffline(false); await issuer.close(); }
  });
}
