import { expect, test } from './releaseTest';
import { pressAdmittedAction } from './keyboardAdmission';

// FOUND-FR-008 / PRD-01-TC-12: admitted primary screens must retain complete
// long user/Organization labels without horizontal document scrolling.
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-01/02/03: primary-screen reflow with long canonical names at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 844 });
    const reflow = async () => {
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    };
    await page.goto('/login'); await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeEnabled(); await reflow();
    await page.goto('/forgot-password'); await expect(page.getByRole('form', { name: 'Request password reset' })).toBeVisible(); await reflow();
    await page.goto('/verify-email#token=reflow-fixture-proof'); await expect(page.getByRole('button', { name: 'Verify email', exact: true })).toBeEnabled(); await reflow();
    await page.goto('/reset-password#token=reflow-fixture-proof'); await expect(page.getByLabel(/^Confirm new password/)).toBeVisible(); await reflow();

    const headers = { 'X-StrataAI-Request': '1' };
    const displayName = 'W'.repeat(100), organizationName = 'O'.repeat(100);
    const credentials = { email: `reflow-${width}-${Date.now()}-${'a'.repeat(30)}@example.test`, password: 'reflow-correct-horse-battery', displayName };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: organizationName } });
    expect(created.status()).toBe(201); const organizationId = (await created.json()).organization.id;
    await page.goto('/app'); await expect(page.getByRole('heading', { name: 'Your organizations', exact: true })).toBeVisible();
    await expect(page.getByText(organizationName, { exact: true })).toBeVisible(); await reflow();
    await page.goto('/app/profile'); await expect(page.getByLabel(/^Display name/)).toHaveValue(displayName);
    await expect(page.getByRole('heading', { name: displayName, exact: true })).toBeVisible(); await reflow();
    await pressAdmittedAction(page.getByRole('button', { name: 'Deactivate account', exact: true }));
    await expect(page.getByRole('dialog', { name: 'Deactivate your account?' })).toBeVisible(); await reflow();
    await pressAdmittedAction(page.getByRole('button', { name: 'Keep account active', exact: true }));
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.goto(`/app/${organizationId}`); await expect(page.getByRole('heading', { name: organizationName, exact: true })).toBeVisible(); await reflow();
    await page.goto(`/app/${organizationId}/settings`); await expect(page.getByLabel(/^Organization name/)).toHaveValue(organizationName); await reflow();
    await page.goto(`/app/${organizationId}/notifications`); await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible(); await reflow();
    await page.goto(`/app/${organizationId}/search`); await expect(page.getByRole('heading', { name: 'Search Cards', exact: true })).toBeVisible(); await reflow();
    await page.goto('/app/invitations'); await expect(page.getByRole('heading', { name: 'Your invitations', exact: true })).toBeVisible(); await reflow();
    const current = await context.request.get('/me'); expect(current.status()).toBe(200);
    expect((await current.json()).displayName).toBe(displayName);
  });
}
