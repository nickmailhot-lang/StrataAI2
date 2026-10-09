import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { focusAdmittedControl, pressAdmittedAction } from './keyboardAdmission';
import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';

for (const width of [1280, 390]) {
  test(`PRD-10: Card labels have keyboard-readable names and reflect persisted deletion at ${width}px`, async ({ page, context }) => {
    test.setTimeout(90_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `card-labels-${width}-${Date.now()}@example.test`, password: 'card-label-correct-horse-battery', displayName: 'Label reader' };
    await registerVerifiedAccountFixture(context.request, account);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Label Organization' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Label Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Labeled work' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board);
      const labels: string[] = [];
      const navigate = async (path: string) => {
        const reads = trackBoardReads(page, board, path);
        await page.goto(path); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      };
      await navigate(`/app/${org}/boards/${board}`);
      const attempts: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board}/labels`, async route => {
        if (route.request().method() !== 'POST') return route.continue();
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const reply = await route.fetch(); expect(reply.status()).toBe(201);
        if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      const createLabel = page.getByRole('button', { name: 'Create label', exact: true });
      await expect(createLabel).toBeEnabled(); await createLabel.press('Enter');
      await page.getByLabel('Label name (optional)').fill('Priority');
      await page.getByRole('combobox', { name: 'Label color' }).press('Enter');
      await page.getByRole('option', { name: 'Red', exact: true }).press('Enter');
      await expect(page.getByRole('listbox', { name: 'Label color', exact: true })).toHaveCount(0);
      const submitCreation = page.getByRole('button', { name: 'Create', exact: true });
      await pressAdmittedAction(submitCreation);
      const retry = page.getByRole('button', { name: 'Retry label creation' }); await expect(retry).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeDisabled();
      await pressAdmittedAction(retry);
      await expect(page.getByRole('dialog')).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Refresh board', exact: true })).toBeFocused();
      expect(attempts).toHaveLength(2); expect(attempts[0]).toEqual(attempts[1]); expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      for (const [name, color] of [['Priority', 'red'], ['', 'blue']]) {
        let id: string;
        if (name === 'Priority') {
          const directory = await context.request.get(`/boards/${board}/labels`); expect(directory.status()).toBe(200);
          const items = (await directory.json()).items; expect(items).toHaveLength(1); expect(items[0].name).toBe('Priority'); expect(items[0].color).toBe('red'); id = items[0].id;
        } else {
          const created = await context.request.post(`/boards/${board}/labels`, { headers, data: { name, color } });
          expect(created.status()).toBe(201); id = (await created.json()).id;
        }
        labels.push(id);
      }
      const path = `/app/${org}/boards/${board}/cards/${card}`;
      await waitForBoardDelivery(context.request, board);
      const admittedCardVersion = trackCardVersion(page, board, card, path);
      await navigate(path);
      const assignmentAttempts: { url: string; key: string | undefined }[] = [];
      await page.route(`**/cards/${card}/labels/${labels[0]}?*`, async route => {
        assignmentAttempts.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'] });
        const reply = await route.fetch(); expect(reply.status()).toBe(200);
        if (assignmentAttempts.length === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      const edit = page.getByRole('button', { name: 'Edit Card labels', exact: true });
      async function openPicker() {
        await expect(async () => {
          if (await edit.getAttribute('aria-expanded') !== 'true') {
            await expect(edit).toBeEnabled({ timeout: 500 });
            await edit.press('Enter', { timeout: 500 });
          }
          await expect(edit).toHaveAttribute('aria-expanded', 'true', { timeout: 500 });
        }).toPass({ timeout: 5_000 });
      }
      await openPicker();
      const addPriority = page.getByRole('button', { name: 'Add label Priority', exact: true });
      await pressAdmittedAction(addPriority);
      const retryAssignment = page.getByRole('button', { name: 'Retry label change' }); await expect(retryAssignment).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Close', exact: true })).toBeDisabled();
      await pressAdmittedAction(retryAssignment); await expect(edit).toBeFocused();
      expect(assignmentAttempts).toHaveLength(2); expect(assignmentAttempts[0]).toEqual(assignmentAttempts[1]);
      // Drain the recovered assignment before starting a different command.
      // A returned trigger alone does not prove the refreshed Card is admitted.
      await waitForBoardDelivery(context.request, board);
      await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(2);
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      await openPicker();
      const addBlue = page.getByRole('button', { name: 'Add label blue', exact: true });
      let blueWrites = 0;
      page.on('request', request => {
        if (request.method() === 'PUT' && new URL(request.url()).pathname === `/cards/${card}/labels/${labels[1]}`) blueWrites++;
      });
      // A protected refresh can withdraw the key's target after focus admission.
      // Retry only while no command was dispatched; never replay a sent write.
      await expect(async () => {
        if (blueWrites === 0) {
          await expect(addBlue).toBeEnabled({ timeout: 500 });
          await addBlue.focus({ timeout: 500 }); await expect(addBlue).toBeFocused({ timeout: 500 });
          await expect(addBlue).toBeEnabled({ timeout: 500 }); await addBlue.press('Enter', { timeout: 500 });
        }
        expect(blueWrites).toBe(1);
      }).toPass({ timeout: 5_000 });
      await expect(edit).toBeFocused(); expect(blueWrites).toBe(1);
      await waitForBoardDelivery(context.request, board);
      await navigate(`/app/${org}/boards/${board}`);
      const face = page.getByRole('link').filter({ hasText: 'Labeled work' });
      await expect(face.getByLabel('Priority, red', { exact: true })).toBeVisible();
      await expect(face.getByText('blue label', { exact: true })).toBeVisible();
      await navigate(path);
      const details = page.getByRole('dialog');
      const show = page.getByRole('button', { name: 'Show labels', exact: true });
      await expect(show).toBeEnabled();
      await show.press('Enter');
      await expect(details.getByLabel('Priority, red', { exact: true })).toBeVisible();
      await expect(details.getByLabel('Unnamed label, blue', { exact: true })).toBeVisible();
      await expect(details.getByText('blue label', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Hide labels', exact: true }).press('Enter');
      await expect(details.getByLabel('Priority, red', { exact: true })).toHaveCount(0);
      await openPicker();
      const removeBlue = page.getByRole('button', { name: 'Remove label blue', exact: true });
      await pressAdmittedAction(removeBlue); await expect(edit).toBeFocused();
      const remaining = await context.request.get(`/cards/${card}/labels`); expect(remaining.status()).toBe(200);
      const remainingItems = (await remaining.json()).items; expect(remainingItems).toHaveLength(1); expect(remainingItems[0].id).toBe(labels[0]);
      await navigate(`/app/${org}/boards/${board}`);
      const edits: { url: string; key: string | undefined; body: string | null }[] = [];
      const filterActor = (await (await context.request.get('/me')).json()).id;
      const filterChanges: { url: string; key: string | undefined; source: Record<string, unknown> }[] = [];
      await page.route(`**/boards/${board}/cards/filter-change?*`, async route => {
        const request = route.request(); expect(request.method()).toBe('POST'); expect(request.postData()).toBeNull();
        expect(request.headers()['x-strataai-expected-actor']).toBe(filterActor);
        const reply = await route.fetch(); expect(reply.status()).toBe(200); expect(reply.headers()['cache-control']).toContain('no-store');
        const source = await reply.json();
        expect(Object.keys(source).sort()).toEqual(['actorId','boardId','createdAt','entityId','entityType','eventId','eventType','metadata','organizationId','version']);
        expect(source).toMatchObject({ actorId: filterActor, organizationId: org, boardId: board, eventType: 'BOARD_FILTER_CHANGED', entityType: 'BoardFilter', version: 1, metadata: {} });
        expect(source.entityId).toBe(source.eventId); expect(source.eventId).toMatch(/^[0-9a-f-]{36}$/); expect(Number.isFinite(Date.parse(source.createdAt))).toBe(true);
        filterChanges.push({ url: request.url(), key: request.headers()['idempotency-key'], source });
        if (filterChanges.length === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      const filterButton = page.getByRole('button', { name: 'Filter Board Cards', exact: true });
      await expect(filterButton).toBeEnabled(); await filterButton.press('Enter');
      const filters = page.getByRole('dialog', { name: 'Filter Board Cards' });
      await expect(filters.getByRole('checkbox', { name: 'Priority (red)', exact: true })).toBeEnabled();
      await filters.getByRole('checkbox', { name: 'Priority (red)', exact: true }).press('Space');
      await filters.getByLabel('Card keyword').fill('absent');
      const apply = filters.getByRole('button', { name: 'Apply filters', exact: true });
      await expect(apply).toBeEnabled(); await apply.press('Enter');
      const originalRetry = filters.getByRole('button', { name: 'Retry original filter change', exact: true });
      await expect(originalRetry).toBeEnabled(); await expect(originalRetry).toBeFocused(); await originalRetry.press('Enter');
      await expect(filters.getByText('Filter change acknowledged.', { exact: true })).toBeVisible();
      expect(filterChanges).toHaveLength(2); expect(filterChanges[1]).toEqual(filterChanges[0]); await expect(apply).toBeFocused();
      await expect(filters.getByText('No Cards match these filters.', { exact: true })).toBeVisible();
      // The combobox and open listbox share the label, and MUI hides the
      // underlying combobox while the menu is open. Observe the visible menu.
      const matchMode = page.getByRole('combobox', { name: 'Match filters', exact: true });
      const matchMenu = page.getByRole('listbox', { name: 'Match filters', exact: true });
      // Reconciliation can disable a field between locating it and keydown.
      // Verify menu admission before selecting; applied commands remain single.
      await expect(async () => {
        if (!await matchMenu.isVisible()) {
          await expect(matchMode).toBeEnabled({ timeout: 500 });
          await matchMode.press('Enter', { timeout: 500 });
        }
        await expect(matchMenu).toBeVisible({ timeout: 500 });
      }).toPass({ timeout: 5_000 });
      await matchMenu.getByRole('option', { name: 'Match ANY', exact: true }).press('Enter');
      await expect(apply).toBeEnabled(); await apply.press('Enter');
      await expect(filters.getByRole('link', { name: 'Labeled work — Planning', exact: true })).toBeVisible();
      expect(filterChanges).toHaveLength(3); expect(filterChanges[2].key).not.toBe(filterChanges[0].key);
      expect(filterChanges[2].source.eventId).not.toBe(filterChanges[0].source.eventId);
      await page.reload(); await expect(filterButton).toBeEnabled(); await filterButton.press('Enter');
      await expect(filters.getByLabel('Card keyword')).toHaveValue('absent');
      await expect(filters.getByRole('checkbox', { name: 'Priority (red)', exact: true })).toBeChecked();
      await expect(filters.getByRole('combobox', { name: 'Match filters' })).toHaveText('Match ANY');
      expect(filterChanges).toHaveLength(3);
      const clearFilters = filters.getByRole('button', { name: 'Clear filters', exact: true });
      let clearDispatches = 0;
      page.on('request', request => {
        const url = new URL(request.url());
        if (request.method() === 'POST' && url.pathname === `/boards/${board}/cards/filter-change`
          && url.searchParams.get('change') === 'clear') clearDispatches++;
      });
      await expect(async () => {
        if (clearDispatches === 0) {
          await expect(clearFilters).toBeEnabled({ timeout: 500 });
          await clearFilters.focus({ timeout: 500 }); await expect(clearFilters).toBeFocused({ timeout: 500 });
          await expect(clearFilters).toBeEnabled({ timeout: 500 }); await clearFilters.press('Enter', { timeout: 500 });
        }
        await expect.poll(() => clearDispatches, { timeout: 500 }).toBe(1);
      }).toPass({ timeout: 5_000 });
      await expect(filters.getByLabel('Card keyword')).toHaveValue('');
      expect(clearDispatches).toBe(1);
      expect(filterChanges).toHaveLength(4); expect(new URL(filterChanges[3].url).searchParams.get('change')).toBe('clear');
      expect(filterChanges[3].key).not.toBe(filterChanges[2].key);
      await filters.getByRole('button', { name: 'Close filters', exact: true }).press('Enter'); await expect(filters).toHaveCount(0);
      await page.route(`**/labels/${labels[0]}`, async route => {
        if (route.request().method() !== 'PATCH') return route.continue();
        edits.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const reply = await route.fetch(); expect(reply.status()).toBe(200);
        if (edits.length === 1) await route.abort('failed'); else await route.fulfill({ response: reply });
      });
      const manageLabels = page.getByRole('button', { name: 'Manage labels', exact: true });
      await pressAdmittedAction(manageLabels);
      const management = page.getByRole('dialog', { name: 'Manage Board labels' });
      const editPriority = management.getByRole('button', { name: 'Edit Priority (red)', exact: true });
      await pressAdmittedAction(editPriority);
      await management.getByLabel('Label name (optional)').fill('Urgent');
      await management.getByRole('combobox', { name: 'Label color' }).press('Enter');
      await page.getByRole('option', { name: 'Purple', exact: true }).press('Enter');
      const saveLabel = management.getByRole('button', { name: 'Save label', exact: true });
      await pressAdmittedAction(saveLabel);
      await expect(management.getByRole('button', { name: 'Done', exact: true })).toBeDisabled();
      const retryLabel = management.getByRole('button', { name: 'Retry label change', exact: true });
      await pressAdmittedAction(retryLabel);
      await expect(management.getByText('Label change confirmed. Reload labels to continue.', { exact: true })).toBeVisible();
      expect(edits).toHaveLength(2); expect(edits[0]).toEqual(edits[1]);
      await expect(management.getByRole('button', { name: 'Reload labels', exact: true })).toBeEnabled();
      const editUrgent = management.getByRole('button', { name: 'Edit Urgent (purple)', exact: true });
      const reloadLabels = management.getByRole('button', { name: 'Reload labels', exact: true });
      await expect(async () => {
        if (!await editUrgent.isVisible()) {
          await expect(reloadLabels).toBeEnabled({ timeout: 500 });
          await reloadLabels.focus({ timeout: 500 }); await expect(reloadLabels).toBeFocused({ timeout: 500 });
          await expect(reloadLabels).toBeEnabled({ timeout: 500 }); await reloadLabels.press('Enter', { timeout: 500 });
        }
        await expect(editUrgent).toBeEnabled({ timeout: 500 });
      }).toPass({ timeout: 5_000 });
      await pressAdmittedAction(editUrgent);
      const moveBefore = management.getByRole('combobox', { name: 'Move label before' });
      const moveMenu = page.getByRole('listbox', { name: 'Move label before', exact: true });
      await expect(async () => {
        if (!await moveMenu.isVisible()) {
          await expect(moveBefore).toBeEnabled({ timeout: 500 }); await moveBefore.press('Enter', { timeout: 500 });
        }
        await expect(moveMenu).toBeVisible({ timeout: 500 });
      }).toPass({ timeout: 5_000 });
      await moveMenu.getByRole('option', { name: 'Unnamed label (blue)', exact: true }).press('Enter');
      const moveLabel = management.getByRole('button', { name: 'Move label', exact: true });
      let moveDispatches = 0;
      page.on('request', request => {
        if (request.method() === 'POST' && new URL(request.url()).pathname === `/labels/${labels[0]}/move`) moveDispatches++;
      });
      // Stop activation on the observed write, even if its reply is still
      // pending. A missing trace entry alone cannot establish non-dispatch.
      await expect(async () => {
        if (moveDispatches === 0) {
          await focusAdmittedControl(moveLabel); await moveLabel.press('Enter', { timeout: 500 });
        }
        await expect.poll(() => moveDispatches, { timeout: 500 }).toBe(1);
      }).toPass({ timeout: 5_000 });
      await expect(management.getByText('Label change confirmed. Reload labels to continue.', { exact: true })).toBeVisible();
      expect(moveDispatches).toBe(1);
      const reordered = await context.request.get(`/boards/${board}/labels`); expect(reordered.status()).toBe(200);
      const ordered = (await reordered.json()).items;
      expect(ordered.find((l: { id: string }) => l.id === labels[0]).rank < ordered.find((l: { id: string }) => l.id === labels[1]).rank).toBe(true);
      expect(ordered.find((l: { id: string }) => l.id === labels[0]).version).toBe(3);
      await expect(management.getByRole('button', { name: 'Reload labels', exact: true })).toBeEnabled();
      await pressAdmittedAction(management.getByRole('button', { name: 'Reload labels', exact: true }));
      await pressAdmittedAction(editUrgent);
      await expect(management.getByRole('button', { name: 'Delete label', exact: true })).toBeDisabled();
      const consent = management.getByRole('checkbox', { name: 'Confirm removal from all Cards' });
      await pressAdmittedAction(consent, 'Space'); await expect(consent).toBeChecked();
      const deleteLabel = management.getByRole('button', { name: 'Delete label', exact: true });
      let deleteDispatches = 0;
      page.on('request', request => {
        if (request.method() === 'DELETE' && new URL(request.url()).pathname === `/labels/${labels[0]}`) deleteDispatches++;
      });
      // Foreground admission can withdraw the focused target before Enter.
      // Stop activating as soon as the single destructive command is sent.
      await expect(async () => {
        if (deleteDispatches === 0) {
          await focusAdmittedControl(deleteLabel);
          await deleteLabel.press('Enter', { timeout: 500 });
        }
        await expect.poll(() => deleteDispatches, { timeout: 500 }).toBe(1);
      }).toPass({ timeout: 5_000 });
      await expect(management.getByText('Label change confirmed. Reload labels to continue.', { exact: true })).toBeVisible();
      expect(deleteDispatches).toBe(1);
      await management.getByRole('button', { name: 'Done', exact: true }).press('Enter'); await expect(management).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Refresh board', exact: true })).toBeFocused();
      await expect(face.getByLabel('Urgent, purple', { exact: true })).toHaveCount(0);
      expect((await context.request.delete(`/labels/${labels[1]}?version=1&confirmed=true`, { headers })).status()).toBe(200);
      await page.goto(path); await expect(show).toBeEnabled(); await show.press('Enter');
      await expect(page.getByText('No labels assigned.', { exact: true })).toBeVisible();
      await expect(page.getByLabel('Priority, red', { exact: true })).toHaveCount(0);
      await expect(page.getByRole('textbox', { name: 'Card title' })).toHaveValue('Labeled work');
    } finally { restoreWorker(); }
  });
}
