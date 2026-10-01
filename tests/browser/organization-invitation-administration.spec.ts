import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03/60: keyboard invitation creation survives reload and separates Portal grants at ${viewport.width}px`, async ({ page, context, browser }) => {
    await page.setViewportSize(viewport);
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    try {
      const email = `invitation-ui-recipient-${viewport.width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: index ? email : `invitation-ui-owner-${viewport.width}-${Date.now()}@example.test`, password: 'browser-invitation-ui-correct-horse', displayName: 'Invitation administrator' };
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const created = await context.request.post('/organizations', { headers, data: { name: 'Keyboard invitation administration' } }); expect(created.status()).toBe(201);
      const org = (await created.json()).organization.id;
      await page.goto(`/app/${org}/members`);
      await page.getByRole('link', { name: 'Create Organization invitation' }).focus(); await page.keyboard.press('Enter');
      await page.getByLabel(/^Invitation email/).fill(email);
      await page.getByRole('combobox', { name: 'Invitation role' }).focus(); await page.keyboard.press('ArrowDown');
      await page.getByRole('option', { name: 'Admin', exact: true }).focus(); await page.keyboard.press('Enter');
      const writes: { key: string | undefined; input: unknown }[] = [];
      await page.route(`**/organizations/${org}/invitations`, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], input: route.request().postDataJSON() });
        if (writes.length === 1) { expect((await route.fetch()).status()).toBe(201); await route.abort('timedout'); }
        else await route.continue();
      });
      await page.getByRole('button', { name: 'Create invitation', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/invitation could not be confirmed/)).toBeVisible(); await expect(page.getByLabel(/^Invitation email/)).toBeDisabled();
      await page.reload(); await expect(page.getByText(/prior invitation request is awaiting acknowledgment/)).toBeVisible();
      await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
      await page.getByRole('button', { name: 'Retry same invitation' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible(); await expect(page.getByText(/Email delivery is not confirmed here/)).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[0]).toEqual(writes[1]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(writes[0].input).toEqual({ email, surface: 'INTERNAL', targetRole: 'ADMIN' });
      const pending = await recipient.request.get('/me/invitations'); expect(pending.status()).toBe(200);
      const invitations = (await pending.json()).items.filter((item: { organizationId: string }) => item.organizationId === org); expect(invitations).toHaveLength(1);
      expect((await recipient.request.post(`/me/invitations/${invitations[0].id}/accept`, { headers })).status()).toBe(200);
      const recipientId = (await (await recipient.request.get('/me')).json()).id;
      const membership = await recipient.request.get(`/organizations/${org}/members/${recipientId}`); expect(membership.status()).toBe(200);
      const before = (await membership.json()).member;
      await page.getByRole('button', { name: 'Create another invitation' }).focus(); await page.keyboard.press('Enter');
      await page.getByLabel(/^Invitation email/).fill(email);
      await page.getByRole('combobox', { name: 'Access surface' }).focus(); await page.keyboard.press('ArrowDown');
      await page.getByRole('option', { name: 'Owner Portal', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('combobox', { name: 'Invitation role' })).toHaveText('Owner');
      await page.getByRole('button', { name: 'Create invitation', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible(); expect(writes).toHaveLength(3);
      expect(writes[2].key).not.toBe(writes[0].key); expect(writes[2].input).toEqual({ email, surface: 'PORTAL', targetRole: 'OWNER' });
      const portalPending = await recipient.request.get('/me/invitations'); expect(portalPending.status()).toBe(200);
      const portalInvite = (await portalPending.json()).items.find((item: { organizationId: string; surface: string }) => item.organizationId === org && item.surface === 'PORTAL'); expect(portalInvite).toBeDefined();
      expect((await recipient.request.post(`/me/invitations/${portalInvite.id}/accept`, { headers })).status()).toBe(200);
      const after = await recipient.request.get(`/organizations/${org}/members/${recipientId}`); expect(after.status()).toBe(200);
      expect((await after.json()).member).toEqual(before);
    } finally { await recipient.close(); }
  });
}
