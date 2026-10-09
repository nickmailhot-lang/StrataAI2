import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  for (const command of ['profile', 'logout', 'deactivate', 'handle'] as const) {
  test(`PRD-02-TC-05/08: stale ${command} command cannot affect a replacement cookie account at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(60_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `expected-account-${command}-${width}-${Date.now()}@example.test`, password: 'expected-account-correct-horse', displayName: 'Original account' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const originalState = await (await context.request.get('/me/sync?after=0')).json();
    const original = await browser.newContext({ baseURL: test.info().project.use.baseURL, storageState: await context.storageState() });
    const replacement = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      const next = { ...account, email: `replacement-${account.email}`, displayName: 'Replacement account' };
      expect((await replacement.request.post('/auth/register', { headers, data: next })).status()).toBe(201);
      expect((await replacement.request.post('/auth/login', { headers, data: next })).status()).toBe(200);
      const replacementState = await (await replacement.request.get('/me/sync?after=0')).json();
      // A disconnected stale form has not yet observed another tab's sign-in.
      // Actual authentication replies and command admission remain unmodified.
      await page.routeWebSocket('**/me/live**', socket => socket.close({ code: 1013 }));
      await page.goto('/app/profile');
      await expect(page.getByLabel(/^Display name/)).toHaveValue(account.displayName);
      await page.getByLabel(/^Display name/).fill('Private original-account draft');
      await page.route(url => url.pathname === '/me/sync', route => route.abort('failed'));
      let path = '/me', method = 'PATCH', label = 'Save profile';
      let handleBefore: readonly unknown[] = [];
      if (command === 'logout') { path = '/auth/logout'; method = 'POST'; label = 'Sign out'; }
      if (command === 'deactivate') {
        path = '/me/deactivate'; method = 'POST'; label = 'Confirm deactivation';
        await page.getByRole('button', { name: 'Deactivate account', exact: true }).press('Enter');
        await expect(page.getByRole('dialog', { name: 'Deactivate your account?' })).toBeVisible();
      }
      if (command === 'handle') {
        path = '/me/mention-handle'; label = 'Save handle';
        await page.getByRole('button', { name: 'Change mention handle', exact: true }).press('Enter');
        const field = page.getByRole('textbox', { name: 'Mention handle', exact: true });
        await expect(field).toBeEnabled(); await field.fill(`expected_${width}_${Date.now()}`);
        handleBefore = await Promise.all([original.request, replacement.request].map(async client => {
          const response = await client.get(path); expect(response.status()).toBe(200); return response.json();
        }));
        // Change the shared cookie after the real original-account preflight
        // reaches the server, but before its reply permits the claim request.
        await page.route(url => url.pathname === '/me', async route => {
          if (route.request().method() !== 'GET') return route.continue();
          const response = await route.fetch(); expect(response.status()).toBe(200);
          expect((await response.json()).id).toBe(originalState.profile.id);
          await context.addCookies(await replacement.cookies());
          await route.fulfill({ response });
        });
      } else {
        await context.addCookies(await replacement.cookies());
        const current = await context.request.get('/me'); expect(current.status()).toBe(200);
        expect((await current.json()).id).toBe(replacementState.profile.id);
      }
      // The real 401 triggers immediate sign-in navigation, which can retire
      // Chromium's response-body handle before the assertion reads it. Retain
      // the actual server code before forwarding the unchanged response.
      let refusalCode: unknown;
      await page.route(url => url.pathname === path, async route => {
        if (route.request().method() !== method) return route.fallback();
        const actual = await route.fetch();
        refusalCode = (await actual.json()).code;
        await route.fulfill({ response: actual });
      });
      const refused = page.waitForResponse(response => new URL(response.url()).pathname === path && response.request().method() === method);
      const save = page.getByRole('button', { name: label, exact: true });
      await expect(save).toBeEnabled(); await save.focus(); await expect(save).toBeFocused(); await page.keyboard.press('Enter');
      const response = await refused; expect(response.status()).toBe(401);
      expect(response.request().headers()['x-strataai-expected-user']).toBe(originalState.profile.id);
      expect(refusalCode).toBe('session_unavailable');
      expect(response.headers()['set-cookie']).toBeUndefined();
      await expect(page).toHaveURL(/\/login$/);
      await expect(page.getByText('Private original-account draft', { exact: true })).toHaveCount(0);
      await expect(page.getByText('Your account is deactivated. Historical activity is preserved.', { exact: true })).toHaveCount(0);
      for (const [client, before] of [[original.request, originalState], [replacement.request, replacementState]] as const) {
        const after = await client.get('/me/sync?after=0'); expect(after.status()).toBe(200); expect(await after.json()).toEqual(before);
      }
      if (command === 'handle') {
        const after = await Promise.all([original.request, replacement.request].map(async client => {
          const response = await client.get(path); expect(response.status()).toBe(200); return response.json();
        }));
        expect(after).toEqual(handleBefore);
      }
    } finally { await original.close(); await replacement.close(); }
  });
  }
}
