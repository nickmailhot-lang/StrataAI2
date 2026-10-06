import { expect, test } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';
import AxeBuilder from '@axe-core/playwright';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/08/09/11/12: live settings preserves drafts and original receipts at ${width}px`, async ({ page, context }) => {
    test.setTimeout(240_000);
    await page.setViewportSize({ width, height: 844 });
    const mirror = await context.newPage(); await mirror.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `live-settings-${width}-${Date.now()}@example.test`, password: 'live-settings-correct-horse', displayName: 'Live settings Owner' };
    let restoreWorker = () => {};
    try {
      expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
      expect((await runtime.json()).mode).toBe('production');
      const created = await context.request.post('/organizations', { headers, data: { name: 'Live settings Organization' } });
      expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
      restoreWorker = scopedBoardWorker(org);
      let deliveredVersion = 0;
      mirror.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type !== 2 || message.item?.organizationId !== org) continue;
            for (const source of message.item.page.events)
              if (source.eventType === 'ORGANIZATION_UPDATED') deliveredVersion = Math.max(deliveredVersion, source.version);
          }
        });
      });
      await page.goto(`/app/${org}`); await mirror.goto(`/app/${org}/settings`);
      await expect(mirror.getByLabel(/^Organization name/)).toHaveValue('Live settings Organization');
      await expect(mirror.getByText('Current settings checked. Review any saved changes before replacing them with your draft.', { exact: true })).toBeVisible();
      const input = mirror.getByLabel(/^Organization name/); await input.fill('Original unsaved draft');
      expect((await context.request.patch(`/organizations/${org}`, { headers, data: { name: 'Other saved version', version: 1 } })).status()).toBe(200);
      await expect.poll(() => deliveredVersion, { timeout: 30_000 }).toBe(2);
      await expect(page.getByRole('heading', { name: 'Other saved version', exact: true })).toBeVisible();
      await expect(mirror.getByText('Name: Other saved version', { exact: true })).toBeVisible();
      await expect(input).toHaveValue('Original unsaved draft');
      const save = mirror.getByRole('button', { name: 'Save Organization settings', exact: true });
      await expect(save).toBeDisabled();
      await mirror.getByRole('button', { name: 'Keep draft after review', exact: true }).focus(); await mirror.keyboard.press('Enter');
      const originals: { key: string; body: string }[] = [];
      await mirror.route(`**/organizations/${org}`, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        originals.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
        const acknowledgment = await route.fetch(); expect(acknowledgment.status()).toBe(200);
        if (originals.length === 1) await route.abort('failed'); else await route.fulfill({ response: acknowledgment });
      });
      await save.focus(); await mirror.keyboard.press('Enter');
      await expect(mirror.getByText(/Your save could not be confirmed/)).toBeVisible();
      await expect.poll(() => deliveredVersion, { timeout: 30_000 }).toBe(3);
      expect((await context.request.patch(`/organizations/${org}`, { headers, data: { name: 'Later saved version', version: 3 } })).status()).toBe(200);
      await expect.poll(() => deliveredVersion, { timeout: 30_000 }).toBe(4);
      await expect(mirror.getByText('Name: Later saved version', { exact: true })).toBeVisible();
      await expect(input).toHaveValue('Original unsaved draft'); await expect(input).toBeDisabled();
      await expect(mirror.getByRole('button', { name: 'Keep draft after review', exact: true })).toBeDisabled();
      const retry = mirror.getByRole('button', { name: 'Retry original save', exact: true });
      await expect(retry).toBeEnabled(); await retry.focus(); await mirror.keyboard.press('Enter');
      await expect(mirror.getByText(/Original save acknowledgment recovered/)).toBeVisible();
      expect(originals).toHaveLength(2); expect(originals[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(originals[1]).toEqual(originals[0]); expect(JSON.parse(originals[0].body).version).toBe(2);
      await mirror.getByRole('button', { name: 'Discard draft and use current settings', exact: true }).focus();
      await mirror.keyboard.press('Enter'); await expect(input).toHaveValue('Later saved version');
      expect((await new AxeBuilder({ page: mirror }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()).violations).toEqual([]);
      expect(await mirror.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    } finally { await mirror.close(); restoreWorker(); }
  });
}
