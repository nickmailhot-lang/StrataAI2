import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-03/60-TC-08/09/11/12: accepted invitation reconciles an active member role at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }; let restoreWorker = () => {};
    try {
      const input = { email: `role-recipient-${width}-${Date.now()}@example.test`, password: 'role-live-correct-horse', displayName: 'Invited role recipient' };
      const owner = { ...input, email: `role-owner-${width}-${Date.now()}@example.test`, displayName: 'Role Owner' };
      expect((await context.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const registration = await recipient.request.post('/auth/register', { headers, data: input }); expect(registration.status()).toBe(201);
      const actor = (await registration.json()).user.id;
      expect((await recipient.request.post('/auth/login', { headers, data: input })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Live invitation role Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id; restoreWorker = scopedBoardWorker(org);
      const accepted: Array<Record<string, unknown>> = []; const additions: Array<Record<string, unknown>> = [];
      page.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type === 2 && message.item?.organizationId === org) {
              accepted.push(...message.item.page.events.filter((row: Record<string, unknown>) => row.eventType === 'INVITATION_ACCEPTED'));
              additions.push(...message.item.page.events.filter((row: Record<string, unknown>) => row.eventType === 'ORGANIZATION_MEMBER_ADDED'));
            }
          }
        });
      });
      let documents = 0; let removals = 0;
      page.on('request', request => { if (request.isNavigationRequest()) documents++; if (request.method() === 'DELETE') removals++; });
      await page.goto(`/app/${org}/members`);
      await expect(page.getByText('Current members checked. Review a membership again before confirming removal.', { exact: true })).toBeVisible();
      const history = await context.newPage(); await history.setViewportSize({ width, height: 844 });
      await history.goto(`/app/${org}/invitations`);
      await expect(history.getByText('Current invitations checked. Review an invitation again before confirming revocation.', { exact: true })).toBeVisible();
      async function issue(role: string, issuer = context) {
        const issued = await issuer.request.post(`/organizations/${org}/invitations`, { headers, data: { email: input.email, surface: 'INTERNAL', targetRole: role } });
        expect(issued.status()).toBe(201); const id = (await issued.json()).id;
        const response = await recipient.request.post(`/me/invitations/${id}/accept`, { headers }); expect(response.status()).toBe(200);
        const ack = await response.json();
        await expect.poll(() => accepted.filter(row => row.entityId === id).length, { timeout: 30_000 }).toBe(1);
        const source = accepted.find(row => row.entityId === id)!;
        expect(source.entityType).toBe('Invitation'); expect(source.actorId).toBe(actor); expect(source.version).toBe(2);
        expect(source.boardId).toBeNull(); expect(source.metadata).toEqual({});
        return { id, ack };
      }
      await issue('MEMBER');
      await expect.poll(() => additions.filter(row => row.actorId === actor).length, { timeout: 30_000 }).toBe(1);
      const article = page.getByRole('heading', { name: input.displayName, exact: true }).locator('..');
      await expect(article.getByText('Role: Member', { exact: true })).toBeVisible();
      const originalReview = await context.request.get(`/organizations/${org}/members/${actor}`); expect(originalReview.status()).toBe(200);
      const originalMembership = (await originalReview.json()).member; expect(originalMembership.version).toBe(1);
      await article.getByRole('button', { name: `Review removal of ${input.displayName}`, exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      const grant = await issue('ADMIN');
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(article.getByText('Role: Admin', { exact: true })).toBeVisible();
      await expect(history.getByText('Accepted', { exact: true })).toHaveCount(2);
      expect(additions.filter(row => row.actorId === actor)).toHaveLength(1);
      const current = await context.request.get(`/organizations/${org}/members/${actor}`); expect(current.status()).toBe(200);
      const membership = (await current.json()).member;
      expect(membership.membershipId).toBe(originalMembership.membershipId); expect(membership.version).toBe(2); expect(membership.role).toBe(1);
      const retry = await recipient.request.post(`/me/invitations/${grant.id}/accept`, { headers });
      expect(retry.status()).toBe(200); expect(await retry.json()).toEqual(grant.ack);
      expect((await (await context.request.get(`/organizations/${org}/members/${actor}`)).json()).member).toEqual(membership);
      // Authority is checked before the Admin's valid self-downgrade.
      const downgrade = await issue('MEMBER', recipient);
      await expect(article.getByText('Role: Member', { exact: true })).toBeVisible();
      await expect(history.getByText('Accepted', { exact: true })).toHaveCount(3);
      const afterDowngrade = await context.request.get(`/organizations/${org}/members/${actor}`); expect(afterDowngrade.status()).toBe(200);
      const downgraded = (await afterDowngrade.json()).member;
      expect(downgraded.membershipId).toBe(originalMembership.membershipId); expect(downgraded.version).toBe(3); expect(downgraded.role).toBe(2);
      expect(additions.filter(row => row.actorId === actor)).toHaveLength(1);
      const repeated = await recipient.request.post(`/me/invitations/${downgrade.id}/accept`, { headers });
      // Current issuer authority is required for retry; the completed command
      // remains committed even though this self-issued retry is now unavailable.
      expect(repeated.status()).toBe(400);
      expect((await (await context.request.get(`/organizations/${org}/members/${actor}`)).json()).member).toEqual(downgraded);
      expect(documents).toBe(1); expect(removals).toBe(0);
      for (const client of [page, history]) expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { restoreWorker(); await recipient.close(); }
  });
}
