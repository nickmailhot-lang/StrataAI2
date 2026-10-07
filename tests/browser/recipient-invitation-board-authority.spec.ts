import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-04/60-TC-05/08/10/11/12: actual Board authority clears an invitation before Board admission at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000);
    await page.setViewportSize({ width, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const suffix = `${width}-${Date.now()}`;
    const base = { password: 'board-authority-correct-horse', displayName: 'Board authority account' };
    const accounts = [owner, issuer, context].map((client, index) => ({ client,
      data: { ...base, email: `board-authority-${suffix}-${index}@example.test` } }));
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
      for (const { client, data } of accounts) {
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe(process.env.STRATAAI_E2E_RUNTIME_MODE ?? 'production');
      const issuerId = (await (await issuer.request.get('/me')).json()).id as string;
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Board authority parent' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
      const membership = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: accounts[1].data.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(membership.status()).toBe(201);
      expect((await issuer.request.post(`/me/invitations/${(await membership.json()).id}/accept`, { headers })).status()).toBe(200);
      // Board administrators may invite current Organization members only.
      // Establish that membership through ordinary onboarding; no Board grant exists.
      const recipientMembership = await owner.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: accounts[2].data.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(recipientMembership.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await recipientMembership.json()).id}/accept`, { headers })).status()).toBe(200);
      const createdBoard = await owner.request.post('/boards', { headers, data: { organizationId: org, name: 'Board authority before' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id as string;
      expect((await owner.request.patch(`/boards/${board}/members/${issuerId}`, { headers, data: { role: 'ADMIN' } })).status()).toBe(200);
      const invitation = await issuer.request.post(`/boards/${board}/invitations`, { headers,
        data: { email: accounts[2].data.email, role: 'MEMBER' } });
      expect(invitation.status()).toBe(201); const invitationId = (await invitation.json()).id as string;
      await page.goto('/app/invitations');
      const before = page.getByRole('button', { name: 'Accept invitation to Board authority parent, Board Board authority before, member', exact: true });
      await expect(before).toBeEnabled();
      await expect.poll(() => envelopes.some(e => e.resetRequired && !e.events.length)).toBe(true);
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(404);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(200);
      await before.focus();
      // Delay the real protected read to observe withdrawal before its response.
      // Source events, memberships, HTTP responses and socket frames remain real.
      let reads = 0;
      const held = new Promise<void>(resolve => { releaseRead = resolve; });
      await page.route('**/me/invitations?**', async route => { reads++; await held; await route.continue(); });
      expect((await owner.request.patch(`/boards/${board}`, { headers, data: { name: 'Board authority after', version: 1 } })).status()).toBe(200);
      await expect.poll(() => reads, { timeout: 45_000 }).toBeGreaterThan(0);
      await expect(before).toHaveCount(0);
      await expect(page.getByText('Board authority before', { exact: true })).toHaveCount(0);
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      await expect(refresh).toBeFocused(); releaseRead!();
      const after = page.getByRole('button', { name: 'Accept invitation to Board authority parent, Board Board authority after, member', exact: true });
      await expect(after).toBeEnabled({ timeout: 45_000 });
      await page.unrouteAll({ behavior: 'wait' });
      expect((await owner.request.post(`/boards/${board}/archive`, { headers, data: { version: 2 } })).status()).toBe(200);
      await expect(after).toHaveCount(0, { timeout: 45_000 });
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      expect((await context.request.post(`/me/invitations/${invitationId}/accept`, { headers })).status()).toBe(400);
      expect((await owner.request.post(`/boards/${board}/restore`, { headers, data: { version: 3 } })).status()).toBe(200);
      await expect(after).toBeEnabled({ timeout: 45_000 });
      await after.focus();
      expect((await owner.request.patch(`/boards/${board}/members/${issuerId}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      await expect(after).toHaveCount(0, { timeout: 45_000 });
      await expect(page.getByText('Board authority after', { exact: true })).toHaveCount(0);
      await expect(page.getByText('No pending invitations on this page.', { exact: true })).toBeVisible();
      await expect(refresh).toBeFocused();
      const denied = await context.request.post(`/me/invitations/${invitationId}/accept`, { headers });
      expect(denied.status()).toBe(400); expect((await denied.json()).code).toBe('invalid_or_expired_invitation');
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(404);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(200);
      expect(documents).toBe(1); expect(envelopes.flatMap(e => e.events)).toEqual([]);
      for (const envelope of envelopes)
        expect(Object.keys(envelope).sort()).toEqual(['cursor', 'events', 'hasMore', 'resetRequired']);
      const wire = JSON.stringify(envelopes);
      for (const privateValue of [accounts[2].data.email, issuerId, org, board, invitationId, 'Board authority before', 'Board authority after'])
        expect(wire).not.toContain(privateValue);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { releaseRead?.(); await issuer.close(); await owner.close(); }
  });
}
