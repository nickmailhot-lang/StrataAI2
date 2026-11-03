import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import type { Locator, TestInfo } from '@playwright/test';

// Canonical protected reads and commands; only the first actual setting reply
// is held until native dialog entry settles. No response body is fabricated.
async function nativeState(target: Locator, field = false) {
  return target.evaluate((node, isField) => {
    const rect = node.getBoundingClientRect();
    const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
    const indicator = isField ? node.closest('.MuiOutlinedInput-root')?.querySelector('fieldset') : node;
    const style = indicator ? getComputedStyle(indicator) : undefined;
    return { active: document.activeElement === node, focusVisible: node.matches(':focus-visible'),
      viewport: rect.left >= 0 && rect.top >= 0 && rect.right <= innerWidth && rect.bottom <= innerHeight,
      centerHit: hit !== null && node.contains(hit),
      indicatorWidth: isField ? style?.borderTopWidth : style?.outlineWidth,
      indicatorStyle: isField ? style?.borderTopStyle : style?.outlineStyle,
      indicatorColor: isField ? style?.borderTopColor : style?.outlineColor };
  }, field);
}
async function capture(target: Locator, name: string, info: TestInfo, field = false) {
  await expect.poll(() => nativeState(target, field)).toMatchObject({ active: true, focusVisible: true,
    viewport: true, centerHit: true, indicatorWidth: '2px', indicatorStyle: 'solid' });
  const before = await nativeState(target, field);
  expect(before).toMatchObject({ active: true, focusVisible: true, viewport: true, centerHit: true,
    indicatorWidth: '2px', indicatorStyle: 'solid' });
  expect(before.indicatorColor).not.toBe('rgba(0, 0, 0, 0)');
  await target.page().screenshot({ path: info.outputPath(name + '.png'), fullPage: false });
  const after = await nativeState(target, field);
  await info.attach(name + '-native-state', { body: JSON.stringify({ before, after }), contentType: 'application/json' });
  expect(after).toEqual(before);
}
for (const viewport of [
  { name: '320px', width: 320, height: 844, zoom: false },
  { name: '390px', width: 390, height: 844, zoom: false },
  { name: '768px', width: 768, height: 1024, zoom: false },
  { name: '1280px', width: 1280, height: 844, zoom: false },
  { name: '768px with 200% text', width: 768, height: 1024, zoom: true },
]) {
  test(`PRD-15 protected handle admission owns native focus, canonical save and return at ${viewport.name}`, async ({ page, context }, info) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    if (viewport.zoom) await context.addInitScript(() => document.addEventListener('DOMContentLoaded', () => {
      document.documentElement.style.fontSize = '32px';
    }, { once: true }));
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `handle-admission-${viewport.width}-${Date.now()}@example.test`,
      password: 'handle-admission-browser-battery-horse', displayName: 'Handle admission account' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const account = await context.request.get('/me'); expect(account.status()).toBe(200);
    const actor = (await account.json()).id as string;
    const originalResponse = await context.request.get('/me/mention-handle'); expect(originalResponse.status()).toBe(200);
    const original = await originalResponse.json();
    expect(original).toMatchObject({ userId: actor, handle: `u_${actor.replaceAll('-', '')}`, userVersion: 1, handleVersion: 1 });
    let release: () => void = () => {};
    const gate = new Promise<void>(resolve => { release = resolve; });
    let held = false; let fetched = false; let fulfilled = false;
    const writes: { key?: string; subject?: string; csrf?: string; body: string | null }[] = [];
    const receipts: { status: number; body: unknown }[] = [];
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route(url => url.pathname === '/me/mention-handle', async route => {
      if (route.request().method() === 'GET' && !held) {
        held = true;
        const response = await route.fetch(); expect(response.status()).toBe(200);
        expect(await response.json()).toEqual(original); fetched = true;
        await gate; await route.fulfill({ response }); fulfilled = true; return;
      }
      if (route.request().method() === 'PATCH') {
        const request = route.request();
        writes.push({ key: request.headers()['idempotency-key'], subject: request.headers()['x-strataai-expected-user'],
          csrf: request.headers()['x-strataai-request'], body: request.postData() });
        const response = await route.fetch(); receipts.push({ status: response.status(), body: await response.json() });
        await route.fulfill({ response }); return;
      }
      await route.continue();
    });
    try {
      await page.goto('/app/profile');
      if (viewport.zoom) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
      const open = page.getByRole('button', { name: 'Change mention handle', exact: true });
      await expect(open).toBeEnabled(); await open.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Your mention handle', exact: true });
      const field = dialog.getByRole('textbox', { name: 'Mention handle', exact: true });
      const close = dialog.getByRole('button', { name: 'Close', exact: true });
      await expect.poll(() => fetched).toBe(true);
      await expect(dialog).toBeFocused(); await expect(field).toBeDisabled(); await expect(close).toBeDisabled();
      await expect(dialog.getByRole('button', { name: 'Save handle', exact: true })).toBeDisabled();
      // MUI Fade owns the container above the dialog paper. Observe the actual
      // ancestor transition too; a paper-subtree query cannot establish entry.
      const entryState = () => dialog.evaluate(node => {
        const container = node.closest('.MuiDialog-container');
        const ancestors: Element[] = [];
        for (let current: Element | null = node; current; current = current.parentElement) ancestors.push(current);
        const animations = new Set([
          ...node.getAnimations({ subtree: true }),
          ...ancestors.flatMap(element => element.getAnimations()),
        ]);
        return { containerPresent: container !== null,
          containerOpacity: container ? getComputedStyle(container).opacity : null,
          ancestorOpacities: ancestors.map(element => getComputedStyle(element).opacity),
          finiteAnimationsRunning: [...animations].filter(animation =>
            animation.effect?.getTiming().iterations !== Infinity && animation.playState !== 'finished').length,
          dialogOwnsFocus: document.activeElement === node,
          busy: node.querySelector('form')?.getAttribute('aria-busy') };
      });
      const requireEntered = (state: Awaited<ReturnType<typeof entryState>>) => {
        expect(state).toMatchObject({ containerPresent: true, containerOpacity: '1',
          finiteAnimationsRunning: 0, dialogOwnsFocus: true, busy: 'true' });
        expect(state.ancestorOpacities.every(opacity => opacity === '1')).toBe(true);
      };
      await expect(async () => requireEntered(await entryState())).toPass({ timeout: 5_000 });
      expect(fulfilled).toBe(false); expect(writes).toEqual([]);
      const entryBefore = await entryState(); requireEntered(entryBefore);
      await page.screenshot({ path: info.outputPath('protected-read-pending.png'), fullPage: false });
      const entryAfter = await entryState(); requireEntered(entryAfter);
      await info.attach('pending-native-entry-state', {
        body: JSON.stringify({ before: entryBefore, after: entryAfter }), contentType: 'application/json',
      });
      expect(fulfilled).toBe(false); await expect(field).toBeDisabled(); await expect(close).toBeDisabled();
      release(); await expect.poll(() => fulfilled).toBe(true);
      await expect(field).toBeEnabled(); await expect(field).toHaveValue(original.handle);
      // Judge the destination before any explicit focusing or text insertion.
      await capture(field, 'admitted-field-focus', info, true);
      await field.press('Tab'); await expect(close).toBeFocused();
      await capture(close, 'deliberate-close-focus', info);
      await close.press('Shift+Tab'); await expect(field).toBeFocused();
      const handle = `admit_${viewport.width}_${Date.now()}`;
      await field.press('ControlOrMeta+A'); await page.keyboard.insertText(` ${handle.toUpperCase()} `);
      await field.press('Enter');
      await expect(dialog.getByText('Handle change confirmed.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(1); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(writes[0].subject).toBe(actor); expect(writes[0].csrf).toBe('1');
      expect(JSON.parse(writes[0].body!)).toEqual({ handle, userVersion: 1, handleVersion: 1 });
      expect(receipts).toHaveLength(1); expect(receipts[0].status).toBe(200);
      expect(receipts[0].body).toMatchObject({ userId: actor, handle, userVersion: 2, handleVersion: 2 });
      const storedResponse = await context.request.get('/me/mention-handle'); expect(storedResponse.status()).toBe(200);
      expect(await storedResponse.json()).toMatchObject({ userId: actor, handle, userVersion: 2, handleVersion: 2 });
      const sync = await context.request.get('/me/sync?after=1'); expect(sync.status()).toBe(200);
      const events = (await sync.json()).events;
      expect(events).toHaveLength(1); expect(events[0]).toMatchObject({ eventType: 'USER_PROFILE_UPDATED', version: 2, metadata: {} });
      expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
      // Choosing Close is deliberate; the return destination is never focused by this test.
      await close.press('Enter'); await expect(dialog).not.toBeVisible();
      await capture(open, 'native-profile-return', info);
      await open.press('Enter'); await expect(field).toBeEnabled(); await expect(field).toHaveValue(handle);
      await capture(field, 'reopened-persisted-handle', info, true);
      expect(writes).toHaveLength(1); expect(errors).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    } finally { release(); }
  });
}
