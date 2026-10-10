import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-27: original configuration survives navigation and reload after a lost acknowledgment at ${width}px`, async ({ page, context }) => {
    await page.setViewportSize({ width, height: 844 });
    await registerNotificationAccount(context.request, { email: `configuration-recovery-${width}-${Date.now()}@example.test`,
      password: 'configuration-recovery-correct-horse', displayName: 'Recovery configuration administrator' });
    const headers = { 'X-StrataAI-Request': '1' };
    const created = await context.request.post('/organizations', { headers, data: { name: 'Configuration recovery acceptance' } });
    expect(created.status()).toBe(201); const organizationId = (await created.json()).organization.id;
    const path = `/organizations/${organizationId}/configuration`; const destination = `/app/${organizationId}/configuration`;
    const restoreWorker = scopedBoardWorker(organizationId);
    try {
      const writes: { body: string; key: string }[] = []; let loseAcknowledgment = true;
      let initialMetadata = false; let initialRecheck = false; let metadataGeneration = 0;
      page.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        const generation = metadataGeneration;
        socket.on('framereceived', frame => {
          if (generation !== metadataGeneration) return;
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type === 2 && message.item?.organizationId === organizationId && Array.isArray(message.item.page?.events)) {
              initialMetadata = true; initialRecheck ||= message.item.page.resetRequired || message.item.page.events.length > 0;
            }
          }
        });
      });
      async function waitForInitialAuthority() {
        await expect.poll(() => initialMetadata).toBe(true);
        if (initialRecheck)
          await expect.poll(async () => Number(await page.getByTestId('configuration-authority').getAttribute('data-generation'))).toBeGreaterThan(0);
        await expect(page.getByTestId('configuration-authority')).toHaveAttribute('data-ready', 'true');
        await expect(page.getByRole('button', { name: 'Load current configuration', exact: true })).toBeEnabled();
        await expect(page.getByText('Private configuration is hidden until current authority is confirmed. Your draft and original submission are preserved.', { exact: true })).not.toBeVisible();
      }
      await page.route(url => url.pathname === path, async route => {
        const request = route.request(); if (request.method() !== 'PATCH') { await route.continue(); return; }
        writes.push({ body: request.postData()!, key: request.headers()['idempotency-key'] });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (loseAcknowledgment) {
          loseAcknowledgment = false;
          await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'configuration_source_unavailable' }) });
        } else await route.fulfill({ response });
      });
      await page.goto(`/app/${organizationId.toUpperCase()}/configuration`);
      await waitForInitialAuthority();
      await expect(page.getByRole('link', { name: 'Back to Organization', exact: true })).toHaveAttribute('href', `/app/${organizationId}`);
      for (const [label, value] of [['Legal name', 'Original reviewed legal name'], ['Jurisdiction', 'CA-BC'], ['Organization timezone (IANA)', 'UTC']])
        await page.getByLabel(new RegExp('^' + label.replace(/[()]/g, '\\$&'))).fill(value);
      await page.getByRole('button', { name: 'Review configuration change', exact: true }).click();
      await page.getByRole('dialog', { name: 'Review configuration change', exact: true }).getByRole('button', { name: 'Approve configuration change' }).click();
      await expect(page.getByRole('button', { name: 'Retry original submission', exact: true })).toBeEnabled();
      expect(writes).toHaveLength(1);
      const original = JSON.parse(writes[0].body);
      const later = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { version: 1, configuration: { ...original.configuration, legalName: 'Later authoritative legal name' } } });
      expect(later.status()).toBe(200);
      await page.getByRole('link', { name: 'Back to Organization', exact: true }).click();
      await page.goto(destination);
      metadataGeneration++; initialMetadata = false; initialRecheck = false;
      await page.reload(); await waitForInitialAuthority();
      await expect(page.getByText('Current revision: 2', { exact: true })).toBeVisible();
      await expect(page.getByLabel(/^Legal name/)).toHaveValue('Original reviewed legal name');
      await expect(page.getByRole('button', { name: 'Review configuration change', exact: true })).toBeDisabled();
      expect(writes).toHaveLength(1);
      const retry = page.getByRole('button', { name: 'Retry original submission', exact: true });
      await expect(retry).toBeEnabled(); await retry.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Change acknowledged. Current configuration revision 2.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      await expect(page.getByLabel(/^Legal name/)).toHaveValue('Later authoritative legal name');
      await page.reload(); await expect(page.getByLabel(/^Legal name/)).toHaveValue('Later authoritative legal name');
      await expect(page.getByRole('button', { name: 'Retry original submission', exact: true })).not.toBeVisible();
    } finally { restoreWorker(); }
  });
}
