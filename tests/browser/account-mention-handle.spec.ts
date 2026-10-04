import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

// Real cookie/API/PostgreSQL commands. Only the first already-committed reply
// is replaced by 503; no handle/receipt/identity data is supplied by a fixture.
for (const width of [1280, 390]) {
  test(`PRD-02/15 native handle original recovery, account conflicts and revocation at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `handle-${width}-${Date.now()}@example.test`, password: 'handle-browser-battery-horse', displayName: 'Handle account' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const original = await (await context.request.get('/me/mention-handle')).json();
    expect(original).toMatchObject({ userId: actor, handle: `u_${actor.replaceAll('-', '')}`, userVersion: 1, handleVersion: 1 });
    const path = '/me/mention-handle'; const writes: { key: string | undefined; body: string | null }[] = [];
    await page.route('**' + path, async route => {
      if (route.request().method() !== 'PATCH') return route.continue();
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      const actual = await route.fetch(); expect(actual.status()).toBe(200);
      if (writes.length === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'identity_storage_unavailable' }) });
      return route.fulfill({ response: actual });
    });
    await page.goto('/app/profile');
    const display = page.getByRole('textbox', { name: 'Display name', exact: true });
    await expect(display).toHaveValue('Handle account'); await display.fill('Unsaved handle peer edit');
    const open = page.getByRole('button', { name: 'Change mention handle', exact: true });
    await open.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Your mention handle', exact: true });
    const field = dialog.getByRole('textbox', { name: 'Mention handle', exact: true });
    await expect(field).toHaveValue(original.handle); await expect(field).toBeEnabled();
    expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(true);
    const name = `native_${width}_${Date.now()}`;
    await field.focus(); await field.press('ControlOrMeta+A'); await page.keyboard.insertText(` ${name.toUpperCase()} `);
    await field.press('Enter');
    const retry = dialog.getByRole('button', { name: 'Retry original handle change', exact: true });
    await expect(retry).toBeEnabled(); await expect(retry).toBeFocused(); await expect(field).toBeDisabled();
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeDisabled();
    await page.keyboard.press('Escape'); await expect(dialog).toBeVisible();
    for (const label of ['Save profile', 'Sign out', 'Deactivate account', 'Discard changes'])
      await expect(page.getByRole('button', { name: label, exact: true, includeHidden: true })).toBeDisabled();
    await retry.press('Enter'); await expect(dialog.getByText('Handle change confirmed.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(2); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    expect(writes[1]).toEqual(writes[0]);
    expect(JSON.parse(writes[0].body!)).toEqual({ handle: name, userVersion: 1, handleVersion: 1 });
    const stored = await (await context.request.get(path)).json();
    expect(stored).toMatchObject({ userId: actor, handle: name, userVersion: 2, handleVersion: 2 });
    const stream = await (await context.request.get('/me/sync?after=1')).json();
    expect(stream.events).toHaveLength(1); expect(stream.events[0]).toMatchObject({ eventType: 'USER_PROFILE_UPDATED', version: 2, metadata: {} });
    await dialog.getByRole('button', { name: 'Close', exact: true }).press('Enter'); await expect(dialog).not.toBeVisible();
    await expect(display).toHaveValue('Unsaved handle peer edit');
    await expect(page.getByText('Your profile changed elsewhere. Your edits are preserved; load the latest profile before saving.', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Save profile', exact: true })).toBeDisabled();
    await page.getByRole('button', { name: 'Discard edits and load latest profile', exact: true }).press('Enter');
    await expect(display).toHaveValue('Handle account'); await expect(open).toBeEnabled(); await open.press('Enter');
    await expect(field).toHaveValue(name);
    // A genuine concurrent account change invalidates a new intent before it
    // can be sent with stale root revisions; review obtains the actual setting.
    expect((await context.request.patch('/me', { headers, data: { displayName: 'Peer profile change', version: 2 } })).status()).toBe(200);
    const renamed = `renamed_${width}_${Date.now()}`; await field.fill(renamed); await field.press('Enter');
    await expect(dialog.getByText('Your account changed elsewhere. Review the current setting before saving again.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(2); await expect(dialog.getByRole('button', { name: 'Save handle', exact: true })).toBeDisabled();
    await dialog.getByRole('button', { name: 'Review current setting', exact: true }).press('Enter');
    await expect(field).toHaveValue(name); await expect(field).toBeEnabled(); await field.fill(renamed); await field.press('Enter');
    await expect(dialog.getByText('Handle change confirmed.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(3); expect(writes[2].key).not.toBe(writes[0].key);
    expect(JSON.parse(writes[2].body!)).toEqual({ handle: renamed, userVersion: 3, handleVersion: 2 });
    const former = await context.request.patch(path, { headers: { ...headers, 'Idempotency-Key': writes[0].key! }, data: JSON.parse(writes[0].body!) });
    expect(former.status()).toBe(409); expect(await former.json()).toMatchObject({ code: 'mention_handle_unavailable' });
    expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
    await field.fill(`denied_${width}`); await field.press('Enter'); await expect(page).toHaveURL(/\/login$/);
    await expect(dialog).not.toBeVisible(); expect(writes).toHaveLength(3);
    expect((await context.request.get(path)).status()).toBe(401);
  });
}
