import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-05/06/07/11/12: deletion deadline and final account admission retain only the original request at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const observer = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `delete-admission-${width}-${Date.now()}@example.test`, password: 'delete-admission-correct-horse', displayName: 'Deletion admission Owner' };
    const other = { ...account, email: `delete-other-${width}-${Date.now()}@example.test`, displayName: 'Different account' };
    let releaseProfile!: () => void; let releaseFirst!: () => void; let releaseSecond!: () => void;
    const profileGate = new Promise<void>(done => { releaseProfile = done; });
    const firstGate = new Promise<void>(done => { releaseFirst = done; });
    const secondGate = new Promise<void>(done => { releaseSecond = done; });
    try {
      const registered = await context.request.post('/auth/register', { headers, data: account }); expect(registered.status()).toBe(201);
      const actor = (await registered.json()).user.id;
      expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      expect((await observer.request.post('/auth/register', { headers, data: other })).status()).toBe(201);
      expect((await observer.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const created = await context.request.post('/organizations', { headers, data: { name: 'Private deletion admission council' } });
      expect(created.status()).toBe(201); const original = (await created.json()).organization; const org = original.id;
      expect((await (await context.request.get('/api/runtime')).json()).mode).toBe('production');
      await page.goto(`/app/${org}/delete`);
      await pressAdmittedAction(page.getByRole('button', { name: 'Review deletion request', exact: true }));
      await expect(page.getByRole('button', { name: 'Cancel deletion request', exact: true })).toBeFocused();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      let profileHeld = false, firstHeld = false, secondHeld = false;
      const writes: { url: string; key: string }[] = []; let documents = 0;
      page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
      await page.route(url => url.pathname === '/me', async route => {
        if (profileHeld) { await route.continue(); return; }
        const response = await route.fetch(); expect(response.status()).toBe(200);
        profileHeld = true; await profileGate; await route.fulfill({ response }).catch(() => {});
      });
      await page.route(url => url.pathname === `/organizations/${org}`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
        const response = await route.fetch(); expect(response.status()).toBe(202);
        if (writes.length === 1) { firstHeld = true; await firstGate; }
        else { secondHeld = true; await secondGate; }
        await route.fulfill({ response }).catch(() => {});
      });
      await page.clock.install();
      const confirm = page.getByRole('button', { name: 'Confirm deletion request', exact: true }); await confirm.focus(); await confirm.press('Enter');
      await expect.poll(() => profileHeld).toBe(true); await page.clock.runFor(8_000); releaseProfile();
      await expect.poll(() => firstHeld).toBe(true); await page.clock.runFor(7_001); await page.clock.runFor(500);
      const retry = page.getByRole('button', { name: 'Retry original deletion request', exact: true }); await expect(retry).toBeEnabled();
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByText('Private deletion admission council', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('status')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Review current deletion permission', exact: true })).toBeDisabled();
      expect(writes).toHaveLength(1); expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      releaseFirst(); await page.clock.runFor(500); await expect(page.getByRole('status')).toHaveCount(0);
      await retry.focus(); await retry.press('Enter'); await expect.poll(() => secondHeld).toBe(true);
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      const originalUrl = new URL(writes[0].url);
      expect(originalUrl.searchParams.get('expectedActorId')).toBe(actor); expect(originalUrl.searchParams.get('version')).toBe(String(original.version));
      expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      // The server already accepted the original Owner's receipt. A different
      // cookie principal before browser acknowledgment must still withdraw it.
      expect((await context.request.post('/auth/login', { headers, data: other })).status()).toBe(200);
      releaseSecond(); await page.clock.runFor(500);
      await expect(page).toHaveURL(/\/login$/); await expect(page.getByRole('status')).toHaveCount(0);
      expect(await page.evaluate(key => sessionStorage.getItem(key), `strataai.organization-deletion.v1:${org}`)).toBeNull();
      expect(documents).toBe(0); expect(writes).toHaveLength(2);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      const observed = await observer.request.get(`/organizations/${org}/deletion-requests/${writes[0].key}?expectedActorId=${actor}`);
      expect(observed.status()).toBe(200); const snapshot = await observed.json();
      expect(snapshot.requestId).toBe(writes[0].key); expect(['PENDING', 'COMPLETED']).toContain(snapshot.state);
      expect(snapshot.version).toBe(original.version + (snapshot.state === 'COMPLETED' ? 2 : 1));
    } finally { releaseProfile(); releaseFirst(); releaseSecond(); await observer.close(); }
  });
}
