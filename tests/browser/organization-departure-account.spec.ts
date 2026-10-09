import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const after of [false, true]) {
  test(`PRD-03-TC-05/11/12: departure account replacement ${after ? 'after' : 'before'} submission at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${after}-${width}-${Date.now()}`;
    const ownerAccount = { email: `departure-issuer-${suffix}@example.test`, password: 'departure-account-correct-horse', displayName: 'Departure issuer' };
    const memberAccount = { ...ownerAccount, email: `departure-reviewed-${suffix}@example.test`, displayName: 'Reviewed member' };
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      await registerVerifiedAccountFixture(issuer.request, ownerAccount);
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Reviewed departure scope' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const registered = await registerVerifiedAccountFixture(context.request, memberAccount);
      const actor = registered.user.id;
      const invitation = await issuer.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: memberAccount.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const beforeReply = await issuer.request.get(`/organizations/${org}/members`); expect(beforeReply.status()).toBe(200);
      const before = (await beforeReply.json()).items.find((row: { userId: string }) => row.userId === actor); expect(before).toBeDefined();
      let writes = 0, documents = 0;
      await page.route(`**/organizations/${org}/leave`, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        writes++; expect(route.request().postDataJSON()).toEqual({ expectedActorId: actor });
        const response = await route.fetch(); expect(response.status()).toBe(204);
        // The old Member session remains valid. Replacing this browser cookie
        // must still withdraw its reviewed admission and private receipt.
        if (after) expect((await context.request.post('/auth/login', { headers, data: ownerAccount })).status()).toBe(200);
        await route.fulfill({ response });
      });
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.goto(`/app/${org}/leave`);
      await page.getByRole('button', { name: 'Review departure', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Cancel departure' })).toBeFocused();
      if (!after) expect((await context.request.post('/auth/login', { headers, data: ownerAccount })).status()).toBe(200);
      await page.getByRole('button', { name: 'Confirm departure', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page).toHaveURL(/\/login(?:\?|$)/);
      await expect(page.getByText('Reviewed departure scope', { exact: true })).toHaveCount(0);
      await expect(page.getByText('You left the Organization.')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Retry original departure' })).toHaveCount(0);
      expect(writes).toBe(after ? 1 : 0); expect(documents).toBe(1);
      const directory = await issuer.request.get(`/organizations/${org}/members`); expect(directory.status()).toBe(200);
      const membership = (await directory.json()).items.find((row: { userId: string }) => row.userId === actor);
      if (after) expect(membership).toBeUndefined(); else expect(membership).toEqual(before);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await issuer.close(); }
  });
}
