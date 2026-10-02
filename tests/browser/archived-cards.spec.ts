import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-18: reviewed Card restore respects parent changes and recovers a lost receipt at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `archived-cards-${width}-${Date.now()}@example.test`, password: 'archive-card-correct-horse', displayName: 'Archive editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Card archive fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Card archive Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    const cardReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Archived work', description: 'Detail stays out of archive UI' } });
    expect(cardReply.status()).toBe(201); const card = await cardReply.json();
    const neighborReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Active neighbor' } });
    expect(neighborReply.status()).toBe(201); const neighbor = await neighborReply.json();
    expect((await context.request.post(`/cards/${card.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
      const boardPath = `/app/${org}/boards/${board}`;
      await other.goto(boardPath); await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await expect(other.getByRole('link', { name: 'Archived work', exact: true })).toHaveCount(0);
      await page.goto(boardPath); await page.getByRole('link', { name: 'Archived cards', exact: true }).click();
      await expect(page.getByRole('heading', { name: 'Archived cards', exact: true })).toBeVisible();
      await expect(page.getByText('Detail stays out of archive UI', { exact: true })).toHaveCount(0);
      await page.getByRole('button', { name: 'Restore Archived work card', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Restore Archived work to Planning?', { exact: true })).toBeVisible();
      expect((await context.request.post(`/lists/${list.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
      await expect(page.getByText('This Card or its parent List changed. Cancel this review and check the current archive before another restore.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm restore', exact: true })).toBeDisabled();
      await page.getByRole('button', { name: 'Cancel restore', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Restore Archived work card', exact: true })).toBeDisabled();
      await expect(page.getByText('Restore the parent List before restoring this Card.', { exact: true })).toBeVisible();
      expect((await context.request.post(`/lists/${list.id}/restore`, { headers, data: { version: 2 } })).status()).toBe(200);
      await expect(page.getByRole('button', { name: 'Restore Archived work card', exact: true })).toBeEnabled();
      const writes: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/cards/${card.id}/restore`, async route => {
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (writes.length === 1) await route.abort('failed'); else await route.fulfill({ response });
      });
      await page.getByRole('button', { name: 'Restore Archived work card', exact: true }).focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Confirm restore', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry this restore', exact: true })).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Cancel restore', exact: true })).toHaveCount(0);
      await expect(other.getByRole('link', { name: 'Archived work', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Retry this restore', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('No archived cards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current archived cards', exact: true })).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(JSON.parse(writes[0].body!)).toEqual({ version: 2 });
      expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      const snapshot = await (await context.request.get(`/boards/${board}`)).json();
      expect(snapshot.lists[0].cards.find((c: { id: string }) => c.id === neighbor.id)).toEqual(neighbor);
      expect(snapshot.lists[0].cards.find((c: { id: string }) => c.id === card.id)).toMatchObject({ listId: list.id, rank: card.rank, version: 3, lifecycleState: 'active' });
      await page.reload(); await expect(page.getByText('No archived cards on this page.', { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await other.close();
    } finally { restoreWorker(); }
  });
}
