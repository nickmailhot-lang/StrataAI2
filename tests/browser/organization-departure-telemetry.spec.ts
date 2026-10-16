import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-03/06/07/11/12: departure observations preserve owner continuity and later rejoin at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const successor = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `departure-metrics-${width}-${Date.now()}@example.test`, password: 'departure-metrics-correct-horse', displayName: 'Private departing Owner' };
    const other = { ...account, email: `departure-successor-${width}-${Date.now()}@example.test`, displayName: 'Private successor Owner' };
    try {
      const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
      const actor = (await registered.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      expect((await successor.request.post('/auth/register', { headers, data: other })).status()).toBe(201);
      expect((await successor.request.post('/auth/login', { headers, data: other })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Private departure metrics council' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
      const reports: unknown[] = []; const statuses: number[] = [];
      let documents = 0;
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
          const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
        }
      });
      page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
      await page.goto(`/app/${org}/leave`);
      await pressAdmittedAction(page.getByRole('button', { name: 'Review departure', exact: true }));
      await expect(page.getByRole('button', { name: 'Cancel departure', exact: true })).toBeFocused();
      await pressAdmittedAction(page.getByRole('button', { name: 'Confirm departure', exact: true }));
      await expect(page.getByText('The last usable owner cannot leave. Another usable owner must remain.', { exact: true })).toBeVisible();
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(200);
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email: other.email, surface: 'INTERNAL', targetRole: 'OWNER' } });
      expect(invitation.status()).toBe(201);
      expect((await successor.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const writes: { key: string; body: string }[] = [];
      await page.route(url => url.pathname === `/organizations/${org}/leave`, async route => {
        if (route.request().method() !== 'POST') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await pressAdmittedAction(page.getByRole('button', { name: 'Review current membership', exact: true }));
      await pressAdmittedAction(page.getByRole('button', { name: 'Review departure', exact: true }));
      await pressAdmittedAction(page.getByRole('button', { name: 'Confirm departure', exact: true }));
      await expect(page.getByText(/Your departure could not be confirmed/)).toBeVisible();
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      const rejoin = await successor.request.post(`/organizations/${org}/invitations`, { headers, data: { email: account.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(rejoin.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await rejoin.json()).id}/accept`, { headers })).status()).toBe(200);
      const before = await successor.request.get(`/organizations/${org}/members`); expect(before.status()).toBe(200);
      const restored = (await before.json()).items.find((row: { userId: string }) => row.userId === actor); expect(restored).toBeTruthy();
      await pressAdmittedAction(page.getByRole('button', { name: 'Retry original departure', exact: true }));
      await expect(page.getByText('Original departure acknowledged. Review current membership to check later access.', { exact: true })).toBeVisible();
      const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
      await expect.poll(() => observed('organization_departure_change', 'success'), { timeout: 15_000 }).toBe(1);
      await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
      expect(observed('organization_departure_disclosure', 'open')).toBe(1);
      expect(observed('organization_departure_read', 'success')).toBe(2);
      expect(observed('organization_departure_read', 'retry')).toBe(1);
      for (const kind of ['use', 'failure']) expect(observed('organization_departure_change', kind)).toBe(2);
      for (const kind of ['conflict', 'exception', 'retry', 'success']) expect(observed('organization_departure_change', kind)).toBe(1);
      for (const row of events) {
        expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
        if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
      }
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(JSON.parse(writes[0].body)).toEqual({ expectedActorId: actor }); expect(documents).toBe(1);
      const serialized = JSON.stringify(reports);
      for (const value of [actor, org, restored.membershipId, account.email, other.email, account.password,
        account.displayName, other.displayName, 'Private departure metrics council', writes[0].key, 'Idempotency-Key', 'expectedActorId']) expect(serialized).not.toContain(value);
      const after = await successor.request.get(`/organizations/${org}/members`); expect(after.status()).toBe(200);
      expect((await after.json()).items.find((row: { userId: string }) => row.userId === actor)).toEqual(restored);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(200);
      await expect(page.getByText('You left the Organization.', { exact: true })).toHaveCount(0);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await successor.close(); }
  });
}
