import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`ARCH-02-AC-003: Portal-only deep links withhold Council navigation at ${width}px`, async ({ context, browser }) => {
    test.setTimeout(90_000);
    const portal = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    const headers = { 'X-StrataAI-Request': '1' };
    try {
      let portalUser = ''; let portalEmail = '';
      for (const [index, client] of [context, portal].entries()) {
        const data = { email: `surface-admission-${width}-${index}-${Date.now()}@example.test`, password: 'surface-admission-correct-horse', displayName: 'Admission fixture' };
        const registered = await client.request.post('/auth/register', { headers, data }); expect(registered.status()).toBe(201);
        if (index === 1) { portalUser = (await registered.json()).user.id; portalEmail = data.email; }
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Surface admission fixture' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const boardIds: string[] = [];
      for (const visibility of ['PRIVATE', 'PUBLIC']) {
        const board = await context.request.post('/boards', { headers, data: { organizationId: org, name: `${visibility} admission board`, visibility } });
        expect(board.status()).toBe(201); boardIds.push((await board.json()).id);
      }
      const grant = async (surface: string, targetRole: string) => {
        const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email: portalEmail, surface, targetRole } });
        expect(invitation.status()).toBe(201);
        expect((await portal.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      };
      await grant('PORTAL', 'OWNER');
      expect((await portal.request.get(`/organizations/${org}/surface-access?surface=PORTAL`)).status()).toBe(200);
      expect((await portal.request.get(`/organizations/${org}/surface-access?surface=INTERNAL`)).status()).toBe(404);
      expect((await portal.request.get(`/organizations/${org}/members`)).status()).toBe(404);
      expect((await portal.request.get(`/organizations/${org}/boards`)).status()).toBe(404);
      const page = await portal.newPage();
      const navigation = page.getByLabel('Internal application navigation');
      await page.goto(`/portal/${org}`);
      await expect(page.getByRole('heading', { name: 'Owner documents', exact: true })).toBeVisible(); await expect(navigation).toHaveCount(0);
      await page.goto(`/app/${org}/members`);
      await expect(page.getByText('Access to this Organization surface is unavailable.')).toBeVisible(); await expect(navigation).toHaveCount(0);
      await page.goto(`/app/${org}/boards/${boardIds[0]}`);
      await expect(page.getByText('This board or action is unavailable.')).toBeVisible(); await expect(navigation).toHaveCount(0);
      await expect(page.getByRole('heading', { name: 'PRIVATE admission board', exact: true })).toHaveCount(0);
      await page.goto(`/app/${org}/boards/${boardIds[1]}`);
      await expect(page.getByRole('heading', { name: 'PUBLIC admission board', exact: true })).toBeVisible(); await expect(navigation).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Add list', exact: true })).toHaveCount(0);
      await grant('INTERNAL', 'MEMBER');
      await page.goto(`/app/${org}`); await expect(navigation).toHaveCount(1);
      const review = await context.request.get(`/organizations/${org}/members/${portalUser}`); expect(review.status()).toBe(200);
      const version = (await review.json()).member.version;
      expect((await context.request.delete(`/organizations/${org}/members/${portalUser}?expectedVersion=${version}`, { headers })).status()).toBe(204);
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      await expect(page.getByText('Access to this Organization surface is unavailable.')).toBeVisible(); await expect(navigation).toHaveCount(0);
      expect((await portal.request.get(`/organizations/${org}/surface-access?surface=PORTAL`)).status()).toBe(200);
      await page.goto(`/portal/${org}`); await expect(page.getByRole('heading', { name: 'Owner documents', exact: true })).toBeVisible();
      expect((await portal.request.post('/auth/logout', { headers })).status()).toBe(204);
      await page.reload(); await expect(page.getByText('Access to this Organization surface is unavailable.')).toBeVisible();
      await expect(page.getByRole('heading', { name: 'Owner documents', exact: true })).toHaveCount(0);
    } finally { await portal.close(); }
  });
}
