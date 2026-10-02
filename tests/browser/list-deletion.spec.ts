import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-07/18: irreversible List deletion reviews impact and recovers its committed receipt at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `list-deletion-${width}-${Date.now()}@example.test`, password: 'deletion-correct-horse-battery', displayName: 'Deletion editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Deletion browser fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deletion Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Reviewed List' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    const neighborReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Neighbor' } });
    expect(neighborReply.status()).toBe(201); const neighbor = await neighborReply.json();
    const activeReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Active contained card' } });
    expect(activeReply.status()).toBe(201); const activeCard = await activeReply.json();
    const archivedReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Archived contained card' } });
    expect(archivedReply.status()).toBe(201); const archivedCard = await archivedReply.json();
    expect((await context.request.post(`/cards/${archivedCard.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    expect((await context.request.post(`/lists/${list.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    const before = await (await context.request.get(`/boards/${board}`)).json();
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
    const path = `/app/${org}/boards/${board}/archived-lists`; const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
      await page.goto(path); await other.goto(path);
      await expect(other.getByRole('heading', { name: 'Reviewed List', exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Permanently delete Reviewed List list', exact: true })).toBeEnabled();
      const writes: { url: string; key: string | undefined; method: string; body: string | null }[] = [];
      await page.route(`**/lists/${list.id}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], method: route.request().method(), body: route.request().postData() });
        const result = await route.fetch(); expect(result.status()).toBe(200);
        if (writes.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      await page.getByRole('button', { name: 'Permanently delete Reviewed List list', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Permanently delete Reviewed List and make its 2 contained cards unavailable?', { exact: true })).toBeVisible();
      await expect(page.getByText('This cannot be undone. This List cannot be restored, and its contained cards can no longer be used through it.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toBeDisabled();
      expect(writes).toHaveLength(0);
      await page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true }).focus(); await page.keyboard.press('Space');
      await page.getByRole('button', { name: 'Confirm permanent deletion', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry this deletion', exact: true })).toBeEnabled();
      await expect(page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Cancel deletion', exact: true })).toHaveCount(0);
      await expect(other.getByText('No archived lists on this page.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Retry this deletion', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('No archived lists on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current archived lists', exact: true })).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      const query = new URL(writes[0].url).searchParams;
      expect([...query.entries()]).toEqual([['version', '2'], ['confirmed', 'true'], ['containedCardCount', '2']]);
      expect(writes[0].body).toBeNull();
      expect((await context.request.post(`/lists/${list.id}/restore`, { headers, data: { version: 3 } })).status()).toBe(404);
      expect((await context.request.post(`/cards/${archivedCard.id}/restore`, { headers, data: { version: 2 } })).status()).toBe(404);
      expect((await context.request.post(`/cards/${activeCard.id}/move`, { headers, data: { destinationListId: neighbor.id, version: 1, moveToEnd: true } })).status()).toBe(404);
      const after = await (await context.request.get(`/boards/${board}`)).json(); expect(after).toEqual(before);
      await page.reload(); await expect(page.getByText('No archived lists on this page.', { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await other.close();
    } finally { restoreWorker(); }
  });
}
