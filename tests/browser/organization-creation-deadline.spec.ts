import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-03-TC-06/07/11/12: complete creation deadline preserves the real committed original at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `creation-deadline-${width}-${Date.now()}@example.test`, password: 'creation-deadline-correct-horse', displayName: 'Creation deadline Owner' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    let releasePost!: () => void, releaseCanonical!: () => void;
    const postGate = new Promise<void>(resolve => { releasePost = resolve; });
    const canonicalGate = new Promise<void>(resolve => { releaseCanonical = resolve; });
    let postHeld = false, canonicalHeld = false, originalId: string | undefined;
    const writes: { key: string; body: string }[] = []; let documents = 0;
    await page.route(url => url.pathname === '/organizations', async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(201);
      if (writes.length === 1) { originalId = (await response.json()).organization.id; postHeld = true; await postGate; }
      await route.fulfill({ response }).catch(() => {});
    });
    await page.route(url => originalId !== undefined && url.pathname === `/organizations/${originalId}`, async route => {
      if (route.request().method() === 'GET' && !canonicalHeld) {
        const response = await route.fetch(); expect(response.status()).toBe(200); canonicalHeld = true; await canonicalGate;
        await route.fulfill({ response }).catch(() => {});
      } else await route.continue();
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    try {
      await page.goto('/app'); await page.getByRole('button', { name: 'Create organization', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('textbox', { name: 'Name', exact: true }).fill('Creation deadline council');
      await page.getByRole('textbox', { name: 'Description', exact: true }).fill('Reserved original creation');
      await page.clock.install(); await page.getByRole('button', { name: 'Create', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect.poll(() => postHeld).toBe(true); await page.clock.runFor(8000); releasePost();
      await expect.poll(() => canonicalHeld).toBe(true); await page.clock.runFor(7001); await page.clock.runFor(500);
      const retry = page.getByRole('button', { name: 'Retry original creation', exact: true }); await expect(retry).toBeEnabled();
      await expect(retry).toBeFocused(); await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toBeDisabled();
      await expect(page).toHaveURL(/\/app$/); expect(writes).toHaveLength(1);
      releaseCanonical(); await expect(page).toHaveURL(/\/app$/);
      await retry.focus(); await page.keyboard.press('Enter'); await expect(page).toHaveURL(new RegExp(`/app/${originalId}$`));
      await expect(page.getByRole('heading', { name: 'Creation deadline council', exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(documents).toBe(1);
      const directory = await context.request.get('/organizations/directory'); expect(directory.status()).toBe(200);
      expect((await directory.json()).items.map((row: { organization: { id: string } }) => row.organization.id)).toEqual([originalId]);
      const stored = await context.request.get(`/organizations/${originalId}`); expect(stored.status()).toBe(200);
      expect((await stored.json()).organization).toMatchObject({ version: 1, name: 'Creation deadline council', description: 'Reserved original creation' });
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { releasePost(); releaseCanonical(); }
  });
}
