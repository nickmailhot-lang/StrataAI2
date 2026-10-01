import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';

for (const viewport of [{ width: 1280, height: 800 }, { width: 390, height: 844 }]) {
  test(`PRD-06: keyboard list positioning persists at ${viewport.width}px`, async ({ page, context }) => {
    await page.setViewportSize(viewport);
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `list-position-${viewport.width}-${Date.now()}@example.test`, password: 'list-position-correct-horse', displayName: 'List mover' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'List positions' } });
    expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Ordered lists', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    const lists: { id: string; name: string; rank: string }[] = [];
    for (const name of ['First', 'Second']) {
      const result = await context.request.post(`/boards/${board}/lists`, { headers, data: { name } });
      expect(result.status()).toBe(201); lists.push(await result.json());
    }
    const reads = trackBoardReads(page, board, `/app/${org}/boards/${board}`);
    await page.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    const move = page.getByRole('button', { name: 'Move Second list', exact: true });
    await expect(move).toBeEnabled(); await move.focus(); await page.keyboard.press('Enter');
    const position = page.getByRole('combobox', { name: 'Position for Second' }); await position.press('ArrowDown');
    await expect(page.getByRole('listbox', { name: 'Position for Second' })).toBeVisible();
    await page.getByRole('option', { name: 'Before First', exact: true }).focus(); await page.keyboard.press('Enter');
    await page.getByRole('button', { name: 'Confirm list move', exact: true }).focus(); await page.keyboard.press('Enter');
    await expect(page.getByText('List move acknowledged. Current ordering is being checked.')).toBeVisible();
    await expect(move).toBeEnabled();
    await expect(move).toBeFocused();
    const result = await context.request.get(`/boards/${board}`); expect(result.status()).toBe(200);
    const order = (await result.json()).lists.map((column: { list: { id: string; rank: string; version: number } }) => column.list);
    expect(order.map((list: { id: string }) => list.id)).toEqual([lists[1].id, lists[0].id]);
    expect(order[0].version).toBe(2); expect(order[1].rank).toBe(lists[0].rank);
    if (viewport.width === 1280) {
      const source = await page.getByRole('button', { name: 'Drag First list', exact: true }).boundingBox();
      const target = await page.getByRole('region', { name: 'Second', exact: true }).boundingBox();
      expect(source).not.toBeNull(); expect(target).not.toBeNull();
      await page.mouse.move(source!.x + source!.width / 2, source!.y + source!.height / 2);
      await page.mouse.down();
      await page.mouse.move(target!.x + target!.width / 2, target!.y + target!.height / 2, { steps: 12 });
      await page.mouse.up();
      await expect(page.getByRole('region').first()).toHaveAccessibleName('First');
      await expect(page.getByRole('button', { name: 'Move First list', exact: true })).toBeEnabled();
      const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
      const persisted = (await current.json()).lists.map((column: { list: { id: string; version: number } }) => column.list);
      expect(persisted.map((list: { id: string }) => list.id)).toEqual([lists[0].id, lists[1].id]);
      expect(persisted.map((list: { version: number }) => list.version)).toEqual([2, 2]);
    }
    await page.reload();
    await expect(page.getByRole('region').first()).toHaveAccessibleName(viewport.width === 1280 ? 'First' : 'Second');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
