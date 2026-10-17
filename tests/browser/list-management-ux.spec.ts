import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { openAdmittedReview, pressAdmittedAction, pressObservedRequest } from './keyboardAdmission';
import { registerNotificationAccount } from './notificationAccountFixture';

for (const width of [1280, 768, 390, 320]) {
  test(`PRD-07-VS-01/FR-003/004/UXA-05/06: keyboard List rename and reorder return at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 1000 });
    if (width === 768) await page.addInitScript(() => document.addEventListener('DOMContentLoaded', () => {
      document.documentElement.style.fontSize = '200%';
    }, { once: true }));
    const headers = { 'X-StrataAI-Request': '1', 'Idempotency-Key': crypto.randomUUID() };
    await registerNotificationAccount(context.request, { email: `list-ux-${crypto.randomUUID()}@example.test`, password: 'list-ux-correct-horse-battery', displayName: 'List workflow editor' });
    const organization = await context.request.post('/organizations', { headers, data: { name: 'List workflow Organization' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id as string;
    const created = await context.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { organizationId: org, name: 'List workflow Board' } });
    expect(created.status()).toBe(201); const board = (await created.json()).id as string;
    const lists: { id: string; name: string; rank: string; version: number }[] = [];
    for (const name of ['First', 'Planning', 'Neighbor']) {
      const response = await context.request.post(`/boards/${board}/lists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { name } });
      expect(response.status()).toBe(201); lists.push(await response.json());
    }
    for (let index = 0; index < 4; index++) expect((await context.request.post(`/lists/${lists[1].id}/cards`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: `Retained planning Card ${index + 1} with a readable longer description of the upcoming workflow` },
    })).status()).toBe(201);
    const before = await (await context.request.get(`/boards/${board}`)).json();
    const path = `/app/${org}/boards/${board}`; await page.goto(path);
    await expect(page.getByRole('heading', { name: 'List workflow Board', exact: true })).toBeVisible();
    const writes: { body: string | null; key: string | undefined }[] = [];
    page.on('request', request => { if (request.method() === 'PATCH' && new URL(request.url()).pathname === `/lists/${lists[1].id}`)
      writes.push({ body: request.postData(), key: request.headers()['idempotency-key'] }); });
    const rename = page.getByRole('button', { name: 'Rename Planning list', exact: true });
    const field = page.getByRole('textbox', { name: 'New list name', exact: true });
    await openAdmittedReview(rename, field); await expect(field).toBeFocused();
    await field.fill('   '); await pressAdmittedAction(page.getByRole('button', { name: 'Save list name', exact: true }));
    await expect(page.getByRole('alert')).toContainText('Use a list name with 1 to 160 characters.'); expect(writes).toHaveLength(0);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`list-rename-review-${width}.png`) });
    await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0); await expect(rename).toBeFocused();
    await openAdmittedReview(rename, field); await field.fill('Planning renamed');
    await pressObservedRequest(page.getByRole('button', { name: 'Save list name', exact: true }), () => writes.length, 1);
    const currentRename = page.getByRole('button', { name: 'Rename Planning renamed list', exact: true });
    await expect(page.getByRole('dialog')).toHaveCount(0); await expect(currentRename).toBeFocused();
    const move = page.getByRole('button', { name: 'Move Planning renamed list', exact: true });
    const position = page.getByRole('combobox', { name: 'Position for Planning renamed', exact: true });
    await openAdmittedReview(move, position);
    await openAdmittedReview(position, page.getByRole('listbox', { name: 'Position for Planning renamed', exact: true }));
    await page.getByRole('option', { name: 'Before First', exact: true }).focus(); await page.keyboard.press('Enter');
    await pressObservedRequest(page.getByRole('button', { name: 'Confirm list move', exact: true }), () => writes.length, 2);
    await expect(move).toBeEnabled(); await expect(move).toBeFocused();
    const geometry = await move.evaluate(control => {
      const target = control.getBoundingClientRect(), header = document.querySelector('header')?.getBoundingClientRect();
      const clipped: string[] = [];
      for (let parent = control.parentElement; parent; parent = parent.parentElement) {
        const style = getComputedStyle(parent), box = parent.getBoundingClientRect();
        if (['auto', 'scroll', 'hidden', 'clip'].includes(style.overflowY) && (target.top < box.top + parent.clientTop - 1 || target.bottom > box.top + parent.clientTop + parent.clientHeight + 1)) clipped.push('vertical:' + parent.tagName);
        if (['auto', 'scroll', 'hidden', 'clip'].includes(style.overflowX) && (target.left < box.left + parent.clientLeft - 1 || target.right > box.left + parent.clientLeft + parent.clientWidth + 1)) clipped.push('horizontal:' + parent.tagName);
      }
      return { clipped, top: target.top, bottom: target.bottom, headerBottom: header?.bottom, height: innerHeight, overflow: document.documentElement.scrollWidth > innerWidth };
    });
    expect(geometry.clipped, 'Focused List action must be fully visible inside local scroll containers').toEqual([]);
    expect(geometry.headerBottom).toBeDefined(); expect(geometry.top).toBeGreaterThanOrEqual(geometry.headerBottom!); expect(geometry.bottom).toBeLessThanOrEqual(geometry.height); expect(geometry.overflow).toBe(false);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath(`list-order-return-${width}.png`) });
    const canonical = await context.request.get(`/boards/${board}`); expect(canonical.status()).toBe(200); const final = await canonical.json();
    expect(final.lists.map((column: { list: { id: string } }) => column.list.id)).toEqual([lists[1].id, lists[0].id, lists[2].id]);
    expect(final.lists[0].list).toMatchObject({ id: lists[1].id, name: 'Planning renamed', version: lists[1].version + 2 });
    expect(final.lists[0].cards).toEqual(before.lists[1].cards); expect(final.lists[1]).toEqual(before.lists[0]); expect(final.lists[2]).toEqual(before.lists[2]);
    expect(writes).toHaveLength(2); expect(JSON.parse(writes[0].body!)).toEqual({ name: 'Planning renamed', version: lists[1].version });
    expect(JSON.parse(writes[1].body!)).toEqual({ name: 'Planning renamed', version: lists[1].version + 1, beforeListId: lists[0].id });
    expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[1].key).toMatch(/^[0-9a-f-]{36}$/); expect(writes[1].key).not.toBe(writes[0].key);
    await page.reload(); await expect(page.getByRole('heading', { name: 'Planning renamed', exact: true })).toBeVisible();
    await expect(page.locator('[aria-label="Kanban board"]').getByRole('region').first()).toHaveAccessibleName('Planning renamed');
    if (width === 768) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
