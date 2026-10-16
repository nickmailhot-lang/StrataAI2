import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/07/11/12: production membership observations retain original removal and exclude private data at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const ownerAccount = { email: `member-metrics-owner-${width}-${Date.now()}@example.test`, password: 'member-metrics-correct-horse', displayName: 'Private metrics Owner' };
    const recipientAccount = { email: `member-metrics-recipient-${width}-${Date.now()}@example.test`, password: 'member-metrics-correct-horse', displayName: 'Private metrics Member' };
    try {
      const registered = await context.request.post('/auth/register', { headers, data: ownerAccount }); expect(registered.status()).toBe(201);
      const actor = (await registered.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: ownerAccount })).status()).toBe(200);
      const recipientRegistered = await recipient.request.post('/auth/register', { headers, data: recipientAccount }); expect(recipientRegistered.status()).toBe(201);
      const target = (await recipientRegistered.json()).user.id;
      expect((await recipient.request.post('/auth/login', { headers, data: recipientAccount })).status()).toBe(200);
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Private membership metrics council' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email: recipientAccount.email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await recipient.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const directory = await context.request.get(`/organizations/${org}/members`); expect(directory.status()).toBe(200);
      const member = (await directory.json()).items.find((row: { userId: string }) => row.userId === target); expect(member).toBeTruthy();
      const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
      const reports: unknown[] = []; const statuses: number[] = []; const writes: { key: string; url: string }[] = [];
      let documents = 0;
      page.on('request', request => {
        if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
        if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
          const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
        }
      });
      page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
      await page.route(url => url.pathname === `/organizations/${org}/members/${target}`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], url: route.request().url() });
        const response = await route.fetch(); expect(response.status()).toBe(204);
        if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
      });
      await page.goto(`/app/${org}/members`);
      await expect(page.getByText('Current members checked. Review a membership again before confirming removal.', { exact: true })).toBeVisible();
      const review = page.getByRole('button', { name: `Review removal of ${recipientAccount.displayName}`, exact: true });
      await expect(review).toBeEnabled(); await review.focus(); await page.keyboard.press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm member removal', exact: true });
      await expect(confirm).toBeEnabled(); await confirm.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/The removal could not be confirmed/)).toBeVisible();
      const retry = page.getByRole('button', { name: 'Retry original removal', exact: true });
      await expect(retry).toBeEnabled(); await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Original removal acknowledged. Review current membership to check later access.', { exact: true })).toBeVisible();
      const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
      await expect.poll(() => observed('organization_member_remove', 'success'), { timeout: 15_000 }).toBe(1);
      await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
      expect(observed('organization_members_disclosure', 'open')).toBe(1);
      expect(observed('organization_members_read', 'success')).toBeGreaterThanOrEqual(1);
      expect(observed('organization_member_review', 'success')).toBeGreaterThanOrEqual(1);
      for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(observed('organization_member_remove', kind)).toBe(1);
      for (const row of events) {
        expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
        if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
      }
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(writes[0].url).toContain(`expectedVersion=${member.version}`); expect(documents).toBe(1);
      const serialized = JSON.stringify(reports);
      for (const privateValue of [actor, target, org, member.membershipId, ownerAccount.email, recipientAccount.email, ownerAccount.password,
        ownerAccount.displayName, recipientAccount.displayName, 'Private membership metrics council', writes[0].key, 'Idempotency-Key', 'expectedVersion']) expect(serialized).not.toContain(privateValue);
      const after = await context.request.get(`/organizations/${org}/members`); expect(after.status()).toBe(200);
      expect((await after.json()).items.map((row: { userId: string }) => row.userId)).not.toContain(target);
      expect((await recipient.request.get(`/organizations/${org}/members`)).status()).toBe(404);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await recipient.close(); }
  });
}
