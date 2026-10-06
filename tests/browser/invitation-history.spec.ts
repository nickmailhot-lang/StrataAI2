import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-60-TC-07/11/12: keyboard invitation history and lost revocation acknowledgment at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const data = { email: `invitation-history-owner-${viewport.width}-${Date.now()}@example.test`, password: 'browser-invitation-history-horse', displayName: 'Invitation history owner' };
    expect((await context.request.post('/auth/register', { headers, data })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Keyboard invitation history' } }); expect(created.status()).toBe(201);
    const org = (await created.json()).organization.id;
    const email = `invitation-history-recipient-${viewport.width}-${Date.now()}@example.test`;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
      data: { email, surface: 'PORTAL', targetRole: 'OWNER' } }); expect(invitation.status()).toBe(201);
    const id = (await invitation.json()).id;
    await page.goto(`/app/${org}/members`);
    await page.getByRole('link', { name: 'Review issued invitations' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Issued invitations' })).toBeVisible();
    await expect(page.getByText('No email delivery recorded')).toBeVisible();
    const action = page.getByRole('button', { name: `Revoke invitation for ${email}` });
    await action.focus(); await page.keyboard.press('Enter'); await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Refresh invitations' })).toBeFocused();
    let writes = 0;
    await page.route(url => url.pathname === `/organizations/${org}/invitations/${id}`, async route => {
      if (route.request().method() !== 'DELETE') { await route.continue(); return; }
      writes++; expect((await route.fetch()).status()).toBe(204); await route.abort('timedout');
    });
    await action.focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Confirm revocation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Revocation could not be confirmed/)).toBeVisible();
    await expect(page.getByText('Invitation revocation confirmed.')).toHaveCount(0);
    await page.getByRole('button', { name: 'Check revocation' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Invitation revocation confirmed.')).toBeVisible(); await expect(page.getByText('Revoked', { exact: true })).toBeVisible();
    expect(writes).toBe(1); await expect(action).toHaveCount(0);
    const state = await context.request.get(`/organizations/${org}/invitations`); expect(state.status()).toBe(200);
    expect((await state.json()).items.find((row: { id: string }) => row.id === id).revokedAt).not.toBeNull();
    expect(await page.evaluate(() => sessionStorage.length + localStorage.length)).toBe(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
