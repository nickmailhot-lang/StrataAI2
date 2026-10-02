import { expect, test, type WebSocketRoute } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

test('PRD-10/16: desktop label changes refresh phone filters through Worker delivery and socket recovery', async ({ page, context, browser }) => {
  test.setTimeout(180_000); await page.setViewportSize({ width: 1280, height: 844 });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `label-filter-live-${Date.now()}@example.test`, password: 'label-filter-live-correct-horse', displayName: 'Label collaboration fixture' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
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
  const phone = await browser.newContext({ viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
  const other = await phone.newPage(); let unavailable = false; let socket: WebSocketRoute | undefined;
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
    await other.keyboard.press('Enter');
    const filters = other.getByRole('dialog', { name: 'Filter Board Cards' });
    const priority = filters.getByRole('checkbox', { name: 'Priority (red)', exact: true });
    await expect(priority).toBeEnabled(); await priority.focus(); await other.keyboard.press('Space');
    await filters.getByRole('button', { name: 'Apply filters', exact: true }).focus(); await other.keyboard.press('Enter');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible();
    await page.goto(`${boardPath}/cards/${card}`);
    await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
    const assignees = page.getByRole('button', { name: 'Show assignees', exact: true });
    await expect(assignees).toBeEnabled(); await assignees.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('region', { name: 'Card assignees' }).getByText('Label collaboration fixture', { exact: true })).toBeVisible();
    const edit = page.getByRole('button', { name: 'Edit Card labels', exact: true });
    async function assignment(action: string) {
      await expect(edit).toBeEnabled(); await edit.focus(); await page.keyboard.press('Enter');
      const change = page.getByRole('button', { name: action, exact: true });
      await expect(change).toBeEnabled(); await change.focus(); await page.keyboard.press('Enter'); await expect(edit).toBeFocused();
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
    await expect(filters.getByLabel('Card keyword')).toHaveValue('');
    const show = filters.getByRole('button', { name: 'Show this page on Board', exact: true });
    await expect(show).toBeEnabled(); await show.focus(); await other.keyboard.press('Enter');
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
    await expect(clear).toBeEnabled(); await clear.focus(); await other.keyboard.press('Enter');
    await expect(other.getByText('Unmatched canvas Card', { exact: true })).toBeVisible();
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
      await expect(editMembers).toBeEnabled(); await editMembers.focus(); await page.keyboard.press('Enter');
      const change = page.getByRole('button', { name: `${action} Label collaboration fixture`, exact: true });
      await expect(change).toBeEnabled(); await change.focus(); await page.keyboard.press('Enter'); await expect(editMembers).toBeFocused();
    }
    await memberChange('Unassign');
    await expect((await phoneAssignees()).getByText('No assignees on this page.', { exact: true })).toBeVisible();
    await memberChange('Assign');
    await expect((await phoneAssignees()).getByText('Label collaboration fixture', { exact: true })).toBeVisible();
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await other.goto(boardPath);
    await expect(other.getByRole('img', { name: 'Assigned to Label collaboration fixture', exact: true })).toBeVisible();
    await expect(openFilters).toBeEnabled(); await openFilters.focus(); await other.keyboard.press('Enter');
    const chooseMembers = filters.getByRole('button', { name: 'Choose assignees', exact: true });
    await expect(chooseMembers).toBeEnabled(); await chooseMembers.focus(); await other.keyboard.press('Enter');
    const selectedMember = filters.getByRole('checkbox', { name: 'Label collaboration fixture', exact: true });
    await expect(selectedMember).toBeEnabled(); await selectedMember.focus(); await other.keyboard.press('Space');
    await filters.getByRole('button', { name: 'Apply filters', exact: true }).focus(); await other.keyboard.press('Enter');
    await expect(matching).toBeVisible();
    await memberChange('Unassign');
    await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(selectedMember).toBeChecked();
    await memberChange('Assign'); await expect(matching).toBeVisible({ timeout: 20_000 });
    expect((await context.request.post(`/boards/${board}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    await expect(filters).toHaveCount(0, { timeout: 20_000 });
    await expect(other.getByText('This board is archived. Editing is unavailable.', { exact: true })).toBeVisible();
    await expect(other.getByRole('button', { name: 'Filter Board Cards', exact: true })).toHaveCount(0);
    expect((await context.request.get(`/boards/${board}/cards?labels=${label}`)).status()).toBe(404);
    expect(await other.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  } finally { await phone.close(); restoreWorker?.(); }
});
