import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { openAdmittedReview, pressObservedLocalAction } from './keyboardAdmission';
import { openFocusedInvitationReview } from './invitationReviewAdmission';

const org = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', invitation = '44444444-4444-4444-8444-444444444444';
const email = 'r@a.test', boardName = 'Private review Board';
const canonical = { id: invitation, email, surface: 'INTERNAL', targetRole: 'MEMBER', createdAt: '2034-01-01T00:00:00Z',
  expiresAt: '2035-01-08T18:00:00Z', acceptedAt: null, revokedAt: null, deliveryState: null };

// Real routed consumer/native transitions with controlled authority/history IO.
// The original real-service clock/Worker/canonical-record fixture remains required.
async function sourceFor(page: Page, boardSurface: boolean) {
  const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
  const state = { malformed: true, changed: false, changeNext: false, denied: false, changedReads: 0, writes: [] as string[] };
  await page.routeWebSocket('**/*', socket => socket.onMessage(message => {
    if (typeof message === 'string' && message.includes('"protocol"')) socket.send('{}\u001e');
  }));
  await page.route('**/*', async route => {
    const request = route.request(); if (!['fetch', 'xhr'].includes(request.resourceType())) return route.continue();
    const url = new URL(request.url()), path = url.pathname;
    if (path.endsWith('/negotiate')) return route.fulfill({ json: { negotiateVersion: 1, connectionId: 'review-admission', connectionToken: 'review-admission',
      availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } });
    if (path === '/me/activity-client-events') return route.fulfill({ status: 204 });
    if (path === '/api/runtime') return route.fulfill({ json: { service: 'strataai-api', mode: 'production', version: '0.1.0', revision: 'a'.repeat(40) } });
    if (path === '/me') return route.fulfill({ json: { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'UTC' } });
    if (path.endsWith('/surface-access')) return route.fulfill({ json: { organizationId: org, surface: 'INTERNAL' } });
    if (path === `/organizations/${org}`) return route.fulfill({ json: { organization: { id: org, name: 'Review scope', status: 0 }, role: 2 } });
    if (path === `/boards/${board}`) return route.fulfill({ json: { board: { id: board, organizationId: org, name: boardName, version: 1, lifecycleState: 'active' },
      access: { canView: true, canEdit: true, canMove: true, canAdminister: true }, lists: [] } });
    if (path === '/navigation/observations') {
      const kind = url.searchParams.get('kind'), eventId = request.headers()['idempotency-key'];
      return route.fulfill({ json: { eventId, actorId: actor, organizationId: org, boardId: kind === 'context' ? null : board,
        eventType: kind === 'context' ? 'APPLICATION_CONTEXT_CHANGED' : 'BOARD_OPENED', entityType: kind === 'context' ? 'Organization' : 'Board',
        entityId: kind === 'context' ? org : board, version: Number(url.searchParams.get('version') ?? 1), metadata: {}, createdAt: '2026-10-22T00:00:00Z' } });
    }
    if (path === root && request.method() === 'GET') {
      if (state.changeNext) { state.changeNext = false; state.changed = true; state.changedReads++; }
      return route.fulfill({ status: state.denied ? 403 : 200, json: { items: [{ ...canonical,
        deliveryState: state.changed ? 'SENT' : null, ...(boardSurface ? { boardTarget: { boardId: board, role: 'MEMBER' } } : {}),
        ...(state.malformed ? { expiresAt: '2035-02-30T12:00:00Z' } : {}) }], nextCursor: null } });
    }
    if (!['GET', 'HEAD'].includes(request.method()) && /^\/(organizations|boards|cards|lists|me\/invitations)(\/|$)/.test(path)) state.writes.push(path);
    return route.fulfill({ status: 503, json: {} });
  });
  return state;
}

for (const width of [1280, 768, 390, 320]) for (const boardSurface of [false, true]) {
  test(`UX-008 invitation review keeps original default Cancel focus after source re-admission (${boardSurface ? 'Board' : 'Organization'}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 }); const source = await sourceFor(page, boardSurface);
    await page.goto(boardSurface ? `/app/${org}/boards/${board}/invitations` : `/app/${org}/invitations`);
    await expect(page.getByText('Unable to confirm invitation history. Please retry.', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: email, exact: true })).toHaveCount(0); expect(source.writes).toEqual([]);
    source.malformed = false;
    const revoke = page.getByRole('button', { name: `Revoke invitation for ${email}`, exact: true });
    await openAdmittedReview(page.getByRole('button', { name: 'Refresh invitations', exact: true }), revoke);
    await expect(revoke).toBeEnabled();
    const dialog = page.getByRole('dialog', { name: 'Revoke invitation?', exact: true });
    // A benign mail-state update arrives during the first opening transition.
    // Current authority must retire that review; never force its default focus.
    source.changeNext = true;
    await revoke.evaluate(node => node.addEventListener('click', () => {
      setTimeout(() => window.dispatchEvent(new Event('focus')), 0);
    }, { once: true }));
    const opening = openFocusedInvitationReview(revoke, dialog);
    try {
      await expect.poll(() => source.changedReads).toBe(1);
      await expect(dialog).toHaveCount(0);
      await opening;
    } finally { await opening.catch(() => {}); }
    const cancel = dialog.getByRole('button', { name: 'Cancel', exact: true });
    await expect(cancel).toBeFocused(); expect(source.writes).toEqual([]);
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await expect(cancel).toBeFocused(); await pressObservedLocalAction(cancel);
    await expect(dialog).toHaveCount(0); expect(source.writes).toEqual([]);
    await expect(page.getByText('Email sent', { exact: true })).toBeVisible();
    source.denied = true; await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page.getByRole('heading', { name: email, exact: true })).toHaveCount(0);
    await expect(revoke).toHaveCount(0); await expect(dialog).toHaveCount(0); expect(source.writes).toEqual([]);
  });
}
