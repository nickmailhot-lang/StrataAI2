import AxeBuilder from '@axe-core/playwright';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-03-VS-06-T-02: authorized Work directory → Board → Card → keyboard return at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 900 });
    const headers = { 'X-StrataAI-Request': '1' };
    const mutationHeaders = () => ({ ...headers, 'Idempotency-Key': crypto.randomUUID() });
    await registerNotificationAccount(context.request, { email: `work-navigation-${crypto.randomUUID()}@example.test`, password: 'work-navigation-correct-horse', displayName: 'Work navigation owner' });
    const organization = await context.request.post('/organizations', { headers: mutationHeaders(), data: { name: 'Work navigation Organization' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers: mutationHeaders(), data: { organizationId: org, name: 'Work navigation Board', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const createdList = await context.request.post(`/boards/${board}/lists`, { headers: mutationHeaders(), data: { name: 'Work navigation List' } });
    expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
    const createdCard = await context.request.post(`/lists/${list}/cards`, { headers: mutationHeaders(), data: { title: 'Work navigation Card' } });
    expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
    async function canonical() {
      const response = await context.request.get(`/boards/${board}`); expect(response.status()).toBe(200);
      const value = await response.json(); return { board: value.board, lists: value.lists };
    }
    const before = await canonical(), writes: string[] = [];
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) &&
        [org, board, list, card].some(id => path.split('/').includes(id))) writes.push(`${request.method()} ${path}`);
    });
    async function accessibility() {
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    }
    async function navigation() {
      if (width < 600) {
        const trigger = page.getByRole('button', { name: 'Open navigation', exact: true });
        await trigger.focus(); await expect(trigger).toBeFocused(); await trigger.press('Enter');
        const drawer = page.getByRole('navigation', { name: 'Mobile application navigation', exact: true });
        await expect(drawer).toBeVisible(); return drawer;
      }
      return page.getByRole('navigation', { name: 'Internal application navigation', exact: true });
    }
    async function closeNavigation() {
      if (width >= 600) return;
      await page.keyboard.press('Escape');
      await expect(page.getByRole('navigation', { name: 'Mobile application navigation', exact: true })).toBeHidden();
      await expect(page.getByRole('button', { name: 'Open navigation', exact: true })).toBeFocused();
    }
    const directoryPath = `/app/${org}`, boardPath = `${directoryPath}/boards/${board}`;
    await page.goto('/app');
    const organizationLink = page.getByRole('link', { name: 'Work navigation Organization', exact: true });
    await organizationLink.focus(); await organizationLink.press('Enter');
    await expect(page).toHaveURL(new RegExp(directoryPath + '$'));
    await expect(page.getByRole('link', { name: 'Work navigation Board', exact: true })).toBeVisible();
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    const directoryNavigation = await navigation();
    await expect(directoryNavigation.getByRole('link', { name: 'Work', exact: true })).toHaveAttribute('aria-current', 'page');
    for (const name of ['Meetings', 'Correspondence', 'Documents', 'Owners & Units', 'Not available yet'])
      await expect(directoryNavigation.getByText(name, { exact: true })).toHaveCount(0);
    await accessibility(); await closeNavigation();
    const boardLink = page.getByRole('link', { name: 'Work navigation Board', exact: true });
    await boardLink.focus(); await boardLink.press('Enter'); await expect(page).toHaveURL(new RegExp(boardPath + '$'));
    const cardLink = page.getByRole('link', { name: 'Work navigation Card', exact: true }); await expect(cardLink).toBeVisible();
    const boardNavigation = await navigation(), work = boardNavigation.getByRole('link', { name: 'Work', exact: true });
    await expect(work).toHaveAttribute('href', directoryPath); await expect(work).toHaveAttribute('aria-current', 'page');
    await work.focus(); await expect(work).toBeFocused(); await accessibility();
    await page.screenshot({ path: test.info().outputPath(`work-navigation-${width}.png`), fullPage: true });
    await closeNavigation();
    await cardLink.focus(); await cardLink.press('Enter');
    const dialog = page.getByRole('dialog', { name: 'Card details', exact: true });
    await expect(dialog.getByRole('textbox', { name: 'Card title', exact: true })).toHaveValue('Work navigation Card');
    await expect(page).toHaveURL(new RegExp(`${boardPath}/cards/${card}$`));
    // The modal correctly hides background navigation from keyboard/AT. Its
    // retained current-section state is checked separately from visible access.
    await expect(page.locator('nav[aria-label="Internal application navigation"] a[aria-current="page"]')).toHaveText('Work');
    await page.goBack(); await expect(page).toHaveURL(new RegExp(boardPath + '$'));
    await expect(dialog).toBeHidden(); await expect(cardLink).toBeFocused();
    const returnNavigation = await navigation(), returnWork = returnNavigation.getByRole('link', { name: 'Work', exact: true });
    await returnWork.focus(); await returnWork.press('Enter');
    await expect(page).toHaveURL(new RegExp(directoryPath + '$'));
    await expect(page.getByRole('link', { name: 'Work navigation Board', exact: true })).toBeVisible();
    await page.goBack(); await expect(page).toHaveURL(new RegExp(boardPath + '$'));
    await expect(cardLink).toBeVisible(); await accessibility();
    expect(await canonical()).toEqual(before); expect(writes).toEqual([]);
    expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
    expect((await context.request.get('/me')).status()).toBe(401);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('navigation', { name: 'Internal application navigation', exact: true })).toHaveCount(0);
    await expect(page.getByRole('navigation', { name: 'Mobile application navigation', exact: true })).toHaveCount(0);
    await expect(page.getByText('Work navigation Board', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Work navigation Card', { exact: true })).toHaveCount(0);
    expect(writes).toEqual([]);
  });
}
