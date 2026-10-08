import { expect, test } from './releaseTest';
import type { Request } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-03-TC-01/06/08/11/12: keyboard Organization metadata review and lost acknowledgment at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `settings-${viewport.width}-${Date.now()}@example.test`, password: 'browser-settings-correct-horse', displayName: 'Organization administrator' };
    const registered = await context.request.post('/auth/register', { headers, data: credentials });
    expect(registered.status()).toBe(201); const actor = (await registered.json()).user.id;
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Organization metadata', description: 'Original description' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization;
    const settingsPath = `/app/${org.id}/settings`; const scopePath = `/organizations/${org.id}`;
    let observedVersion = 0, readVersion = 0; const pendingReads = new Map<Request, number>();
    page.on('websocket', socket => {
      if (new URL(socket.url()).pathname !== '/organizations/live/metadata') return;
      const watches = new Set<string>();
      socket.on('framesent', frame => {
        if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== settingsPath) return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          try {
            const value = JSON.parse(part);
            if (value.type === 4 && value.target === 'Watch' && value.arguments?.[0] === org.id && typeof value.invocationId === 'string') watches.add(value.invocationId);
          } catch { /* Passive fixture observation only. */ }
        }
      });
      socket.on('framereceived', frame => {
        if (typeof frame.payload !== 'string' || new URL(page.url()).pathname !== settingsPath) return;
        for (const part of frame.payload.split('\u001e').filter(Boolean)) {
          try {
            const value = JSON.parse(part); const item = value.item;
            if (value.type !== 2 || !watches.has(value.invocationId) || item?.organizationId !== org.id || item.userId !== actor || !Array.isArray(item.page?.events)) continue;
            for (const source of item.page.events)
              if (source.eventType === 'ORGANIZATION_UPDATED' && source.organizationId === org.id && source.entityId === org.id && source.entityType === 'Organization' && Number.isSafeInteger(source.version))
                observedVersion = Math.max(observedVersion, source.version);
          } catch { /* No source, request or application state is injected. */ }
        }
      });
    });
    page.on('request', request => {
      if (observedVersion && new URL(page.url()).pathname === settingsPath && request.method() === 'GET' && new URL(request.url()).pathname === scopePath)
        pendingReads.set(request, observedVersion);
    });
    page.on('response', response => {
      const version = pendingReads.get(response.request()); pendingReads.delete(response.request());
      if (version !== undefined && version === observedVersion && response.status() === 200) readVersion = version;
    });
    page.on('requestfailed', request => pendingReads.delete(request));
    const settled = (version: number) => observedVersion >= version && readVersion === observedVersion;
    await page.goto(`/app/${org.id}`);
    await page.getByRole('link', { name: 'Organization settings', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('Organization metadata');
    const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
    if ((await runtime.json()).mode === 'production')
      await expect(page.getByText('Current settings checked. Review any saved changes before replacing them with your draft.', { exact: true })).toBeVisible();
    await page.getByLabel('Logo URL').fill('http://example.test/logo.png');
    await page.getByRole('button', { name: 'Save Organization settings' }).click();
    await expect(page.getByText('Use a secure HTTPS logo URL without embedded credentials, or leave it empty.')).toBeVisible();
    await page.getByLabel('Logo URL').fill('https://example.test/logo.png');
    await page.getByLabel(/^Organization name/).fill('My reviewed draft');
    expect((await context.request.patch(`/organizations/${org.id}`, { headers, data: { name: 'Other administrator update', description: 'Other description', logoUrl: null, version: 1 } })).status()).toBe(200);
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/The Organization changed elsewhere/)).toBeVisible();
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('My reviewed draft');
    await expect(page.getByRole('button', { name: 'Save Organization settings' })).toBeDisabled();
    await page.getByRole('button', { name: 'Load current settings' }).click();
    await expect(page.getByText('Name: Other administrator update')).toBeVisible();
    await expect.poll(() => settled(2), { timeout: 30_000 }).toBe(true);
    await expect(page.getByRole('button', { name: 'Keep draft after review' })).toBeEnabled();
    await page.getByRole('button', { name: 'Keep draft after review' }).focus(); await page.keyboard.press('Enter');
    const writes: number[] = [];
    const retryKeys: string[] = []; const bodies: string[] = [];
    await page.route(`**/organizations/${org.id}`, async route => {
      if (route.request().method() !== 'PATCH') { await route.continue(); return; }
      writes.push(route.request().postDataJSON().version);
      retryKeys.push(route.request().headers()['idempotency-key']); bodies.push(route.request().postData()!);
      const response = await route.fetch(); expect(response.status()).toBe(200);
      if (writes.length === 1) await route.abort('timedout'); else await route.fulfill({ response });
    });
    await page.getByRole('button', { name: 'Save Organization settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Your save could not be confirmed/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Load current settings' })).toBeDisabled();
    await expect(page.getByLabel(/^Organization name/)).toBeDisabled();
    expect((await context.request.patch(`/organizations/${org.id}`, { headers,
      data: { name: 'Later administrator update', description: 'Later description', logoUrl: null, version: 3 } })).status()).toBe(200);
    await expect.poll(() => settled(4), { timeout: 30_000 }).toBe(true);
    await expect(page.getByRole('button', { name: 'Retry original save' })).toBeEnabled();
    await page.getByRole('button', { name: 'Retry original save' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText(/Original save acknowledgment recovered/)).toBeVisible();
    await page.getByRole('button', { name: 'Load current settings' }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('Name: Later administrator update')).toBeVisible();
    expect(writes).toEqual([2, 2]); expect(retryKeys[0]).toMatch(/^[0-9a-f-]{36}$/);
    expect(retryKeys[1]).toBe(retryKeys[0]); expect(bodies[1]).toBe(bodies[0]);
    const stored = (await (await context.request.get('/organizations')).json()).find((item: { organization: { id: string } }) => item.organization.id === org.id).organization;
    expect(stored).toMatchObject({ name: 'Later administrator update', description: 'Later description', logoUrl: null, version: 4 });
    // Live metadata refresh preserves the draft while temporarily disabling
    // review choices. Keyboard activation must wait for fresh review readiness.
    const discard = page.getByRole('button', { name: 'Discard draft and use current settings' });
    await expect(discard).toBeEnabled(); await discard.focus(); await expect(discard).toBeFocused(); await discard.press('Enter');
    await expect(page.getByLabel(/^Organization name/)).toHaveValue('Later administrator update');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.reload(); await expect(page.getByLabel(/^Organization name/)).toHaveValue('Later administrator update');
    await expect(page.getByText('Organization settings saved.')).toHaveCount(0);
  });
}
