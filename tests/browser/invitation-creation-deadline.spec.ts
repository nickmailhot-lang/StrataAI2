import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const boardSurface of [false, true]) {
  test(`PRD-03/05/60: aggregate invitation deadline preserves original committed request (Board=${boardSurface}, ${width}px)`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; const suffix = `${boardSurface}-${width}-${Date.now()}`;
    const account = { email: `create-deadline-owner-${suffix}@example.test`, password: 'create-deadline-correct-horse', displayName: 'Creation reviewer' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const created = await context.request.post('/organizations', { headers, data: { name: 'Creation deadline scope' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id;
    let board: string | undefined;
    if (boardSurface) {
      const result = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Creation deadline Board' } });
      expect(result.status()).toBe(201); board = (await result.json()).id;
    }
    const root = board ? `/boards/${board}/invitations` : `/organizations/${org}/invitations`;
    const email = `create-deadline-recipient-${suffix}@example.test`;
    let holdProfile = false, profileHeld = false, commandHeld = false, documents = 0;
    let releaseProfile!: () => void, releaseCommand!: () => void;
    const profileGate = new Promise<void>(resolve => { releaseProfile = resolve; });
    const commandGate = new Promise<void>(resolve => { releaseCommand = resolve; });
    const writes: { key: string; body: string }[] = []; let originalId: string | undefined;
    await page.route(url => url.pathname === '/me', async route => {
      if (holdProfile) { holdProfile = false; profileHeld = true; await profileGate; }
      await route.continue().catch(() => {});
    });
    await page.route(url => url.pathname === root, async route => {
      if (route.request().method() !== 'POST') { await route.continue(); return; }
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      const response = await route.fetch(); expect(response.status()).toBe(201);
      if (writes.length === 1) { originalId = (await response.json()).id; commandHeld = true; await commandGate; }
      await route.fulfill({ response }).catch(() => {});
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    try {
      await page.goto(board ? `/app/${org}/boards/${board}/invite` : `/app/${org}/invite`);
      const runtime = await context.request.get('/api/runtime'); expect(runtime.status()).toBe(200);
      if ((await runtime.json()).mode === 'production')
        await expect(page.getByText('Current invitation permissions checked. Review the request before submitting.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Create invitation', exact: true })).toBeEnabled();
      await page.getByLabel(/^Invitation email/).fill(email); await page.clock.install(); holdProfile = true;
      await page.getByRole('button', { name: 'Create invitation', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect.poll(() => profileHeld).toBe(true); await page.clock.runFor(8_000); releaseProfile();
      await expect.poll(() => commandHeld).toBe(true); expect(writes).toHaveLength(1);
      await page.clock.runFor(7_001); await page.clock.runFor(500);
      await expect(page.getByText('Invitation creation acknowledged.')).toHaveCount(0);
      releaseCommand();
      // A genuine live invalidation can already queue fresh admission. Either
      // route must return to the reserved request, never a replacement command.
      const permission = page.getByRole('button', { name: 'Retry permission check' });
      const retry = page.getByRole('button', { name: 'Retry same invitation' });
      await expect.poll(async () => (await permission.count() > 0 && await permission.isEnabled())
        || (await retry.count() > 0 && await retry.isEnabled())).toBe(true);
      if (await permission.count() > 0) {
        await page.getByRole('button', { name: 'Retry permission check' }).focus(); await page.keyboard.press('Enter');
      }
      await expect(page.getByRole('button', { name: 'Retry same invitation' })).toBeEnabled();
      await expect(page.getByLabel(/^Invitation email/)).toHaveValue(email);
      await expect(page.getByText('Invitation creation acknowledged.')).toHaveCount(0);
      await page.getByRole('button', { name: 'Retry same invitation' }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Invitation creation acknowledged.')).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(documents).toBe(1);
      const history = await context.request.get(root); expect(history.status()).toBe(200);
      const matching = (await history.json()).items.filter((row: { email: string }) => row.email === email);
      expect(matching).toHaveLength(1); expect(matching[0].id).toBe(originalId);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { holdProfile = false; releaseProfile(); releaseCommand(); }
  });
}
