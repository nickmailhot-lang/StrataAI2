import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const stage of ['before', 'during', 'after'] as const) {
  test(`PRD-03-TC-05/07/11/12: settings reviewed actor replacement ${stage} submission at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${stage}-${width}-${Date.now()}`;
    const owner = { email: `settings-owner-${suffix}@example.test`, password: 'settings-actor-correct-horse', displayName: 'Reviewed owner' };
    const admin = { ...owner, email: `settings-admin-${suffix}@example.test`, displayName: 'Replacement administrator' };
    const replacement = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      expect((await context.request.post('/auth/register', { headers, data: owner })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: owner })).status()).toBe(200);
      const profile = await context.request.get('/me'); expect(profile.status()).toBe(200); const actor = (await profile.json()).id;
      const created = await context.request.post('/organizations', { headers, data: { name: 'Private reviewed settings' } });
      expect(created.status()).toBe(201); const original = (await created.json()).organization;
      expect((await replacement.request.post('/auth/register', { headers, data: admin })).status()).toBe(201);
      expect((await replacement.request.post('/auth/login', { headers, data: admin })).status()).toBe(200);
      const invitation = await context.request.post(`/organizations/${original.id}/invitations`, { headers,
        data: { email: admin.email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201);
      expect((await replacement.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      let writes = 0, documents = 0;
      const replaceCookie = async () => {
        expect((await context.request.post('/auth/login', { headers, data: admin })).status()).toBe(200);
      };
      await page.route(`**/organizations/${original.id}`, async route => {
        expect(route.request().headers()['x-strataai-expected-actor']).toBe(actor);
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        writes++;
        if (stage === 'during') await replaceCookie();
        // Use the browser's current real cookie even though the intercepted
        // request was created before the concurrent sign-in completed.
        const cookie = (await context.cookies(route.request().url())).map(value => `${value.name}=${value.value}`).join('; ');
        const response = await route.fetch({ headers: { ...route.request().headers(), cookie } });
        expect(response.status()).toBe(stage === 'during' ? 401 : 200);
        if (stage === 'after') await replaceCookie();
        await route.fulfill({ response });
      });
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.goto(`/app/${original.id}/settings`);
      await expect(page.getByLabel(/^Organization name/)).toHaveValue(original.name);
      const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
      if ((await runtime.json()).mode === 'production')
        await expect(page.getByText('Current settings checked. Review any saved changes before replacing them with your draft.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeEnabled();
      await page.getByLabel(/^Organization name/).fill('Reviewed owner edit');
      if (stage === 'before') await replaceCookie();
      await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
      await expect(page).toHaveURL(/\/login(?:\?|$)/);
      await expect(page.getByLabel(/^Organization name/)).toHaveCount(0);
      await expect(page.getByText('Organization settings saved.', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Retry original save' })).toHaveCount(0);
      expect(writes).toBe(stage === 'before' ? 0 : 1); expect(documents).toBe(1);
      const stored = await replacement.request.get(`/organizations/${original.id}`); expect(stored.status()).toBe(200);
      const current = (await stored.json()).organization;
      if (stage === 'after') expect(current).toMatchObject({ name: 'Reviewed owner edit', version: original.version + 1 });
      else expect(current).toEqual(original);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await replacement.close(); }
  });
}
