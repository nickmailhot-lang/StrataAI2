import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-TC-04/05/08/11/12: keyboard member review and missing removal acknowledgment at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport);
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    try {
      const ids: string[] = [];
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: `member-ui-${viewport.width}-${index}-${Date.now()}@example.test`, password: 'browser-member-ui-correct-horse', displayName: index ? 'Invited administrator' : 'Current owner' };
        const registered = await client.request.post('/auth/register', { headers, data }); expect(registered.status()).toBe(201); ids.push((await registered.json()).user.id);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Member administration' } }); expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id;
      const email = (await (await recipient.request.get('/me')).json()).email;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } }); expect(invitation.status()).toBe(201);
      expect((await recipient.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      await page.goto(`/app/${org}`);
      // Initial live reconciliation can replace the home controls; activate only
      // after current access is checked, through the keyboard locator itself.
      await expect(page.getByRole('status')).toHaveText('Current Board access checked.');
      const members = page.getByRole('link', { name: 'Organization members', exact: true });
      await members.focus(); await expect(members).toBeFocused(); await members.press('Enter');
      await expect(page).toHaveURL(new RegExp(`/app/${org}/members$`));
      const action = page.getByRole('button', { name: 'Review removal of Invited administrator' }); await expect(action).toBeVisible();
      await action.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0);
      await page.getByRole('button', { name: 'Load current members' }).focus(); await page.keyboard.press('Enter');
      await action.focus(); await page.keyboard.press('Enter'); await expect(page.getByText('Current role: Admin')).toBeVisible();
      const writes: string[] = [];
      await page.route(`**/organizations/${org}/members/${ids[1]}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push(route.request().url()); expect(new URL(route.request().url()).searchParams.get('expectedVersion')).toMatch(/^[1-9][0-9]*$/);
        expect((await route.fetch()).status()).toBe(204); await route.abort('timedout');
      });
      await page.getByRole('button', { name: 'Confirm member removal' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByText(/The removal could not be confirmed/)).toBeVisible();
      await expect(page.getByText(email, { exact: true })).toHaveCount(0);
      await page.getByRole('button', { name: 'Review current membership' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/currently no longer an internal member.*earlier removal acknowledgment was unavailable/)).toBeVisible();
      expect(writes).toHaveLength(1); await expect(page.getByText('Member removed.', { exact: true })).toHaveCount(0);
      expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
      const exact = await context.request.get(`/organizations/${org}/members/${ids[1]}`); expect(exact.status()).toBe(200); expect((await exact.json()).member).toBeNull();
      await page.getByRole('button', { name: 'Load current members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: 'Current owner (you)' })).toBeVisible(); await expect(action).toHaveCount(0);
    } finally { await recipient.close(); }
  });
}
