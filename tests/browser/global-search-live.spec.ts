import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-16 global search crosses Organizations and recovers changed and missed state at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `global-search-${width}-${Date.now()}@example.test`,
      password: 'global-search-correct-horse', displayName: 'Global search reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const records: { org: string; board: string; card: string; title: string; version: number }[] = [];
    for (const name of ['One', 'Two']) {
      const orgReply = await context.request.post('/organizations', { headers, data: { name: `Global search ${name}` } });
      expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
      const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: `Board ${name}`, visibility: 'PRIVATE' } });
      expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
      const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
      expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
      const title = `Global needle ${name}`;
      const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title } });
      expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
      const labelReply = await context.request.post(`/boards/${board}/labels`, { headers, data: { name: 'Priority', color: 'red' } });
      expect(labelReply.status()).toBe(201); const label = (await labelReply.json()).id;
      expect((await context.request.put(`/cards/${card}/labels/${label}?version=1`, { headers, data: {} })).status()).toBe(200);
      expect((await context.request.put(`/cards/${card}/members/${actor}?version=2`, { headers, data: {} })).status()).toBe(200);
      records.push({ org, board, card, title, version: 3 });
    }
    let restoreWorker = () => {};
    try {
      await page.goto(`/app/${records[0].org}/search`);
      await page.getByRole('textbox', { name: 'Card text', exact: true }).fill('needle');
      await page.getByRole('textbox', { name: 'Label name', exact: true }).fill('prior');
      await page.getByRole('textbox', { name: 'Member name', exact: true }).fill('search reader');
      await page.getByRole('button', { name: 'Search', exact: true }).press('Enter');
      const firstLink = page.getByRole('link', { name: /^Global needle/ });
      await expect(firstLink).toHaveCount(1);
      const firstTitle = await firstLink.innerText(); const current = records.find(r => r.title === firstTitle)!;
      expect(current).toBeDefined();
      await expect(firstLink).toHaveAttribute('href', `/app/${current.org}/boards/${current.board}/cards/${current.card}`);
      await expect(page.getByText('Labels: Priority', { exact: true })).toBeVisible();
      await expect(page.getByText('Members: Global search reader', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Next search page', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: records.find(r => r !== current)!.title, exact: true })).toBeVisible();
      // Start again to observe the first canonical result on its initial page.
      await page.getByRole('button', { name: 'Search', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: current.title, exact: true })).toBeVisible();
      restoreWorker = scopedBoardWorker(current.org); await waitForBoardDelivery(context.request, current.board);
      async function rename(title: string) {
        const reply = await context.request.patch(`/cards/${current.card}`, {
          headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
          data: { title, description: null, version: current.version },
        });
        expect(reply.status()).toBe(200); current.version = (await reply.json()).version;
      }
      await rename('Global needle changed');
      await expect(page.getByRole('link', { name: 'Global needle changed', exact: true })).toBeVisible({ timeout: 25_000 });
      await context.setOffline(true);
      await page.getByRole('button', { name: 'Refresh results', exact: true }).press('Enter');
      await expect(page.getByText(/Search is unavailable/)).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('link', { name: 'Global needle changed', exact: true })).toHaveCount(0);
      await rename('Global needle recovered'); await waitForBoardDelivery(context.request, current.board);
      await context.setOffline(false);
      await expect(page.getByRole('link', { name: 'Global needle recovered', exact: true })).toBeVisible({ timeout: 30_000 });
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { try { await context.setOffline(false); } finally { restoreWorker(); } }
  });
}
