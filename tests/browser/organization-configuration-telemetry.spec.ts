import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-27: configuration telemetry observes exact recovery without private material at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
    const account = { email: `configuration-metrics-${width}-${crypto.randomUUID()}@example.test`,
      password: 'configuration-metrics-correct-horse', displayName: 'Private configuration telemetry Owner' };
    const owner = await registerNotificationAccount(context.request, account, production);
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Private telemetry Organization' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    const path = `/organizations/${organization}/configuration`;
    const first = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
      data: { version: 0, configuration: { legalName: 'Private telemetry legal name', jurisdiction: 'CA-BC', timezone: 'UTC' } } });
    expect(first.status()).toBe(200);
    const restoreWorker = scopedBoardWorker(organization);
    const reports: { events: { action: string; kind: string; count: number; durationMs?: number }[] }[] = [];
    const statuses: number[] = []; const writes: { key: string; body: string }[] = [];
    const bootstrapped = new Set<string>();
    page.on('websocket', socket => {
      const endpoint = new URL(socket.url()).pathname;
      if (!['/organizations/live/configuration', '/organizations/live/metadata'].includes(endpoint)) return;
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          try { if (JSON.parse(part).type === 2) bootstrapped.add(endpoint); } catch { /* Partial handshake, no usable source. */ }
        }
      });
    });
    page.on('request', request => {
      if (new URL(request.url()).pathname === '/me/activity-client-events' && request.method() === 'POST') reports.push(request.postDataJSON());
    });
    page.on('response', response => { if (new URL(response.url()).pathname === '/me/activity-client-events') statuses.push(response.status()); });
    await page.route(url => url.pathname === path, async route => {
      if (route.request().method() !== 'PATCH') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(200);
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    try {
      await page.goto(`/app/${organization}/configuration`);
      await expect.poll(() => bootstrapped.size).toBe(2);
      await expect(page.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true');
      await page.getByLabel(/^Legal name/).fill('Private proposed telemetry legal name');
      const review = page.getByRole('button', { name: 'Review configuration change', exact: true });
      await expect(review).toBeEnabled(); await review.focus(); await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Review configuration change', exact: true }); await expect(dialog).toBeVisible();
      await dialog.getByRole('button', { name: 'Approve configuration change' }).focus(); await page.keyboard.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original submission' }); await expect(retry).toBeVisible();
      await expect(retry).toBeEnabled(); await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Change acknowledged. Current configuration revision 2.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'View configuration history' }).click();
      await expect(page.getByRole('heading', { name: 'Configuration history', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Load configuration Activity' }).click();
      await expect(page.getByText('Configuration changed at revision 2', { exact: true })).toBeVisible();
      const events = () => reports.flatMap(report => report.events);
      for (const kind of ['use', 'retry', 'exception', 'failure', 'success'])
        await expect.poll(() => events().some(row => row.action === 'organization_configuration_change' && row.kind === kind), { timeout: 15_000 }).toBe(true);
      for (const action of ['organization_configuration_history', 'organization_configuration_activity'])
        await expect.poll(() => events().some(row => row.action === action && row.kind === 'success'), { timeout: 15_000 }).toBe(true);
      await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
      for (const row of events()) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
        ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      const serialized = JSON.stringify(reports);
      for (const secret of [owner.user.id, organization, account.email, account.password, account.displayName,
        'Private telemetry Organization', 'Private telemetry legal name', 'Private proposed telemetry legal name', path, writes[0].key]) expect(serialized).not.toContain(secret);
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      const current = await context.request.get(path); expect(current.status()).toBe(200);
      expect((await current.json()).revision.configuration.legalName).toBe('Private proposed telemetry legal name');
    } finally { restoreWorker(); }
  });
}
