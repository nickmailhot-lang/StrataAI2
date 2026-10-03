import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-13-TC-05/08/09: concurrent item drafts, socket recovery and revoked scope at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const recipient = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    const email = `checklist-peer-${width}-${Date.now()}@example.test`;
    let restoreWorker = () => {}; let releaseConflict = () => {}; let unavailable = false; let socket: WebSocketRoute | undefined;
    await recipient.routeWebSocket('**/boards/live*', route => {
      if (unavailable) { route.close({ code: 1013 }); return; }
      socket = route; route.connectToServer();
    });
    try {
      for (const [index, client] of [context, recipient].entries()) {
        const data = { email: index ? email : `checklist-owner-${width}-${Date.now()}@example.test`, password: 'checklist-browser-fixture-battery', displayName: index ? 'Checklist contributor' : 'Checklist owner' };
        expect((await client.request.post('/auth/register', { headers, data })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data })).status()).toBe(200);
      }
      const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Checklist collaboration Organization' } })).json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201); expect((await recipient.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const actor = (await (await recipient.request.get('/me')).json()).id;
      const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Checklist collaboration Board', visibility: 'PRIVATE' } })).json()).id;
      const grant = await context.request.patch(`/boards/${board}/members/${actor}`, { headers, data: { role: 'MEMBER' } }); expect(grant.status()).toBe(200); const membershipVersion = (await grant.json()).version;
      const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Checklist collaboration List' } })).json()).id;
      const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Concurrent checklist work' } })).json()).id;
      const created = await context.request.post(`/cards/${card}/checklists`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Preparations', cardVersion: 1 } }); expect(created.status()).toBe(200); const checklist = (await created.json()).checklist;
      const added = await context.request.post(`/cards/${card}/checklists/${checklist.id}/items`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Initial preparation', cardVersion: 2, checklistVersion: 1 } }); expect(added.status()).toBe(200); const item = (await added.json()).item;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const route = `/app/${org}/boards/${board}/cards/${card}`; const peer = await recipient.newPage(); await page.goto(route); await peer.goto(route);
      for (const target of [page, peer]) await expect(target.getByText('Live updates connected.', { exact: true })).toBeVisible();
      async function edit(target: Page, text = 'Initial preparation') {
        const manage = target.getByRole('button', { name: 'Manage checklists', exact: true }); await expect(manage).toBeEnabled(); await manage.press('Enter');
        await target.getByRole('button', { name: 'Manage items in Preparations', exact: true }).press('Enter');
        const review = target.getByRole('button', { name: 'Review checklist items', exact: true }); await expect(review).toBeEnabled(); await review.press('Enter');
        await target.getByRole('button', { name: `Edit item: ${text}`, exact: true }).press('Enter');
      }
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible();
      await edit(peer); const dirty = peer.getByRole('textbox', { name: 'Checklist item text' }); await dirty.fill('Contributor dirty draft'); await peer.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      await edit(page); await page.getByRole('textbox', { name: 'Checklist item text' }).fill('Owner canonical change');
      const path = `/cards/${card}/checklists/${checklist.id}/items`;
      let held = false; const competing = new Promise<void>(resolve => { releaseConflict = resolve; });
      await peer.route(`**${path}/${item.id}`, async intercepted => {
        if (intercepted.request().method() !== 'PATCH') { await intercepted.continue(); return; }
        expect(intercepted.request().postDataJSON()).toEqual({ text: 'Contributor dirty draft', completed: true, cardVersion: 3, checklistVersion: 2, version: 1 });
        held = true; await competing; const response = await intercepted.fetch(); expect(response.status()).toBe(409); await intercepted.fulfill({ response });
      });
      await peer.getByRole('button', { name: 'Save checklist item', exact: true }).press('Enter'); await expect.poll(() => held).toBe(true);
      await page.getByRole('button', { name: 'Save checklist item', exact: true }).press('Enter');
      await expect(page.getByText('Checklist item saved.', { exact: true })).toBeVisible();
      releaseConflict(); await expect(peer.getByText('This item change is unavailable. Your text and completion choice are preserved. Load the current Card and checklist before reviewing another change.', { exact: true })).toBeVisible();
      await peer.unroute(`**${path}/${item.id}`);
      await expect(peer.getByText('This Card changed elsewhere. Your item text and completion choice are preserved.', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(dirty).toHaveValue('Contributor dirty draft'); await expect(peer.getByRole('checkbox', { name: 'Item complete', exact: true })).toBeChecked(); await expect(peer.getByRole('button', { name: 'Save checklist item', exact: true })).toBeDisabled();
      const canonical = await context.request.get(path); expect(canonical.status()).toBe(200); const state = await canonical.json(); expect(state.cardVersion).toBe(4); expect(state.items[0].text).toBe('Owner canonical change'); expect(state.items[0].completed).toBe(false);
      await peer.getByRole('button', { name: 'Discard item review and load latest', exact: true }).press('Enter');
      await edit(peer, 'Owner canonical change'); await expect(dirty).toHaveValue('Owner canonical change');
      await dirty.fill('Draft during socket outage'); await peer.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      expect(socket).toBeDefined(); unavailable = true; await socket!.close({ code: 1012 });
      const missed = await context.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Recovered during socket outage', completed: true, cardVersion: 4, checklistVersion: 3, version: 2 } }); expect(missed.status()).toBe(200);
      await expect(peer.getByText('1 of 1 items complete (100%)', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(dirty).toHaveValue('Draft during socket outage'); const showItems = peer.getByRole('button', { name: 'Show items in Preparations', exact: true }); await expect(showItems).toBeEnabled(); await showItems.press('Enter');
      await expect(peer.getByText('Complete: Recovered during socket outage', { exact: true })).toBeVisible();
      unavailable = false; await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 45_000 });
      const pushed = await context.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'After socket reconnect', completed: false, cardVersion: 5, checklistVersion: 4, version: 3 } }); expect(pushed.status()).toBe(200);
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible({ timeout: 20_000 }); await expect(dirty).toHaveValue('Draft during socket outage');
      await expect(peer.getByRole('button', { name: 'Save checklist item', exact: true })).toBeDisabled();
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const removal = await context.request.delete(`/boards/${board}/members/${actor}`, { headers: { ...headers, 'If-Match': `"${membershipVersion}"`, 'Idempotency-Key': crypto.randomUUID() } }); expect(removal.status()).toBe(204);
      await expect(peer.getByRole('heading', { name: 'Checklist collaboration Board', exact: true })).toHaveCount(0, { timeout: 20_000 }); await expect(dirty).toHaveCount(0);
      await expect(peer.getByText('Draft during socket outage', { exact: true })).toHaveCount(0); expect((await recipient.request.get(path)).status()).toBe(404);
      const denied = await recipient.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Unauthorized draft', completed: true, cardVersion: 6, checklistVersion: 5, version: 4 } }); expect(denied.status()).toBe(404);
      const unchanged = await context.request.get(path); expect(unchanged.status()).toBe(200); expect((await unchanged.json()).items[0].text).toBe('After socket reconnect');
    } finally { releaseConflict(); await recipient.close(); restoreWorker(); }
  });
}
