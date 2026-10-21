import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const event = '33333333-3333-4333-8333-333333333333';

// Actual routed consumers with controlled source responses; existing real-service
// global-search scenarios remain mandatory for permission/persistence evidence.
for (const width of [1280, 768, 390, 320]) {
  test(`UX-007 Search first-use, empty continuation and safe recovery at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    const source = { failure: 0, requests: [] as string[] };
    await page.route('**/*', async route => {
      if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
      const url = new URL(route.request().url()); const path = url.pathname;
      if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'ux-search', connectionToken: 'ux-search', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
      if (path === `/organizations/${org}/notifications`) return route.fulfill({ json: { organizationId: org, items: [], nextCursor: null } });
      if (path === '/search') {
        source.requests.push(url.search);
        if (source.failure) return route.fulfill({ status: source.failure, json: {} });
        return route.fulfill({ json: { items: [], nextCursor: url.searchParams.has('after') ? null : 'guidance-next',
          interaction: { eventId: event, entityId: event, actorId: actor, version: 1, eventType: 'SEARCH_EXECUTED', entityType: 'Search', organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-20T12:00:00Z' } } });
      }
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}/notifications`);
    await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible();
    await page.getByRole('link', { name: 'Search Cards', exact: true }).press('Enter');
    await expect(page.getByText('Choose filters, then select Search. Leave filters empty to browse Cards you can access.', { exact: true })).toBeVisible();
    const query = page.getByRole('textbox', { name: 'Card text', exact: true });
    await query.fill('Council minutes');
    expect(source.requests).toHaveLength(0);
    await query.press('Enter');
    await expect(page.getByRole('heading', { name: 'Keep searching', exact: true })).toBeVisible();
    await expect(query).toHaveValue('Council minutes'); await expect(query).toBeFocused();
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`ux-search-continuation-${width}.png`), fullPage: true });
    await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
    await expect(page.getByRole('heading', { name: 'No matches on this page', exact: true })).toBeVisible();
    const feedback = page.getByRole('region', { name: 'Global Card search' }).getByRole('status');
    await expect(feedback).toBeFocused();
    await expect(feedback).toHaveCSS('outline-style', 'solid');
    expect(await feedback.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
      return rect.top >= 0 && rect.bottom <= innerHeight && !!hit && element.contains(hit);
    })).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`ux-search-paging-focus-${width}.png`) });
    expect(source.requests.some(value => value.includes('after=guidance-next') && value.includes('q=Council+minutes'))).toBe(true);
    await expect(query).toHaveValue('Council minutes');
    await expect(page.getByRole('button', { name: 'Next search page', exact: true })).toHaveCount(0);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Refresh results', exact: true })).toBeFocused();
    await page.screenshot({ path: test.info().outputPath(`ux-search-final-${width}.png`), fullPage: true });
    await page.getByRole('combobox', { name: 'Search scope', exact: true }).press('Enter');
    await page.getByRole('option', { name: 'Archived work', exact: true }).press('Enter');
    await query.press('Enter');
    await expect(page.getByRole('heading', { name: 'Keep searching', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
    await expect(page.getByText('This search has reached the last page. Try fewer filters, or choose Active work in Search scope for current Cards.', { exact: true })).toBeVisible();
    expect(source.requests.some(value => value.includes('scope=archived') && value.includes('after=guidance-next'))).toBe(true);
    await expect(query).toHaveValue('Council minutes');
    const readsBeforeRefresh = source.requests.length;
    await query.press('ArrowLeft');
    await page.evaluate(() => window.dispatchEvent(new Event('online')));
    await expect.poll(() => source.requests.length).toBeGreaterThan(readsBeforeRefresh);
    await expect(page.getByText('This search has reached the last page. Try fewer filters, or choose Active work in Search scope for current Cards.', { exact: true })).toBeVisible();
    await expect(query).toBeFocused();
    source.failure = 503;
    await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(page.getByText('Search is unavailable. Check your account and access, then search again.', { exact: true })).toBeVisible();
    await expect(feedback).toBeFocused();
    await expect(page.getByRole('region', { name: 'Search guidance' })).toHaveCount(0);
    await expect(query).toHaveValue('Council minutes');
    source.failure = 403; await query.press('Enter');
    await expect(query).toHaveValue('');
    await expect(query).toBeFocused();
    await expect(page.getByRole('region', { name: 'Search guidance' })).toHaveCount(0);
    await page.goBack();
    await expect(page).toHaveURL(new RegExp(`/app/${org}/notifications$`));
    await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible();
  });
}
