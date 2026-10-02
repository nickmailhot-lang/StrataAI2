import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test('PRD-17: desktop and phone recover personal watches for Board, List and moving Card', async ({ page, context, browser }) => {
  test.setTimeout(180_000); await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `watch-browser-${Date.now()}@example.test`, password: 'watch-browser-correct-horse', displayName: 'Watch browser fixture' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const organization = await context.request.post('/organizations', { headers, data: { name: 'Keyboard watching' } });
  expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
  const created = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Watched Board', visibility: 'PRIVATE' } });
  expect(created.status()).toBe(201); const board = (await created.json()).id;
  const firstList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Watched List' } });
  expect(firstList.status()).toBe(201); const list = (await firstList.json()).id;
  const otherList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Moved destination' } });
  expect(otherList.status()).toBe(201); const destination = (await otherList.json()).id;
  const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Watched moving Card' } });
  expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
  const phone = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
  const other = await phone.newPage(); let restoreWorker = () => {};
  try {
    restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
    const path = `/app/${org}/boards/${board}`;
    await page.goto(path); await other.goto(path);
    for (const kind of ['Board', 'List']) {
      const desktopOpen = kind === 'List' ? page.getByRole('button', { name: 'List watching', exact: true }).first() : page.getByRole('button', { name: 'Board watching', exact: true });
      const phoneOpen = kind === 'List' ? other.getByRole('button', { name: 'List watching', exact: true }).first() : other.getByRole('button', { name: 'Board watching', exact: true });
      await expect(desktopOpen).toBeEnabled(); await desktopOpen.focus(); await page.keyboard.press('Enter');
      await expect(phoneOpen).toBeEnabled(); await phoneOpen.focus(); await other.keyboard.press('Enter');
      const desktop = page.getByRole('dialog', { name: `${kind} watching`, exact: true });
      const mobile = other.getByRole('dialog', { name: `${kind} watching`, exact: true });
      await expect(desktop.getByText(`You are not watching this ${kind}.`, { exact: true })).toBeVisible();
      await expect(mobile.getByText(`You are not watching this ${kind}.`, { exact: true })).toBeVisible();
      await desktop.getByRole('button', { name: `Watch ${kind}`, exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(desktop.getByText(`You are watching this ${kind}.`, { exact: true })).toBeVisible();
      await expect(mobile.getByText(`You are watching this ${kind}.`, { exact: true })).toBeVisible({ timeout: 25_000 });
      await expect(desktop.getByRole('button', { name: 'Check current watching' })).toBeFocused();
      await mobile.getByRole('button', { name: `Unwatch ${kind}`, exact: true }).focus(); await other.keyboard.press('Enter');
      await expect(mobile.getByText(`You are not watching this ${kind}.`, { exact: true })).toBeVisible();
      await expect(desktop.getByText(`You are not watching this ${kind}.`, { exact: true })).toBeVisible({ timeout: 25_000 });
      expect(await other.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await desktop.getByRole('button', { name: 'Done watching' }).focus(); await page.keyboard.press('Enter');
      await mobile.getByRole('button', { name: 'Done watching' }).focus(); await other.keyboard.press('Enter');
      await expect(desktop).toHaveCount(0); await expect(mobile).toHaveCount(0); await expect(phoneOpen).toBeFocused();
    }
    await page.goto(`${path}/cards/${card}`); await other.goto(`${path}/cards/${card}`);
    for (const target of [page, other]) {
      const open = target.getByRole('button', { name: 'Card watching', exact: true }); await expect(open).toBeEnabled(); await open.focus(); await target.keyboard.press('Enter');
      await expect(target.getByText('You are not watching this Card.', { exact: true })).toBeVisible();
    }
    let writes = 0; let key: string | undefined; let body: string | null = null;
    await other.route(`**/watch/CARD/${card}?version=0`, async route => {
      if (route.request().method() !== 'PUT') { await route.continue(); return; }
      if (++writes === 1) {
        key = route.request().headers()['idempotency-key']; body = route.request().postData();
        const committed = await route.fetch(); expect(committed.status()).toBe(200); await route.abort('failed');
      } else { expect(route.request().headers()['idempotency-key']).toBe(key); expect(route.request().postData()).toBe(body); await route.continue(); }
    });
    const mobile = other.getByRole('dialog', { name: 'Card watching', exact: true });
    await mobile.getByRole('button', { name: 'Watch Card', exact: true }).focus(); await other.keyboard.press('Enter');
    await expect(mobile.getByRole('button', { name: 'Retry same watch change' })).toBeFocused();
    await mobile.getByRole('button', { name: 'Check current watching' }).focus(); await other.keyboard.press('Enter');
    await expect(mobile.getByText('You are watching this Card.', { exact: true })).toBeVisible();
    await mobile.getByRole('button', { name: 'Retry same watch change' }).focus(); await other.keyboard.press('Enter');
    await expect(mobile.getByRole('button', { name: 'Retry same watch change' })).toHaveCount(0); expect(writes).toBe(2);
    await expect(page.getByText('You are watching this Card.', { exact: true })).toBeVisible({ timeout: 25_000 });
    const before = await (await context.request.get(`/watch/CARD/${card}`)).json(); expect(before.version).toBe(1);
    expect((await context.request.post(`/cards/${card}/move`, { headers, data: { destinationListId: destination, expectedVersion: 1 } })).status()).toBe(200);
    const after = await (await context.request.get(`/watch/CARD/${card}`)).json(); expect(after.subscriptionId).toBe(before.subscriptionId); expect(after.version).toBe(1); expect(after.watching).toBe(true);
    expect((await context.request.post(`/lists/${destination}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    await expect(mobile.getByText('Watching is unavailable for this entity.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await expect(page.getByRole('dialog', { name: 'Card watching', exact: true }).getByText('Watching is unavailable for this entity.', { exact: true })).toBeVisible({ timeout: 25_000 });
    expect((await context.request.get(`/watch/CARD/${card}`)).status()).toBe(404);
  } finally { restoreWorker(); await phone.close(); }
});
