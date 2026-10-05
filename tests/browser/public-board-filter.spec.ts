import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { trackBoardReads } from './boardReadTracker';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-16 anonymous PUBLIC Board filters retain criteria and withdraw disclosure at ${width}px`, async ({ page, context, request }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `public-filter-${width}-${Date.now()}@example.test`, password: 'public-filter-correct-horse', displayName: 'Private filter owner' };
    expect((await request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await request.post('/organizations', { headers, data: { name: 'Public filter fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await request.post('/boards', { headers, data: { organizationId: org, name: 'Public filtered Board', visibility: 'PUBLIC' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await request.post(`/lists/${list}/cards`, { headers, data: { title: 'Public 100%_ work' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    expect((await request.post(`/lists/${list}/cards`, { headers, data: { title: 'Other public work' } })).status()).toBe(201);
    const labelReply = await request.post(`/boards/${board}/labels`, { headers, data: { name: 'Priority', color: 'red' } });
    expect(labelReply.status()).toBe(201); const label = (await labelReply.json()).id;
    expect((await request.put(`/cards/${card}/labels/${label}?version=1`, { headers, data: {} })).status()).toBe(200);
    let restoreWorker = () => {};
    try {
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(request, board);
      expect((await context.request.get('/me')).status()).toBe(401);
      let privateFilterWrites = 0;
      page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/cards/filter-change')) privateFilterWrites++; });
      const beforeReply = await request.get(`/boards/${board}`); expect(beforeReply.status()).toBe(200);
      const before = await beforeReply.json();
      const path = `/app/${org}/boards/${board}`; const reads = trackBoardReads(page, board, path);
      await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const trigger = page.getByRole('button', { name: 'Filter Board Cards', exact: true });
      await expect(trigger).toBeEnabled(); await trigger.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Filter Board Cards', exact: true });
      const priority = dialog.getByRole('checkbox', { name: 'Priority (red)', exact: true });
      await expect(priority).toBeEnabled(); await priority.press('Space');
      await dialog.getByRole('textbox', { name: 'Card keyword', exact: true }).fill('100%_');
      await expect(dialog.getByRole('button', { name: 'Choose assignees', exact: true })).toHaveCount(0);
      await dialog.getByRole('button', { name: 'Apply filters', exact: true }).press('Enter');
      await expect(dialog.getByRole('link', { name: 'Public 100%_ work — Planning', exact: true })).toBeVisible();
      await dialog.getByRole('button', { name: 'Show this page on Board', exact: true }).press('Enter');
      await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Other public work', exact: true })).toHaveCount(0);
      await page.reload();
      await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('link', { name: 'Public 100%_ work', exact: true })).toBeVisible();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const retainedReply = await request.get(`/boards/${board}`); expect(retainedReply.status()).toBe(200);
      expect(await retainedReply.json()).toEqual(before);
      expect(privateFilterWrites).toBe(0);
      const withdraw = await request.patch(`/boards/${board}/visibility`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { visibility: 'PRIVATE', version: before.board.version },
      });
      expect(withdraw.status()).toBe(200); await waitForBoardDelivery(request, board);
      await expect(page.getByRole('link', { name: 'Public 100%_ work', exact: true })).toHaveCount(0, { timeout: 20_000 });
      await expect(page.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toHaveCount(0);
      expect((await context.request.get(`/boards/${board}/cards?keyword=100%25_`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${board}/labels`)).status()).toBe(404);
      expect((await context.request.get('/search?q=100%25_')).status()).toBe(401);
      const afterReply = await request.get(`/boards/${board}`); expect(afterReply.status()).toBe(200);
      const after = await afterReply.json(); expect(after.lists).toEqual(before.lists); expect(after.cardLabels).toEqual(before.cardLabels);
      expect(after.board).toEqual({ ...before.board, visibility: 'PRIVATE', version: before.board.version + 1, updatedAt: after.board.updatedAt });
    } finally { restoreWorker(); }
  });
}
