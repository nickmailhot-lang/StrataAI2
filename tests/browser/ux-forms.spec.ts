import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressObservedRequest, pressObservedValidation } from './keyboardAdmission';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';

// Controlled UI coverage; the existing organization-settings real-service suite
// remains required for authorization, persistence and original-key recovery.
for (const width of [1280, 768, 390, 320]) {
  test(`UX-005 controlled field recovery, native submit and zoom at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    let denied = false;
    let writes = 0;
    let record = { id: org, name: 'Council', description: 'Preserved description', logoUrl: null as string | null, status: 0, version: 1 };
    await page.route('**/*', async route => {
      if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/organizations/live/metadata/negotiate') return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'ux-fixture', connectionToken: 'ux-fixture', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
      if (path === `/organizations/${org}`) {
        if (route.request().method() === 'PATCH') {
          writes++;
          if (writes === 1) return route.fulfill({ status: 400, json: { code: 'invalid_organization_logo_url' } });
          const draft = route.request().postDataJSON();
          record = { ...record, ...draft, version: record.version + 1 };
          return route.fulfill({ json: record });
        }
        return denied ? route.fulfill({ status: 403, json: {} }) : route.fulfill({ json: { organization: record, role: 0 } });
      }
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}/settings`);
    const name = page.getByRole('textbox', { name: 'Organization name' });
    const logo = page.getByRole('textbox', { name: 'Logo URL' });
    await expect(name).toHaveValue('Council');
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    await name.fill('   '); await expect(name).toHaveValue('   ');
    await pressObservedValidation(name, page.locator('form'));
    await expect(name).toBeFocused();
    await expect(name).toHaveAttribute('aria-invalid', 'true');
    await expect(name).toHaveAccessibleDescription('Use 1–160 characters; spaces alone are not a name.');
    expect(writes).toBe(0);
    await name.fill('Council'); await expect(name).toHaveValue('Council');
    await logo.fill('http://example.test/logo.png'); await expect(logo).toHaveValue('http://example.test/logo.png');
    await pressObservedRequest(logo, () => writes, 1);
    await expect(logo).toBeFocused();
    await expect(logo).toHaveAttribute('aria-invalid', 'true');
    await expect(logo).toHaveAccessibleDescription('Enter an HTTPS URL without a username or password.');
    await expect(page.getByRole('textbox', { name: 'Description' })).toHaveValue('Preserved description');
    expect(writes).toBe(1);
    await expect.poll(async () => {
      const header = await page.getByRole('banner').boundingBox();
      const field = await logo.boundingBox();
      return field!.y - (header!.y + header!.height);
    }).toBeGreaterThanOrEqual(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`ux-form-error-${width}.png`), fullPage: true });
    await logo.fill('https://example.test/logo.png'); await expect(logo).toHaveValue('https://example.test/logo.png');
    // A foreground account check can hide this preserved form after insertion.
    // Keep native field Enter; stop admission as soon as its PATCH starts.
    await pressObservedRequest(logo, () => writes, 2);
    await expect(page.getByRole('status').filter({ hasText: 'Organization settings saved.' })).toBeVisible();
    await expect(logo).toHaveAttribute('aria-invalid', 'false');
    expect(writes).toBe(2);
    denied = true; await page.reload();
    await expect(page.getByText('Organization settings are unavailable to your account.', { exact: true })).toBeVisible();
    await expect(name).toHaveCount(0); await expect(logo).toHaveCount(0);
    expect(writes).toBe(2);
  });
}
