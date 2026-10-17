import AxeBuilder from '@axe-core/playwright';
import { test, expect } from './releaseTest';

for (const width of [1280, 768, 390, 320]) {
  test(`UXA-02/04/05/06: real work create assign edit filter and return at ${width}px`, async ({ page, context, browser }, info) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width, height: 900 });
    async function activate(name: string, scope = page.getByRole('main')) {
      const control = scope.getByRole('button', { name, exact: true });
      await expect(control).toBeEnabled(); await control.press('Enter');
    }
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `work-ux-${width}-${Date.now()}@example.test`, password: 'work-ux-correct-horse-battery', displayName: 'Work reviewer' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: `Cedar Court ${width}` } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Maintenance work', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listIds: string[] = [];
    for (const name of ['Reported', 'Scheduled', 'Resolved']) {
      const reply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name } });
      expect(reply.status()).toBe(201); listIds.push((await reply.json()).id);
    }
    expect((await context.request.post(`/lists/${listIds[0]}/cards`, { headers, data: { title: 'Replace lobby lamp' } })).status()).toBe(201);
    const boardPath = `/app/${org}/boards/${board}`;
    await page.goto(boardPath);
    await expect(page.getByRole('heading', { name: 'Maintenance work', exact: true })).toBeVisible();
    if (width < 600) {
      const trigger = page.getByRole('button', { name: 'Open navigation', exact: true });
      await trigger.press('Enter');
      const nav = page.getByRole('navigation', { name: 'Mobile application navigation' });
      await expect(nav.getByRole('link', { name: 'Work', exact: true })).toHaveAttribute('aria-current', 'page');
      await page.keyboard.press('Escape'); await expect(trigger).toBeFocused();
    } else await expect(page.getByRole('link', { name: 'Work', exact: true })).toHaveAttribute('aria-current', 'page');
    await activate('Add card to Reported', page.locator('body'));
    await page.getByRole('textbox', { name: 'Card title', exact: false }).fill('Inspect roof');
    await activate('Create', page.locator('body'));
    await page.getByRole('link', { name: 'Inspect roof', exact: true }).press('Enter');
    const detail = page.getByRole('dialog', { name: 'Card details', exact: true });
    await expect(detail).toBeVisible();
    const cardPath = new URL(page.url()).pathname;
    const cardId = cardPath.split('/').at(-1)!;
    await activate('Edit Card assignees', detail);
    await activate('Assign Work reviewer', detail);
    await expect(detail.getByRole('button', { name: 'Edit Card assignees', exact: true })).toBeEnabled();
    await activate('Edit dates', detail);
    await detail.getByLabel('Due date', { exact: true }).fill('2026-10-01');
    await activate('Save dates', detail);
    await expect(detail.getByText('Dates saved.', { exact: true })).toBeVisible();
    await activate('Close', detail);
    const face = page.getByRole('link', { name: 'Inspect roof', exact: true });
    await expect(face).toBeFocused(); await expect(face).toContainText('Assigned to Work reviewer');
    await expect(face).toContainText('Oct 1, 2026'); await expect(face).toContainText('Overdue');
    await page.screenshot({ path: info.outputPath('board.png'), fullPage: true });

    await activate('Filter Board Cards', page.locator('body'));
    const filters = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
    await filters.getByRole('textbox', { name: 'Card keyword', exact: true }).fill('Inspect');
    await activate('Apply filters', filters);
    await expect(filters.getByRole('link', { name: 'Inspect roof — Reported', exact: true })).toBeVisible();
    await activate('Show this page on Board', filters);
    await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
    await face.focus();
    const scroll = await page.getByRole('region', { name: 'Kanban board', exact: true }).evaluate(el => el.scrollLeft);
    await face.press('Enter'); await expect(detail).toBeVisible();
    expect(await page.getByRole('region', { name: 'Kanban board', exact: true, includeHidden: true }).evaluate(el => el.scrollLeft)).toBe(scroll);
    await expect(page.locator('[data-card-drag-id]').filter({ hasText: 'Replace lobby lamp' })).toHaveCount(0);
    const title = detail.getByRole('textbox', { name: 'Card title', exact: false });
    await title.fill('Inspect roof and gutters');
    await activate('Close', detail);
    await activate('Keep editing', page.locator('body'));
    await expect(title).toHaveValue('Inspect roof and gutters');
    await page.goBack();
    await activate('Keep editing', page.locator('body'));
    await expect(page).toHaveURL(new RegExp(`${cardPath}$`));
    await expect(title).toHaveValue('Inspect roof and gutters');
    const attempts: string[] = [];
    await page.route(`**/cards/${cardId}`, async route => {
      if (route.request().method() !== 'PATCH') return route.continue();
      attempts.push(route.request().headers()['idempotency-key']);
      if (attempts.length === 1) await route.fulfill({ status: 503, json: {} }); else await route.continue();
    });
    await activate('Save card', detail);
    await expect(detail.getByRole('alert').first()).toBeVisible();
    await expect(title).toHaveValue('Inspect roof and gutters');
    await activate('Save card', detail);
    await expect(detail.getByText('Changes saved.', { exact: true })).toBeVisible();
    expect(attempts).toHaveLength(2); expect(attempts[0]).toBe(attempts[1]);
    await page.unroute(`**/cards/${cardId}`);
    await activate('Move card', detail);
    await detail.getByRole('combobox', { name: 'Destination list', exact: true }).click();
    await page.getByRole('option', { name: 'Scheduled', exact: true }).click();
    await activate('Confirm card move', detail);
    await expect(detail.getByText('Move acknowledged. Current placement is being checked.', { exact: true })).toBeVisible();
    await activate('Close', detail);
    await expect(page.getByRole('link', { name: 'Inspect roof and gutters', exact: true })).toBeFocused();
    await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
    await activate('Clear Board filters', page.locator('body'));
    await expect(page.getByRole('link', { name: 'Replace lobby lamp', exact: true })).toBeVisible();
    const canonical = await (await context.request.get(`/boards/${board}`)).json();
    expect(canonical.lists.find((column: { list: { id: string } }) => column.list.id === listIds[1]).cards.some((card: { id: string }) => card.id === cardId)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    await page.getByRole('link', { name: 'Inspect roof and gutters', exact: true }).press('Enter');
    await expect(detail).toBeVisible();
    await page.screenshot({ path: info.outputPath('detail.png'), fullPage: true });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    const outsider = await browser.newContext();
    try {
      const denied = await outsider.request.get(`${new URL(page.url()).origin}/boards/${board}`);
      expect([401, 403, 404]).toContain(denied.status()); expect(await denied.text()).not.toContain('Maintenance work');
    } finally { await outsider.close(); }
  });
}
