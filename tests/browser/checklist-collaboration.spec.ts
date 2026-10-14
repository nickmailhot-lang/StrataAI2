import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackCardVersion } from './boardReadTracker';

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
      const route = `/app/${org}/boards/${board}/cards/${card}`; const peer = await recipient.newPage();
      const admittedPeerVersion = trackCardVersion(peer, board, card, route);
      await page.goto(route); await peer.goto(route);
      for (const target of [page, peer]) await expect(target.getByText('Live updates connected.', { exact: true })).toBeVisible();
      async function edit(target: Page, text = 'Initial preparation') {
        const manage = target.getByRole('button', { name: 'Manage checklists', exact: true });
        const items = target.getByRole('button', { name: 'Manage items in Preparations', exact: true });
        const review = target.getByRole('button', { name: 'Review checklist items', exact: true });
        const item = target.getByRole('button', { name: `Edit item: ${text}`, exact: true });
        const editor = target.getByRole('textbox', { name: 'Checklist item text' });
        // A parent admission can retire any stage of this read-only opening.
        // Recover a rejected read only before an editable draft is admitted;
        // the later text/completion changes and save commands stay outside.
        await expect(async () => {
          if (await editor.isVisible()) {
            await expect(editor).toHaveValue(text, { timeout: 500 }); return;
          }
          if (await target.getByText('Unable to review current checklist items. Load the current Card and checklist before trying again.', { exact: true }).isVisible()) {
            const discard = target.getByRole('button', { name: 'Discard item review and load latest', exact: true });
            await expect(discard).toBeEnabled({ timeout: 500 }); await discard.focus({ timeout: 500 });
            await expect(discard).toBeFocused({ timeout: 500 }); await discard.press('Enter', { timeout: 500 });
          }
          if (!await items.isVisible()) {
            if (!await review.isVisible() && !await item.isVisible()) {
              await expect(manage).toBeEnabled({ timeout: 500 });
              await manage.focus({ timeout: 500 }); await expect(manage).toBeFocused({ timeout: 500 });
              await expect(manage).toBeEnabled({ timeout: 500 }); await manage.press('Enter', { timeout: 500 });
            }
          } else {
            await expect(items).toBeEnabled({ timeout: 500 }); await items.focus({ timeout: 500 });
            await expect(items).toBeFocused({ timeout: 500 }); await items.press('Enter', { timeout: 500 });
          }
          if (await review.isVisible()) {
            await expect(review).toBeEnabled({ timeout: 500 }); await review.focus({ timeout: 500 });
            await expect(review).toBeFocused({ timeout: 500 }); await review.press('Enter', { timeout: 500 });
          }
          await expect(item).toBeEnabled({ timeout: 500 }); await item.focus({ timeout: 500 });
          await expect(item).toBeFocused({ timeout: 500 }); await item.press('Enter', { timeout: 500 });
          await expect(editor).toHaveValue(text, { timeout: 500 });
        }).toPass({ timeout: 5_000 });
      }
      const show = peer.getByRole('button', { name: 'Show checklists', exact: true }); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible();
      await edit(peer); const dirty = peer.getByRole('textbox', { name: 'Checklist item text' }); await dirty.fill('Contributor dirty draft'); await peer.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      async function discardPeerReview() {
        const discard = peer.getByRole('button', { name: 'Discard item review and load latest', exact: true });
        const section = peer.getByRole('region', { name: 'Manage checklist items', exact: true, includeHidden: true });
        // Only the scenario's explicit discard may close the existing draft.
        // A previously pushed Card version does not prove Enter closed it.
        await expect(async () => {
          // A protected read can remove the editor while retaining this review.
          // Observe retirement of the entire item-management component instead.
          if (await section.count()) {
            await expect(discard).toBeEnabled({ timeout: 500 });
            await discard.focus({ timeout: 500 });
            await expect(discard).toBeFocused({ timeout: 500 });
            await expect(discard).toBeEnabled({ timeout: 500 });
            await discard.press('Enter', { timeout: 500 });
          }
          await expect(section).toHaveCount(0, { timeout: 500 });
          await expect(dirty).toHaveCount(0, { timeout: 500 });
        }).toPass({ timeout: 5_000 });
      }
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
      await discardPeerReview();
      // The owner committed revision 4. Reopening is an independent review and
      // must wait for its actual protected parent admission after discard.
      await expect.poll(admittedPeerVersion).toBe(4);
      await expect(peer.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      await edit(peer, 'Owner canonical change'); await expect(dirty).toHaveValue('Owner canonical change');
      await dirty.fill('Draft during socket outage'); await peer.getByRole('checkbox', { name: 'Item complete', exact: true }).press('Space');
      expect(socket).toBeDefined(); unavailable = true; await socket!.close({ code: 1012 });
      const missed = await context.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Recovered during socket outage', completed: true, cardVersion: 4, checklistVersion: 3, version: 2 } }); expect(missed.status()).toBe(200);
      await expect(peer.getByText('1 of 1 items complete (100%)', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(dirty).toHaveValue('Draft during socket outage'); const showItems = peer.getByRole('button', { name: 'Show items in Preparations', exact: true }); await expect(showItems).toBeEnabled(); await showItems.press('Enter');
      await expect(peer.getByText('Complete: Recovered during socket outage', { exact: true })).toBeVisible();
      unavailable = false; await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 45_000 });
      const pushed = await context.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'After socket reconnect', completed: false, cardVersion: 5, checklistVersion: 4, version: 3 } }); expect(pushed.status()).toBe(200);
      // Complete this command's real durable delivery before beginning the
      // next independent review. Pending delivery still invalidates admission.
      await waitForBoardDelivery(context.request, board);
      await expect(peer.getByText('0 of 1 items complete (0%)', { exact: true })).toBeVisible({ timeout: 20_000 }); await expect(dirty).toHaveValue('Draft during socket outage');
      await expect(peer.getByRole('button', { name: 'Save checklist item', exact: true })).toBeDisabled();
      expect((await new AxeBuilder({ page: peer }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      // Exercise revocation with an actual committed command whose reply was
      // lost, as well as the dirty-draft conflict already checked above.
      await discardPeerReview();
      // The independent editor must use the pushed revision after discard refresh.
      await expect.poll(admittedPeerVersion).toBeGreaterThanOrEqual(6);
      await expect(peer.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      await edit(peer, 'After socket reconnect'); await dirty.fill('Committed before revocation');
      let original: { key: string; body: string } | undefined;
      await peer.route(`**${path}/${item.id}`, async intercepted => {
        if (intercepted.request().method() !== 'PATCH') { await intercepted.continue(); return; }
        const key = intercepted.request().headers()['idempotency-key']; const body = intercepted.request().postData();
        expect(key).toMatch(/^[0-9a-f-]{36}$/); expect(body).not.toBeNull(); original = { key: key!, body: body! };
        const response = await intercepted.fetch(); expect(response.status()).toBe(200); await intercepted.abort('failed');
      });
      const save = peer.getByRole('button', { name: 'Save checklist item', exact: true }); await expect(save).toBeEnabled(); await save.press('Enter');
      await expect(peer.getByRole('button', { name: 'Retry checklist item change', exact: true })).toBeEnabled();
      expect(original).toBeDefined(); expect(JSON.parse(original!.body)).toEqual({ text: 'Committed before revocation', completed: false, cardVersion: 6, checklistVersion: 5, version: 4 });
      const beforeRevocation = await context.request.get(path); expect(beforeRevocation.status()).toBe(200); const committed = await beforeRevocation.json();
      expect(committed.cardVersion).toBe(7); expect(committed.items[0].text).toBe('Committed before revocation'); expect(committed.items[0].version).toBe(5);
      const removal = await context.request.delete(`/boards/${board}/members/${actor}`, { headers: { ...headers, 'If-Match': `"${membershipVersion}"`, 'Idempotency-Key': crypto.randomUUID() } }); expect(removal.status()).toBe(204);
      await expect(peer.getByRole('heading', { name: 'Checklist collaboration Board', exact: true })).toHaveCount(0, { timeout: 20_000 }); await expect(dirty).toHaveCount(0);
      await expect(peer.getByText('Draft during socket outage', { exact: true })).toHaveCount(0); expect((await recipient.request.get(path)).status()).toBe(404);
      await expect(peer.getByRole('button', { name: 'Retry checklist item change', exact: true })).toHaveCount(0);
      await expect(peer.getByText('Committed before revocation', { exact: true })).toHaveCount(0);
      const receiptDenied = await recipient.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Content-Type': 'application/json', 'Idempotency-Key': original!.key }, data: original!.body });
      expect(receiptDenied.status()).toBe(404);
      const denied = await recipient.request.patch(`${path}/${item.id}`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { text: 'Unauthorized draft', completed: true, cardVersion: 6, checklistVersion: 5, version: 4 } }); expect(denied.status()).toBe(404);
      const unchanged = await context.request.get(path); expect(unchanged.status()).toBe(200); expect(await unchanged.json()).toEqual(committed);
    } finally { releaseConflict(); await recipient.close(); restoreWorker(); }
  });
}
