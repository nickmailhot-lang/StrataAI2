import AxeBuilder from '@axe-core/playwright';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';

const headers = { 'X-StrataAI-Request': '1' };
async function fixture(request: import('@playwright/test').APIRequestContext) {
  const credentials = { email: `card-return-${crypto.randomUUID()}@example.test`, password: 'card-return-correct-horse', displayName: 'Card return account' };
  await registerNotificationAccount(request, credentials);
  const mutationHeaders = () => ({ ...headers, 'Idempotency-Key': crypto.randomUUID() });
  const organization = await request.post('/organizations', { headers: mutationHeaders(), data: { name: 'Card return Organization' } });
  expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
  const createdBoard = await request.post('/boards', { headers: mutationHeaders(), data: { organizationId: org, name: 'Private return Board', visibility: 'PRIVATE' } });
  expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
  const createdList = await request.post(`/boards/${board}/lists`, { headers: mutationHeaders(), data: { name: 'Return List' } });
  expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
  const createdCard = await request.post(`/lists/${list}/cards`, { headers: mutationHeaders(), data: { title: 'Private return Card' } });
  expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
  return { credentials, org, board, card, path: `/app/${org}/boards/${board}/cards/${card}` };
}

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-01-VS-04/AC-01/02/04: signed-out Card link → sign-in → canonical Card → Board at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 900 });
    const data = await fixture(context.request);
    expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
    await page.goto(data.path);
    await expect(page.getByText('Private return Card', { exact: true })).toHaveCount(0);
    const signIn = page.getByRole('link', { name: 'Sign in', exact: true });
    await signIn.focus(); await expect(signIn).toBeFocused(); await signIn.press('Enter');
    await expect(page).toHaveURL(/\/login$/);
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByLabel(/^Email/).fill(data.credentials.email);
    await page.getByLabel(/^Password/).fill(data.credentials.password);
    const attempts: { body: string | null; key: string | undefined }[] = [];
    await page.route('**/auth/login', async route => {
      attempts.push({ body: route.request().postData(), key: route.request().headers()['idempotency-key'] });
      if (attempts.length === 1) { expect((await route.fetch()).status()).toBe(200); await route.abort('timedout'); }
      else await route.continue();
    });
    const submit = page.getByRole('button', { name: 'Sign in', exact: true });
    await submit.focus(); await submit.press('Enter');
    await expect(page.getByRole('alert')).toContainText('Retry with the same details');
    await expect(page).toHaveURL(/\/login$/);
    await submit.focus(); await submit.press('Enter');
    await expect(page).toHaveURL(new RegExp(data.path+'$'));
    expect(attempts).toHaveLength(2); expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
    expect(attempts[1]).toEqual(attempts[0]);
    const dialog = page.getByRole('dialog', { name: 'Card details', exact: true });
    const title = dialog.getByRole('textbox', { name: 'Card title', exact: true });
    await expect(title).toHaveValue('Private return Card'); await expect(title).toBeEnabled();
    await title.fill('Resumed canonical Card');
    const save = dialog.getByRole('button', { name: 'Save card', exact: true });
    await save.focus(); await save.press('Enter');
    await expect(dialog.getByRole('status').filter({ hasText: 'Changes saved.' })).toHaveText('Changes saved.');
    const canonical = await context.request.get(`/boards/${data.board}`);
    expect(canonical.status()).toBe(200);
    const columns = (await canonical.json()).lists as { cards: { id: string; title: string }[] }[];
    expect(columns.flatMap(column => column.cards).find(card => card.id === data.card)?.title).toBe('Resumed canonical Card');
    await page.reload(); await expect(title).toHaveValue('Resumed canonical Card');
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await title.focus(); await expect(title).toBeFocused();
    await page.screenshot({ path: test.info().outputPath(`resumed-card-${width}.png`), fullPage: true });
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(page).toHaveURL(new RegExp(`/app/${data.org}/boards/${data.board}$`));
    await expect(page.getByRole('heading', { name: 'Private return Board', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Resumed canonical Card', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    // Login must not be reintroduced into Back history after a successful return.
    await page.goBack(); expect(new URL(page.url()).pathname).not.toBe('/login');
  });
}

