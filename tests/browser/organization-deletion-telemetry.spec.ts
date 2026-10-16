import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/07/11/12: deletion observations distinguish original request acknowledgment and verified Worker completion at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `delete-metrics-${width}-${Date.now()}@example.test`, password: 'delete-metrics-correct-horse', displayName: 'Private deletion metrics Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Private deletion metrics council' } });
    expect(created.status()).toBe(201); const original = (await created.json()).organization; const org = original.id;
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
    const reports: unknown[] = []; const statuses: number[] = []; const writes: { url: string; key: string }[] = [];
    let documents = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
        const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
      }
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    await page.route(url => url.pathname === `/organizations/${org}`, async route => {
      if (route.request().method() !== 'DELETE') { await route.continue(); return; }
      writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
      const response = await route.fetch(); expect(response.status()).toBe(202);
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.goto(`/app/${org}/delete`);
    const launcher = page.getByRole('button', { name: 'Review deletion request', exact: true });
    await pressAdmittedAction(launcher);
    await expect(page.getByRole('button', { name: 'Cancel deletion request', exact: true })).toBeFocused();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.keyboard.press('Enter'); await expect(page.getByRole('dialog')).toHaveCount(0); expect(writes).toHaveLength(0);
    await pressAdmittedAction(launcher); await pressAdmittedAction(page.getByRole('button', { name: 'Confirm deletion request', exact: true }));
    const retry = page.getByRole('button', { name: 'Retry original deletion request', exact: true });
    await expect(retry).toBeEnabled(); await pressAdmittedAction(retry);
    const notice = page.getByRole('status'); await expect(notice).toHaveText('Deletion request acknowledged. Deletion has not been confirmed complete.');
    const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    await expect.poll(() => observed('organization_deletion_change', 'success'), { timeout: 15_000 }).toBe(1);
    expect(observed('organization_deletion_completion', 'open')).toBe(0);
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
    const writeUrl = new URL(writes[0].url);
    expect(writeUrl.searchParams.get('version')).toBe(String(original.version)); expect(writeUrl.searchParams.get('expectedActorId')).toBe(actor);
    expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    const observationPath = `/organizations/${org}/deletion-requests/${writes[0].key}?expectedActorId=${actor}`;
    await expect.poll(async () => {
      const response = await context.request.get(observationPath); expect(response.status()).toBe(200); return (await response.json()).state;
    }, { timeout: 30_000 }).toBe('COMPLETED');
    const terminal = await context.request.get(observationPath); expect(terminal.status()).toBe(200); const snapshot = await terminal.json();
    expect(snapshot.requestId).toBe(writes[0].key); expect(snapshot.version).toBe(original.version + 2);
    expect(snapshot.eventId).toMatch(/^[0-9a-f-]{36}$/); expect(Number.isFinite(Date.parse(snapshot.completedAt))).toBe(true);
    expect(observed('organization_deletion_completion', 'open')).toBe(0); await expect(notice).toHaveText('Deletion request acknowledged. Deletion has not been confirmed complete.');
    await pressAdmittedAction(page.getByRole('button', { name: 'Check deletion status', exact: true }));
    await expect(notice).toHaveText('Organization deletion confirmed complete.');
    await expect.poll(() => observed('organization_deletion_completion', 'open'), { timeout: 15_000 }).toBe(1);
    await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
    expect(observed('organization_deletion_disclosure', 'open')).toBe(1); expect(observed('organization_deletion_read', 'success')).toBe(1);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(observed('organization_deletion_change', kind)).toBe(1);
    expect(observed('organization_deletion_status', 'use')).toBe(1); expect(observed('organization_deletion_status', 'success')).toBe(1);
    for (const row of events) {
      expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      if (row.durationMs !== undefined) { expect(Number.isFinite(row.durationMs)).toBe(true); expect(row.durationMs).toBeGreaterThanOrEqual(0); expect(row.durationMs).toBeLessThanOrEqual(60_000); }
    }
    const text = JSON.stringify(reports);
    for (const value of [org, actor, account.email, account.password, account.displayName, 'Private deletion metrics council', writes[0].key,
      snapshot.eventId, snapshot.completedAt, 'requestId', 'eventId', 'expectedActorId', 'Idempotency-Key']) expect(text).not.toContain(value);
    expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404); expect(documents).toBe(1);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
