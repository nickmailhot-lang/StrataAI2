import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

// Actual production-compiled router/AuthPage/theme with a synthetic HTTP refusal.
// This is fast UI evidence; canonical auth, tenant/Portal and persistence coverage
// continues unchanged in the nightly/exact-candidate production suite.
// PRD-02-TC-07/11/12, UXA-05/06/07: native recovery and accessible account entry.
for (const width of [390, 1280]) test(`compiled sign-in keeps native keyboard retry identity at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const attempts: { key: string | undefined; body: string | null }[] = [];
  await page.route('**/auth/login', async route => {
    const request = route.request();
    expect(request.method()).toBe('POST');
    attempts.push({ key: request.headers()['idempotency-key'], body: request.postData() });
    await route.fulfill({ status: 401, contentType: 'application/problem+json', body: JSON.stringify({ code: 'invalid_credentials' }) });
  });
  await page.goto('/login');
  await page.getByRole('textbox', { name: 'Email', exact: true }).fill('fast-ui-fixture@example.invalid');
  const password = page.getByLabel(/^Password/);
  await expect(password).toHaveAccessibleName('Password');
  await password.fill('Synthetic fixture password only');
  const submit = page.getByRole('button', { name: 'Sign in', exact: true });
  await submit.focus();
  await expect(submit).toBeFocused();
  await submit.press('Enter');
  await expect(page.getByRole('alert')).toHaveText('The email or password is incorrect.');
  await expect(submit).toBeEnabled();
  await submit.focus();
  await submit.press('Enter');
  await expect.poll(() => attempts.length).toBe(2);
  await expect(submit).toBeEnabled();
  expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
  expect(attempts[1]).toEqual(attempts[0]);
  await expect(page).toHaveURL(/\/login$/);
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  expect(errors).toEqual([]);
  await page.screenshot({ path: test.info().outputPath('sign-in-landmark.png'), fullPage: true });
});
