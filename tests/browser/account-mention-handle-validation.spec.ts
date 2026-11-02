import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

const validation = 'Use 3–40 letters, digits or underscores, beginning with a letter. Reserved names cannot be chosen.';
const guidance = '3–40 letters, digits or underscores. Start with a letter. Names are case insensitive.';

// Real account, protected reads, commands and persisted state. No HTTP replies
// or account/handle identities are supplied by a browser route fixture.
for (const viewport of [
  { name: '320px', width: 320, height: 844, textZoom: false },
  { name: '390px', width: 390, height: 844, textZoom: false },
  { name: '768px', width: 768, height: 1024, textZoom: false },
  { name: '1280px', width: 1280, height: 844, textZoom: false },
  { name: '768px with 200% text', width: 768, height: 1024, textZoom: true },
]) {
  test(`PRD-15 native handle field validation, deliberate save and return at ${viewport.name}`, async ({ page, context }, info) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    if (viewport.textZoom) await context.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => {
        document.documentElement.style.fontSize = '32px';
      }, { once: true });
    });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `handle-field-${viewport.width}-${viewport.textZoom}-${Date.now()}@example.test`,
      password: 'handle-field-browser-battery-horse', displayName: 'Handle field account' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const original = await (await context.request.get('/me/mention-handle')).json();
    expect(original).toMatchObject({ userId: actor, handle: `u_${actor.replaceAll('-', '')}`, userVersion: 1, handleVersion: 1 });
    const reads: string[] = [];
    const writes: { key: string | undefined; subject: string | undefined; body: string | null }[] = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (request.method() === 'GET' && ['/me', '/me/mention-handle'].includes(path)) reads.push(path);
      if (path === '/me/mention-handle' && request.method() === 'PATCH') writes.push({
        key: request.headers()['idempotency-key'], subject: request.headers()['x-strataai-expected-user'], body: request.postData(),
      });
    });
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    await page.goto('/app/profile');
    if (viewport.textZoom) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
    const open = page.getByRole('button', { name: 'Change mention handle', exact: true });
    await expect(open).toBeEnabled(); await open.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Your mention handle', exact: true });
    const field = dialog.getByRole('textbox', { name: 'Mention handle', exact: true });
    await expect(field).toBeEnabled(); await expect(field).toHaveValue(original.handle); await expect(field).toBeFocused();
    await expect(field).toHaveAccessibleDescription(guidance);
    const admittedReads = [...reads];
    for (const invalid of ['', '_invalid', 'board']) {
      await field.fill(invalid); await field.press('Enter');
      await expect(field).toHaveAttribute('aria-invalid', 'true');
      await expect(field).toHaveAccessibleDescription(validation);
      await expect(dialog.getByRole('alert')).toHaveText(validation);
      await expect(field).toHaveValue(invalid); await expect(field).toBeFocused(); await expect(field).toBeInViewport();
      expect(await field.evaluate(element => {
        const rect = element.getBoundingClientRect();
        return document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2) === element;
      })).toBe(true);
      expect(reads).toEqual(admittedReads); expect(writes).toEqual([]);
      expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    await page.screenshot({ path: info.outputPath('invalid-handle-field.png'), fullPage: true });
    const handle = `field_${viewport.width}_${Date.now()}`;
    await field.fill(` ${handle.toUpperCase()} `);
    await expect(field).not.toHaveAttribute('aria-invalid', 'true');
    await expect(field).toHaveAccessibleDescription(guidance); await expect(dialog.getByRole('alert')).toHaveCount(0);
    expect(reads).toEqual(admittedReads); expect(writes).toEqual([]);
    await field.press('Enter');
    await expect(dialog.getByText('Handle change confirmed.', { exact: true })).toBeVisible();
    expect(writes).toHaveLength(1); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[0].subject).toBe(actor);
    expect(JSON.parse(writes[0].body!)).toEqual({ handle, userVersion: 1, handleVersion: 1 });
    const stored = await (await context.request.get('/me/mention-handle')).json();
    expect(stored).toMatchObject({ userId: actor, handle, userVersion: 2, handleVersion: 2 });
    const stream = await (await context.request.get('/me/sync?after=1')).json();
    expect(stream.events).toHaveLength(1);
    expect(stream.events[0]).toMatchObject({ eventType: 'USER_PROFILE_UPDATED', version: 2, metadata: {} });
    expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
    await dialog.getByRole('button', { name: 'Close', exact: true }).press('Enter');
    await expect(dialog).not.toBeVisible(); await expect(open).toBeFocused(); await expect(open).toBeInViewport();
    expect(await open.evaluate(element => {
      const rect = element.getBoundingClientRect();
      const hit = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
      return hit !== null && element.contains(hit);
    })).toBe(true);
    await open.press('Enter'); await expect(field).toHaveValue(handle); await expect(field).toBeEnabled();
    await expect(field).toBeFocused();
    await page.screenshot({ path: info.outputPath('persisted-handle-field.png'), fullPage: true });
    expect(writes).toHaveLength(1); expect(pageErrors).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
