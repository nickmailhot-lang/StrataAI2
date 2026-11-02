import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import type { Locator } from '@playwright/test';
import { focusAdmittedControl } from './keyboardAdmission';

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const org = id(1), board = id(2), actor = id(3), card = id(4);

// Actual production router/theme/history consumer with controlled read-only IO.
// The existing activity-history real-service/Worker/revocation scenario remains
// mandatory; this fixture isolates native paging focus visibility, not persistence.
async function visibleFocus(target: Locator) {
  await expect(target).toBeFocused(); await expect(target).toBeInViewport();
  await expect.poll(() => target.evaluate(element => {
    const box = element.getBoundingClientRect(), x = box.x + box.width / 2, y = box.y + box.height / 2;
    return x >= 0 && y >= 0 && x < innerWidth && y < innerHeight && element.contains(document.elementFromPoint(x, y));
  })).toBe(true);
}
for (const [width, textZoom] of [[320, 1], [390, 1], [768, 1], [1280, 1], [768, 2]] as const) for (const kind of ['BOARD', 'CARD'] as const) {
  test(`PRD-15: visible ${kind} history paging focus at ${width}px text${textZoom}x`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    const activityReads: string[] = [], writes: string[] = [], errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
      if (typeof message !== 'string') return;
      for (const part of message.split('\u001e').filter(Boolean)) {
        const frame = JSON.parse(part) as { protocol?: string; type?: number; target?: string; invocationId?: string };
        if (frame.protocol) socket.send('{}\u001e');
        if (frame.type === 4 && frame.target === 'Watch' && new URL(socket.url()).pathname === '/boards/live')
          socket.send(JSON.stringify({ type: 2, invocationId: frame.invocationId, item: { cursor: '0', hasMore: false, pending: false, resetRequired: false, events: [] } }) + '\u001e');
        if (frame.type === 4 && frame.target === 'Watch' && new URL(socket.url()).pathname === '/me/live')
          socket.send(JSON.stringify({ type: 2, invocationId: frame.invocationId, item: { profile: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' }, cursor: 1, latestSequence: 1, hasMore: false, events: [] } }) + '\u001e');
      }
    }));
    const summary = { organization: { id: org, name: 'Activity focus Organization', description: null, status: 0 }, role: 2 };
    await page.route('**/*', async route => {
      const request = route.request(); if (!['fetch', 'xhr'].includes(request.resourceType())) return route.continue();
      const url = new URL(request.url()), path = url.pathname;
      if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'activity-focus', connectionToken: 'activity-focus', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
      if (!['GET', 'HEAD'].includes(request.method()) && /^\/(boards|cards|lists)(\/|$)/.test(path)) writes.push(`${request.method()} ${path}`);
      if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
      if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-US', timezone: 'UTC' } });
      if (path === '/me/activity-client-events') return route.fulfill({ status: 204 });
      if (path === '/navigation/observations') {
        const target = url.searchParams.get('kind'), context = target === 'context';
        return route.fulfill({ json: { eventId: request.headers()['idempotency-key'], actorId: actor, organizationId: org, boardId: context ? null : board,
          eventType: context ? 'APPLICATION_CONTEXT_CHANGED' : target === 'card' ? 'CARD_OPENED' : 'BOARD_OPENED', entityType: context ? 'Organization' : target === 'card' ? 'Card' : 'Board',
          entityId: context ? org : url.searchParams.get('cardId') ?? board, version: 1, metadata: {}, createdAt: '2026-10-21T12:00:00Z' } });
      }
      if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
      if (path === '/organizations/directory') return route.fulfill({ json: { items: [summary], nextCursor: null } });
      if (path === `/organizations/${org}`) return route.fulfill({ json: summary });
      if (path === `/boards/${board}`) return route.fulfill({ json: {
        board: { id: board, organizationId: org, name: 'Activity focus Board', description: null, version: 1, lifecycleState: 'active', backgroundType: 'COLOR', backgroundValue: null },
        access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
        lists: [{ list: { id: id(10), name: 'Activity List', rank: '1'.padStart(30, '0'), version: 1, lifecycleState: 'active' },
          cards: [{ id: card, title: 'Activity focus Card', description: null, rank: '1'.padStart(30, '0'), version: 1 }] }],
      } });
      const historyPath = `/${kind === 'BOARD' ? 'boards' : 'cards'}/${kind === 'BOARD' ? board : card}/activity`;
      if (path === historyPath) {
        expect(request.method()).toBe('GET'); const after = url.searchParams.get('after');
        expect([null, 'older-page']).toContain(after); activityReads.push(path + url.search);
        return route.fulfill({ json: { organizationId: org, kind, targetId: kind === 'BOARD' ? board : card,
          items: Array.from({ length: after ? 1 : 50 }, (_, index) => ({ eventId: id((after ? 900 : 1000) - index), organizationId: org, boardId: board,
            actorId: actor, actorLabel: 'Historical teammate', eventType: 'CARD_UPDATED', entityType: 'Card', entityId: card, version: '1',
            createdAt: '2026-10-21T12:00:00Z', metadata: {}, currentBoardId: board })), nextCursor: after ? null : 'older-page' } });
      }
      return route.fulfill({ status: 503, json: {} });
    });
    await page.goto(`/app/${org}/boards/${board}`);
    if (textZoom === 2) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    const cardLink = page.getByRole('link', { name: 'Activity focus Card', exact: true });
    await expect(cardLink).toBeVisible(); if (kind === 'CARD') { await focusAdmittedControl(cardLink); await cardLink.press('Enter'); }
    const label = kind === 'BOARD' ? 'Board' : 'Card';
    const opener = page.getByRole('button', { name: `Review ${label} activity`, exact: true });
    await focusAdmittedControl(opener); await opener.press('Enter');
    const history = page.getByRole('region', { name: `${label} activity`, exact: true });
    await expect(history.getByRole('listitem')).toHaveCount(50);
    // The existing identity stream's first confirmed profile head performs a
    // second protected read. Preserve it; paging begins after that handoff.
    await expect.poll(() => activityReads.map(value => value.split('?')[1] ?? '')).toEqual(['', '']);
    const older = history.getByRole('button', { name: 'Older activity', exact: true });
    await expect(older).toBeFocused();
    await expect.poll(() => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === 'running'
      && animation.effect?.getComputedTiming().iterations !== Infinity).length)).toBe(0);
    await page.screenshot({ path: info.outputPath(`history-first-focus-${kind}-${width}.png`) });
    await visibleFocus(older);
    await older.press('Enter'); await expect(history.getByRole('listitem')).toHaveCount(1);
    const newer = history.getByRole('button', { name: 'Newer activity', exact: true });
    await visibleFocus(newer);
    await newer.press('Enter'); await expect(history.getByRole('listitem')).toHaveCount(50);
    await visibleFocus(older);
    expect((await new AxeBuilder({ page }).include(`[aria-label="${label} activity"]`).analyze()).violations).toEqual([]);
    await page.screenshot({ path: info.outputPath(`history-returned-focus-${kind}-${width}.png`) });
    expect(activityReads.map(value => value.split('?')[1] ?? '')).toEqual(['', '', 'after=older-page', '']);
    expect(writes).toEqual([]); expect(errors).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await history.getByRole('button', { name: 'Close activity', exact: true }).press('Enter');
    await visibleFocus(opener);
  });
}
