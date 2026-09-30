import { expect, test } from '@playwright/test';

test('PRD-02-TC-01/07/11/12: mobile verification and recovery consume Worker-delivered links using keyboard', async ({ page, request }) => {
  test.skip(process.env.STRATAAI_E2E_IDENTITY_MAIL !== '1', 'Requires isolated provider and verified-email fixture.');
  test.setTimeout(120_000);
  await page.setViewportSize({ width: 390, height: 844 });
  const email = `mobile-identity-${Date.now()}@example.test`;
  const password = 'mobile-correct-horse-battery';
  async function tokenFor(subject: string) {
    let token = '';
    await expect.poll(async () => {
      const response = await request.get('http://localhost:19090/messages');
      const entries = await response.json() as { payload: { to: string[]; subject: string; text: string }; attempts: number }[];
      const entry = entries.find(item => item.payload.to.includes(email) && item.payload.subject === subject && item.attempts >= 2);
      token = entry?.payload.text.match(/#token=([A-Za-z0-9_-]+)/)?.[1] ?? '';
      return Boolean(token);
    }, { timeout: 45_000, intervals: [500, 1000] }).toBe(true);
    return token;
  }
  await page.goto('/login');
  await page.getByRole('tab', { name: 'Register', exact: true }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Display name/).fill('Mobile identity');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('verification email');
  const verification = await tokenFor('Verify your StrataAI2 email');
  await page.goto(`/verify-email#token=${verification}`);
  await expect(page).toHaveURL(/\/verify-email$/);
  await page.getByRole('button', { name: 'Verify email', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('Email verified.');
  await page.getByRole('link', { name: 'Back to sign in' }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill(password);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/app$/);
  await page.goto('/forgot-password');
  await page.getByLabel(/^Email/).fill(email);
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('Request received.');
  const reset = await tokenFor('Reset your StrataAI2 password');
  await page.goto(`/reset-password#token=${reset}`);
  await expect(page).toHaveURL(/\/reset-password$/);
  await page.getByLabel(/^New password/).fill('mobile-new-correct-horse');
  await page.getByLabel(/^Confirm new password/).fill('mobile-new-correct-horse');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('status')).toContainText('Password reset.');
  await page.getByRole('link', { name: 'Back to sign in' }).focus();
  await page.keyboard.press('Enter');
  await page.getByLabel(/^Email/).fill(email);
  await page.getByLabel(/^Password/).fill('mobile-new-correct-horse');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/app$/);
});
