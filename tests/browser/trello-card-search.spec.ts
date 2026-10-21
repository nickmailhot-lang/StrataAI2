import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview } from './keyboardAdmission';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const list = '44444444-4444-4444-8444-444444444444';
const card = '55555555-5555-4555-8555-555555555555';
const event = '66666666-6666-4666-8666-666666666666';
const rank = '500000000000000000000000000000';
const title = 'Inspect the parkade water leak';
const boardPath = `/app/${org}/boards/${board}`;
const polished = process.env.STRATAAI_E2E_WORK_UX !== '0';

// Controlled HTTP/quiet transport exercises real routed feature consumers.
// Existing real-service mutation, roles, Markdown, recovery and release gates
// remain mandatory and are never replaced by this presentation fixture.
async function sourceFor(page: Page) {
  const source = { boardFailure: 0, searchFailure: 0, searches: [] as string[], writes: [] as string[] };
  await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/*', async route => {
    if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
    const url = new URL(route.request().url()), path = url.pathname;
    if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'detail-search', connectionToken: 'detail-search', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
    if (!['GET', 'HEAD'].includes(route.request().method())) source.writes.push(path);
    if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
    if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
    if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
    if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: 'Cedar Court', status: 0 }, role: 2 } });
    if (path === '/navigation/observations') {
      const kind = url.searchParams.get('kind'), organizationId = url.searchParams.get('organizationId');
      const eventId = route.request().headers()['idempotency-key'];
      return route.fulfill({ json: { eventId, actorId: actor, organizationId, boardId: kind === 'context' ? null : board,
        eventType: kind === 'context' ? 'APPLICATION_CONTEXT_CHANGED' : kind === 'board' ? 'BOARD_OPENED' : 'CARD_OPENED',
        entityType: kind === 'context' ? 'Organization' : kind === 'board' ? 'Board' : 'Card', entityId: kind === 'context' ? org : kind === 'board' ? board : card,
        version: Number(url.searchParams.get('version') ?? 1), metadata: {}, createdAt: '2026-10-21T12:00:00Z' } });
    }
    if (path === `/boards/${board}`) return route.fulfill({ status: source.boardFailure || 200, json: {
      board: { id: board, organizationId: org, name: 'Council operations', description: 'Council and maintenance work', version: 1, lifecycleState: 'active' },
      access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
      lists: [{ list: { id: list, name: 'To do', rank, version: 1, lifecycleState: 'active' }, cards: [{ id: card, title, description: 'Record the source before arranging repairs.', rank, version: 1 }] }],
    } });
    if (path === '/search') {
      source.searches.push(url.search);
      return route.fulfill({ status: source.searchFailure || 200, json: { nextCursor: url.searchParams.has('after') ? null : 'detail-search-next',
        items: [{ sourceKind: 'CARD', card: { id: card, organizationId: org, boardId: board, listId: list, title, version: 1, lifecycleState: 'active', dueAt: null, dueHasTime: false, dueComplete: false },
          boardName: 'Council operations', listName: 'To do', boardDateTimezone: null, labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false }],
        interaction: { eventId: event, entityId: event, actorId: actor, version: 1, eventType: 'SEARCH_EXECUTED', entityType: 'Search', organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-21T12:00:00Z' },
      } });
    }
    return route.fulfill({ status: 503, json: {} });
  });
  return source;
}
const noDomainWrites = (paths: string[]) => expect(paths.filter(path => /^\/(boards|cards|lists)(\/|$)/.test(path))).toEqual([]);
async function fit(page: Page, width: number) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
}
async function axe(page: Page) {
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
}

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-09 Trello Card context, dirty return and private withdrawal at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
    await page.goto(`${boardPath}/cards/${card}`);
    const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
    const location = detail.getByRole('region', { name: 'Card location', exact: true });
    const input = detail.getByRole('textbox', { name: 'Card title', exact: true });
    await expect(input).toBeEditable(); await expect(input).toHaveValue(title);
    if (polished) await expect(location).toHaveText('Council operations / To do');
    else await expect(location).toHaveCount(0);
    await expect(detail.getByRole('button', { name: 'Save card', exact: true })).toBeVisible();
    await expect(detail.getByRole('button', { name: 'Close', exact: true })).toBeVisible();
    await fit(page, width); await page.screenshot({ path: info.outputPath(`card-details-${width}.png`), fullPage: true });
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    await fit(page, width); expect(await detail.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true); await axe(page);
    source.boardFailure = 503; await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
    await expect(detail.getByRole('alert')).toBeVisible(); await expect(location).toHaveCount(0);
    source.boardFailure = 0; await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
    await expect(input).toBeEditable(); if (polished) await expect(location).toBeVisible();
    await input.fill('Unsaved repair investigation');
    const guard = page.getByRole('dialog', { name: 'Discard unsaved changes?', exact: true });
    await openAdmittedReview(detail.getByRole('button', { name: 'Close', exact: true }), guard);
    await guard.getByRole('button', { name: 'Keep editing', exact: true }).press('Enter');
    await expect(input).toHaveValue('Unsaved repair investigation');
    await openAdmittedReview(detail.getByRole('button', { name: 'Close', exact: true }), guard);
    await guard.getByRole('button', { name: 'Discard changes and leave', exact: true }).press('Enter');
    await expect(detail).toHaveCount(0);
    const origin = page.getByRole('link', { name: title, exact: true }); await expect(origin).toBeFocused();
    await origin.press('Enter'); await expect(input).toHaveValue(title);
    source.boardFailure = 403; await detail.getByRole('button', { name: 'Refresh card', exact: true }).press('Enter');
    await expect(input).toHaveCount(0); await expect(location).toHaveCount(0); noDomainWrites(source.writes);
  });

  test(`PRD-16 Trello Search surfaces preserve drafts, paging and denial at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
    await page.goto(`/app/${org}/search`);
    const query = page.getByRole('textbox', { name: 'Card text', exact: true });
    await query.fill('roof'); await query.press('Enter');
    const result = page.getByRole('link', { name: title, exact: true });
    await expect(result).toHaveAttribute('href', `${boardPath}/cards/${card}`);
    await expect(page.getByText('Council operations / To do', { exact: true })).toBeVisible();
    await expect(page.getByText('Labels: None', { exact: true })).toBeVisible();
    await expect(page.getByText('Members: None', { exact: true })).toBeVisible();
    await expect(page.getByText('No deadline', { exact: true })).toBeVisible();
    await fit(page, width); await page.screenshot({ path: info.outputPath(`search-results-${width}.png`), fullPage: true });
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; }); await fit(page, width); await axe(page);
    await query.fill('unsent draft'); await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
    const feedback = page.getByRole('region', { name: 'Global Card search', exact: true }).getByRole('status');
    await expect(feedback).toBeFocused(); await expect(query).toHaveValue('unsent draft');
    expect(new URLSearchParams(source.searches.at(-1)).get('q')).toBe('roof');
    expect(new URLSearchParams(source.searches.at(-1)).get('after')).toBe('detail-search-next');
    source.searchFailure = 503; await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(page.getByText('Search is unavailable. Check your account and access, then search again.', { exact: true })).toBeVisible();
    await expect(feedback).toBeFocused(); await expect(query).toHaveValue('unsent draft'); await expect(result).toHaveCount(0);
    source.searchFailure = 0; await page.getByRole('button', { name: 'Search', exact: true }).press('Enter'); await expect(result).toBeVisible();
    expect(new URLSearchParams(source.searches.at(-1)).get('q')).toBe('unsent draft');
    source.searchFailure = 403; await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
    await expect(query).toHaveValue(''); await expect(result).toHaveCount(0); noDomainWrites(source.writes);
  });
}
