import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) for (const deleting of [false, true]) {
  test(`PRD-04/18: archived Board account uncertainty before and after command (delete=${deleting}, ${width}px)`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `archive-account-${deleting}-${width}-${Date.now()}@example.test`, password: 'archive-account-correct-horse', displayName: 'Archive reviewer' };
    const registered = await registerVerifiedAccountFixture(context.request, account);
    const actor = registered.user.id;
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Archive account fixture' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Reviewed archive Board', visibility: 'PRIVATE' } });
    expect(created.status()).toBe(201); const board = await created.json();
    const archived = await context.request.post(`/boards/${board.id}/archive`, { headers, data: { version: board.version } });
    expect(archived.status()).toBe(200); const before = await archived.json();
    let failProfile = false, failAfter = true, documents = 0;
    const commands: { key: string | undefined; body: string | null; url: string }[] = [];
    await page.route(url => url.pathname === '/me', async route => {
      if (!failProfile) { await route.continue(); return; }
      failProfile = false; await route.fulfill({ status: 503, contentType: 'application/json', body: '{}' });
    });
    await page.route(url => url.pathname === `/boards/${board.id}${deleting ? '' : '/restore'}`, async route => {
      if (route.request().method() !== (deleting ? 'DELETE' : 'POST')) { await route.continue(); return; }
      expect(route.request().headers()['x-strataai-expected-actor']).toBe(actor);
      commands.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), url: route.request().url() });
      const result = await route.fetch(); expect(result.status()).toBe(200);
      if (failAfter) { failAfter = false; failProfile = true; }
      await route.fulfill({ response: result });
    });
    page.on('request', request => { if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documents++; });
    await page.goto(`/app/${org}/archived-boards`);
    await expect(page.getByRole('status')).toHaveText('Current archived boards checked.');
    const review = page.getByRole('button', { name: deleting ? `Permanently delete ${board.name} board` : `Restore ${board.name} board`, exact: true });
    const confirm = page.getByRole('button', { name: deleting ? 'Confirm permanent deletion' : 'Confirm restore', exact: true });
    async function openReview() {
      await review.focus(); await review.press('Enter');
      if (deleting) await page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true }).check();
      await expect(confirm).toBeEnabled();
    }
    await openReview(); failProfile = true; await confirm.press('Enter');
    await expect(page.getByText('Unable to confirm the current account. No Board change was sent. Check current archived boards before reviewing again.')).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0); expect(commands).toHaveLength(0);
    await expect(page.getByRole('heading', { name: board.name })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Retry this change' })).toHaveCount(0);
    const unchanged = await context.request.get(`/boards/${board.id}`); expect(unchanged.status()).toBe(200);
    expect((await unchanged.json()).board).toEqual(before);
    await page.getByRole('button', { name: 'Check current archived boards', exact: true }).click();
    await openReview(); await confirm.press('Enter');
    await expect(page.getByText('This change is unconfirmed. Check current archived boards, then retry the same request to recover its acknowledgment.')).toBeVisible();
    await expect(page.getByRole('dialog')).toHaveCount(0); expect(commands).toHaveLength(1);
    const acknowledged = deleting ? 'Board deletion acknowledged.' : 'Board restore acknowledged.';
    await expect(page.getByText(acknowledged)).toHaveCount(0);
    const current = await context.request.get(`/boards/${board.id}`);
    expect(current.status()).toBe(deleting ? 404 : 200);
    if (!deleting) { const value = (await current.json()).board; expect(value.lifecycleState).toBe('active'); expect(value.version).toBe(before.version + 1); }
    const retry = page.getByRole('button', { name: 'Retry this change', exact: true }); await expect(retry).toBeDisabled();
    await page.getByRole('button', { name: 'Check current archived boards', exact: true }).click();
    await expect(retry).toBeEnabled(); await retry.focus(); await retry.press('Enter');
    await expect(page.getByText(acknowledged)).toBeVisible(); expect(commands).toHaveLength(2); expect(commands[1]).toEqual(commands[0]);
    await expect(page.getByRole('button', { name: 'Check current archived boards', exact: true })).toBeFocused();
    expect(documents).toBe(1); expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
