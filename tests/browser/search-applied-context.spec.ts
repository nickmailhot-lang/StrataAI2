import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const card = '44444444-4444-4444-8444-444444444444';
const event = '55555555-5555-4555-8555-555555555555';
const hint = 'Filter changes are not applied. Select Search to update results. Paging and Refresh results still use the last search.';

// Real routed UI with controlled responses. Complete real-service Search,
// role, withdrawal and immutable-release scenarios remain mandatory in CI.
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-16 applied Search context survives edited filters and withdraws safely at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    const source = { failure: 0, searches: [] as string[], writes: [] as string[] };
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    await page.route('**/*', async route => {
      const request = route.request(); if (!['fetch', 'xhr'].includes(request.resourceType())) return route.continue();
      const url = new URL(request.url()), path = url.pathname;
      if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'search-context', connectionToken: 'search-context', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      if (!['GET', 'HEAD'].includes(request.method())) source.writes.push(path);
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: 'Search council', status: 0 }, role: 2 } });
      if (path === '/navigation/observations') {
        const eventId = request.headers()['idempotency-key'];
        return route.fulfill({ json: { eventId, actorId: actor, organizationId: org, boardId: null, eventType: 'APPLICATION_CONTEXT_CHANGED', entityType: 'Organization', entityId: org, version: 1, metadata: {}, createdAt: '2026-10-23T12:00:00Z' } });
      }
      if (path === '/search') {
        source.searches.push(url.search); if (source.failure) return route.fulfill({ status: source.failure, json: {} });
        return route.fulfill({ json: { nextCursor: url.searchParams.has('after') ? null : 'context-next',
          items: [{ sourceKind: 'CARD', card: { id: card, organizationId: org, boardId: board, listId: event,
            title: 'Admitted landscaping result', version: 1, lifecycleState: 'active', dueAt: null, dueHasTime: false, dueComplete: false },
            boardName: 'Council work', listName: 'Planning', boardDateTimezone: null, labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false }],
          interaction: { eventId: event, entityId: event, actorId: actor, version: 1, eventType: 'SEARCH_EXECUTED', entityType: 'Search', organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-23T12:00:00Z' } } });
      }
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}/search`);
    const query = page.getByRole('textbox', { name: 'Card text', exact: true });
    await query.fill('landscaping'); await query.press('Enter');
    const link = page.getByRole('link', { name: 'Admitted landscaping result', exact: true });
    await expect(link).toHaveAttribute('href', `/app/${org}/boards/${board}/cards/${card}`);
    await expect(page.getByText(hint, { exact: true })).toHaveCount(0);
    await query.fill('roof'); await expect(page.getByText(hint, { exact: true })).toBeVisible(); await expect(query).toBeFocused();
    await expect(link).toBeVisible();
    // Changing fields never submits the draft; every existing read still uses
    // the previous applied criteria, even if background reconciliation runs.
    expect(source.searches.every(value => new URLSearchParams(value).get('q') === 'landscaping')).toBe(true);
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`search-unapplied-${width}.png`), fullPage: true });
    await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
    await expect(page.getByText('1 result on this page. Search complete.', { exact: true })).toBeVisible();
    await expect(page.getByText(hint, { exact: true })).toBeVisible(); await expect(query).toHaveValue('roof');
    await expect(link).toBeVisible();
    expect(source.searches.some(value => value.includes('after=context-next') && value.includes('q=landscaping'))).toBe(true);
    await query.press('Enter'); await expect(link).toBeVisible(); await expect(page.getByText(hint, { exact: true })).toHaveCount(0);
    expect(source.searches.some(value => new URLSearchParams(value).get('q') === 'roof' && !new URLSearchParams(value).has('after'))).toBe(true);
    await query.fill('unsent private edit'); source.failure = 503;
    await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(page.getByText(/Search is unavailable/)).toBeVisible();
    await expect(page.getByText(hint, { exact: true })).toHaveCount(0); await expect(link).toHaveCount(0);
    await expect(query).toHaveValue('unsent private edit');
    source.failure = 403; await query.press('Enter'); await expect(query).toHaveValue('');
    await expect(page.getByText(hint, { exact: true })).toHaveCount(0); await expect(link).toHaveCount(0);
    expect(source.writes.filter(path => /^\/(boards|cards|lists)(\/|$)/.test(path))).toEqual([]);
  });
}
