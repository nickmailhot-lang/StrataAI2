import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

const org = '11111111-1111-4111-8111-111111111111';
const actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333';
const list = '44444444-4444-4444-8444-444444444444';
const card = '55555555-5555-4555-8555-555555555555';
const otherList = '66666666-6666-4666-8666-666666666666';
const label = '77777777-7777-4777-8777-777777777777';
const rank = '500000000000000000000000000000';

// Actual routed feature consumers with controlled HTTP/transport. Mandatory
// real-service drag, recovery, roles, privacy and release suites remain intact.
for (const width of [1280, 768, 390, 320]) {
  test(`UX-003 Trello-style Organization and Work consumers at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
    }));
    const source = { viewer: false, denied: false, directoryFailure: false, writes: [] as string[] };
    const summary = () => ({ organization: { id: org, name: 'Cedar Court', description: 'Council and maintenance work', status: 0 }, role: 2 });
    await page.route('**/*', async route => {
      if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
      const url = new URL(route.request().url()), path = url.pathname;
      if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'trello-polish', connectionToken: 'trello-polish', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      // SignalR's transport negotiation POST is not a domain command.
      if (!['GET', 'HEAD'].includes(route.request().method())) source.writes.push(path);
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' } });
      if (path === '/navigation/observations') {
        const kind = url.searchParams.get('kind'), organization = url.searchParams.get('organizationId');
        const boardId = url.searchParams.get('boardId'), cardId = url.searchParams.get('cardId');
        const eventId = route.request().headers()['idempotency-key'];
        return route.fulfill({ json: { eventId, actorId: actor, organizationId: organization, boardId: kind === 'context' ? null : boardId,
          eventType: kind === 'context' ? 'APPLICATION_CONTEXT_CHANGED' : kind === 'board' ? 'BOARD_OPENED' : 'CARD_OPENED',
          entityType: kind === 'context' ? organization ? 'Organization' : 'ApplicationContext' : kind === 'board' ? 'Board' : 'Card',
          entityId: kind === 'context' ? organization ?? eventId : cardId ?? boardId, version: Number(url.searchParams.get('version') ?? 1), metadata: {}, createdAt: '2026-10-20T12:00:00Z' } });
      }
      if (path.endsWith('/surface-access')) return route.fulfill({ status: source.denied ? 403 : 200, json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/organizations/directory') return route.fulfill({ json: { items: [summary()], nextCursor: null } });
      if (path === `/organizations/${org}`) return route.fulfill({ status: source.denied ? 403 : 200, json: summary() });
      if (path === `/organizations/${org}/boards/directory`) return route.fulfill({ status: source.directoryFailure ? 503 : 200,
        json: { organizationId: org, items: [{ id: board, name: 'Council operations', version: 1 }], nextCursor: null } });
      if (path === `/boards/${board}`) return route.fulfill({ status: source.denied ? 403 : 200, json: {
        board: { id: board, organizationId: org, name: 'Council operations', description: 'Track the work through each list.', version: 1, lifecycleState: 'active', backgroundType: 'COLOR', backgroundValue: 'blue' },
        access: { canView: true, canEdit: !source.viewer, canMove: !source.viewer, canAdminister: false },
        lists: [{ list: { id: list, name: 'To do', rank, version: 1, lifecycleState: 'active' }, cards: [{ id: card, title: 'Inspect the parkade water leak', description: 'Record the source before arranging repairs.', rank, version: 1 }] },
          { list: { id: otherList, name: 'In progress', rank, version: 1, lifecycleState: 'active' }, cards: [] }],
        cardLabels: { [card]: { items: [{ id: label, name: 'Maintenance', color: 'blue' }], total: 1 } },
        cardMembers: { [card]: { cardVersion: 1, total: 0, items: [] } },
      } });
      return route.fulfill({ status: 503, json: {} });
    });
    const fit = async () => expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    const polished = process.env.STRATAAI_E2E_WORK_UX !== '0';
    await page.goto('/app');
    const organization = page.getByRole('link', { name: 'Cedar Court', exact: true });
    await expect(organization).toBeVisible();
    if (polished) await expect(organization).toHaveAccessibleDescription('Member');
    await organization.press('Enter');
    await expect(page.getByRole('heading', { name: 'Cedar Court', exact: true })).toBeVisible();
    await expect(page.locator('header').getByText('Cedar Court', { exact: true })).toBeVisible();
    if (polished) await expect(page.getByText('Boards in this Organization · Member', { exact: true })).toBeVisible();
    else await expect(page.getByText('Boards in this Organization · Member', { exact: true })).toHaveCount(0);
    const boardLink = page.getByRole('link', { name: 'Council operations', exact: true });
    await expect(boardLink).toHaveAttribute('href', `/app/${org}/boards/${board}`);
    await fit(); await page.screenshot({ path: info.outputPath(`organization-work-${width}.png`), fullPage: true });
    source.directoryFailure = true; await page.getByRole('button', { name: 'Refresh', exact: true }).press('Enter');
    await expect(page.getByRole('button', { name: 'Retry', exact: true })).toBeVisible();
    await expect(boardLink).toHaveCount(0);
    source.directoryFailure = false; await page.getByRole('button', { name: 'Retry', exact: true }).press('Enter');
    await boardLink.press('Enter');
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    const cardLink = page.getByRole('link', { name: 'Inspect the parkade water leak', exact: true });
    await expect(cardLink).toBeVisible(); await expect(cardLink).toContainText('Unassigned'); await expect(cardLink).toContainText('No due date');
    const cardDrag = page.getByRole('button', { name: 'Drag Inspect the parkade water leak card', exact: true });
    const listDrag = page.getByRole('button', { name: 'Drag To do list', exact: true });
    await expect(cardDrag).toBeEnabled(); await expect(listDrag).toBeEnabled();
    await expect(page.getByRole('button', { name: 'Move To do list', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Rename To do list', exact: true })).toBeVisible();
    if (polished) {
      await expect(cardDrag).not.toHaveText('Drag Inspect the parkade water leak card');
      await expect(listDrag).not.toHaveText('Drag To do list');
      expect(await cardLink.evaluate(el => !!(el.querySelector('[aria-label="Card label indicators"]')!.compareDocumentPosition(el.querySelector('[title]')!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
    } else {
      await expect(cardDrag).toHaveText('Drag Inspect the parkade water leak card');
      await expect(listDrag).toHaveText('Drag To do list');
    }
    await cardLink.press('Enter'); await expect(page.getByRole('dialog', { name: 'Card details', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Close', exact: true }).press('Enter');
    await expect(cardLink).toBeFocused();
    await page.keyboard.press('Shift+Tab'); await expect(cardDrag).toBeFocused();
    expect(await cardDrag.evaluate(el => {
      const r = el.getBoundingClientRect(); const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      return !!hit && el.contains(hit);
    })).toBe(true);
    await fit(); await page.screenshot({ path: info.outputPath(`work-board-standard-${width}.png`), fullPage: true });
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    await fit();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`work-board-${width}.png`), fullPage: true });
    source.viewer = true; await page.reload(); await expect(cardLink).toBeVisible();
    await expect(cardDrag).toHaveCount(0); await expect(listDrag).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Add card', exact: true })).toHaveCount(0);
    source.denied = true; await page.getByRole('button', { name: 'Refresh board', exact: true }).press('Enter');
    await expect(cardLink).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Council operations', exact: true })).toHaveCount(0);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.locator('header').getByText('Cedar Court', { exact: true })).toHaveCount(0);
    expect(source.writes.filter(path => /^\/(boards|cards|lists)(\/|$)/.test(path))).toEqual([]);
  });
}
