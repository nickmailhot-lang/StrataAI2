import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) for (const listParent of [false, true]) {
  test(`PRD-13-TC-06/07/10: pending rename across ${listParent ? 'List' : 'Card'} archive/restore/delete at ${width}px`, async ({ page, context }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' }; let restoreWorker = () => {};
    const account = { email: `checklist-lifecycle-${listParent}-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: 'Checklist administrator' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist lifecycle Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist lifecycle Board', visibility: 'PRIVATE' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist lifecycle List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Retained checklist work' } })).json()).id;
    const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 } }); expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
    const added = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Retained preparation', cardVersion: 2, checklistVersion: 1 } }); expect(added.status()).toBe(200); const item = (await added.json()).item;
    try {
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      await page.goto(`/app/${org}/boards/${board}/cards/${card}`);
      await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
      const show = page.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(page.getByRole('heading', { name: 'Preparations', exact: true })).toBeVisible();
      const manage = page.getByRole('button', { name: 'Manage checklists', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
      await page.getByRole('button', { name: 'Rename Preparations', exact: true }).press('Enter'); await page.getByRole('textbox', { name: 'Checklist title' }).fill('Recovered title');
      const commandPath = `/cards/${card}/checklists/${checklist.id}`;
      let intent: { key: string; body: { title: string; cardVersion: number; version: number } } | undefined;
      await page.route(`**${commandPath}`, async intercepted => {
        if (intercepted.request().method() !== 'PATCH') { await intercepted.continue(); return; }
        expect(intent).toBeUndefined(); intent = { key: intercepted.request().headers()['idempotency-key'], body: intercepted.request().postDataJSON() };
        const response = await intercepted.fetch(); expect(response.status()).toBe(200); await intercepted.abort('failed');
      });
      await page.getByRole('button', { name: 'Save checklist title', exact: true }).press('Enter');
      await expect(page.getByRole('button', { name: 'Retry checklist rename', exact: true })).toBeEnabled();
      expect(intent).toBeDefined(); expect(intent!.body).toEqual({ title: 'Recovered title', cardVersion: 3, version: 2 });
      const parentPath = listParent ? `/lists/${list}` : `/cards/${card}`;
      const archive = await context.request.post(`${parentPath}/archive`, { headers, data: { version: listParent ? 1 : 4 } }); expect(archive.status()).toBe(200);
      await expect(page.getByText('This card is unavailable in this board.', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('textbox', { name: 'Checklist title' })).toHaveCount(0); await expect(page.getByRole('button', { name: 'Retry checklist rename', exact: true })).toHaveCount(0);
      const itemPath = `${commandPath}/items`; const readOnly = await context.request.get(itemPath); expect(readOnly.status()).toBe(200); const retained = await readOnly.json();
      expect(retained.canEdit).toBe(false); expect(retained.summary.checklist.title).toBe('Recovered title'); expect(retained.summary.checklist.version).toBe(3); expect(retained.items).toEqual([item]);
      const replayHeaders = { ...headers, 'Idempotency-Key': intent!.key };
      expect((await context.request.patch(commandPath, { headers: replayHeaders, data: intent!.body })).status()).toBe(404);
      const restored = await context.request.post(`${parentPath}/restore`, { headers, data: { version: listParent ? 2 : 5 } }); expect(restored.status()).toBe(200);
      await expect(manage).toBeEnabled({ timeout: 20_000 }); await expect(page.getByRole('button', { name: 'Retry checklist rename', exact: true })).toHaveCount(0);
      const receipt = await context.request.patch(commandPath, { headers: replayHeaders, data: intent!.body }); expect(receipt.status()).toBe(200);
      expect((await receipt.json()).cardVersion).toBe(4);
      await expect(show).toBeEnabled(); await show.press('Enter'); await expect(page.getByRole('heading', { name: 'Recovered title', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Show items in Recovered title', exact: true }).press('Enter'); await expect(page.getByText('Incomplete: Retained preparation', { exact: true })).toBeVisible();
      const current = await context.request.get(itemPath); expect(current.status()).toBe(200); expect((await current.json()).items).toEqual([item]);
      const archivedAgain = await context.request.post(`${parentPath}/archive`, { headers, data: { version: listParent ? 3 : 6 } }); expect(archivedAgain.status()).toBe(200);
      await expect(page.getByText('This card is unavailable in this board.', { exact: true })).toBeVisible({ timeout: 20_000 });
      const deletion = await context.request.delete(`${parentPath}?version=${listParent ? 4 : 7}&confirmed=true${listParent ? '&containedCardCount=1' : ''}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } }); expect(deletion.status()).toBe(200);
      expect((await context.request.get(itemPath)).status()).toBe(404); expect((await context.request.patch(commandPath, { headers: replayHeaders, data: intent!.body })).status()).toBe(404);
      await expect(page.getByText('Incomplete: Retained preparation', { exact: true })).toHaveCount(0); await expect(page.getByRole('heading', { name: 'Recovered title', exact: true })).toHaveCount(0);
    } finally { restoreWorker(); }
  });
}
