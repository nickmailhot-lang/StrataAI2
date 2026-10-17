import AxeBuilder from '@axe-core/playwright';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test, type WebSocketRoute } from './releaseTest';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-04-VS-01: Board draft survives lifecycle transport loss and explicit original-key retry at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 1000 });
    await registerNotificationAccount(context.request, {
      email: `board-recovery-${crypto.randomUUID()}@example.test`, password: 'board-recovery-correct-horse', displayName: 'Board recovery',
    });
    const created = await context.request.post('/organizations', {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Draft recovery Organization' },
    });
    expect(created.status()).toBe(201);
    const org = (await created.json()).organization.id as string;
    let socket: WebSocketRoute | undefined;
    await page.routeWebSocket(/\/organizations\/live\/lifecycle(?:\?|$)/, route => {
      socket = route; route.connectToServer();
    });
    await page.goto(`/app/${org}`);
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    await expect(page.getByRole('heading', { name: 'Draft recovery Organization', exact: true })).toBeVisible();
    await expect.poll(() => !!socket).toBe(true);
    await page.getByRole('button', { name: 'Create board', exact: true }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('Name', { exact: false }).fill('Recovered planning Board');
    await dialog.getByLabel('Description', { exact: true }).fill('Keep this unsent draft');
    const submit = dialog.getByRole('button', { name: 'Create', exact: true });
    await submit.focus(); await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Tab');
    await expect(submit).toBeFocused();
    let release!: () => void;
    const held = new Promise<void>(resolve => { release = resolve; });
    let checking = false;
    await page.route(`**/organizations/${org}`, async route => {
      const canonical = await route.fetch(); checking = true;
      await held; await route.fulfill({ response: canonical });
    });
    socket!.close({ code: 1013 });
    await expect.poll(() => checking).toBe(true);
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('heading', { name: 'Draft recovery Organization', exact: true })).toHaveCount(0);
    release();
    await expect(dialog).toBeVisible();
    await expect(dialog.getByLabel('Name', { exact: false })).toHaveValue('Recovered planning Board');
    await expect(dialog.getByLabel('Description', { exact: true })).toHaveValue('Keep this unsent draft');
    await expect(submit).toBeFocused();
    await page.unroute(`**/organizations/${org}`);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`recovered-board-draft-${width}.png`), fullPage: true });
    // Closing a recovered unsent form must target the replacement launcher,
    // rather than the original button removed by access withdrawal.
    if (width >= 768) await page.keyboard.press('Escape');
    else await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(dialog).toBeHidden();
    const launcher = page.getByRole('button', { name: 'Create board', exact: true });
    await expect(launcher).toBeFocused();
    await expect(launcher).toBeInViewport();
    await page.screenshot({ path: test.info().outputPath(`recovered-board-return-${width}.png`), fullPage: true });
    await launcher.press('Enter');
    await expect(dialog.getByLabel('Name', { exact: false })).toHaveValue('');
    await dialog.getByLabel('Name', { exact: false }).fill('Recovered planning Board');
    await dialog.getByLabel('Description', { exact: true }).fill('Keep this unsent draft');
    const attempts: { body: string | null; key: string | undefined }[] = [];
    await page.route('**/boards', async route => {
      if (route.request().method() !== 'POST') return route.continue();
      attempts.push({ body: route.request().postData(), key: route.request().headers()['idempotency-key'] });
      const canonical = await route.fetch(); expect(canonical.status()).toBe(201);
      if (attempts.length === 1) await route.abort('failed');
      else await route.fulfill({ response: canonical });
    });
    await submit.press('Enter');
    await expect(dialog.getByRole('alert')).toContainText('Unable to confirm the save');
    await expect(dialog.getByLabel('Name', { exact: false })).toHaveValue('Recovered planning Board');
    await submit.focus(); await expect(submit).toBeFocused(); await submit.press('Enter');
    await expect(page.getByRole('heading', { name: 'Recovered planning Board', exact: true })).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
    expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
    const boardId = new URL(page.url()).pathname.split('/')[4];
    const canonical = await context.request.get(`/boards/${boardId}`);
    expect(canonical.status()).toBe(200);
    expect((await canonical.json()).board).toMatchObject({ organizationId: org, name: 'Recovered planning Board', description: 'Keep this unsent draft', visibility: 'PRIVATE' });
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Recovered planning Board', exact: true })).toBeVisible();
    const menu = page.getByRole('button', { name: 'Open navigation', exact: true });
    if (await menu.isVisible()) await menu.click();
    await page.getByRole('link', { name: 'Boards', exact: true }).click();
    await expect(page.getByRole('link', { name: 'Recovered planning Board', exact: true })).toBeVisible();
    const directory = await context.request.get(`/organizations/${org}/boards/directory`);
    expect(directory.status()).toBe(200);
    expect((await directory.json()).items).toHaveLength(1);
  });
}
