import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: keyboard item positioning, exact retry and live ordering at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-item-position-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Item ordering Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Item ordering Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Item ordering List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Ordered item work' } })).json()).id;
    const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 } });
    expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
    const items: { id: string }[] = [];
    for (const [index, text] of ['Prepare', 'Execute', 'Review'].entries()) {
      const response = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text, cardVersion: index + 2, checklistVersion: index + 1 } });
      expect(response.status()).toBe(200); items.push((await response.json()).item);
    }
    const route = `/app/${org}/boards/${board}/cards/${card}`; await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      const showItems = peer.getByRole('button', { name: 'Show items in Preparations', exact: true }); await expect(showItems).toBeEnabled(); await showItems.press('Enter');
      const rows = peer.getByRole('region', { name: 'Card checklists', exact: true }).getByText(/^Incomplete:/);
      await expect(rows).toHaveText(['Incomplete: Prepare', 'Incomplete: Execute', 'Incomplete: Review']);
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true });
      async function move() {
        await expect(manage).toBeEnabled(); await manage.press('Enter');
        await page.getByRole('button', { name: 'Manage items in Preparations', exact: true }).press('Enter');
        const review = page.getByRole('button', { name: 'Review checklist items', exact: true }); await expect(review).toBeEnabled(); await review.press('Enter');
        await page.getByRole('button', { name: 'Move item: Prepare', exact: true }).press('Enter');
      }
      await move(); const save = page.getByRole('button', { name: 'Save item position', exact: true }); await expect(save).toBeDisabled();
      await page.getByRole('button', { name: 'Place before item: Review', exact: true }).press('Enter'); await expect(save).toBeEnabled();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      const commandPath = `**/cards/${card}/checklists/${checklist.id}/items/${items[0].id}/position`;
      await page.route(commandPath, async intercepted => {
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      await save.press('Enter'); const retry = page.getByRole('button', { name: 'Retry checklist item move', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Place before item: Review', exact: true })).toHaveCount(0);
      await retry.press('Enter'); await expect(page.getByText('Checklist item moved.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ beforeId: items[2].id, cardVersion: 5, checklistVersion: 4, version: 1 });
      await expect(showItems).toBeEnabled({ timeout: 20_000 }); await showItems.press('Enter');
      await expect(rows).toHaveText(['Incomplete: Execute', 'Incomplete: Prepare', 'Incomplete: Review']);
      await page.unroute(commandPath); await move(); await page.getByRole('button', { name: 'Place item at end', exact: true }).press('Enter'); await save.press('Enter');
      await expect(page.getByText('Checklist item moved.', { exact: true })).toBeVisible();
      await expect(showItems).toBeEnabled({ timeout: 20_000 }); await showItems.press('Enter');
      await expect(rows).toHaveText(['Incomplete: Execute', 'Incomplete: Review', 'Incomplete: Prepare']);
      const response = await context.request.get(`/cards/${card}/checklists/${checklist.id}/items`); expect(response.status()).toBe(200); const result = await response.json();
      expect(result.cardVersion).toBe(7); expect(result.summary.checklist.version).toBe(6); expect(result.summary.total).toBe(3); expect(result.summary.completed).toBe(0);
      expect(result.items.map((item: { id: string }) => item.id)).toEqual([items[1].id, items[2].id, items[0].id]); expect(result.items[2].version).toBe(3);
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
