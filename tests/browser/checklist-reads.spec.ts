import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: keyboard checklist reads, canonical progress and two-session live refresh at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-read-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist reader' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Prepared work' } })).json()).id;
    const parentReply = await context.request.post(`/cards/${card}/checklists`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 },
    }); expect(parentReply.status()).toBe(200); const parent = (await parentReply.json()).checklist;
    const added = await context.request.post(`/cards/${card}/checklists/${parent.id}/items`, {
      headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Prepare materials', cardVersion: 2, checklistVersion: 1 },
    }); expect(added.status()).toBe(200); const item = (await added.json()).item;
    const route = `/app/${org}/boards/${board}/cards/${card}`;
    await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage();
      for (const reader of [page, peer]) {
        await reader.goto(route);
        const show = reader.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
        await expect(reader.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible();
        const children = reader.getByRole('button', { name: 'Show items in Preparations', exact: true }); await children.press('Enter');
        await expect(reader.getByText('Incomplete: Prepare materials', { exact: true })).toBeVisible();
        expect((await new AxeBuilder({ page: reader }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      }
      const complete = await peerContext.request.patch(`/cards/${card}/checklists/${parent.id}/items/${item.id}`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { text: 'Prepare materials', completed: true, cardVersion: 3, checklistVersion: 2, version: 1 },
      }); expect(complete.status()).toBe(200);
      for (const reader of [page, peer]) {
        await expect(reader.getByText('1 of 1 items complete (100%)', { exact: true })).toBeVisible({ timeout: 20_000 });
        await reader.getByRole('button', { name: 'Show items in Preparations', exact: true }).press('Enter');
        await expect(reader.getByText('Complete: Prepare materials', { exact: true })).toBeVisible();
        await reader.getByRole('button', { name: 'Hide checklists', exact: true }).press('Enter');
        await expect(reader.getByRole('region', { name: 'Card checklists', exact: true })).toHaveCount(0);
      }
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      await page.route(`**/cards/${card}/checklists`, async intercepted => {
        if (intercepted.request().method() !== 'POST') { await intercepted.continue(); return; }
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      const add = page.getByRole('button', { name: 'Add checklist', exact: true }); await expect(add).toBeEnabled(); await add.press('Enter');
      await page.getByRole('textbox', { name: /New checklist title/ }).fill('Follow-up preparation');
      await page.getByRole('button', { name: 'Create checklist', exact: true }).press('Enter');
      const retry = page.getByRole('button', { name: 'Retry checklist creation', exact: true }); await expect(retry).toBeEnabled();
      await expect(retry).toBeFocused(); await expect(page.getByRole('textbox', { name: /New checklist title/ })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Checklist created.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ title: 'Follow-up preparation', cardVersion: 4 });
      await page.unroute(`**/cards/${card}/checklists`);
      for (const reader of [page, peer]) {
        const show = reader.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
        await expect(reader.getByRole('heading', { name: 'Follow-up preparation', exact: true })).toBeVisible();
        await expect(reader.getByText('0 of 0 items complete (0%)', { exact: true })).toBeVisible();
        expect((await new AxeBuilder({ page: reader }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      }
    } finally { await peerContext.close(); }
  });
}
