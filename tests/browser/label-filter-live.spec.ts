import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { pressAdmittedAction } from './keyboardAdmission';
import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';

test('PRD-10/16: desktop label changes refresh phone filters through Worker delivery and socket recovery', async ({ page, context, browser }) => {
  test.setTimeout(180_000); await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `label-filter-live-${Date.now()}@example.test`, password: 'label-filter-live-correct-horse', displayName: 'Label collaboration fixture' };
  await registerVerifiedAccountFixture(context.request, account);
  const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Label collaboration' } });
  expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
  const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Live labels', visibility: 'PRIVATE' } });
  expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
  const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
  expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
  const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Collaborative labeled Card' } });
  expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
  const actor = (await (await context.request.get('/me')).json()).id;
  expect((await context.request.put(`/cards/${card}/members/${actor}?version=1`, { headers, data: {} })).status()).toBe(200);
  expect((await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Unmatched canvas Card' } })).status()).toBe(201);
  const labelReply = await context.request.post(`/boards/${board}/labels`, { headers, data: { name: 'Priority', color: 'red' } });
  expect(labelReply.status()).toBe(201); const label = (await labelReply.json()).id;
  const phone = await browser.newContext({ baseURL: new URL(cardReply.url()).origin, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
  const other = await phone.newPage(); let unavailable = false; let socket: WebSocketRoute | undefined;
  let filterChanges = 0;
  other.on('request', request => { if (request.method() === 'POST'
    && new URL(request.url()).pathname === `/boards/${board}/cards/filter-change`) filterChanges++; });
  await phone.routeWebSocket('**/boards/live*', route => {
    if (unavailable) { route.close({ code: 1013 }); return; }
    socket = route; route.connectToServer();
  });
  let restoreWorker: (() => void) | undefined;
  try {
    restoreWorker = scopedBoardWorker(org);
    await waitForBoardDelivery(context.request, board);
    const boardPath = `/app/${org}/boards/${board}`;
    await other.goto(boardPath); await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible();
    const openFilters = other.getByRole('button', { name: 'Filter Board Cards', exact: true });
    await expect(openFilters).toBeEnabled(); await openFilters.focus(); await expect(openFilters).toBeFocused();
    await openFilters.press('Enter');
    const filters = other.getByRole('dialog', { name: 'Filter Board Cards' });
    const priority = filters.getByRole('checkbox', { name: 'Priority (red)', exact: true });
    await expect(priority).toBeEnabled(); await priority.press('Space');
    await filters.getByRole('button', { name: 'Apply filters', exact: true }).press('Enter');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible();
    expect(filterChanges).toBe(1);
    await page.goto(`${boardPath}/cards/${card}`);
    await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
    const assignees = page.getByRole('button', { name: 'Show assignees', exact: true });
    await expect(assignees).toBeEnabled(); await assignees.press('Enter');
    await expect(page.getByRole('region', { name: 'Card assignees' }).getByText('Label collaboration fixture', { exact: true })).toBeVisible();
    const edit = page.getByRole('button', { name: 'Edit Card labels', exact: true });
    async function assignment(action: string) {
      // A live parent read can disable the opener between enabled admission
      // and keydown. Observe the read-only picker opening before its command.
      await expect(async () => {
        if (await edit.getAttribute('aria-expanded') !== 'true') {
          await expect(edit).toBeEnabled({ timeout: 500 });
          await edit.press('Enter', { timeout: 500 });
        }
        await expect(edit).toHaveAttribute('aria-expanded', 'true', { timeout: 500 });
      }).toPass({ timeout: 5_000 });
      const change = page.getByRole('button', { name: action, exact: true });
      await pressAdmittedAction(change); await expect(edit).toBeFocused();
    }
    await assignment('Add label Priority');
    const matching = filters.getByRole('link', { name: 'Collaborative labeled Card — Planning', exact: true });
    await expect(matching).toBeVisible({ timeout: 20_000 });
    expect((await context.request.patch(`/labels/${label}`, { headers, data: { name: 'Urgent', color: 'purple', version: 1 } })).status()).toBe(200);
    const urgent = filters.getByRole('checkbox', { name: 'Urgent (purple)', exact: true });
    await expect(urgent).toBeChecked({ timeout: 20_000 }); await expect(matching).toBeVisible();
    // Close the real proxied socket and reject reconnects. HTTP replay must
    // recover the missed mutation without navigation or a manual Board refresh.
    expect(socket).toBeDefined(); unavailable = true; await socket!.close({ code: 1012 });
    await assignment('Remove label Urgent');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(urgent).toBeChecked(); await expect(matching).toHaveCount(0);
    unavailable = false;
    await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 45_000 });
    await assignment('Add label Urgent'); await expect(matching).toBeVisible({ timeout: 20_000 });
    expect(filterChanges).toBe(1);
    const completion = filters.getByRole('combobox', { name: 'Due completion', exact: true });
    await completion.press('Enter'); await other.getByRole('option', { name: 'Due complete', exact: true }).press('Enter');
    await filters.getByRole('button', { name: 'Apply filters', exact: true }).press('Enter');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible();
    const snapshot = await (await context.request.get(`/boards/${board}`)).json();
    const current = snapshot.lists.flatMap((column: { cards: { id: string; version: number }[] }) => column.cards).find((row: { id: string }) => row.id === card);
    expect(current).toBeDefined();
    const dates = { dueAt: '2026-11-02T15:00:00Z', dueTimezone: 'UTC', dueHasTime: true, dueComplete: true, version: current.version };
    const completed = await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: dates });
    expect(completed.status()).toBe(200); const completedVersion = (await completed.json()).card.version;
    await expect(matching).toBeVisible({ timeout: 20_000 });
    const reopened = await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { ...dates, dueComplete: false, version: completedVersion } });
    expect(reopened.status()).toBe(200); const reopenedVersion = (await reopened.json()).card.version;
    await expect(matching).toHaveCount(0, { timeout: 20_000 });
    const recompleted = await context.request.patch(`/cards/${card}/dates`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { ...dates, version: reopenedVersion } });
    expect(recompleted.status()).toBe(200); await expect(matching).toBeVisible({ timeout: 20_000 });
    expect(filterChanges).toBe(2);
    await expect(filters.getByLabel('Card keyword')).toHaveValue('');
    const show = filters.getByRole('button', { name: 'Show this page on Board', exact: true });
    await expect(show).toBeEnabled(); await show.press('Enter');
    await expect(other.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
    await expect(other.getByText('Unmatched canvas Card', { exact: true })).toHaveCount(0);
    await expect(other.getByRole('button', { name: 'Drag Collaborative labeled Card card', exact: true })).toHaveCount(0);
    await other.reload();
    await expect(other.getByText('Filtered Board: 1 matching Cards on this page.', { exact: true })).toBeVisible();
    await expect(other.getByText('Unmatched canvas Card', { exact: true })).toHaveCount(0);
    await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible();
    await assignment('Remove label Urgent');
    await expect(other.getByText('Filtered Board: 0 matching Cards on this page.', { exact: true })).toBeVisible({ timeout: 20_000 });
    const clear = other.getByRole('button', { name: 'Clear Board filters', exact: true });
    await expect(clear).toBeEnabled(); await clear.press('Enter');
    await expect(other.getByText('Unmatched canvas Card', { exact: true })).toBeVisible();
    expect(filterChanges).toBe(3);
    await expect(other.getByRole('button', { name: 'Drag Collaborative labeled Card card', exact: true })).toBeEnabled();
    await expect(other.getByRole('img', { name: 'Assigned to Label collaboration fixture', exact: true })).toBeVisible();
    await other.goto(`${boardPath}/cards/${card}`);
    await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible();
    async function phoneAssignees() {
      const show = other.getByRole('button', { name: 'Show assignees', exact: true });
      if (await show.count()) { await expect(show).toBeEnabled({ timeout: 20_000 }); await show.press('Enter'); }
      else await expect(other.getByRole('button', { name: 'Hide assignees', exact: true })).toBeEnabled({ timeout: 20_000 });
      await expect(other.getByRole('region', { name: 'Card assignees' })).toBeVisible();
      return other.getByRole('region', { name: 'Card assignees' });
    }
    await expect((await phoneAssignees()).getByText('Label collaboration fixture', { exact: true })).toBeVisible();
    const editMembers = page.getByRole('button', { name: 'Edit Card assignees', exact: true });
    async function memberChange(action: string) {
      await expect(async () => {
        if (await editMembers.getAttribute('aria-expanded') !== 'true') {
          await expect(editMembers).toBeEnabled({ timeout: 500 }); await editMembers.press('Enter', { timeout: 500 });
        }
        await expect(editMembers).toHaveAttribute('aria-expanded', 'true', { timeout: 500 });
      }).toPass({ timeout: 5_000 });
      const change = page.getByRole('button', { name: `${action} Label collaboration fixture`, exact: true });
      await pressAdmittedAction(change); await expect(editMembers).toBeFocused();
    }
    await memberChange('Unassign');
    await expect((await phoneAssignees()).getByText('No assignees on this page.', { exact: true })).toBeVisible();
    await memberChange('Assign');
    await expect((await phoneAssignees()).getByText('Label collaboration fixture', { exact: true })).toBeVisible();
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await other.goto(boardPath);
    await expect(other.getByRole('img', { name: 'Assigned to Label collaboration fixture', exact: true })).toBeVisible();
    await expect(openFilters).toBeEnabled(); await openFilters.press('Enter');
    const chooseMembers = filters.getByRole('button', { name: 'Choose assignees', exact: true });
    await pressAdmittedAction(chooseMembers);
    const selectedMember = filters.getByRole('checkbox', { name: 'Label collaboration fixture', exact: true });
    await pressAdmittedAction(selectedMember, 'Space'); await expect(selectedMember).toBeChecked();
    await pressAdmittedAction(filters.getByRole('button', { name: 'Apply filters', exact: true }));
    await expect(matching).toBeVisible();
    await memberChange('Unassign');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(selectedMember).toBeChecked();
    await memberChange('Assign'); await expect(matching).toBeVisible({ timeout: 20_000 });
    expect(filterChanges).toBe(4);
    expect((await context.request.post(`/boards/${board}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    await expect(filters).toHaveCount(0, { timeout: 20_000 });
    await expect(other.getByText('This board is archived. Editing is unavailable.', { exact: true })).toBeVisible();
    await expect(other.getByRole('button', { name: 'Filter Board Cards', exact: true })).toHaveCount(0);
    expect(filterChanges).toBe(4);
    expect((await context.request.get(`/boards/${board}/cards?labels=${label}`)).status()).toBe(404);
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  } finally { await phone.close(); restoreWorker?.(); }
});
