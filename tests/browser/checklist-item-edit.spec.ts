import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: item completion history, exact retry and live progress at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-item-edit-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const actor = (await (await context.request.get('/me')).json()).id;
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist editing Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist editing Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist editing List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Editable item work' } })).json()).id;
    const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 } });
    expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
    const added = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Prepare', cardVersion: 2, checklistVersion: 1 } });
    expect(added.status()).toBe(200); const item = (await added.json()).item;
    const route = `/app/${org}/boards/${board}/cards/${card}`; await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible();
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true });
      async function edit(text: string) {
        await expect(manage).toBeEnabled(); await manage.press('Enter');
        await page.getByRole('button', { name: 'Manage items in Preparations', exact: true }).press('Enter');
        const review = page.getByRole('button', { name: 'Review checklist items', exact: true }); await expect(review).toBeEnabled(); await review.press('Enter');
        await page.getByRole('button', { name: `Edit item: ${text}`, exact: true }).press('Enter');
      }
      await edit('Prepare'); const text = page.getByRole('textbox', { name: 'Checklist item text' }); await expect(text).toBeFocused();
      await text.fill(' Prepared carefully '); await page.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      const commandPath = `**/cards/${card}/checklists/${checklist.id}/items/${item.id}`;
      await page.route(commandPath, async intercepted => {
        if (intercepted.request().method() !== 'PATCH') { await intercepted.continue(); return; }
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      await page.getByRole('button', { name: 'Save checklist item', exact: true }).press('Enter');
      const retry = page.getByRole('button', { name: 'Retry checklist item change', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await expect(text).toBeDisabled(); await expect(page.getByRole('checkbox', { name: 'Item complete', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Checklist item saved.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ text: 'Prepared carefully', completed: true, cardVersion: 3, checklistVersion: 2, version: 1 });
      await expect(peer.getByText('1 of 1 items complete (100%)', { exact: true })).toBeVisible({ timeout: 20_000 });
      const path = `/cards/${card}/checklists/${checklist.id}/items`;
      const first = await context.request.get(path); expect(first.status()).toBe(200); const completed = await first.json();
      expect(completed.cardVersion).toBe(4); expect(completed.items[0].completedBy).toBe(actor); expect(completed.items[0].completedAt).toBe(completed.items[0].updatedAt);
      await page.unroute(commandPath); await edit('Prepared carefully'); await text.fill('Prepared thoroughly');
      await expect(page.getByRole('checkbox', { name: 'Item complete', exact: true })).toBeChecked();
      await page.getByRole('button', { name: 'Save checklist item', exact: true }).press('Enter'); await expect(page.getByText('Checklist item saved.', { exact: true })).toBeVisible();
      const second = await context.request.get(path); expect(second.status()).toBe(200); const renamed = await second.json();
      expect(renamed.cardVersion).toBe(5); expect(renamed.items[0].completedBy).toBe(completed.items[0].completedBy); expect(renamed.items[0].completedAt).toBe(completed.items[0].completedAt);
      await edit('Prepared thoroughly'); await page.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      await page.getByRole('button', { name: 'Save checklist item', exact: true }).press('Enter'); await expect(page.getByText('Checklist item saved.', { exact: true })).toBeVisible();
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible({ timeout: 20_000 });
      await peer.getByRole('button', { name: 'Show items in Preparations', exact: true }).press('Enter');
      await expect(peer.getByText('Incomplete: Prepared thoroughly', { exact: true })).toBeVisible();
      const final = await context.request.get(path); expect(final.status()).toBe(200); const undone = await final.json();
      expect(undone.cardVersion).toBe(6); expect(undone.summary.checklist.version).toBe(5); expect(undone.items[0].version).toBe(4);
      expect(undone.items[0].completedBy).toBeNull(); expect(undone.items[0].completedAt).toBeNull();
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
