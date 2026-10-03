import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: keyboard checklist positioning, original retry and live ordering at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-position-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist contributor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist positioning Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist positioning Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist positioning List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Ordered work' } })).json()).id;
    const children: { id: string }[] = [];
    for (const [index, title] of ['Preparations', 'Execution', 'Review'].entries()) {
      const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title, cardVersion: index + 1 } });
      expect(created.status()).toBe(200); children.push((await created.json()).checklist);
    }
    const route = `/app/${org}/boards/${board}/cards/${card}`; await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      const titles = peer.getByRole('region', { name: 'Card checklists', exact: true }).getByRole('heading', { level: 3 });
      await expect(titles).toHaveText(['Preparations', 'Execution', 'Review']);
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
      await page.getByRole('button', { name: 'Move Preparations', exact: true }).press('Enter');
      const save = page.getByRole('button', { name: 'Save checklist position', exact: true }); await expect(save).toBeDisabled();
      await page.getByRole('button', { name: 'Place before Review', exact: true }).press('Enter'); await expect(save).toBeEnabled();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      await page.route(`**/cards/${card}/checklists/${children[0].id}/position`, async intercepted => {
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      await save.press('Enter'); const retry = page.getByRole('button', { name: 'Retry checklist move', exact: true }); await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Place before Review', exact: true })).toHaveCount(0);
      await retry.press('Enter'); await expect(page.getByText('Checklist moved.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ beforeId: children[2].id, cardVersion: 4, version: 1 });
      await expect(titles).toHaveText(['Execution', 'Preparations', 'Review'], { timeout: 20_000 });
      await page.unroute(`**/cards/${card}/checklists/${children[0].id}/position`);
      await expect(manage).toBeEnabled(); await manage.press('Enter');
      await page.getByRole('button', { name: 'Move Preparations', exact: true }).press('Enter');
      await page.getByRole('button', { name: 'Place at end', exact: true }).press('Enter'); await save.press('Enter');
      await expect(page.getByText('Checklist moved.', { exact: true })).toBeVisible();
      await expect(titles).toHaveText(['Execution', 'Review', 'Preparations'], { timeout: 20_000 });
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
