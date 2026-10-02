import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-07: list rename recovers a lost acknowledgment and preserves a live draft at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000);
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `list-rename-${width}-${Date.now()}@example.test`, password: 'rename-correct-horse-battery', displayName: 'List editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'List rename fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Rename board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    expect((await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Neighbor' } })).status()).toBe(201);
    expect((await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Keep this card' } })).status()).toBe(201);
    const before = await (await context.request.get(`/boards/${board}`)).json();
    const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
    const path = `/app/${org}/boards/${board}`;
      const reads = trackBoardReads(page, board, path); const otherReads = trackBoardReads(other, board, path);
      await page.goto(path); await other.goto(path);
      await expect.poll(reads).toBeGreaterThanOrEqual(2); await expect.poll(otherReads).toBeGreaterThanOrEqual(2);
      await other.getByRole('button', { name: 'Rename Planning list', exact: true }).click();
      await other.getByRole('textbox', { name: 'New list name' }).fill('Preserved draft');
      const requests: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/lists/${list.id}`, async route => {
        if (route.request().method() !== 'PATCH') { await route.continue(); return; }
        requests.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (requests.length === 1) await route.abort('failed'); else await route.fulfill({ response });
      });
      await page.getByRole('button', { name: 'Rename Planning list', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('textbox', { name: 'New list name' }).fill('Renamed');
      await page.getByRole('button', { name: 'Save list name', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Retry this rename', exact: true })).toBeEnabled();
      await expect(page.getByRole('textbox', { name: 'New list name' })).toHaveValue('Renamed');
      await expect(page.getByRole('textbox', { name: 'New list name' })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Cancel rename', exact: true })).toHaveCount(0);
      await expect(other.getByText('This list changed elsewhere. Your draft is preserved.', { exact: true })).toBeVisible();
      await expect(other.getByRole('textbox', { name: 'New list name' })).toHaveValue('Preserved draft');
      await expect(other.getByRole('button', { name: 'Save list name', exact: true })).toBeDisabled();
      await page.getByRole('button', { name: 'Retry this rename', exact: true }).click();
      await expect(page.getByText('List rename acknowledged. Checking current list.', { exact: true })).toBeVisible();
      expect(requests).toHaveLength(2); expect(requests[1]).toEqual(requests[0]);
      expect(requests[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(JSON.parse(requests[0].body!)).toEqual({ name: 'Renamed', version: list.version });
      const current = await (await context.request.get(`/boards/${board}`)).json();
      expect(current.lists[0].list).toMatchObject({ id: list.id, name: 'Renamed', rank: list.rank, version: list.version + 1 });
      expect(current.lists[0].cards).toEqual(before.lists[0].cards); expect(current.lists[1]).toEqual(before.lists[1]);
      await other.getByRole('button', { name: 'Discard draft and use current list', exact: true }).click();
      await expect(other.getByRole('textbox', { name: 'New list name' })).toHaveValue('Renamed');
      await other.getByRole('textbox', { name: 'New list name' }).fill('Final name');
      await expect(other.getByRole('button', { name: 'Save list name', exact: true })).toBeEnabled();
      await other.getByRole('button', { name: 'Save list name', exact: true }).click();
      await expect(page.getByRole('heading', { name: 'Final name', exact: true })).toBeVisible();
      await expect(other.getByRole('heading', { name: 'Final name', exact: true })).toBeVisible();
      await page.reload(); await expect(page.getByRole('heading', { name: 'Final name', exact: true })).toBeVisible();
      const final = await (await context.request.get(`/boards/${board}`)).json();
      expect(final.lists[0].list).toMatchObject({ id: list.id, name: 'Final name', rank: list.rank, version: list.version + 2 });
      expect(final.lists[0].cards).toEqual(before.lists[0].cards); expect(final.lists[1]).toEqual(before.lists[1]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await other.close();
    } finally { restoreWorker(); }
  });
}
