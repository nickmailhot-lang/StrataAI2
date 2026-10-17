import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';

// Controlled responses test the actual migrated consumers and production CSS.
// Existing real-service suites separately verify authorization and persistence.
for (const width of [1280, 768, 390, 320]) {
  test(`UX-002/003 controlled role discovery, shared consumers and zoom at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    let role = 0;
    const longName = 'Organization' + 'A'.repeat(130);
    const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
    await page.route('**/*', async route => {
      if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
      const path = new URL(route.request().url()).pathname;
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/me') return route.fulfill({ json: profile });
      if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: longName, description: null, status: 0 }, role } });
      if (path.endsWith('/boards/directory')) return route.fulfill({ json: { organizationId: org, items: [], nextCursor: null } });
      if (path === `/organizations/${org}/notifications`) return route.fulfill({ json: { organizationId: org, items: [], nextCursor: null } });
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}`);
    await page.getByText('Organization actions', { exact: true }).press('Enter');
    const settings = page.getByRole('link', { name: 'Organization settings', exact: true });
    await expect(settings).toBeVisible();
    await expect(page.getByRole('link', { name: 'Request Organization deletion', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    // Re-admit a lower role from authoritative HTTP; navigation never owns a
    // cached capability copy or starts another polling/SignalR subscription.
    role = 1; await page.reload(); await page.getByText('Organization actions', { exact: true }).press('Enter'); await expect(settings).toBeVisible();
    await expect(page.getByRole('link', { name: 'Request Organization deletion', exact: true })).toHaveCount(0);
    role = 2; await page.reload();
    await page.getByText('Organization actions', { exact: true }).press('Enter');
    await expect(page.getByRole('link', { name: 'Leave Organization', exact: true })).toBeVisible();
    await expect(settings).toHaveCount(0);
    await page.goto(`/app/${org}/search`);
    await expect(page.getByRole('heading', { name: 'Search Cards', level: 2 })).toBeVisible();
    await page.getByRole('textbox', { name: 'Card text', exact: true }).fill('A'.repeat(150));
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.getByText('Search is unavailable. Check your account and access, then search again.')).toBeVisible();
    // 200% root text size exercises wrapping without changing viewport pixels.
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await expect.poll(async () => {
      const header = await page.getByRole('banner').boundingBox();
      const heading = await page.getByRole('heading', { name: 'Search Cards', level: 2 }).boundingBox();
      return heading!.y - (header!.y + header!.height);
    }).toBeGreaterThanOrEqual(0);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`ux-zoom-${width}.png`), fullPage: true });
    await page.evaluate(() => { document.documentElement.style.fontSize = ''; });
    await page.goto(`/app/${org}/notifications`);
    await expect(page.getByRole('heading', { name: 'Notifications', level: 2 })).toBeVisible();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible();
    const refresh = page.getByRole('button', { name: 'Refresh notifications', exact: true });
    await refresh.focus(); await expect(refresh).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`ux-inbox-${width}.png`), fullPage: true });
  });
}
