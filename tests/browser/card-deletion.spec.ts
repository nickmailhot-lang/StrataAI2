import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-08/18: irreversible Card deletion recovers its exact committed receipt at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `card-deletion-${width}-${Date.now()}@example.test`, password: 'card-deletion-correct-horse', displayName: 'Deletion editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Card deletion fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Deletion Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    const cardReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Reviewed Card' } });
    expect(cardReply.status()).toBe(201); const card = await cardReply.json();
    expect((await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Active neighbor' } })).status()).toBe(201);
    expect((await context.request.post(`/cards/${card.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    if (width === 390) expect((await context.request.post(`/lists/${list.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    const before = await (await context.request.get(`/boards/${board}`)).json();
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
      const path = `/app/${org}/boards/${board}/archived-cards`; await page.goto(path); await other.goto(path);
      await expect(other.getByRole('heading', { name: 'Reviewed Card', exact: true })).toBeVisible();
      const writes: { key: string | undefined; body: string | null; url: string; method: string }[] = [];
      await page.route(`**/cards/${card.id}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData(), url: route.request().url(), method: route.request().method() });
        const response = await route.fetch(); expect(response.status()).toBe(200);
        if (writes.length === 1) await route.abort('failed'); else await route.fulfill({ response });
      });
      await expect(page.getByText('Archive updates: live.', { exact: true })).toBeVisible();
      const reviewDeletion = page.getByRole('button', { name: 'Permanently delete Reviewed Card card', exact: true });
      await expect(reviewDeletion).toBeEnabled(); await reviewDeletion.focus(); await expect(reviewDeletion).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page.getByText('Permanently delete Reviewed Card from Planning?', { exact: true })).toBeVisible();
      await expect(page.getByText('This cannot be undone. This Card can no longer be restored or used.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toBeDisabled(); expect(writes).toHaveLength(0);
      await page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true }).focus(); await page.keyboard.press('Space');
      await page.getByRole('button', { name: 'Confirm permanent deletion', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry this deletion', exact: true })).toBeEnabled();
      await expect(page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Cancel deletion', exact: true })).toHaveCount(0);
      await expect(other.getByText('No archived cards on this page.', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Retry this deletion', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('No archived cards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current archived cards', exact: true })).toBeFocused();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]); expect(writes[0].body).toBeNull(); expect(writes[0].method).toBe('DELETE');
      expect([...new URL(writes[0].url).searchParams.entries()]).toEqual([['version', '2'], ['confirmed', 'true']]);
      expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect((await context.request.post(`/cards/${card.id}/restore`, { headers, data: { version: 3 } })).status()).toBe(404);
      expect((await context.request.get(`/boards/${board}`)).status()).toBe(200);
      const after = await (await context.request.get(`/boards/${board}`)).json(); expect(after).toEqual(before);
      await page.reload(); await expect(page.getByText('No archived cards on this page.', { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true); await other.close();
    } finally { restoreWorker(); }
  });
}
