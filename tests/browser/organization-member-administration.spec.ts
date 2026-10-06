import AxeBuilder from '@axe-core/playwright';
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
      if ((await (await context.request.get('/api/runtime')).json()).mode === 'production')
        await expect(page.getByText('Current members checked. Review a membership again before confirming removal.', { exact: true })).toBeVisible();
      const action = page.getByRole('button', { name: 'Review removal of Invited administrator' }); await expect(action).toBeVisible();
      await action.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0);
      await page.getByRole('button', { name: 'Load current members' }).focus(); await page.keyboard.press('Enter');
      await action.focus(); await page.keyboard.press('Enter'); await expect(page.getByText('Current role: Admin')).toBeVisible();
      const writes: { url: string; key: string | undefined }[] = [];
      await page.route(`**/organizations/${org}/members/${ids[1]}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
        expect(new URL(route.request().url()).searchParams.get('expectedActorId')).toBe(ids[0]); expect(new URL(route.request().url()).searchParams.get('expectedVersion')).toMatch(/^[1-9][0-9]*$/);
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await page.getByRole('button', { name: 'Confirm member removal' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('dialog')).toHaveCount(0); await expect(page.getByText(/The removal could not be confirmed/)).toBeVisible();
      await expect(page.getByText(email, { exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Review current membership' })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Load current members' })).toBeDisabled();
      expect(writes).toHaveLength(1);
      expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
      const exact = await context.request.get(`/organizations/${org}/members/${ids[1]}`); expect(exact.status()).toBe(200); expect((await exact.json()).member).toBeNull();
      const rejoin = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(rejoin.status()).toBe(201);
      expect((await recipient.request.post(`/me/invitations/${(await rejoin.json()).id}/accept`, { headers })).status()).toBe(200);
      const restoredResponse = await context.request.get(`/organizations/${org}/members/${ids[1]}`); expect(restoredResponse.status()).toBe(200);
      const restored = (await restoredResponse.json()).member; expect(restored.userId).toBe(ids[1]);
      const retry = page.getByRole('button', { name: 'Retry original removal' }); await expect(retry).toBeEnabled();
      await retry.focus(); await retry.press('Enter');
      await expect(page.getByText('Original removal acknowledged. Review current membership to check later access.', { exact: true })).toBeVisible();
      await expect(page.getByRole('status')).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      await expect(page.getByText('Member removed.', { exact: true })).toHaveCount(0);
      const preservedResponse = await context.request.get(`/organizations/${org}/members/${ids[1]}`); expect(preservedResponse.status()).toBe(200);
      expect((await preservedResponse.json()).member).toEqual(restored);
      expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(200);
      const currentReview = page.getByRole('button', { name: 'Review current membership' }); await expect(currentReview).toBeEnabled();
      await currentReview.focus(); await currentReview.press('Enter');
      await expect(page.getByText('Current role: Admin')).toBeVisible();
      await expect(page.getByRole('button', { name: 'Cancel removal' })).toBeFocused();
      await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0);
      expect(writes).toHaveLength(2);
      await page.getByRole('button', { name: 'Load current members' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: 'Current owner (you)' })).toBeVisible(); await expect(action).toBeVisible();
    } finally { await recipient.close(); }
  });
}
