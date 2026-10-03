import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';

for (const width of [1280, 390]) {
  test(`PRD-13: confirmed checklist deletion, exact retry and two-session live removal at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `checklist-delete-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist administrator' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist deletion Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist deletion Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist deletion List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Deletion work' } })).json()).id;
    const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Old preparation', cardVersion: 1 } });
    expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
    const added = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Retained history', cardVersion: 2, checklistVersion: 1 } });
    expect(added.status()).toBe(200);
    const route = `/app/${org}/boards/${board}/cards/${card}`; await page.goto(route);
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      const peer = await peerContext.newPage(); await peer.goto(route);
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(peer.getByRole('heading', { name: 'Old preparation', exact: true })).toBeVisible();
      await peer.getByRole('button', { name: 'Show items in Old preparation', exact: true }).press('Enter');
      await expect(peer.getByText('Incomplete: Retained history', { exact: true })).toBeVisible();
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
      await page.getByRole('button', { name: 'Delete Old preparation', exact: true }).press('Enter');
      const confirmed = page.getByRole('button', { name: 'Delete confirmed checklist', exact: true }); await expect(confirmed).toBeDisabled();
      await expect(page.getByRole('alert')).toContainText('1 active item');
      await page.getByRole('checkbox', { name: 'Confirm checklist deletion', exact: true }).press('Space'); await expect(confirmed).toBeEnabled();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const attempts: { key: string | undefined; body: string | null }[] = []; let drop = true;
      await page.route(`**/cards/${card}/checklists/${checklist.id}`, async intercepted => {
        if (intercepted.request().method() !== 'DELETE') { await intercepted.continue(); return; }
        attempts.push({ key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postData() });
        const response = await intercepted.fetch(); expect(response.status()).toBe(200); expect((await response.json()).deletedItems).toBe(1);
        if (drop) { drop = false; await intercepted.abort('failed'); } else await intercepted.fulfill({ response });
      });
      await confirmed.press('Enter'); const retry = page.getByRole('button', { name: 'Retry checklist deletion', exact: true }); await expect(retry).toBeEnabled();
      await expect(retry).toBeFocused(); await expect(page.getByRole('checkbox', { name: 'Confirm checklist deletion', exact: true })).toBeDisabled();
      await expect(page.getByRole('button', { name: 'Add checklist', exact: true })).toBeDisabled();
      await retry.press('Enter'); await expect(page.getByText('Checklist deleted.', { exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); expect(JSON.parse(attempts[0].body!)).toEqual({ confirmed: true, cardVersion: 3, version: 2 });
      await expect(peer.getByText('No checklists on this page.', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(peer.getByText('Incomplete: Retained history', { exact: true })).toHaveCount(0);
      const hidden = await context.request.get(`/cards/${card}/checklists/${checklist.id}/items`); expect(hidden.status()).toBe(404);
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { await peerContext.close(); }
  });
}
