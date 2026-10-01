import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-05: keyboard member consent, conflict and removal recovery at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport);
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const email = `board-member-recipient-${viewport.width}-${Date.now()}@example.test`;
    try {
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: index ? email : `board-member-owner-${viewport.width}-${Date.now()}@example.test`,
          password: 'browser-board-member-correct-horse', displayName: index ? 'Jordan participant' : 'Board owner' };
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const organization = await context.request.post('/organizations', { headers, data: { name: 'Member consent' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const issued = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(issued.status()).toBe(201); const invitation = (await issued.json()).id;
      expect((await recipient.request.post(`/me/invitations/${invitation}/accept`, { headers })).status()).toBe(200);
      const user = (await (await recipient.request.get('/me')).json()).id;
      const membershipBefore = await context.request.get(`/organizations/${org}/members/${user}`); expect(membershipBefore.status()).toBe(200);
      const organizationMember = (await membershipBefore.json()).member;
      const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Member review', visibility: 'PRIVATE' } });
      expect(created.status()).toBe(201); const board = (await created.json()).id;
      const grant = await context.request.patch(`/boards/${board}/members/${user}`, { headers, data: { role: 'MEMBER' } });
      expect(grant.status()).toBe(200); const version = (await grant.json()).version;
      expect((await recipient.request.get(`/boards/${board}/members`)).status()).toBe(404);
      await page.goto(`/app/${org}/boards/${board}`);
      await page.getByRole('link', { name: 'Board members', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Live member updates connected.')).toBeVisible();
      await expect(page.getByRole('progressbar', { name: 'Loading Board members' })).toHaveCount(0);
      await page.getByRole('button', { name: 'Make administrator: Jordan participant' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Check current members' })).toBeFocused();
      await page.getByRole('button', { name: 'Make administrator: Jordan participant' }).focus(); await page.keyboard.press('Enter');
      let currentVersion = version;
      let roles = 0;
      await page.route(`**/boards/${board}/members/${user}`, async route => {
        roles++;
        // Compete after consent has submitted, before the server evaluates it.
        // Earlier live events correctly cancel an open, stale review.
        if (roles === 1) {
          const competing = await context.request.patch(`/boards/${board}/members/${user}`, { headers, data: { role: 'ADMIN' } });
          expect(competing.status()).toBe(200); currentVersion = (await competing.json()).version;
        }
        expect(route.request().method()).toBe('PATCH');
        expect(route.request().headers()['if-match']).toBe(`"${roles === 1 ? version : currentVersion}"`);
        expect(route.request().headers()['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
        expect(route.request().postDataJSON()).toEqual({ role: roles === 1 ? 'ADMIN' : 'MEMBER' });
        const response = await route.fetch(); expect(response.status()).toBe(roles === 1 ? 409 : 200);
        if (roles === 1) await route.fulfill({ response }); else await route.abort('timedout');
      });
      await page.getByRole('button', { name: 'Confirm member change' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/This membership changed|Board membership changed/).first()).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current members' })).toBeEnabled();
      await page.getByRole('button', { name: 'Check current members' }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Make member: Jordan participant' }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Confirm member change' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/The member change could not be confirmed/)).toBeVisible();
      await page.getByRole('button', { name: 'Check current members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Make administrator: Jordan participant' })).toBeVisible(); expect(roles).toBe(2);
      await page.unroute(`**/boards/${board}/members/${user}`);
      let removals = 0;
      await page.route(`**/boards/${board}/members/${user}`, async route => {
        removals++; expect(route.request().method()).toBe('DELETE');
        expect(route.request().headers()['if-match']).toBe(`"${currentVersion + 1}"`);
        expect((await route.fetch()).status()).toBe(204); await route.abort('timedout');
      });
      await page.getByRole('button', { name: 'Remove from Board: Jordan participant' }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Confirm member change' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/The member change could not be confirmed/)).toBeVisible();
      await page.getByRole('button', { name: 'Check current members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: 'Board owner', exact: true })).toBeVisible();
      await expect(page.getByText(email, { exact: true })).toHaveCount(0); expect(removals).toBe(1);
      await expect(page.getByText('Member change acknowledged. Review the current directory.')).toHaveCount(0);
      expect((await recipient.request.get(`/boards/${board}`)).status()).toBe(404);
      expect((await recipient.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Denied edit' } })).status()).toBe(404);
      const membershipAfter = await context.request.get(`/organizations/${org}/members/${user}`); expect(membershipAfter.status()).toBe(200);
      expect((await membershipAfter.json()).member).toEqual(organizationMember);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { await recipient.close(); }
  });
}
