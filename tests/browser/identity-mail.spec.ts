import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
test(`PRD-02-TC-01/07/11/12: verification and recovery consume Worker-delivered links using keyboard at ${viewport.width}px`, async ({ page, request }) => {
  test.skip(process.env.STRATAAI_E2E_IDENTITY_MAIL !== '1', 'Requires isolated provider and verified-email fixture.');
  test.setTimeout(120_000);
  await page.setViewportSize(viewport);
  async function accessible() {
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    expect(results.violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  }
  const email = `mobile-identity-${Date.now()}@example.test`;
  const password = 'mobile-correct-horse-battery';
  const verificationAttempts: { key: string; body: string }[] = [];
  const resetAttempts: { key: string; body: string }[] = [];
  for (const [path, attempts] of [['/auth/verify-email', verificationAttempts], ['/auth/password/reset', resetAttempts]] as const) {
    await page.route(`**${path}`, async route => {
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const committed = await route.fetch();
      expect(committed.status()).toBe(200);
      if (attempts.length === 1) await route.abort('failed');
      else await route.fulfill({ response: committed });
    });
  }
  async function tokenFor(subject: string) {
    let token = '';
    await expect.poll(async () => {
      const response = await request.get(`${process.env.STRATAAI_E2E_IDENTITY_PROVIDER_URL ?? 'http://localhost:19090'}/messages`);
      const entries = await response.json() as { payload: { to: string[]; subject: string; text: string }; attempts: number }[];
      const entry = entries.find(item => item.payload.to.includes(email) && item.payload.subject === subject && item.attempts >= 2);
      token = entry?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
      return Boolean(token);
    }, { timeout: 45_000, intervals: [500, 1000] }).toBe(true);
    return token;
  }
  await page.goto('/login');
  await accessible();
  await page.getByRole('tab', { name: 'Register', exact: true }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Display name/).fill('Mobile identity');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('verification email');
  await accessible();
  const verification = await tokenFor('Verify your StrataAI2 email');
  await page.goto(`/verify-email#token=${verification}`);
  await expect(page).toHaveURL(/\/verify-email$/);
  await accessible();
  await page.getByRole('button', { name: 'Verify email', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('alert')).toContainText('could not be confirmed');
  await expect(page.getByRole('status')).toHaveCount(0);
  await accessible();
  await page.getByRole('button', { name: 'Verify email', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('Email verified.');
  await accessible();
  expect(verificationAttempts).toHaveLength(2);
  expect(verificationAttempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
  expect(verificationAttempts[1]).toEqual(verificationAttempts[0]);
  await page.getByRole('link', { name: 'Back to sign in' }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/app$/);
  await page.goto('/forgot-password');
  await accessible();
  await page.getByLabel(/^Email/).fill(email);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('Request received.');
  await accessible();
  const reset = await tokenFor('Reset your StrataAI2 password');
  await page.goto(`/reset-password#token=${reset}`);
  await expect(page).toHaveURL(/\/reset-password$/);
  await accessible();
  await page.getByLabel(/^New password/).fill('mobile-new-correct-horse');
  await page.getByLabel(/^Confirm new password/).fill('mobile-new-correct-horse');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('alert')).toContainText('could not be confirmed');
  await expect(page.getByRole('status')).toHaveCount(0);
  await expect(page.getByLabel(/^New password/)).toHaveValue('mobile-new-correct-horse');
  await accessible();
  await page.getByLabel(/^Confirm new password/).press('Enter');
  await expect(page.getByRole('status')).toContainText('Password reset.');
  await accessible();
  expect(resetAttempts).toHaveLength(2);
  expect(resetAttempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
  expect(resetAttempts[1]).toEqual(resetAttempts[0]);
  await page.getByRole('link', { name: 'Back to sign in' }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill('mobile-new-correct-horse');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/app$/);
});
}