for (const surface of ['outsider', 'portal-only']) {
  test(`PRD-01-VS-04/AC-03: sign-in return does not grant ${surface} access to a private Card`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width: 390, height: 844 });
    const owner = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    try {
      const data = await fixture(owner.request);
      const credentials = { email: `return-denied-${crypto.randomUUID()}@example.test`, password: 'return-denied-correct-horse', displayName: 'Restricted account' };
      await registerNotificationAccount(context.request, credentials);
      if (surface === 'portal-only') {
        const invitation = await owner.request.post(`/organizations/${data.org}/invitations`, { headers, data: { email: credentials.email, surface: 'PORTAL', targetRole: 'OWNER' } });
        expect(invitation.status()).toBe(201);
        expect((await context.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      }
      expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
      await page.goto(data.path); await page.getByRole('link', { name: 'Sign in', exact: true }).click();
      await page.getByLabel(/^Email/).fill(credentials.email); await page.getByLabel(/^Password/).fill(credentials.password);
      await page.getByRole('button', { name: 'Sign in', exact: true }).click();
      await expect(page).toHaveURL(new RegExp(data.path+'$'));
      await expect(page.getByRole('alert')).toContainText('unavailable');
      await expect(page.getByText('Private return Board', { exact: true })).toHaveCount(0);
      await expect(page.getByText('Private return Card', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('dialog', { name: 'Card details', exact: true })).toHaveCount(0);
      await expect(page.getByLabel('Internal application navigation')).toHaveCount(0);
      expect((await context.request.get(`/boards/${data.board}`)).status()).toBe(404);
      expect((await context.request.get(`/organizations/${data.org}/surface-access?surface=INTERNAL`)).status()).toBe(404);
      if (surface === 'portal-only') expect((await context.request.get(`/organizations/${data.org}/surface-access?surface=PORTAL`)).status()).toBe(200);
    } finally { await owner.close(); }
  });
}

const sessionReturns = [
  { width: 1280, target: 'board' }, { width: 390, target: 'board' },
  ...[1280, 768, 390, 320].map(width => ({ width, target: 'card' })),
];
for (const { width, target } of sessionReturns) {
  test(`PRD-01-VS-04-T-02: revoked session → explicit sign-in → current ${target} → canonical Card → Board at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 900 });
    const data = await fixture(context.request);
    const boardPath = `/app/${data.org}/boards/${data.board}`;
    const destination = target === 'card' ? data.path : boardPath;
    const writes: { key: string | undefined; body: string | null }[] = [];
    page.on('request', request => {
      if (new URL(request.url()).pathname === `/cards/${data.card}` && request.method() === 'PATCH')
        writes.push({ key: request.headers()['idempotency-key'], body: request.postData() });
    });
    await page.goto(destination);
    if (target === 'card') await expect(page.getByRole('dialog', { name: 'Card details', exact: true })
      .getByRole('textbox', { name: 'Card title', exact: true })).toBeEnabled();
    else await expect(page.getByRole('link', { name: 'Private return Card', exact: true })).toBeVisible();
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    // Revoke this actual session. The outer boundary must withdraw private
    // content, confirm a fresh anonymous session and retain only its safe route.
    expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
    expect((await context.request.get('/me')).status()).toBe(401);
    await page.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByText('Private return Board', { exact: true })).toHaveCount(0);
    await expect(page.getByRole('dialog', { name: 'Card details', exact: true })).toHaveCount(0);
    expect(writes).toHaveLength(0);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByLabel(/^Email/).fill(data.credentials.email);
    await page.getByLabel(/^Password/).fill(data.credentials.password);
    const submit = page.getByRole('button', { name: 'Sign in', exact: true });
    await submit.focus(); await expect(submit).toBeFocused(); await submit.press('Enter');
    await expect(page).toHaveURL(new RegExp(destination + '$'));
    expect(writes).toHaveLength(0);
    if (target === 'board') {
      const card = page.getByRole('link', { name: 'Private return Card', exact: true });
      await card.focus(); await card.press('Enter');
    }
    const dialog = page.getByRole('dialog', { name: 'Card details', exact: true });
    const title = dialog.getByRole('textbox', { name: 'Card title', exact: true });
    await expect(title).toHaveValue('Private return Card'); await expect(title).toBeEnabled();
    await title.fill('Session-resumed canonical Card');
    const save = dialog.getByRole('button', { name: 'Save card', exact: true });
    await expect(save).toBeEnabled(); await save.focus(); await save.press('Enter');
    await expect(dialog.getByRole('status').filter({ hasText: 'Changes saved.' })).toHaveText('Changes saved.');
    expect(writes).toHaveLength(1); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
    const canonical = await context.request.get(`/boards/${data.board}`); expect(canonical.status()).toBe(200);
    const lists = (await canonical.json()).lists as { cards: { id: string; title: string }[] }[];
    expect(lists.flatMap(list => list.cards).find(card => card.id === data.card)?.title).toBe('Session-resumed canonical Card');
    await page.reload(); await expect(title).toHaveValue('Session-resumed canonical Card');
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await title.focus(); await expect(title).toBeFocused();
    await page.screenshot({ path: test.info().outputPath(`revoked-${target}-return-${width}.png`), fullPage: true });
    const close = dialog.getByRole('button', { name: 'Close', exact: true }); await close.focus(); await close.press('Enter');
    await expect(page).toHaveURL(new RegExp(boardPath + '$'));
    await expect(page.getByRole('link', { name: 'Session-resumed canonical Card', exact: true })).toBeVisible();
    expect(writes).toHaveLength(1);
    await page.goBack(); expect(new URL(page.url()).pathname).not.toBe('/login');
  });
}
