import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03/60-TC-05/08/09/11/12: nonmember Portal recipient discovers, revokes and recovers actual live invitations at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${width}-${Date.now()}`;
    const owner = { email: `recipient-live-issuer-${suffix}@example.test`, password: 'recipient-live-correct-horse', displayName: 'Live issuer' };
    const recipient = { ...owner, email: `recipient-live-${suffix}@example.test`, displayName: 'Live recipient' };
    let documents = 0;
    const envelopes: { cursor: string; resetRequired: boolean; hasMore: boolean; events: { eventId: string; eventType: string; sequence: string; createdAt: string }[] }[] = [];
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
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const organization = await issuer.request.post('/organizations', { headers, data: { name: 'Live Portal scope' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const root = `/organizations/${org}/invitations`;
      async function create() {
        const response = await issuer.request.post(root, { headers, data: { email: recipient.email, surface: 'PORTAL', targetRole: 'OWNER' } });
        expect(response.status()).toBe(201); return (await response.json()).id as string;
      }
      await page.goto('/app/invitations'); await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      await expect.poll(() => envelopes.some(p => p.resetRequired && !p.events.length)).toBe(true);
      const refresh = page.getByRole('button', { name: 'Refresh invitations' }); await refresh.focus();
      const first = await create(); const accept = page.getByRole('button', { name: 'Accept invitation to Live Portal scope' });
      await expect(accept).toBeEnabled({ timeout: 15_000 }); await expect(refresh).toBeFocused();
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      expect((await issuer.request.delete(`${root}/${first}`, { headers })).status()).toBe(204);
      await expect(accept).toHaveCount(0); await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      await context.setOffline(true);
      await expect(page.getByRole('status')).toContainText('interrupted', { timeout: 15_000 });
      const missed = await create(); await context.setOffline(false);
      await expect(accept).toBeEnabled({ timeout: 45_000 }); await expect(refresh).toBeFocused();
      await expect.poll(() => envelopes.flatMap(p => p.events).length).toBe(3);
      const events = envelopes.flatMap(p => p.events);
      expect(events.map(e => e.eventType)).toEqual(['INVITATION_CREATED', 'INVITATION_REVOKED', 'INVITATION_CREATED']);
      expect(events.map(e => e.sequence)).toEqual(['1', '2', '3']); expect(new Set(events.map(e => e.eventId)).size).toBe(3);
      for (const envelope of envelopes) {
        expect(Object.keys(envelope).sort()).toEqual(['cursor', 'events', 'hasMore', 'resetRequired']);
        for (const event of envelope.events) expect(Object.keys(event).sort()).toEqual(['createdAt', 'eventId', 'eventType', 'sequence']);
      }
      const wire = JSON.stringify(envelopes); expect(wire).not.toContain(recipient.email); expect(wire).not.toContain(org);
      expect(wire).not.toContain(first); expect(wire).not.toContain(missed);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404); expect(documents).toBe(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
      await expect(page).toHaveURL(/\/login(?:\?|$)/, { timeout: 15_000 });
      await expect(page.getByText('Live Portal scope', { exact: true })).toHaveCount(0);
    } finally { await context.setOffline(false); await issuer.close(); }
  });
}
