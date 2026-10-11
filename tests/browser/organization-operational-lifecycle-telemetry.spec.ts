import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-27: lifecycle telemetry observes original recovery without private values at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
    const account = { email: `lifecycle-metrics-${width}-${crypto.randomUUID()}@example.test`,
      password: 'lifecycle-metrics-correct-horse', displayName: 'Private lifecycle telemetry Owner' };
    const owner = await registerNotificationAccount(context.request, account, production);
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Private lifecycle telemetry Organization' } });
    expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
    const path = `/organizations/${organization}/operational-lifecycle`;
    const restoreWorker = scopedBoardWorker(organization);
    const reports: { events: { action: string; kind: string; count: number; durationMs?: number }[] }[] = [];
    const statuses: number[] = []; const writes: { key: string; body: string }[] = [];
    let bootstrapped = false;
    page.on('websocket', socket => {
      if (new URL(socket.url()).pathname !== '/organizations/live/operational-lifecycle') return;
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string') return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          try { if (JSON.parse(part).type === 2) bootstrapped = true; } catch { /* Handshake fragment. */ }
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
      await page.goto(`/app/${organization}/operational-lifecycle`);
      await expect.poll(() => bootstrapped).toBe(true);
      const launch = page.getByRole('button', { name: 'Review lifecycle change', exact: true });
      await expect(launch).toBeEnabled(); await launch.focus(); await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Confirm lifecycle change', exact: true }); await expect(dialog).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Cancel lifecycle change' })).toBeFocused();
      await page.keyboard.press('Tab'); await page.keyboard.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original lifecycle change' });
      await expect(retry).toBeVisible(); await expect(retry).toBeEnabled();
      // Another acknowledged transition supersedes the lost original result.
      const suspended = await context.request.get(path); expect(suspended.status()).toBe(200);
      expect((await suspended.json()).status).toBe('SUSPENDED');
      const later = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID(),
        'X-StrataAI-Expected-Actor': owner.user.id }, data: { version: 2, status: 'ACTIVE' } });
      expect(later.status()).toBe(200); expect((await later.json()).version).toBe(3);
      await retry.focus(); await page.keyboard.press('Enter');
      const acknowledgment = page.getByRole('status').filter({ hasText: 'Original change acknowledged: Suspended, revision 2.' });
      await expect(acknowledgment).toBeVisible(); await expect(acknowledgment).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      await page.getByRole('button', { name: 'Load current lifecycle' }).click();
      await expect(page.getByText(/Current lifecycle: Active/)).toBeVisible();
      await expect(page.getByText(/Revision 3/)).toBeVisible();
      const events = () => reports.flatMap(report => report.events);
      for (const kind of ['use', 'retry', 'exception', 'failure', 'success'])
        await expect.poll(() => events().some(row => row.action === 'organization_lifecycle_change' && row.kind === kind), { timeout: 15_000 }).toBe(true);
      await expect.poll(() => events().some(row => row.action === 'organization_lifecycle_disclosure' && row.kind === 'open'), { timeout: 15_000 }).toBe(true);
      await expect.poll(() => events().some(row => row.action === 'organization_lifecycle_read' && row.kind === 'retry'), { timeout: 15_000 }).toBe(true);
      await expect.poll(() => statuses.length).toBe(reports.length); expect(statuses.every(status => status === 204)).toBe(true);
      for (const row of events()) expect(Object.keys(row).sort()).toEqual(row.durationMs === undefined
        ? ['action', 'count', 'kind'] : ['action', 'count', 'durationMs', 'kind']);
      const serialized = JSON.stringify(reports);
      for (const secret of [owner.user.id, organization, account.email, account.password, account.displayName,
        'Private lifecycle telemetry Organization', path, writes[0].key, writes[0].body, 'SUSPENDED', 'ACTIVE', 'ORGANIZATION_OWNER']) expect(serialized).not.toContain(secret);
      const current = await context.request.get(path); expect(current.status()).toBe(200);
      expect((await current.json()).version).toBe(3);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { restoreWorker(); }
  });
}
