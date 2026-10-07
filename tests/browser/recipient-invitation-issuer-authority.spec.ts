import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-05/08/11/12, PRD-60-TC-04/06/11/12: actual issuer removal withdraws a nonmember Portal invitation at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${Date.now()}`;
    const credentials = { email: `issuer-authority-owner-${suffix}@example.test`, password: 'issuer-authority-correct-horse', displayName: 'Authority owner' };
    const adminCredentials = { ...credentials, email: `issuer-authority-admin-${suffix}@example.test`, displayName: 'Invitation administrator' };
    const recipientCredentials = { ...credentials, email: `issuer-authority-recipient-${suffix}@example.test`, displayName: 'Portal recipient' };
    let releaseRead: (() => void) | undefined;
    let documents = 0;
    const envelopes: { cursor: string; resetRequired: boolean; hasMore: boolean; events: unknown[] }[] = [];
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    page.on('websocket', socket => {
      if (!new URL(socket.url()).pathname.startsWith('/invitations/live')) return;
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          const value = JSON.parse(part); if (value.type === 2) envelopes.push(value.item);
        }
      });
    });
    try {
      for (const [client, data] of [[owner, credentials], [issuer, adminCredentials], [context, recipientCredentials]] as const) {
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe(process.env.STRATAAI_E2E_RUNTIME_MODE ?? 'production');
      const adminId = (await (await issuer.request.get('/me')).json()).id as string;
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Issuer authority Portal parent' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
      // Establish the administrator through a real invitation and acceptance,
      // without direct membership/source inserts or bypassing role safeguards.
      const adminInvitation = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: adminCredentials.email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(adminInvitation.status()).toBe(201);
      const adminInvitationId = (await adminInvitation.json()).id as string;
      expect((await issuer.request.post(`/me/invitations/${adminInvitationId}/accept`, { headers })).status()).toBe(200);
      const invitation = await issuer.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: recipientCredentials.email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(invitation.status()).toBe(201); const invitationId = (await invitation.json()).id as string;
      await page.goto('/app/invitations');
      const accept = page.getByRole('button', { name: 'Accept invitation to Issuer authority Portal parent', exact: true });
      await expect(accept).toBeEnabled();
      await expect.poll(() => envelopes.some(e => e.resetRequired && !e.events.length)).toBe(true);
      await accept.focus();
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      let reads = 0;
      const held = new Promise<void>(resolve => { releaseRead = resolve; });
      await page.route('**/me/invitations?**', async route => { reads++; await held; await route.continue(); });
      expect((await owner.request.delete(`/organizations/${org}/members/${adminId}`, { headers })).status()).toBe(204);
      await expect.poll(() => reads, { timeout: 45_000 }).toBeGreaterThan(0);
      await expect(accept).toHaveCount(0);
      await expect(page.getByText('Issuer authority Portal parent', { exact: true })).toHaveCount(0);
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      await expect(refresh).toBeFocused();
      releaseRead!();
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible({ timeout: 45_000 });
      await page.unrouteAll({ behavior: 'wait' });
      // Stale clients cannot accept the undisclosed invitation either.
      const denied = await context.request.post(`/me/invitations/${invitationId}/accept`, { headers });
      expect(denied.status()).toBe(400);
      expect((await denied.json()).code).toBe('invalid_or_expired_invitation');
      await expect(page.getByRole('link', { name: 'Open Owner Portal', exact: true })).toHaveCount(0);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect(documents).toBe(1);
      expect(envelopes.flatMap(e => e.events)).toEqual([]);
      for (const envelope of envelopes)
        expect(Object.keys(envelope).sort()).toEqual(['cursor', 'events', 'hasMore', 'resetRequired']);
      const wire = JSON.stringify(envelopes);
      for (const privateValue of [recipientCredentials.email, adminId, org, invitationId, 'Issuer authority Portal parent'])
        expect(wire).not.toContain(privateValue);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { releaseRead?.(); await issuer.close(); await owner.close(); }
  });
}
