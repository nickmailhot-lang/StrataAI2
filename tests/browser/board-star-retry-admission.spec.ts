import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';
import { disclosedWorkControl } from './workActionDisclosure';
import { trackBoardStarChanges } from './boardReadTracker';

const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', starPath = `/boards/${board}/star`;
const boardPath = `/app/${org}/boards/${board}`;

// Real routed consumer and native keys, with controlled admission/response IO.
// Actual private Worker delivery, distinct accounts and exact original history
// remain required in board-star.spec.ts and the complete release gates.
async function sourceFor(page: Page) {
  const source = { version: 0, holdReads: false, heldReads: 0, denied: false,
    releaseReads: [] as Array<() => void>, releaseMutation: undefined as (() => void) | undefined,
    writes: [] as Array<{ method: string; key: string | undefined; version: string | null }>, otherWrites: [] as string[] };
  await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/*', async route => {
    if (!['fetch', 'xhr'].includes(route.request().resourceType())) return route.continue();
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'star-admission', connectionToken: 'star-admission',
      availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
    if (path === '/me/activity-client-events') return route.fulfill({ status: 204 });
    if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
    if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' } });
    if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
    if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: 'Star admission scope', status: 0 }, role: 2 } });
    if (path === '/navigation/observations') {
      const kind = url.searchParams.get('kind'), eventId = request.headers()['idempotency-key'];
      return route.fulfill({ json: { eventId, actorId: actor, organizationId: org, boardId: kind === 'context' ? null : board,
        eventType: kind === 'context' ? 'APPLICATION_CONTEXT_CHANGED' : 'BOARD_OPENED', entityType: kind === 'context' ? 'Organization' : 'Board',
        entityId: kind === 'context' ? org : board, version: Number(url.searchParams.get('version') ?? 1), metadata: {}, createdAt: '2026-10-22T00:00:00Z' } });
    }
    if (path === `/boards/${board}`) return route.fulfill({ json: {
      board: { id: board, organizationId: org, name: 'Recoverable personal stars', description: null, version: 1, lifecycleState: 'active' },
      access: { canView: true, canEdit: true, canMove: true, canAdminister: false }, lists: [],
    } });
    if (path === starPath && request.method() === 'GET') {
      if (source.holdReads) { source.heldReads++; await new Promise<void>(resolve => source.releaseReads.push(resolve)); }
      if (request.failure()) return;
      return route.fulfill({ status: source.denied ? 403 : 200, json: { organizationId: org, boardId: board, userId: actor,
        starred: false, version: source.version, createdAt: source.version ? '2026-10-22T00:00:00Z' : null,
        updatedAt: source.version ? '2026-10-22T00:00:01Z' : null } });
    }
    if (path === starPath && ['PUT', 'DELETE'].includes(request.method())) {
      source.writes.push({ method: request.method(), key: request.headers()['idempotency-key'], version: url.searchParams.get('version') });
      if (source.writes.length === 1) { source.version = 2; return route.abort('failed'); }
      if (source.writes.length === 2) { await new Promise<void>(resolve => { source.releaseMutation = resolve; }); return route.abort('failed'); }
      return route.fulfill({ status: 204 });
    }
    if (!['GET', 'HEAD'].includes(request.method()) && /^\/(boards|cards|lists)(\/|$)/.test(path)) source.otherWrites.push(path);
    return route.fulfill({ status: 503, json: {} });
  });
  return source;
}

for (const width of [1280, 768, 390, 320]) {
  test(`UX-008 original star recovery admits one native request and never replays a failed reply at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page);
    const observed = trackBoardStarChanges(page, board);
    const release = () => { source.holdReads = false; source.releaseReads.splice(0).forEach(resolve => resolve()); };
    try {
      await page.goto(boardPath);
      const dialog = page.getByRole('dialog', { name: 'Board starring', exact: true });
      await openAdmittedReview(await disclosedWorkControl(page, 'Board actions', 'button', { name: 'Board starring', exact: true }), dialog);
      await pressObservedRequest(dialog.getByRole('button', { name: 'Star Board', exact: true }), observed, 1);
      const retry = dialog.getByRole('button', { name: 'Retry same star change', exact: true });
      await expect(retry).toBeEnabled();
      await expect(dialog.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Done', exact: true })).toHaveCount(0);
      await dialog.getByRole('button', { name: 'Check current star', exact: true }).focus();
      source.holdReads = true;
      await retry.evaluate(node => node.addEventListener('focus', () => window.dispatchEvent(new Event('focus')), { once: true }));
      const admission = pressObservedRequest(retry, observed, 2);
      await expect.poll(() => source.heldReads).toBeGreaterThan(0);
      await expect(retry).toBeDisabled(); expect(observed()).toBe(1); expect(source.writes).toHaveLength(1);
      release(); await admission;
      await expect.poll(() => source.writes.length).toBe(2);
      await expect(retry).toBeDisabled(); expect(observed()).toBe(2);
      source.releaseMutation!();
      await expect(retry).toBeEnabled();
      await expect(dialog.getByRole('status')).toHaveText('This star change is unconfirmed. Retry the same change.');
      // A completed failed response cannot trigger a third request. Only the
      // next explicit native recovery activation below may start one.
      expect(observed()).toBe(2); expect(source.writes).toHaveLength(2);
      await pressObservedRequest(retry, observed, 3);
      await expect(dialog.getByRole('button', { name: 'Star Board', exact: true })).toBeEnabled();
      await expect(dialog.getByText('You have not starred this Board.', { exact: true })).toBeVisible();
      expect(source.writes).toHaveLength(3); expect(source.writes.every(row => row.method === 'PUT' && row.version === '0')).toBe(true);
      expect(source.writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(source.writes.map(row => row.key)).toEqual(Array(3).fill(source.writes[0].key));
      await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      source.denied = true;
      await dialog.getByRole('button', { name: 'Check current star', exact: true }).press('Enter');
      await expect(dialog.getByText('Board starring is unavailable. Check access or sign in.', { exact: true })).toBeVisible();
      await expect(retry).toHaveCount(0);
      await expect(dialog.getByRole('button', { name: 'Star Board', exact: true })).toHaveCount(0);
      expect(observed()).toBe(3); expect(source.otherWrites).toEqual([]);
    } finally { release(); source.releaseMutation?.(); }
  });
}
