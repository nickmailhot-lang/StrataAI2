import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from './releaseTest';
import { focusAdmittedControl } from './keyboardAdmission';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

async function assertContext(page: Page, name: string, organizationId: string) {
  const header = page.getByRole('banner');
  const caption = header.getByTitle(name, { exact: true });
  await expect(caption).toBeVisible(); await expect(caption).toContainText(name);
  await expect(header).not.toContainText(organizationId);
  await expect.poll(() => caption.evaluate(element => {
    const box = element.getBoundingClientRect();
    return box.left >= 0 && box.right <= window.innerWidth + 1
      && element.scrollWidth <= element.clientWidth + 1 && element.scrollHeight <= element.clientHeight + 1
      && getComputedStyle(element).whiteSpace !== 'nowrap' && getComputedStyle(element).overflow !== 'hidden';
  })).toBe(true);
  await expect.poll(async () => {
    const height = await header.evaluate(element => Math.ceil(element.getBoundingClientRect().height));
    const offset = await page.getByRole('main').evaluate(element => element.firstElementChild?.getBoundingClientRect().height);
    return offset === height;
  }).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
}

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-03-VS-01-T-02-TEST-02: sign-in, named Organization, Board, Card and private-context withdrawal at ${width}px`, async ({ page, context, browser }, info) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 900 });
    if (width === 768) await page.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => { document.documentElement.style.fontSize = '32px'; }, { once: true });
    });
    const credentials = { email: `named-context-${width}-${Date.now()}@example.test`, password: 'named-context-correct-horse-battery', displayName: 'Context participant' };
    await registerNotificationAccount(context.request, credentials, true);
    const headers = { 'X-StrataAI-Request': '1' };
    const name = (`Context ${width} ` + 'North garden '.repeat(14)).slice(0, 159) + 'X';
    expect(name).toHaveLength(160);
    const orgReply = await context.request.post('/organizations', { headers, data: { name } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Context work', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Inspect entrance', description: 'Keep the canonical context' } });
    expect(cardReply.status()).toBe(201); const original = await cardReply.json();
    const restoreWorker = scopedBoardWorker(org);
    let replacement: Awaited<ReturnType<typeof browser.newContext>> | undefined;
    try {
      await waitForBoardDelivery(context.request, board);
      await context.clearCookies();
      await page.goto('/login');
      await page.getByLabel(/^Email/).fill(credentials.email);
      await page.getByLabel(/^Password/).fill(credentials.password);
      const signIn = page.getByRole('button', { name: 'Sign in', exact: true });
      await focusAdmittedControl(signIn); await signIn.press('Enter');
      await expect(page).toHaveURL(/\/app$/);
      const organization = page.getByRole('link', { name, exact: true });
      await expect(organization).toBeVisible(); await focusAdmittedControl(organization); await organization.press('Enter');
      await expect(page).toHaveURL(new RegExp(`/app/${org}$`));
      await assertContext(page, name, org);
      if (width === 768) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
      const boardLink = page.getByRole('link', { name: 'Context work', exact: true });
      await focusAdmittedControl(boardLink); await boardLink.press('Enter');
      await expect(page).toHaveURL(new RegExp(`/app/${org}/boards/${board}$`));
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
      await assertContext(page, name, org);
      await expect.poll(async () => {
        const heading = await page.getByRole('heading', { name: 'Context work', exact: true }).boundingBox();
        const header = await page.getByRole('banner').boundingBox();
        return !!heading && !!header && heading.y >= header.y + header.height
          && heading.y + heading.height <= 900;
      }).toBe(true);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await page.screenshot({ path: info.outputPath('named-board.png'), fullPage: true });
      const face = page.getByRole('link', { name: 'Inspect entrance', exact: true });
      await focusAdmittedControl(face);
      const boardScroll = await page.evaluate(() => ({ x: scrollX, y: scrollY }));
      await face.press('Enter');
      const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
      await expect(detail).toBeVisible();
      await expect(detail.getByRole('textbox', { name: 'Card title', exact: false })).toHaveValue('Inspect entrance');
      await expect(detail.getByRole('textbox', { name: 'Card title', exact: false })).toBeFocused();
      await page.screenshot({ path: info.outputPath('named-card.png') });
      const close = detail.getByRole('button', { name: 'Close', exact: true });
      await focusAdmittedControl(close); await close.press('Enter');
      await expect(detail).toHaveCount(0); await expect(face).toBeFocused();
      expect(await page.evaluate(() => ({ x: scrollX, y: scrollY }))).toEqual(boardScroll);
      await assertContext(page, name, org);
      if (width < 600) {
        const trigger = page.getByRole('button', { name: 'Open navigation', exact: true });
        await focusAdmittedControl(trigger); await trigger.press('Enter');
        const nav = page.getByRole('navigation', { name: 'Mobile application navigation' });
        await expect(nav.getByRole('link', { name: 'Work', exact: true })).toHaveAttribute('aria-current', 'page');
        const work = nav.getByRole('link', { name: 'Work', exact: true });
        await focusAdmittedControl(work); await work.press('Enter');
        await expect(nav).toHaveCount(0);
      } else {
        const work = page.getByRole('navigation', { name: 'Internal application navigation' }).getByRole('link', { name: 'Work', exact: true });
        await expect(work).toHaveAttribute('aria-current', 'page');
        await focusAdmittedControl(work); await work.press('Enter');
      }
      await expect(page).toHaveURL(new RegExp(`/app/${org}$`));
      await page.reload(); await assertContext(page, name, org);
      if (width === 768) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
      const canonical = await context.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200);
      const cards = (await canonical.json()).lists.flatMap((item: { cards: { id: string; title: string; description: string; rank: string; version: number }[] }) => item.cards);
      expect(cards).toHaveLength(1);
      expect(cards[0]).toMatchObject({ id: original.id, title: 'Inspect entrance', description: 'Keep the canonical context', rank: original.rank, version: original.version });
      await page.screenshot({ path: info.outputPath('named-return.png'), fullPage: true });
      replacement = await browser.newContext({ baseURL: info.project.use.baseURL });
      const next = { ...credentials, email: `replacement-${credentials.email}`, displayName: 'Unrelated participant' };
      const created = await registerNotificationAccount(replacement.request, next, true);
      await context.addCookies(await replacement.cookies());
      expect((await (await context.request.get('/me')).json()).id).toBe(created.user.id);
      expect((await context.request.get(`/organizations/${org}`)).status()).toBe(404);
      await page.evaluate(() => window.dispatchEvent(new Event('focus')));
      await expect(page.getByText(name, { exact: true })).toHaveCount(0);
      await expect(page.getByTitle(name, { exact: true })).toHaveCount(0);
      await expect(page.getByRole('navigation', { name: 'Internal application navigation' })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Open navigation', exact: true })).toHaveCount(0);
      await page.screenshot({ path: info.outputPath('context-withdrawn.png'), fullPage: true });
    } finally {
      await replacement?.close(); restoreWorker();
    }
  });
}
