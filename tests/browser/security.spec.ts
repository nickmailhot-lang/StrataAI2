import { expect, test } from './releaseTest';

test('PRD-24-TC-03/04: release CSP blocks script injection and headers protect errors', async ({ page, request }) => {
  test.skip(process.env.STRATAAI_E2E_RELEASE_HEADERS !== '1', 'Requires the exact Nginx release image.');
  const responses = [
    await request.get('/login'),
    await request.get('/me'),
    await request.post('/auth/logout'),
    await request.get('/api/not-a-route'),
  ];
  expect(responses.map(response => response.status())).toEqual([200, 401, 403, 404]);
  for (const response of responses) {
    const headers = response.headers();
    expect(headers['content-security-policy']).toContain("script-src 'self'");
    expect(headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(headers['content-security-policy']).not.toContain("script-src 'self' 'unsafe-inline'");
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options']).toBe('DENY');
    expect(headers['referrer-policy']).toBe('no-referrer');
    expect(headers['permissions-policy']).toContain('camera=()');
  }
  await page.goto('/login');
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible();
  await page.evaluate(() => {
    document.body.dataset.cspViolations = '0';
    document.addEventListener('securitypolicyviolation', event => {
      if (event.effectiveDirective === 'script-src-elem') {
        document.body.dataset.cspViolations = String(Number(document.body.dataset.cspViolations) + 1);
      }
    });
    const script = document.createElement('script');
    script.textContent = "document.body.dataset.injectedScript = 'ran'";
    document.body.append(script);
    const external = document.createElement('script');
    external.src = 'https://attacker.example.invalid/payload.js';
    document.body.append(external);
  });
  await expect(page.locator('body')).toHaveAttribute('data-csp-violations', '2');
  await expect(page.locator('body')).not.toHaveAttribute('data-injected-script', 'ran');
});
