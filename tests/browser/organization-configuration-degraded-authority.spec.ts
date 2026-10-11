import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-27: invalid unsent configuration survives failed admission and is withdrawn after membership removal at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const production = process.env.STRATAAI_E2E_RUNTIME_MODE !== 'demo';
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {}; let refuseRead = false; let writes = 0;
    try {
      const nonce = crypto.randomUUID();
      await registerNotificationAccount(owner.request, { email: `degraded-owner-${nonce}@example.test`,
        password: 'degraded-owner-correct-horse', displayName: 'Private degraded Owner' }, production);
      const email = `degraded-admin-${nonce}@example.test`;
      const admin = await registerNotificationAccount(context.request, { email,
        password: 'degraded-admin-correct-horse', displayName: 'Private degraded Admin' }, production);
      const headers = { 'X-StrataAI-Request': '1' };
      const created = await owner.request.post('/organizations', { headers, data: { name: 'Private degraded Organization' } });
      expect(created.status()).toBe(201); const organization = (await created.json()).organization.id;
      const path = `/organizations/${organization}/configuration`;
      const first = await owner.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 0, configuration: { legalName: 'Private retained original legal name', jurisdiction: 'CA-BC', timezone: 'UTC' } } });
      expect(first.status()).toBe(200);
      const invitation = await owner.request.post(`/organizations/${organization}/invitations`, { headers,
        data: { email, surface: 'INTERNAL', targetRole: 'ADMIN' } });
      expect(invitation.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      restoreWorker = scopedBoardWorker(organization);
      const streams = new Set<string>();
      page.on('websocket', socket => {
        const endpoint = new URL(socket.url()).pathname;
        if (!['/organizations/live/configuration', '/organizations/live/metadata'].includes(endpoint)) return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\u001e').filter(Boolean)) {
            try { if (JSON.parse(raw).type === 2) streams.add(endpoint); } catch { /* Handshake fragment. */ }
          }
        });
      });
      page.on('request', request => { if (new URL(request.url()).pathname === path && request.method() === 'PATCH') writes++; });
      await page.route(url => url.pathname === path, async route => {
        if (refuseRead && route.request().method() === 'GET') {
          await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({
            code: 'configuration_source_unavailable', detail: 'Private diagnostic must stay hidden' }) });
        } else await route.continue();
      });
      await page.goto(`/app/${organization}/configuration`);
      await expect.poll(() => streams.size).toBe(2);
      await expect(page.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true');
      await page.getByLabel(/^Legal name/).fill('Private invalid unsent legal proposal');
      await page.getByLabel('Lot count', { exact: true }).fill('0');
      refuseRead = true;
      const load = page.getByRole('button', { name: 'Load current configuration' });
      await load.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Current configuration could not be checked. Your draft and any original submission are preserved.', { exact: true })).toBeVisible();
      expect(await page.locator('body').innerText()).not.toContain('Private diagnostic must stay hidden');
      expect(writes).toBe(0);
      refuseRead = false; await expect(load).toBeEnabled(); await load.focus(); await page.keyboard.press('Enter');
      await expect(page.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true');
      await expect(page.getByLabel(/^Legal name/)).toHaveValue('Private invalid unsent legal proposal');
      await expect(page.getByLabel('Lot count', { exact: true })).toHaveValue('0');
      const review = page.getByRole('button', { name: 'Review configuration change', exact: true });
      await review.focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('alert').filter({ hasText: /Lot count/ })).toBeVisible();
      await expect(page.getByRole('dialog')).toHaveCount(0); expect(writes).toBe(0);
      await page.getByLabel('Lot count', { exact: true }).fill('15'); await review.focus(); await page.keyboard.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Review configuration change', exact: true });
      await expect(dialog).toBeVisible(); await expect(dialog.getByText('Private invalid unsent legal proposal', { exact: true })).toBeVisible();
      await dialog.getByRole('button', { name: 'Return to draft' }).focus(); await page.keyboard.press('Enter');
      await expect(dialog).not.toBeVisible(); await expect(review).toBeFocused();
      await page.getByRole('button', { name: 'View configuration history' }).click();
      await expect(page.getByRole('heading', { name: 'Configuration history', exact: true })).toBeVisible();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const member = await owner.request.get(`/organizations/${organization}/members/${admin.user.id}`); expect(member.status()).toBe(200);
      const version = (await member.json()).member.version;
      const removed = await owner.request.delete(`/organizations/${organization}/members/${admin.user.id}?expectedVersion=${version}`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } }); expect(removed.status()).toBe(204);
      await expect(page.getByLabel(/^Legal name/)).toHaveCount(0, { timeout: 10_000 });
      await expect(page.getByRole('heading', { name: 'Configuration history', exact: true })).toHaveCount(0);
      await expect(page.getByRole('dialog')).toHaveCount(0);
      for (const value of ['Private invalid unsent legal proposal', 'Private retained original legal name', 'Private diagnostic must stay hidden'])
        expect(await page.locator('body').innerText()).not.toContain(value);
      expect((await context.request.get('/me')).status()).toBe(200);
      for (const endpoint of [path, `${path}/history`, `${path}/activity`]) {
        const denied = await context.request.get(endpoint); expect(denied.status()).toBe(404);
        expect(await denied.text()).not.toContain('Private retained original legal name');
      }
      const retained = await owner.request.get(path); expect(retained.status()).toBe(200);
      const current = await retained.json(); expect(current.version).toBe(1);
      expect(current.revision.configuration.legalName).toBe('Private retained original legal name'); expect(writes).toBe(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { refuseRead = false; await owner.close(); restoreWorker(); }
  });
}
