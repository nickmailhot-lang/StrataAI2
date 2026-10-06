import { expect, test } from './releaseTest';
import AxeBuilder from '@axe-core/playwright';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-TC-01/06/08/11/12: keyboard Organization metadata review and lost acknowledgment at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `settings-${viewport.width}-${Date.now()}@example.test`, password: 'browser-settings-correct-horse', displayName: 'Organization administrator' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Organization metadata', description: 'Original description' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization;
    await page.goto(`/app/${org.id}`);
    await page.getByRole('link', { name: 'Organization settings', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('Organization metadata');
    await page.getByLabel('Logo URL').fill('http://example.test/logo.png');
    await page.getByRole('button', { name: 'Save Organization settings' }).click();
    await expect(page.getByText('Use a secure HTTPS logo URL without embedded credentials, or leave it empty.')).toBeVisible();
    await page.getByLabel('Logo URL').fill('https://example.test/logo.png');
    await page.getByLabel(/^Organization name/).fill('My reviewed draft');
    expect((await context.request.patch(`/organizations/${org.id}`, { headers, data: { name: 'Other administrator update', description: 'Other description', logoUrl: null, version: 1 } })).status()).toBe(200);
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/The Organization changed elsewhere/)).toBeVisible();
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('My reviewed draft');
    await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeDisabled();
    await page.getByRole('button', { name: 'Load current settings' }).click();
    await expect(page.getByText('Name: Other administrator update')).toBeVisible();
    await page.getByRole('button', { name: 'Keep draft after review' }).focus(); await page.keyboard.press('Enter');
    const writes: number[] = [];
    const retryKeys: string[] = []; const bodies: string[] = [];
    await page.route(`**/organizations/${org.id}`, async route => {
      if (route.request().method() !== 'PATCH') { await route.continue(); return; }
      writes.push(route.request().postDataJSON().version);
      retryKeys.push(route.request().headers()['idempotency-key']); bodies.push(route.request().postData()!);
      const response = await route.fetch(); expect(response.status()).toBe(200);
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Your save could not be confirmed/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Load current settings' })).toBeDisabled();
    await expect(page.getByLabel(/^Organization name/)).toBeDisabled();
    expect((await context.request.patch(`/organizations/${org.id}`, { headers,
      data: { name: 'Later administrator update', description: 'Later description', logoUrl: null, version: 3 } })).status()).toBe(200);
    await page.getByRole('button', { name: 'Retry original save' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Original save acknowledgment recovered/)).toBeVisible();
    await page.getByRole('button', { name: 'Load current settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Name: Later administrator update')).toBeVisible();
    expect(writes).toEqual([2, 2]); expect(retryKeys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(retryKeys[1]).toBe(retryKeys[0]); expect(bodies[1]).toBe(bodies[0]);
    const stored = (await (await context.request.get('/organizations')).json()).find((item: { organization: { id: string } }) => item.organization.id === org.id).organization;
    expect(stored).toMatchObject({ name: 'Later administrator update', description: 'Later description', logoUrl: null, version: 4 });
    await page.getByRole('button', { name: 'Discard draft and use current settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('Later administrator update');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.reload(); await expect(page.getByLabel(/^Organization name/)).toHaveValue('Later administrator update');
    await expect(page.getByText('Organization settings saved.')).toHaveCount(0);
  });
}
