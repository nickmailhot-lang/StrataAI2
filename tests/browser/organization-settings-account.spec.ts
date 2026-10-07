import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const after of [false, true]) {
  test(`PRD-03-TC-06/07/11/12: settings account uncertainty ${after ? 'after' : 'before'} submission at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `settings-account-${after}-${width}-${Date.now()}@example.test`, password: 'settings-account-correct-horse', displayName: 'Reviewed administrator' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Settings account review' } });
    expect(created.status()).toBe(201); const original = (await created.json()).organization;
    let failProfile = false, profileRefusals = 0, documents = 0;
    const writes: { key: string; body: string }[] = [];
    await page.route('**/me', async route => {
      if (!failProfile) { await route.continue(); return; }
      failProfile = false; profileRefusals++;
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ status: 503, code: 'temporarily_unavailable' }) });
    });
    await page.route(`**/organizations/${original.id}`, async route => {
      if (route.request().method() !== 'PATCH') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(200);
      if (after && writes.length === 1) failProfile = true;
      await route.fulfill({ response });
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    await page.goto(`/app/${original.id}/settings`);
    await expect(page.getByLabel(/^Organization name/)).toHaveValue(original.name);
    const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
    if ((await runtime.json()).mode === 'production')
      await expect(page.getByText('Current settings checked. Review any saved changes before replacing them with your draft.', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeEnabled();
    await page.getByLabel(/^Organization name/).fill('Preserved reviewed edit');
    if (!after) failProfile = true;
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    if (after) {
      await expect(page.getByText(/Your save could not be confirmed/)).toBeVisible();
      await expect(page.getByText('Organization settings saved.', { exact: true })).toHaveCount(0);
      expect(writes).toHaveLength(1);
      const stored = await context.request.get(`/organizations/${original.id}`); expect(stored.status()).toBe(200);
      expect((await stored.json()).organization).toMatchObject({ name: 'Preserved reviewed edit', version: original.version + 1 });
      await page.getByRole('button', { name: 'Retry original save' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/Original save acknowledgment recovered/)).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    } else {
      await expect(page.getByText(/No save was sent/)).toBeVisible();
      expect(writes).toHaveLength(0);
      await expect(page.getByRole('button', { name: 'Retry original save' })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeDisabled();
      const stored = await context.request.get(`/organizations/${original.id}`); expect(stored.status()).toBe(200);
      expect((await stored.json()).organization).toEqual(original);
    }
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('Preserved reviewed edit');
    await page.getByRole('button', { name: 'Load current settings' }).focus(); await page.keyboard.press('Enter');
    if (!after) {
      await expect(page.getByText(`Name: ${original.name}`, { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Keep draft after review' }).focus(); await page.keyboard.press('Enter');
    }
    await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeEnabled();
    const final = await context.request.get(`/organizations/${original.id}`); expect(final.status()).toBe(200);
    expect((await final.json()).organization).toMatchObject({
      name: after ? 'Preserved reviewed edit' : original.name,
      version: original.version + (after ? 1 : 0),
    });
    expect(profileRefusals).toBe(1); expect(documents).toBe(1);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
