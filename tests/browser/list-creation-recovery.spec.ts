import AxeBuilder from '@axe-core/playwright';
import { registerNotificationAccount } from './notificationAccountFixture';
import { openCardCreation } from './cardCreationEntry';
import { expect, test } from './releaseTest';

const headers = { 'X-StrataAI-Request': '1' };
for (const width of [1280, 768, 390, 320]) {
  test(`PRD-07-VS-05/AC-02/04: original-account List recovery continues into a canonical Card at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 1000 });
    await registerNotificationAccount(context.request, { email: `list-recovery-${crypto.randomUUID()}@example.test`, password: 'list-recovery-correct-horse', displayName: 'List recovery' });
    const actor = (await (await context.request.get('/me')).json()).id as string;
    const created = await context.request.post('/organizations', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name: 'List recovery Organization' } });
    expect(created.status()).toBe(201); const org = (await created.json()).organization.id as string;
    const boardResponse = await context.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { organizationId: org, name: 'List recovery Board' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id as string;
    const boardPath = `/app/${org}/boards/${board}`;
    if (width === 768) await page.addInitScript(() => { document.addEventListener('DOMContentLoaded', () => { document.documentElement.style.fontSize = '200%'; }, { once: true }); });
    await page.goto(boardPath); await expect(page.getByRole('heading', { name: 'List recovery Board', exact: true })).toBeVisible();
    const dialog = page.getByRole('dialog', { name: 'Add list', exact: true });
    await expect(async () => {
      if (await dialog.isVisible()) return;
      const launcher = page.getByRole('button', { name: 'Add list', exact: true });
      if (await launcher.isVisible() && await launcher.isEnabled({ timeout: 500 })) {
        await launcher.focus({ timeout: 500 }); await expect(launcher).toBeFocused({ timeout: 500 }); await expect(launcher).toBeEnabled({ timeout: 500 }); await launcher.press('Enter', { timeout: 500 });
      }
      await expect(dialog).toBeVisible({ timeout: 500 });
    }).toPass({ timeout: 5_000 });
    const name = dialog.getByLabel('List name', { exact: false }); await expect(name).toBeFocused(); await name.fill('Recovered planning');
    const submit = dialog.getByRole('button', { name: 'Create', exact: true }); await submit.focus();
    await page.keyboard.press('Shift+Tab'); await page.keyboard.press('Tab'); await expect(submit).toBeFocused();
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath(`list-creation-draft-${width}.png`) });
    const attempts: { key: string | undefined; body: string | null; actor: string | null; status: number; id: string }[] = [];
    const creationUrl = (url: URL) => url.pathname === `/boards/${board}/lists`;
    await page.route(creationUrl, async route => {
      if (route.request().method() !== 'POST') return route.continue();
      const result = await route.fetch(); const body = await result.json();
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), actor: new URL(route.request().url()).searchParams.get('expectedActorId'), status: result.status(), id: body.id });
      if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
    });
    await submit.press('Enter'); await expect(dialog.getByRole('alert')).toContainText('Unable to confirm the save');
    await expect(name).toHaveValue('Recovered planning'); await expect(submit).toBeEnabled(); await submit.focus(); await submit.press('Enter');
    await expect(page.getByRole('heading', { name: 'Recovered planning', exact: true })).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[0].status).toBe(201); expect(attempts[1]).toEqual(attempts[0]); expect(attempts[0].actor).toBe(actor);
    await page.unroute(creationUrl);
    await page.reload(); await expect(page.getByRole('heading', { name: 'Recovered planning', exact: true })).toBeVisible();
    if (width === 768) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
    const canonical = await context.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200); const snapshot = await canonical.json();
    expect(snapshot.lists).toHaveLength(1); expect(snapshot.lists[0].list).toMatchObject({ id: attempts[0].id, name: 'Recovered planning', lifecycleState: 'active' }); expect(snapshot.lists[0].list.rank).toBeTruthy();
    const cardDialog = await openCardCreation(page, 'Recovered planning');
    await cardDialog.getByLabel('Card title', { exact: false }).fill('Continue from recovered List');
    const createCard = cardDialog.getByRole('button', { name: 'Create', exact: true }); await createCard.focus(); await createCard.press('Enter');
    const link = page.getByRole('link', { name: 'Continue from recovered List', exact: true }); await expect(link).toBeVisible(); await link.focus(); await link.press('Enter');
    await expect(page).toHaveURL(/\/cards\/[0-9a-f-]+$/); await page.reload();
    await expect(page.getByLabel('Card title', { exact: false })).toHaveValue('Continue from recovered List');
    const close = page.getByRole('button', { name: 'Close', exact: true }); await close.focus(); await close.press('Enter');
    await expect(page).toHaveURL(new RegExp(`${boardPath}$`)); await expect(page.getByRole('link', { name: 'Continue from recovered List', exact: true })).toBeFocused();
    const returnBox = await page.getByRole('link', { name: 'Continue from recovered List', exact: true }).boundingBox();
    const headerBox = await page.getByRole('banner').boundingBox();
    if (!returnBox || !headerBox) throw new Error('Missing recovered List/Card return geometry');
    expect(returnBox.y).toBeGreaterThanOrEqual(headerBox.y + headerBox.height);
    expect(returnBox.y + returnBox.height).toBeLessThanOrEqual(1000);
    const clipping = await page.getByRole('link', { name: 'Continue from recovered List', exact: true }).evaluate(link => {
      const target = link.getBoundingClientRect(), clipped: { tag: string; axis: string; targetStart: number; targetEnd: number; visibleStart: number; visibleEnd: number }[] = [];
      for (let parent = link.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent), box = parent.getBoundingClientRect();
        for (const [axis, overflow, start, end, origin, size] of [
          ['vertical', style.overflowY, target.top, target.bottom, box.top + parent.clientTop, parent.clientHeight],
          ['horizontal', style.overflowX, target.left, target.right, box.left + parent.clientLeft, parent.clientWidth],
        ] as const) if (['auto', 'scroll', 'hidden', 'clip'].includes(overflow) && (start < origin - 1 || end > origin + size + 1))
          clipped.push({ tag: parent.tagName, axis, targetStart: start, targetEnd: end, visibleStart: origin, visibleEnd: origin + size });
      }
      return clipped;
    });
    expect(clipping, 'Focused Card must be fully visible inside every local scroll container').toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: test.info().outputPath(`list-to-card-return-${width}.png`) });
  });
}
