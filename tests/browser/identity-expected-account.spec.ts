import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-02-TC-05/08: stale profile cannot save into a replacement cookie account at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(60_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `expected-account-${width}-${Date.now()}@example.test`, password: 'expected-account-correct-horse', displayName: 'Original account' };
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
      await context.addCookies(await replacement.cookies());
      const current = await context.request.get('/me'); expect(current.status()).toBe(200);
      expect((await current.json()).id).toBe(replacementState.profile.id);
      const refused = page.waitForResponse(response => new URL(response.url()).pathname === '/me' && response.request().method() === 'PATCH');
      const save = page.getByRole('button', { name: 'Save profile', exact: true });
      await expect(save).toBeEnabled(); await save.focus(); await expect(save).toBeFocused(); await page.keyboard.press('Enter');
      const response = await refused; expect(response.status()).toBe(401);
      expect(response.request().headers()['x-strataai-expected-user']).toBe(originalState.profile.id);
      expect((await response.json()).code).toBe('session_unavailable');
      expect(response.headers()['set-cookie']).toBeUndefined();
      await expect(page).toHaveURL(/\/login$/);
      await expect(page.getByText('Private original-account draft', { exact: true })).toHaveCount(0);
      for (const [client, before] of [[original.request, originalState], [replacement.request, replacementState]] as const) {
        const after = await client.get('/me/sync?after=0'); expect(after.status()).toBe(200); expect(await after.json()).toEqual(before);
      }
    } finally { await original.close(); await replacement.close(); }
  });
}
