import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: keyboard item creation, exact retry and two-session progress at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-item-create-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist item Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist item Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist item List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Item work' } })).json()).id;
    const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 } });
    expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
    const route = `/app/${org}/boards/${board}/cards/${card}`; await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await peer.getByRole('button', { name: 'Show items in Preparations', exact: true }).press('Enter');
      await expect(peer.getByText('No items on this page.', { exact: true })).toBeVisible();
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
      await page.getByRole('button', { name: 'Add item to Preparations', exact: true }).press('Enter');
      const save = page.getByRole('button', { name: 'Create checklist item', exact: true }); await expect(save).toBeDisabled();
      const text = page.getByRole('textbox', { name: 'Checklist item text' }); await expect(text).toBeFocused(); await text.fill(' Prepare materials ');
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      await page.route(`**/cards/${card}/checklists/${checklist.id}/items`, async intercepted => {
        if (intercepted.request().method() !== 'POST') { await intercepted.continue(); return; }
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      await save.press('Enter'); const retry = page.getByRole('button', { name: 'Retry checklist item creation', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await expect(text).toBeDisabled(); await expect(text).toHaveValue(' Prepare materials ');
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Checklist item added.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); expect(JSON.parse(attempts[0].body!)).toEqual({ text: 'Prepare materials', cardVersion: 2, checklistVersion: 1 });
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible({ timeout: 20_000 });
      await peer.getByRole('button', { name: 'Show items in Preparations', exact: true }).press('Enter');
      await expect(peer.getByText('Incomplete: Prepare materials', { exact: true })).toBeVisible();
      const response = await context.request.get(`/cards/${card}/checklists/${checklist.id}/items`); expect(response.status()).toBe(200);
      const result = await response.json(); expect(result.cardVersion).toBe(3); expect(result.summary.checklist.version).toBe(2);
      expect(result.summary.total).toBe(1); expect(result.items).toHaveLength(1); expect(result.items[0].completedBy).toBeNull(); expect(result.items[0].completedAt).toBeNull();
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
