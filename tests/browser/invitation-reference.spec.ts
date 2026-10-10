import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) for (const action of ['discovery', 'acceptance']) {
  test(`PRD-03 / NFR-FR-010: invitation ${action} actual refusal reference at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const issuer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' }, suffix = `${width}-${action}-${Date.now()}`;
    try {
      await registerNotificationAccount(issuer.request, { email: `reference-issuer-${suffix}@example.test`,
        password: 'invitation-reference-correct-horse', displayName: 'Invitation reference issuer' });
      const email = `reference-recipient-${suffix}@example.test`;
      await registerNotificationAccount(context.request, { email,
        password: 'invitation-reference-correct-horse', displayName: 'Invitation reference recipient' });
      const created = await issuer.request.post('/organizations', { headers, data: { name: 'Invitation reference scope' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const root = `/organizations/${org}/invitations`;
      const issued = await issuer.request.post(root, { headers, data: { email, surface: 'PORTAL', targetRole: 'OWNER' } });
      expect(issued.status()).toBe(201); const id = (await issued.json()).id;
      const before = await issuer.request.get(root); expect(before.status()).toBe(200); const canonical = await before.json();
      let dispatches = 0, reference: string | undefined;
      await page.route(url => url.pathname === (action === 'discovery' ? '/me/invitations' : `/me/invitations/${id}/accept`), async route => {
        if (++dispatches !== 1) { await route.continue(); return; }
        const target = new URL(route.request().url());
        if (action === 'discovery') target.searchParams.set('after', 'invalid-cursor');
        else target.pathname = '/me/invitations/11111111-1111-4111-8111-111111111111/accept';
        const response = await route.fetch({ url: target.toString(),
          headers: { ...route.request().headers(), 'X-Correlation-ID': 'i'.repeat(64) } });
        expect(response.status()).toBe(400); reference = response.headers()['x-correlation-id'];
        expect(reference).toBe('i'.repeat(64)); await route.fulfill({ response });
      });
      await page.goto('/app/invitations');
      if (action === 'acceptance') await pressAdmittedAction(page.getByRole('button', { name: 'Accept invitation to Invitation reference scope', exact: true }));
      await expect(page.getByText(action === 'discovery' ? 'Unable to load invitations. Please refresh and try again.'
        : 'This invitation is no longer available to your account. Refresh to check current invitations.', { exact: true })).toBeVisible();
      expect(reference).toBeDefined(); expect(dispatches).toBe(1);
      const unchanged = await issuer.request.get(root); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(canonical);
      await expect(page.getByText(`Reference: ${reference}`, { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const refresh = page.getByRole('button', { name: 'Refresh invitations', exact: true });
      await expect(refresh).toHaveAttribute('aria-disabled', 'false'); await pressAdmittedAction(refresh);
      await expect(page.getByRole('button', { name: 'Accept invitation to Invitation reference scope', exact: true })).toBeVisible();
      await expect(page.getByText(/^Reference:/)).toHaveCount(0);
      const recovered = await issuer.request.get(root); expect(recovered.status()).toBe(200); expect(await recovered.json()).toEqual(canonical);
    } finally { await issuer.close(); }
  });
}
