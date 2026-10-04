import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

for (const width of [1280, 390]) {
  test(`PRD-08 Card copy keyboard review and original retry update the destination without changing the source at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const account = { email: `copy-${width}-${Date.now()}@example.test`, password: 'copy-correct-horse-battery', displayName: 'Copy reviewer' };
    expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Card copy keyboard' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const boards: string[] = []; const lists: string[] = [];
    for (const name of ['Source Board', 'Copy destination']) {
      const b = await context.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(b.status()).toBe(201); const board = (await b.json()).id; boards.push(board);
      const l = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: `${name} List` } });
      expect(l.status()).toBe(201); lists.push((await l.json()).id);
    }
    const created = await context.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Original copy source', description: 'Retained description' } });
    expect(created.status()).toBe(201); const createdSource = await created.json();
    // Compare persisted snapshots at the database's timestamp precision.
    const baselineReply = await context.request.get(`/boards/${boards[0]}`);
    expect(baselineReply.status()).toBe(200);
    const sourceCard = (await baselineReply.json()).lists.flatMap((column: { cards: { id: string }[] }) => column.cards)
      .find((card: { id: string }) => card.id === createdSource.id);
    expect(sourceCard).toBeDefined();
    const peer = await browser.newContext({ baseURL: new URL(created.url()).origin, viewport: { width, height: 844 } }); let restore = () => {};
    try {
      expect((await peer.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
      restore = scopedBoardWorker(org); for (const board of boards) await waitForBoardDelivery(context.request, board);
      const sourcePeer = await peer.newPage(); const targetPeer = await peer.newPage();
      for (const [p, board] of [[page, boards[0]], [sourcePeer, boards[0]], [targetPeer, boards[1]]] as const) {
        const reads = trackBoardReads(p, board, `/app/${org}/boards/${board}`);
        await p.goto(`/app/${org}/boards/${board}`); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      }
      let copies = 0; let originalBody = ''; let originalKey = ''; let copiedId = '';
      await page.route(`**/cards/${sourceCard.id}/copy`, async route => {
        copies++; expect(route.request().postDataJSON()).toEqual({ sourceBoardId: boards[0], destinationListId: lists[1], title: 'Independent copied Card', expectedVersion: 1 });
        if (copies === 1) {
          originalBody = route.request().postData()!; originalKey = route.request().headers()['idempotency-key'];
          const result = await route.fetch(); expect(result.status()).toBe(200); const acknowledgment = await result.json();
          copiedId = acknowledgment.id; expect(copiedId).not.toBe(sourceCard.id);
          expect(acknowledgment).toMatchObject({ boardId: boards[1], listId: lists[1], title: 'Independent copied Card', description: 'Retained description', version: 1 });
          await route.abort('timedout');
        } else {
          expect(copies).toBe(2); expect(route.request().postData()).toBe(originalBody);
          expect(route.request().headers()['idempotency-key']).toBe(originalKey); await route.continue();
        }
      });
      await page.getByRole('link', { name: 'Original copy source', exact: true }).focus(); await page.keyboard.press('Enter');
      const open = page.getByRole('button', { name: 'Copy Card', exact: true }); await expect(open).toBeEnabled(); await open.focus(); await page.keyboard.press('Enter');
      await expect(page.getByText(/attachments and covers are not copied/)).toBeVisible();
      const title = page.getByRole('textbox', { name: 'Copied Card title', exact: true }); await title.focus(); await title.press('ControlOrMeta+A'); await page.keyboard.type('Independent copied Card');
      const boardChoice = page.getByRole('combobox', { name: 'Copy destination Board', exact: true }); await expect(boardChoice).toBeEnabled();
      await boardChoice.press('ArrowDown'); await page.getByRole('option', { name: 'Copy destination', exact: true }).focus(); await page.keyboard.press('Enter');
      const listChoice = page.getByRole('combobox', { name: 'Copy destination List', exact: true }); await expect(listChoice).toBeEnabled();
      await listChoice.press('ArrowDown'); await page.getByRole('option', { name: 'Copy destination List', exact: true }).focus(); await page.keyboard.press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm Card copy', exact: true }); await confirm.focus(); await page.keyboard.press('Enter');
      const retry = page.getByRole('button', { name: 'Retry this Card copy', exact: true }); await expect(retry).toBeEnabled();
      await expect(sourcePeer.getByRole('link', { name: 'Original copy source', exact: true })).toBeVisible();
      await expect(targetPeer.getByRole('link', { name: 'Independent copied Card', exact: true })).toBeVisible({ timeout: 20_000 });
      expect(copies).toBe(1); await retry.focus(); await page.keyboard.press('Enter');
      const link = page.getByRole('link', { name: 'Open copied Card', exact: true }); await expect(link).toHaveAttribute('href', `/app/${org}/boards/${boards[1]}/cards/${copiedId}`);
      expect(copies).toBe(2);
      const source = await context.request.get(`/boards/${boards[0]}`); const sourceCards = (await source.json()).lists.flatMap((l: { cards: typeof sourceCard[] }) => l.cards);
      expect(sourceCards).toHaveLength(1); expect(sourceCards[0]).toMatchObject(sourceCard);
      const destination = await context.request.get(`/boards/${boards[1]}`);
      expect((await destination.json()).lists.flatMap((l: { cards: { id: string }[] }) => l.cards)).toHaveLength(1);
      await link.focus(); await page.keyboard.press('Enter'); await expect(page.getByRole('heading', { name: 'Card details', exact: true })).toBeVisible();
      await page.getByRole('button', { name: 'Review Card activity', exact: true }).click();
      await expect(page.getByRole('region', { name: 'Card activity' }).getByText('Copy reviewer copied a Card.', { exact: true })).toBeVisible();
      await page.reload(); await expect(page.getByRole('heading', { name: 'Card details', exact: true })).toBeVisible();
    } finally { restore(); await peer.close(); }
  });
}
