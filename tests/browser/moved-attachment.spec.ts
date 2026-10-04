import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-14 moved attachment retains identity, destination keyboard lifecycle and missed-delivery recovery at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `moved-attachment-${width}-${Date.now()}@example.test`, password: 'moved-attachment-correct-horse', displayName: 'Attachment mover' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Moved attachment delivery' } })).json()).organization.id;
    const boards: string[] = []; const lists: string[] = [];
    for (const name of ['Attachment source', 'Attachment destination']) {
      const board = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(board.status()).toBe(201); boards.push((await board.json()).id);
      const list = await context.request.post(`/boards/${boards.at(-1)}/lists`, { headers, data: { name: 'Attachment parent' } });
      expect(list.status()).toBe(201); lists.push((await list.json()).id);
    }
    const created = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Moving attachment Card' } });
    expect(created.status()).toBe(201); const card = (await created.json()).id; const path = `/cards/${card}/attachments`;
    const added = await context.request.post(`${path}/url`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { title: 'Stable moved reference', url: 'https://example.test/moved', cardVersion: 1 } });
    expect(added.status()).toBe(200); const attachment = (await added.json()).attachment;
    const peer = await browser.newContext({ baseURL: new URL(created.url()).origin, viewport: { width, height: 844 } }); let restoreWorker = () => {};
    try {
      expect((await peer.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      restoreWorker = scopedBoardWorker(org); for (const board of boards) await waitForBoardDelivery(context.request, board);
      const source = await peer.newPage(); const sourcePath = `/app/${org}/boards/${boards[0]}`; const targetPath = `/app/${org}/boards/${boards[1]}`;
      const sourceReads = trackBoardReads(source, boards[0], sourcePath); const targetReads = trackBoardReads(page, boards[1], targetPath);
      await source.goto(sourcePath); await page.goto(targetPath);
      await expect.poll(sourceReads).toBeGreaterThanOrEqual(2); await expect.poll(targetReads).toBeGreaterThanOrEqual(2);
      await expect(source.getByRole('link', { name: 'Moving attachment Card', exact: true })).toBeVisible();
      const moved = await peer.request.post(`/cards/${card}/move`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: { sourceBoardId: boards[0], destinationListId: lists[1], expectedVersion: 2 } });
      expect(moved.status()).toBe(200);
      const destinationCard = page.getByRole('link', { name: 'Moving attachment Card', exact: true });
      await expect(destinationCard).toBeVisible({ timeout: 20_000 });
      await expect(source.getByRole('link', { name: 'Moving attachment Card', exact: true })).toHaveCount(0, { timeout: 20_000 });
      await destinationCard.focus(); await page.keyboard.press('Enter');
      await page.getByRole('button', { name: 'Show attachments', exact: true }).press('Enter');
      await expect(page.getByRole('link', { name: 'Stable moved reference (opens in a new tab)', exact: true })).toBeVisible();
      const current = await (await context.request.get(path)).json(); expect(current.boardId).toBe(boards[1]); expect(current.cardVersion).toBe(3); expect(current.items).toEqual([attachment]);
      // Keep a second genuine destination detail open before the lifecycle
      // commands. This page must consume/recover Worker-delivered updates.
      const detailPath = `${targetPath}/cards/${card}`;
      const observerReads = trackBoardReads(source, boards[1], detailPath);
      await source.goto(detailPath); await expect.poll(observerReads).toBeGreaterThanOrEqual(2);
      await source.getByRole('button', { name: 'Show attachments', exact: true }).press('Enter');
      const observedAttachment = source.getByRole('link', { name: 'Stable moved reference (opens in a new tab)', exact: true });
      await expect(observedAttachment).toBeVisible();
      await page.getByRole('button', { name: 'Manage attachments', exact: true }).press('Enter');
      await page.getByRole('button', { name: 'Archive attachment Stable moved reference', exact: true }).press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm attachment archive', exact: true }); await expect(confirm).toBeEnabled(); await confirm.press('Enter');
      await expect(page.getByText('Attachment archived.', { exact: true })).toBeVisible();
      const retained = await (await context.request.get(`${path}/archive`)).json(); expect(retained.boardId).toBe(boards[1]); expect(retained.cardVersion).toBe(4); expect(retained.items).toHaveLength(1); expect(retained.items[0].id).toBe(attachment.id);
      await expect(observedAttachment).toHaveCount(0, { timeout: 20_000 });
      await expect(source.getByText('No attachments on this page.', { exact: true })).toBeVisible();
      await peer.setOffline(true);
      const restored = await context.request.post(`${path}/${attachment.id}/restore`, {
        headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { cardVersion: retained.cardVersion, attachmentVersion: retained.items[0].version },
      });
      expect(restored.status()).toBe(200);
      await waitForBoardDelivery(context.request, boards[1]);
      await peer.setOffline(false);
      await expect(observedAttachment).toBeVisible({ timeout: 30_000 });
      const recovered = await (await context.request.get(path)).json();
      expect(recovered.boardId).toBe(boards[1]); expect(recovered.cardVersion).toBe(5);
      expect(recovered.items).toHaveLength(1); expect(recovered.items[0]).toMatchObject({ id: attachment.id, version: 3, displayName: attachment.displayName });
      expect((await new AxeBuilder({ page: source }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { restoreWorker(); await peer.close(); }
  });
}
