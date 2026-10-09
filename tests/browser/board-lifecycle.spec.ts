import { registerNotificationAccount as registerVerifiedAccountFixture } from './notificationAccountFixture';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';
import { trackArchivedBoardChanges } from './invitationAdmissionTracker';
import { pressAdmittedAction } from './keyboardAdmission';

for (const width of [1280, 390]) {
  test(`PRD-04/18: Board lifecycle reviews recover original receipts and preserve child states at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `board-lifecycle-${width}-${Date.now()}@example.test`, password: 'lifecycle-correct-horse-battery', displayName: 'Lifecycle administrator' };
    await registerVerifiedAccountFixture(context.request, account);
    const actor = (await (await context.request.get('/me')).json()).id;
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Board lifecycle fixture' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Lifecycle Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = await boardReply.json();
    const listReply = await context.request.post(`/boards/${board.id}/lists`, { headers, data: { name: 'Active planning' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    expect((await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Active retained card' } })).status()).toBe(201);
    const cardReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Archived retained card' } });
    expect(cardReply.status()).toBe(201); const card = await cardReply.json();
    expect((await context.request.post(`/cards/${card.id}/archive`, { headers, data: { version: card.version } })).status()).toBe(200);
    const hiddenReply = await context.request.post(`/boards/${board.id}/lists`, { headers, data: { name: 'Archived planning' } });
    expect(hiddenReply.status()).toBe(201); const hiddenList = await hiddenReply.json();
    expect((await context.request.post(`/lists/${hiddenList.id}/cards`, { headers, data: { title: 'Retained in archived List' } })).status()).toBe(201);
    expect((await context.request.post(`/lists/${hiddenList.id}/archive`, { headers, data: { version: hiddenList.version } })).status()).toBe(200);
    const before = await (await context.request.get(`/boards/${board.id}`)).json();
    const beforeLists = await (await context.request.get(`/boards/${board.id}/archived-lists`)).json();
    const beforeCards = await (await context.request.get(`/boards/${board.id}/archived-cards`)).json();
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, board.id);
      const boardPath = `/app/${org}/boards/${board.id}`; const archivePath = `/app/${org}/archived-boards`;
      const archiveChanges = trackArchivedBoardChanges(page, org, actor, board.id, archivePath);
      const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
      const otherReads = trackBoardReads(other, board.id, boardPath);
      const initiatingReads = trackBoardReads(page, board.id, boardPath);
      await other.goto(boardPath); await page.goto(boardPath);
      for (const client of [page, other]) await expect(client.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await expect.poll(otherReads).toBeGreaterThanOrEqual(2);
      await expect.poll(initiatingReads).toBeGreaterThanOrEqual(2);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const archives: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board.id}/archive`, async route => {
        archives.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const result = await route.fetch(); expect(result.status()).toBe(200);
        if (archives.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      const archiveEntry = page.getByRole('button', { name: 'Archive Board', exact: true });
      await expect(archiveEntry).toBeEnabled();
      await archiveEntry.focus(); await expect(archiveEntry).toBeFocused(); await archiveEntry.press('Enter');
      await expect(page.getByText(/Its Lists and Cards remain associated with it/)).toBeVisible();
      const confirmArchive = page.getByRole('button', { name: 'Confirm archive', exact: true });
      await expect(confirmArchive).toBeEnabled(); await confirmArchive.press('Enter');
      await expect.poll(() => archives.length).toBe(1);
      await expect(page.getByRole('button', { name: 'Retry this archive', exact: true })).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Cancel archive', exact: true })).toHaveCount(0);
      await expect(other.getByText('This board is archived. Editing is unavailable.', { exact: true })).toBeVisible();
      await expect(other.getByRole('button', { name: 'Add list', exact: true })).toHaveCount(0);
      expect((await new AxeBuilder({ page: other }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const activeDirectory = await context.request.get(`/organizations/${org}/boards`); expect(activeDirectory.status()).toBe(200);
      expect(await activeDirectory.json()).toEqual([]);
      const retryArchive = page.getByRole('button', { name: 'Retry this archive', exact: true });
      await expect(async () => {
        if (archives.length === 1) await pressAdmittedAction(retryArchive);
        await expect.poll(() => archives.length, { timeout: 500 }).toBe(2);
      }).toPass({ timeout: 5_000 });
      await expect(page.getByText('Board archive acknowledged. Current Board state is being checked.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Refresh board', exact: true })).toBeFocused();
      expect(archives).toHaveLength(2); expect(archives[1]).toEqual(archives[0]);
      expect(archives[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.parse(archives[0].body!)).toEqual({ version: before.board.version });
      await pressAdmittedAction(page.getByRole('link', { name: 'Manage archived Boards', exact: true }));
      await expect(page).toHaveURL(new RegExp(`${archivePath}$`));
      await expect(page.getByRole('heading', { name: board.name, exact: true })).toBeVisible();
      await expect(page.getByText('Active retained card', { exact: true })).toHaveCount(0);
      // The initial live reset retires consent. Wait for its protected archive
      // read before opening a new review, rather than keying into a closing one.
      await expect(page.getByRole('status')).toHaveText('Current archived boards checked.');
      await expect.poll(archiveChanges.ready).toBe(true);
      const restores: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board.id}/restore`, async route => {
        restores.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const result = await route.fetch(); expect(result.status()).toBe(200);
        if (restores.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      await pressAdmittedAction(page.getByRole('button', { name: `Restore ${board.name} board`, exact: true }));
      await expect(page.getByText('Restoration makes the Board active again. Its Lists and Cards retain their own lifecycle states.', { exact: true })).toBeVisible();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      const confirmRestore = page.getByRole('button', { name: 'Confirm restore', exact: true });
      await expect(confirmRestore).toBeEnabled(); await confirmRestore.press('Enter');
      await expect.poll(() => restores.length).toBe(1);
      await expect(page.getByRole('button', { name: 'Retry this change', exact: true })).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Cancel change', exact: true })).toHaveCount(0);
      await expect(other.getByRole('button', { name: 'Add list', exact: true })).toBeEnabled();
      await pressAdmittedAction(page.getByRole('button', { name: 'Retry this change', exact: true }));
      await expect(page.getByText('Board restore acknowledged.', { exact: true })).toBeVisible();
      await expect(page.getByText('No administrable archived Boards on this page.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current archived boards', exact: true })).toBeFocused();
      expect(restores).toHaveLength(2); expect(restores[1]).toEqual(restores[0]);
      expect(restores[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(JSON.parse(restores[0].body!)).toEqual({ version: before.board.version + 1 });
      const after = await (await context.request.get(`/boards/${board.id}`)).json();
      expect(after.board).toMatchObject({ id: board.id, lifecycleState: 'active', version: before.board.version + 2 });
      expect(after.lists).toEqual(before.lists);
      expect((await (await context.request.get(`/boards/${board.id}/archived-lists`)).json()).items).toEqual(beforeLists.items);
      expect((await (await context.request.get(`/boards/${board.id}/archived-cards`)).json()).items).toEqual(beforeCards.items);
      await expect(other.getByText('Active retained card', { exact: true })).toBeVisible();
      await expect(other.getByText('Archived retained card', { exact: true })).toHaveCount(0);
      await expect(other.getByRole('heading', { name: 'Archived planning', exact: true })).toHaveCount(0);
      const priorArchive = archiveChanges.count('BOARD_ARCHIVED');
      const rearchive = await context.request.post(`/boards/${board.id}/archive`, { headers, data: { version: after.board.version } });
      expect(rearchive.status()).toBe(200); const archived = await rearchive.json();
      await expect.poll(() => archiveChanges.settled('BOARD_ARCHIVED', priorArchive + 1)).toBe(true);
      await pressAdmittedAction(page.getByRole('button', { name: 'Check current archived boards', exact: true }));
      await expect(page.getByRole('button', { name: `Permanently delete ${board.name} board`, exact: true })).toBeEnabled();
      const deletes: { url: string; key: string | undefined; body: string | null }[] = [];
      await page.route(`**/boards/${board.id}?*`, async route => {
        if (route.request().method() !== 'DELETE') { await route.continue(); return; }
        deletes.push({ url: route.request().url(), key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const result = await route.fetch(); expect(result.status()).toBe(200);
        if (deletes.length === 1) await route.abort('failed'); else await route.fulfill({ response: result });
      });
      await pressAdmittedAction(page.getByRole('button', { name: `Permanently delete ${board.name} board`, exact: true }));
      await expect(page.getByText('This cannot be undone. This Board cannot be restored, and its Lists and Cards become unavailable through it.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Confirm permanent deletion', exact: true })).toBeDisabled(); expect(deletes).toHaveLength(0);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      await pressAdmittedAction(page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true }), 'Space');
      const priorDelete = archiveChanges.count('BOARD_DELETED');
      const confirmDelete = page.getByRole('button', { name: 'Confirm permanent deletion', exact: true });
      await pressAdmittedAction(confirmDelete);
      await expect.poll(() => deletes.length).toBe(1);
      await expect.poll(() => archiveChanges.settled('BOARD_DELETED', priorDelete + 1)).toBe(true);
      await expect(page.getByRole('button', { name: 'Retry this change', exact: true })).toBeEnabled();
      // The canonical deletion withdraws private review content even while the
      // original command acknowledgment remains unresolved. Its key survives
      // for the explicit retry below; stale destructive consent does not.
      await expect(page.getByRole('checkbox', { name: 'I understand this cannot be undone.', exact: true })).toHaveCount(0);
      await expect(page.getByRole('heading', { name: board.name, exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Cancel change', exact: true })).toHaveCount(0);
      await expect(other.getByRole('alert')).toContainText('This board or action is unavailable.');
      await expect(other.getByRole('heading', { name: board.name, exact: true })).toHaveCount(0);
      await pressAdmittedAction(page.getByRole('button', { name: 'Retry this change', exact: true }));
      await expect(page.getByText('Board deletion acknowledged.', { exact: true })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Check current archived boards', exact: true })).toBeFocused();
      expect(deletes).toHaveLength(2); expect(deletes[1]).toEqual(deletes[0]); expect(deletes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect([...new URL(deletes[0].url).searchParams.entries()]).toEqual([['version', String(archived.version)], ['confirmed', 'true']]);
      expect(deletes[0].body).toBeNull();
      expect((await context.request.get(`/boards/${board.id}`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${board.id}/archived-lists`)).status()).toBe(404);
      expect((await context.request.get(`/boards/${board.id}/archived-cards`)).status()).toBe(404);
      expect((await context.request.post(`/boards/${board.id}/restore`, { headers, data: { version: archived.version + 1 } })).status()).toBe(404);
      await page.reload(); await expect(page.getByText('No administrable archived Boards on this page.', { exact: true })).toBeVisible();
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await other.close();
    } finally { restoreWorker(); }
  });
}
