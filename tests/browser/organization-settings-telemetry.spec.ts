import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/07/11/12: production settings telemetry observes original recovery without private material at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `settings-metrics-${width}-${Date.now()}@example.test`, password: 'settings-metrics-correct-horse', displayName: 'Settings metrics Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    const created = await context.request.post('/organizations', { headers, data: { name: 'Private telemetry council', description: 'Private original description' } });
    expect(created.status()).toBe(201); const original = (await created.json()).organization;
    const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
    const reports: unknown[] = []; const statuses: number[] = [];
    const writes: { key: string; body: string }[] = []; let documents = 0;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
        const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
      }
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    await page.route(url => url.pathname === `/organizations/${original.id}`, async route => {
      if (route.request().method() !== 'PATCH') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(200);
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.goto(`/app/${original.id}/settings`);
    await expect(page.getByText('Current settings checked. Review any saved changes before replacing them with your draft.', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeEnabled();
    await page.getByLabel(/^Organization name/).fill('Private updated telemetry name');
    await page.getByLabel('Description', { exact: true }).fill('Private updated telemetry body');
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Your save could not be confirmed/)).toBeVisible();
    await page.getByRole('button', { name: 'Retry original save' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Original save acknowledgment recovered/)).toBeVisible();
    await expect.poll(() => events.some(row => row.action === 'organization_settings_update' && row.kind === 'success'), { timeout: 15_000 }).toBe(true);
    await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
    const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    expect(observed('organization_settings_disclosure', 'open')).toBe(1);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(observed('organization_settings_update', kind)).toBe(1);
    for (const row of events) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
      ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(documents).toBe(1);
    const serialized = JSON.stringify(reports);
    for (const privateValue of [actor, original.id, account.email, account.password, original.name, original.description,
      'Private updated telemetry name', 'Private updated telemetry body', writes[0].key, 'Idempotency-Key']) expect(serialized).not.toContain(privateValue);
    const stored = await context.request.get(`/organizations/${original.id}`); expect(stored.status()).toBe(200);
    expect((await stored.json()).organization).toMatchObject({ name: 'Private updated telemetry name', description: 'Private updated telemetry body', version: original.version + 1 });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
