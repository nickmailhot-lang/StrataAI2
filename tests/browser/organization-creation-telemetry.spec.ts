import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/07/11/12: production creation telemetry observes original recovery without private material at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `creation-metrics-${width}-${Date.now()}@example.test`, password: 'creation-metrics-correct-horse', displayName: 'Creation metrics Owner' };
    const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
    const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
    const events: { action: string; kind: string; count: number; durationMs?: number }[] = [];
    const reports: unknown[] = []; const statuses: number[] = [];
    const writes: { key: string; body: string }[] = []; let documents = 0; let originalId: string | undefined;
    page.on('request', request => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++;
      if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') {
        const body = request.postDataJSON(); reports.push(body); events.push(...body.events);
      }
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    await page.route(url => url.pathname === '/organizations', async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(201);
      const id = (await response.json()).organization.id;
      if (originalId) expect(id).toBe(originalId); else originalId = id;
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.goto('/app'); await page.getByRole('button', { name: 'Create organization', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Private telemetry creation');
    await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Private original creation body');
    await page.getByRole('button', { name: 'Create', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/may already have succeeded/)).toBeVisible();
    await page.getByRole('button', { name: 'Retry original creation', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page).toHaveURL(new RegExp(`/app/${originalId}$`));
    await expect(page.getByRole('heading', { name: 'Private telemetry creation', exact: true })).toBeVisible();
    await expect.poll(() => events.some(row => row.action === 'organization_creation' && row.kind === 'success'), { timeout: 15_000 }).toBe(true);
    await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
    const observed = (action: string, kind: string) => events.filter(row => row.action === action && row.kind === kind).reduce((sum, row) => sum + row.count, 0);
    expect(observed('organization_creation_disclosure', 'open')).toBe(1);
    for (const kind of ['use', 'exception', 'failure', 'retry', 'success']) expect(observed('organization_creation', kind)).toBe(1);
    for (const row of events) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
      ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
    expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(documents).toBe(1);
    for (const privateValue of [actor, originalId!, account.email, account.password, 'Private telemetry creation', 'Private original creation body', writes[0].key, 'Idempotency-Key']) expect(JSON.stringify(reports)).not.toContain(privateValue);
    const directory = await context.request.get('/organizations/directory'); expect(directory.status()).toBe(200);
    expect((await directory.json()).items.map((row: { organization: { id: string } }) => row.organization.id)).toEqual([originalId]);
    const stored = await context.request.get(`/organizations/${originalId}`); expect(stored.status()).toBe(200);
    expect((await stored.json()).organization).toMatchObject({ version: 1, name: 'Private telemetry creation', description: 'Private original creation body' });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
