import { readFileSync } from 'node:fs';
import { expect, test } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Mandatory alternate phase: the shell has selected its actual Worker-published
// source as a private cover. The following HTTP phase recovers this same command.
test('PRD-14 actual source archive withdraws the selected cover from distinct member desktop and phone sessions', async ({ page, context, browser }) => {
  test.setTimeout(150_000);
  expect(process.env.CI).toBe('true');
  const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE; expect(path).toBeTruthy();
  const fixture = JSON.parse(readFileSync(path!, 'utf8')) as {
    email: string; password: string; organizationId: string; boardId: string; cardId: string;
    archiveKey: string; peer: { email: string; password: string };
  };
  for (const id of [fixture.organizationId, fixture.boardId, fixture.cardId, fixture.archiveKey]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
  const headers = { 'X-StrataAI-Request': '1' };
  expect((await context.request.post('/auth/login', { headers, data: { email: fixture.email, password: fixture.password } })).status()).toBe(200);
  const coverPath = `/cards/${fixture.cardId}/cover`;
  const coverResponse = await context.request.get(coverPath); expect(coverResponse.status()).toBe(200);
  const cover = await coverResponse.json();
  expect(cover).toMatchObject({ cardVersion: 9, attachmentVersion: 3 });
  expect(cover.attachmentId).toMatch(/^[0-9a-f-]{36}$/);
  const boardResponse = await context.request.get(`/boards/${fixture.boardId}`); expect(boardResponse.status()).toBe(200);
  expect((await boardResponse.json()).board.visibility).toBe('PRIVATE');
  const ownerResponse = await context.request.get('/me'); expect(ownerResponse.status()).toBe(200);
  const owner = await ownerResponse.json();
  await waitForBoardDelivery(context.request, fixture.boardId);
  const cardPath = `/app/${fixture.organizationId}/boards/${fixture.boardId}/cards/${fixture.cardId}`;
  const ownerReads = trackBoardReads(page, fixture.boardId, cardPath);
  await page.goto(cardPath); await expect.poll(ownerReads).toBeGreaterThanOrEqual(2);
  await expect(page.getByRole('img', { name: 'Card cover', exact: true })).toBeVisible();
  const clients = [];
  try {
    for (const width of [1280, 390]) {
      const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
      const peer = await peerContext.newPage();
      const client = { context: peerContext, page: peer, sequence: 0n, navigations: 0 }; clients.push(client);
      expect((await peerContext.request.post('/auth/login', { headers, data: { email: fixture.peer.email, password: fixture.peer.password } })).status()).toBe(200);
      const profileResponse = await peerContext.request.get('/me'); expect(profileResponse.status()).toBe(200);
      expect((await profileResponse.json()).id).not.toBe(owner.id);
      peer.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/boards/live') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type !== 2 || !Array.isArray(message.item?.events)) continue;
            for (const event of message.item.events) {
              if (event.organizationId === fixture.organizationId && event.boardId === fixture.boardId
                && typeof event.sequence === 'string' && /^[0-9]{1,19}$/.test(event.sequence)
                && BigInt(event.sequence) > client.sequence) client.sequence = BigInt(event.sequence);
            }
          }
        });
      });
      const peerReads = trackBoardReads(peer, fixture.boardId, cardPath);
      await peer.goto(cardPath); await expect.poll(peerReads).toBeGreaterThanOrEqual(2);
      const image = peer.getByRole('img', { name: 'Card cover', exact: true });
      await expect(image).toBeVisible();
      await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible();
      peer.on('framenavigated', frame => { if (frame === peer.mainFrame()) client.navigations++; });
    }
    const syncResponse = await context.request.get(`/boards/${fixture.boardId}/sync`); expect(syncResponse.status()).toBe(200);
    const sync = await syncResponse.json(); expect(sync).toMatchObject({ hasMore: false, pending: false, resetRequired: false });
    expect(sync.cursor).toMatch(/^[0-9]{1,19}$/);
    const archivedResponse = await context.request.post(`/cards/${fixture.cardId}/attachments/${cover.attachmentId}/archive`, {
      headers: { ...headers, 'Idempotency-Key': fixture.archiveKey }, data: { cardVersion: 9, version: 3 },
    });
    expect(archivedResponse.status()).toBe(200);
    expect(await archivedResponse.json()).toMatchObject({ cardVersion: 10, attachment: { id: cover.attachmentId, version: 4, lifecycleState: 1 } });
    await waitForBoardDelivery(context.request, fixture.boardId);
    await expect(page.getByRole('img', { name: 'Card cover', exact: true })).toHaveCount(0, { timeout: 20_000 });
    for (const client of clients) {
      await expect.poll(() => client.sequence > BigInt(sync.cursor), { timeout: 20_000 }).toBe(true);
      await expect(client.page.getByRole('img', { name: 'Card cover', exact: true })).toHaveCount(0, { timeout: 20_000 });
      await expect(client.page).toHaveURL(new RegExp(`/cards/${fixture.cardId}$`)); expect(client.navigations).toBe(0);
      const currentCover = await client.context.request.get(coverPath); expect(currentCover.status()).toBe(200);
      expect(await currentCover.json()).toMatchObject({ cardVersion: 10, attachmentId: null, attachmentVersion: null });
      expect((await client.context.request.get(coverPath + '/image')).status()).toBe(404);
      for (const suffix of ['download-options', 'download', 'preview']) {
        expect((await client.context.request.get(`/cards/${fixture.cardId}/attachments/${cover.attachmentId}/${suffix}`)).status()).toBe(404);
      }
    }
    expect((await context.request.get(coverPath + '/image')).status()).toBe(404);
  } finally { for (const client of clients) await client.context.close(); }
});
