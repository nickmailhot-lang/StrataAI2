import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const list = '44444444-4444-4444-8444-444444444444';
const cards = Array.from({ length: 50 }, (_, i) => ({ id: `55555555-5555-4555-8555-${String(i + 1).padStart(12, '0')}`,
  organizationId: org, boardId: board, listId: list, title: `Maintenance item ${i + 1}`, description: null,
  rank: String((i + 1) * 10).padStart(30, '0'), version: 1, lifecycleState: 'active' }));
const last = 'This is the last filter page. Select First filtered Cards to return, or change or clear the filters.';

// Controlled HTTP exercises the current routed filter consumer. Real-service
// cursor/roles/recovery and immutable release gates remain separately required.
async function fixture(page: Page) {
  const source = { failure: 0, hold: false, waiters: [] as Array<() => void>, queries: [] as string[], changes: [] as string[], otherWrites: [] as string[] };
  await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/*', async route => {
    if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
    const url = new URL(route.request().url()), path = url.pathname, method = route.request().method();
    if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'filter-guidance', connectionToken: 'filter-guidance', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
    if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
    if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
    if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
    if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: 'Cedar Court', status: 0 }, role: 2 } });
    if (path === '/navigation/observations' || path === `/boards/${board}/cards/filter-change`) {
      const filter = path.endsWith('filter-change'), kind = url.searchParams.get('kind');
      const key = route.request().headers()['idempotency-key'];
      if (filter) { expect(method).toBe('POST'); source.changes.push(url.search); }
      return route.fulfill({ json: { eventId: key, entityId: filter ? key : kind === 'context' ? org : board, actorId: actor, organizationId: org,
        boardId: !filter && kind === 'context' ? null : board,
        eventType: filter ? 'BOARD_FILTER_CHANGED' : kind === 'context' ? 'APPLICATION_CONTEXT_CHANGED' : 'BOARD_OPENED',
        entityType: filter ? 'BoardFilter' : kind === 'context' ? 'Organization' : 'Board', version: 1, metadata: {}, createdAt: '2026-10-21T12:00:00Z' } });
    }
    if (!['GET', 'HEAD'].includes(method)) source.otherWrites.push(path);
    if (path === `/boards/${board}`) return route.fulfill({ json: {
      board: { id: board, organizationId: org, name: 'Council operations', description: null, version: 1, lifecycleState: 'active' },
      access: { canView: true, canEdit: false, canMove: false, canAdminister: false },
      lists: [{ list: { id: list, name: 'To do', rank: '500000000000000000000000000000', version: 1, lifecycleState: 'active' }, cards }],
    } });
    if (path === `/boards/${board}/labels`) return route.fulfill({ json: { organizationId: org, boardId: board, items: [], nextCursor: null } });
    if (path === `/boards/${board}/cards`) {
      source.queries.push(url.search);
      if (source.hold) await new Promise<void>(resolve => source.waiters.push(resolve));
      return route.fulfill({ status: source.failure || 200, json: { organizationId: org, boardId: board,
        items: url.searchParams.has('after') ? [] : cards, nextCursor: url.searchParams.has('after') ? null : cards.at(-1)!.id } });
    }
    return route.fulfill({ status: 503, json: {} });
  });
  return source;
}

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-16 Board filter last-page guidance, return and withdrawal at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 }); const source = await fixture(page);
    await page.goto(`/app/${org}/boards/${board}`);
    const dialog = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
    await openAdmittedReview(page.getByRole('button', { name: 'Filter Board Cards', exact: true }), dialog);
    const keyword = dialog.getByRole('textbox', { name: 'Card keyword', exact: true });
    await expect(keyword).toBeEnabled(); await keyword.fill('maintenance');
    await pressObservedRequest(dialog.getByRole('button', { name: 'Apply filters', exact: true }), () => source.changes.length, 1);
    await expect(dialog.getByText('50 Cards on this page', { exact: true })).toBeVisible();
    await keyword.fill('unsent edit'); await dialog.getByRole('button', { name: 'Next filtered Cards', exact: true }).press('Enter');
    const filterRegion = dialog.getByRole('region', { name: 'Board filter choices and results', exact: true });
    await expect(filterRegion).toBeFocused();
    await expect(filterRegion).toHaveCSS('outline-style', 'solid');
    await expect(dialog.getByText(last, { exact: true })).toBeVisible();
    await expect(dialog.getByText('No matching Cards on this page.', { exact: true })).toBeVisible();
    await expect(dialog.getByText('No Cards match these filters.', { exact: true })).toHaveCount(0);
    await expect(keyword).toHaveValue('unsent edit');
    expect(new URLSearchParams(source.queries.at(-1)).get('keyword')).toBe('maintenance');
    expect(new URLSearchParams(source.queries.at(-1)).get('after')).toBe(cards.at(-1)!.id);
    await dialog.getByRole('button', { name: 'Show this page on Board', exact: true }).press('Enter');
    await expect(dialog).toHaveCount(0); await expect(page.getByText(last, { exact: true })).toBeVisible();
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`filter-last-page-${width}.png`), fullPage: true });
    source.hold = true;
    await page.getByRole('button', { name: 'First filtered Cards', exact: true }).press('Enter');
    const feedback = page.getByRole('status', { name: 'Filtered Board results', exact: true });
    await expect(feedback).toBeFocused();
    await expect(feedback).toHaveText('Checking filtered Board Cards…');
    await expect(feedback).toHaveCSS('outline-style', 'solid');
    expect(await feedback.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
      return rect.top >= 0 && rect.bottom <= innerHeight && !!hit && element.contains(hit);
    })).toBe(true);
    await expect.poll(() => source.waiters.length).toBeGreaterThan(0); await expect(page.getByText(last, { exact: true })).toHaveCount(0);
    source.failure = 503; source.hold = false; source.waiters.splice(0).forEach(resolve => resolve());
    await expect(page.getByText('Filtered Cards could not be loaded. Try again or refresh the Board.', { exact: true })).toBeVisible();
    await expect(feedback).toBeFocused();
    await expect(feedback).toHaveText('Filtered Board results are unavailable.');
    await expect(page.getByText(last, { exact: true })).toHaveCount(0);
    source.failure = 0; await page.getByRole('button', { name: 'Retry filtered Cards', exact: true }).press('Enter');
    await expect(page.getByText('Filtered Board: 50 matching Cards on this page.', { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Next filtered Cards', exact: true }).press('Enter'); await expect(page.getByText(last, { exact: true })).toBeVisible();
    await pressObservedRequest(page.getByRole('button', { name: 'Clear Board filters', exact: true }), () => source.changes.length, 2);
    await expect(page.getByText(last, { exact: true })).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Maintenance item 1', exact: true })).toBeVisible();
    await openAdmittedReview(page.getByRole('button', { name: 'Filter Board Cards', exact: true }), dialog);
    source.failure = 403;
    await pressObservedRequest(dialog.getByRole('button', { name: 'Apply filters', exact: true }), () => source.changes.length, 3);
    await expect(dialog).toHaveCount(0); await expect(page.getByText(last, { exact: true })).toHaveCount(0);
    await expect(page.getByText('Filters are unavailable. Sign in or refresh the Board to check your access.', { exact: true })).toBeVisible();
    expect(source.otherWrites).toEqual([]); expect(source.changes.map(query => new URLSearchParams(query).get('change'))).toEqual(['apply', 'clear', 'apply']);
  });
}
