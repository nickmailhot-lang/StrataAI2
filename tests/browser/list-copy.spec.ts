import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';

for (const width of [1280, 390]) {
  test(`PRD-07: reviewed cross-Board List copy recovers its receipt and reaches another client at ${width}px`, async ({ page, context }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `list-copy-${width}-${Date.now()}@example.test`, password: 'list-copy-correct-horse-battery', displayName: 'List copy editor' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Copy Organization' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    async function board(name: string) {
      const response = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(response.status()).toBe(201); return (await response.json()).id as string;
    }
    const sourceBoard = await board('Source Board'); const destinationBoard = await board('Delivery');
    const listReply = await context.request.post(`/boards/${sourceBoard}/lists`, { headers, data: { name: 'Planning' } });
    expect(listReply.status()).toBe(201); const list = await listReply.json();
    const activeReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Active copied work', description: 'Copy content persists' } });
    expect(activeReply.status()).toBe(201); const active = await activeReply.json();
    const archivedReply = await context.request.post(`/lists/${list.id}/cards`, { headers, data: { title: 'Archived copied work', description: 'Archived copied detail' } });
    expect(archivedReply.status()).toBe(201); const archived = await archivedReply.json();
    expect((await context.request.post(`/cards/${archived.id}/archive`, { headers, data: { version: 1 } })).status()).toBe(200);
    const restoreWorker = scopedBoardWorker(org);
    try {
      await waitForBoardDelivery(context.request, sourceBoard);
      const other = await context.newPage(); await other.setViewportSize({ width, height: 844 });
      const destinationPath = `/app/${org}/boards/${destinationBoard}`;
      await other.goto(destinationPath); await expect(other.getByText('Live updates connected.', { exact: true })).toBeVisible();
      const sourcePath = `/app/${org}/boards/${sourceBoard}`;
      await page.goto(sourcePath); await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
      const attempts: { key: string | undefined; body: string | null }[] = [];
      await page.route(`**/lists/${list.id}/copy`, async route => {
        attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const response = await route.fetch(); expect(response.status()).toBe(201);
        if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({ response });
      });
      const copyButton = page.getByRole('button', { name: 'Copy list', exact: true });
      // A connected stream can invalidate the snapshot before its read settles.
      // Focusing a disabled button silently does nothing, so admit the keyboard
      // action only after the canonical Board read enables this control.
      await expect(copyButton).toBeEnabled(); await copyButton.press('Enter');
      await expect(page.getByRole('button', { name: 'Reload copy destinations' })).toBeEnabled();
      async function choose(label: string, name: string) {
        await page.getByRole('combobox', { name: label }).focus(); await page.keyboard.press('Enter');
        await page.getByRole('option', { name, exact: true }).focus(); await page.keyboard.press('Enter');
      }
      await choose('List to copy', 'Planning');
      const name = page.getByRole('textbox', { name: 'Copy name' }); await name.focus();
      await page.keyboard.press('ControlOrMeta+A'); await page.keyboard.type('Reviewed copy');
      await choose('Destination Board', 'Delivery');
      await page.getByRole('button', { name: 'Review List copy', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByText('Copy Planning as Reviewed copy to Delivery?', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Confirm List copy', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Retry this List copy', exact: true })).toBeEnabled();
      await expect(page.getByRole('button', { name: 'Cancel copy' })).toHaveCount(0);
      await expect(other.getByRole('heading', { name: 'Reviewed copy', exact: true })).toBeVisible();
      await expect(other.getByRole('link', { name: 'Active copied work', exact: true })).toBeVisible();
      await expect(other.getByRole('link', { name: 'Archived copied work', exact: true })).toHaveCount(0);
      expect((await context.request.patch(`/lists/${list.id}`, { headers, data: { name: 'Changed source', version: 1 } })).status()).toBe(200);
      // The live revision must preserve the original copy intent and reviewed text.
      await expect(page.getByText('Copy Planning as Reviewed copy to Delivery?', { exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Retry this List copy', exact: true }).focus(); await page.keyboard.press('Enter');
      await expect(page.getByRole('button', { name: 'Refresh board', exact: true })).toBeFocused();
      await expect(page.getByRole('heading', { name: 'Changed source', exact: true })).toBeVisible();
      expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(JSON.parse(attempts[0].body!)).toEqual({ destinationBoardId: destinationBoard, name: 'Reviewed copy', version: 1 });
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/);
      const copiedSnapshot = await context.request.get(`/boards/${destinationBoard}`); expect(copiedSnapshot.status()).toBe(200);
      const columns = (await copiedSnapshot.json()).lists; expect(columns).toHaveLength(1);
      const copied = columns[0]; expect(copied.list.id).not.toBe(list.id); expect(copied.list.version).toBe(1);
      expect(copied.cards).toHaveLength(1); expect(copied.cards[0].id).not.toBe(active.id);
      expect(copied.cards[0]).toMatchObject({ title: active.title, description: active.description, rank: active.rank, version: 1 });
      const archives = await context.request.get(`/boards/${destinationBoard}/archived-cards`); expect(archives.status()).toBe(200);
      const archivedItems = (await archives.json()).items; expect(archivedItems).toHaveLength(1);
      expect(archivedItems[0].card.id).not.toBe(archived.id);
      expect(archivedItems[0].card).toMatchObject({ title: archived.title, rank: archived.rank, version: 1, lifecycleState: 'archived' });
      const sourceSnapshot = await context.request.get(`/boards/${sourceBoard}`); expect(sourceSnapshot.status()).toBe(200);
      const original = (await sourceSnapshot.json()).lists; expect(original).toHaveLength(1);
      expect(original[0].list).toMatchObject({ id: list.id, name: 'Changed source', rank: list.rank, version: 2 });
      expect(original[0].cards[0]).toMatchObject({ id: active.id, rank: active.rank, version: 1 });
      await other.reload(); await expect(other.getByRole('heading', { name: 'Reviewed copy', exact: true })).toBeVisible();
      await expect(other.getByRole('link', { name: 'Active copied work', exact: true })).toBeVisible();
    } finally { restoreWorker(); }
  });
}
