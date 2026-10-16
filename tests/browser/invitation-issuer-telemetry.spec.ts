import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressObservedRequest } from './keyboardAdmission';
import { trackInvitationAdmission } from './invitationAdmissionTracker';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-03/04-TC-06/07/11/12: Organization and Board invitation observations retain original requests and exclude private data at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `issuer-metrics-${width}-${Date.now()}@example.test`, password: 'issuer-metrics-correct-horse', displayName: 'Private invitation metrics Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Private invitation metrics council' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Private invitation metrics Board', visibility: 'PRIVATE' } });
    expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    const restore = scopedBoardWorker(org);
    const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
    const reports: unknown[] = []; const statuses: number[] = [];
    const privateValues = [org, board, actor, account.email, account.password, account.displayName, 'Private invitation metrics council', 'Private invitation metrics Board'];
    let documents = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
        const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
      }
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    try {
      await waitForBoardDelivery(context.request, board);
      for (const boardSurface of [false, true]) {
        const prefix = boardSurface ? 'board_invitation' : 'organization_invitation';
        const root = boardSurface ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
        const routePath = boardSurface ? `/app/${org}/boards/${board}/invite` : `/app/${org}/invite`;
        const recipient = `private-issuer-recipient-${width}-${boardSurface}-${Date.now()}@example.test`; privateValues.push(recipient);
        const writes: { url: string; key: string; body: string }[] = []; let invitation = '';
        const matches = (url: URL) => url.pathname === root;
        const permissionPath = `/boards/${board}`; const permissionMatches = (url: URL) => url.pathname === permissionPath;
        let permissionRefused = false;
        if (boardSurface) await page.route(permissionMatches, async route => {
          if (route.request().method() === 'GET' && !permissionRefused) {
            permissionRefused = true;
            await route.fulfill({ status: 503, contentType: 'application/json', body: '{"code":"temporary_permission_failure"}' });
          } else await route.continue();
        });
        await page.route(matches, async route => {
          if (route.request().method() !== 'POST') { await route.continue(); return; }
          writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
          const response = await route.fetch(); expect(response.status()).toBe(201);
          const acknowledgment = await response.json(); expect(acknowledgment.invitationToken).toBeNull();
          if (writes.length === 1) { invitation = acknowledgment.id; await route.abort('timedout'); }
          else { expect(acknowledgment.id).toBe(invitation); await route.fulfill({ response }); }
        });
        const ready = trackInvitationAdmission(page, org, actor, boardSurface ? board : undefined, routePath);
        await page.goto(routePath);
        if (boardSurface) {
          await expect(page.getByText('Unable to verify current invitation permissions. Please retry.', { exact: true })).toBeVisible();
          await expect(page.getByLabel(/^Invitation email/)).toHaveCount(0); expect(writes).toHaveLength(0);
          await openAdmittedReview(page.getByRole('button', { name: 'Retry permission check', exact: true }), page.getByLabel(/^Invitation email/));
        }
        await expect.poll(ready.ready, { timeout: 30_000 }).toBe(true);
        const create = page.getByRole('button', { name: 'Create invitation', exact: true });
        await expect(create).toBeEnabled(); await page.getByLabel(/^Invitation email/).fill(recipient);
        await pressObservedRequest(create, () => writes.length, 1);
        const retry = page.getByRole('button', { name: 'Retry same invitation', exact: true });
        await expect(retry).toBeEnabled(); await pressObservedRequest(retry, () => writes.length, 2);
        await expect(page.getByRole('button', { name: 'Create another invitation', exact: true })).toBeEnabled();
        await expect.poll(() => observed(prefix + '_create', 'success'), { timeout: 15_000 }).toBe(1);
        expect(observed(prefix + '_disclosure', 'open')).toBe(1); expect(observed(prefix + '_read', 'success')).toBeGreaterThanOrEqual(1);
        if (boardSurface) {
          expect(permissionRefused).toBe(true);
          for (const kind of ['exception', 'failure', 'retry']) expect(observed(prefix + '_read', kind)).toBe(1);
        }
        for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(observed(prefix + '_create', kind)).toBe(1);
        expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(new URL(writes[0].url).searchParams.get('expectedActorId')).toBe(actor);
        expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
        expect(JSON.parse(writes[0].body)).toEqual(boardSurface ? { email: recipient, role: 'MEMBER' } : { email: recipient, surface: 'INTERNAL', targetRole: 'MEMBER' });
        const history = await context.request.get(root + `?expectedActorId=${actor}`); expect(history.status()).toBe(200);
        const rows = (await history.json()).items.filter((row: { email: string }) => row.email.toLowerCase() === recipient.toLowerCase());
        expect(rows).toHaveLength(1); expect(rows[0].id).toBe(invitation);
        expect(boardSurface ? rows[0].boardTarget : rows[0].boardTarget ?? null).toEqual(boardSurface ? { boardId: board, role: 'MEMBER' } : null);
        privateValues.push(invitation, writes[0].key);
        expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
        await page.unroute(matches);
        if (boardSurface) await page.unroute(permissionMatches);
      }
      expect(documents).toBe(2);
      for (const row of events) {
        expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
        if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
      }
      const text = JSON.stringify(reports);
      for (const value of [...privateValues, 'Idempotency-Key', 'expectedActorId', 'invitationToken', 'targetRole']) expect(text).not.toContain(value);
    } finally { restore(); }
  });
}
