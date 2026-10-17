import AxeBuilder from '@axe-core/playwright';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expect, test } from './releaseTest';
import { openCardCreation } from './cardCreationEntry';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-08-VS-04/AC-02/04: original-account Card retry persists once and returns to its Board at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 1000 });
    await registerNotificationAccount(context.request, { email: `card-recovery-${crypto.randomUUID()}@example.test`, password: 'card-recovery-correct-horse', displayName: 'Card recovery' });
    const actor = (await (await context.request.get('/me')).json()).id as string;
    const created = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Card recovery Organization' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
    const boardResponse = await context.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { organizationId: org, name: 'Card recovery Board' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id as string;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'Planning' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id as string;
    const boardPath = `/app/${org}/boards/${board}`;
    await page.goto(boardPath);
    if (width === 768) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    await expect(page.getByRole('heading', { name: 'Card recovery Board', exact: true })).toBeVisible();
    const dialog = await openCardCreation(page, 'Planning');
    const title = dialog.getByLabel('Card title', { exact: false }); await expect(title).toBeFocused();
    await title.fill('Recover one canonical Card');
    const submit = dialog.getByRole('button', { name: 'Create', exact: true });
    await submit.focus(); await submit.press('Shift+Tab'); await page.keyboard.press('Tab'); await expect(submit).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`card-creation-draft-${width}.png`), fullPage: true });
    const attempts: { key: string | undefined; body: string | null; actor: string | null; id: string; status: number }[] = [];
    const creationUrl = (url: URL) => url.pathname === `/lists/${list}/cards`;
    await page.route(creationUrl, async route => {
      if (route.request().method() !== 'POST') return route.continue();
      const result = await route.fetch(); const body = await result.json();
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), actor: new URL(route.request().url()).searchParams.get('expectedActorId'), id: body.id, status: result.status() });
      if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
    });
    await submit.press('Enter');
    await expect(dialog.getByRole('alert')).toContainText('Unable to confirm the save');
    await expect(title).toHaveValue('Recover one canonical Card'); await expect(submit).toBeEnabled();
    await submit.focus(); await submit.press('Enter');
    const link = page.getByRole('link', { name: 'Recover one canonical Card', exact: true }); await expect(link).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[0].status).toBe(201); expect(attempts[1]).toEqual(attempts[0]); expect(attempts[0].actor).toBe(actor);
    await page.unroute(creationUrl);
    const canonical = await context.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200);
    const snapshot = await canonical.json(); const cards = snapshot.lists.find((x: { list: { id: string } }) => x.list.id === list).cards;
    expect(cards).toHaveLength(1); expect(cards[0]).toMatchObject({ id: attempts[0].id, title: 'Recover one canonical Card' }); expect(cards[0].rank).toBeTruthy();
    await link.focus(); await link.press('Enter'); await expect(page).toHaveURL(new RegExp(`/cards/${cards[0].id}$`));
    await page.reload(); await expect(page.getByLabel('Card title', { exact: false })).toHaveValue('Recover one canonical Card');
    if (width === 768) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
    const close = page.getByRole('button', { name: 'Close', exact: true }); await close.focus(); await close.press('Enter');
    await expect(page).toHaveURL(new RegExp(`${boardPath}$`)); await expect(page.getByRole('link', { name: 'Recover one canonical Card', exact: true })).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath(`card-creation-return-${width}.png`), fullPage: true });
  });
}
