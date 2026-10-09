import AxeBuilder from '@axe-core/playwright';
import type { BrowserContext, Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { createHash, randomUUID } from 'node:crypto';
import { expect, test, type WebSocketRoute } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { focusAdmittedControl, pressAdmittedAction } from './keyboardAdmission';

// Interrupt only the committed response. Fetch interception at Response stage
// preserves Chromium's original File request body (the request mirror omits it).
// https://chromedevtools.github.io/devtools-protocol/tot/Fetch/
async function loseUploadReply(context: BrowserContext, page: Page, path: string) {
  const session = await context.newCDPSession(page);
  let resolve!: (value: unknown) => void, reject!: (error: unknown) => void;
  const reply = new Promise<unknown>((accept, refuse) => { resolve = accept; reject = refuse; });
  let lost = false;
  session.on('Fetch.requestPaused', async event => {
    try {
      if (!lost && event.request.method === 'POST' && event.responseStatusCode === 200) {
        lost = true;
        const response = await session.send('Fetch.getResponseBody', { requestId: event.requestId });
        const value = JSON.parse(response.base64Encoded ? Buffer.from(response.body, 'base64').toString('utf8') : response.body);
        await session.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'TimedOut' });
        await session.send('Fetch.disable'); await session.detach(); resolve(value);
      } else await session.send('Fetch.continueRequest', { requestId: event.requestId });
    } catch (error) { reject(error); }
  });
  await session.send('Fetch.enable', { patterns: [{ urlPattern: new URL(path, page.url()).href, requestStage: 'Response' }] });
  return { reply };
}

test('PRD-14 private published cover and attachment review withdraw when Board membership is removed at desktop and phone sizes', async ({ context, browser }) => {
  test.setTimeout(150_000);
  expect(process.env.CI).toBe('true');
  const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE; expect(path).toBeTruthy();
  const fixture = JSON.parse(readFileSync(path!, 'utf8')) as {
    email: string; password: string; organizationId: string; boardId: string; cardId: string;
    peer: { email: string; password: string };
  };
  for (const id of [fixture.organizationId, fixture.boardId, fixture.cardId]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
  const headers = { 'X-StrataAI-Request': '1' };
  expect((await context.request.post('/auth/login', { headers, data: { email: fixture.email, password: fixture.password } })).status()).toBe(200);
  const coverPath = `/cards/${fixture.cardId}/cover`;
  const originalCoverResponse = await context.request.get(coverPath); expect(originalCoverResponse.status()).toBe(200);
  const originalCover = await originalCoverResponse.json(); expect(originalCover).toMatchObject({ cardVersion: 9, attachmentVersion: 3, isPublic: false });
  expect(originalCover.attachmentId).toMatch(/^[0-9a-f-]{36}$/);
  const ownerResponse = await context.request.get('/me'); expect(ownerResponse.status()).toBe(200); const owner = await ownerResponse.json();
  const cardPath = `/app/${fixture.organizationId}/boards/${fixture.boardId}/cards/${fixture.cardId}`;
  const clients = []; let peerId: string | undefined; let removed = false;
  try {
    await waitForBoardDelivery(context.request, fixture.boardId);
    for (const width of [1280, 390]) {
      const peerContext = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
      const peer = await peerContext.newPage(); const client = { context: peerContext, page: peer, navigations: 0 }; clients.push(client);
      expect((await peerContext.request.post('/auth/login', { headers, data: { email: fixture.peer.email, password: fixture.peer.password } })).status()).toBe(200);
      const profileResponse = await peerContext.request.get('/me'); expect(profileResponse.status()).toBe(200);
      const profile = await profileResponse.json(); expect(profile.id).not.toBe(owner.id);
      if (peerId) expect(profile.id).toBe(peerId); else peerId = profile.id;
      const reads = trackBoardReads(peer, fixture.boardId, cardPath);
      await peer.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const image = peer.getByRole('img', { name: 'Card cover', exact: true }); await expect(image).toBeVisible();
      await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await focusAdmittedControl(peer.getByRole('button', { name: 'Manage attachments', exact: true })); await peer.keyboard.press('Enter');
      await expect(peer.getByRole('group', { name: 'Attachment Private original.png', exact: true })).toBeVisible();
      peer.on('framenavigated', frame => { if (frame === peer.mainFrame()) client.navigations++; });
    }
    expect(peerId).toMatch(/^[0-9a-f-]{36}$/);
    const organizationMemberPath = `/organizations/${fixture.organizationId}/members/${peerId}`;
    const beforeMembershipResponse = await context.request.get(organizationMemberPath); expect(beforeMembershipResponse.status()).toBe(200);
    const beforeMembership = await beforeMembershipResponse.json();
    const directoryResponse = await context.request.get(`/boards/${fixture.boardId}/members`); expect(directoryResponse.status()).toBe(200);
    const directory = await directoryResponse.json(); expect(Array.isArray(directory)).toBe(true);
    const member = directory.find((row: { userId: string }) => row.userId === peerId);
    expect(member).toMatchObject({ active: true, role: 'MEMBER' }); expect(Number.isSafeInteger(member.version) && member.version > 0).toBe(true);
    const removal = await context.request.delete(`/boards/${fixture.boardId}/members/${peerId}`, {
      headers: { ...headers, 'Idempotency-Key': randomUUID(), 'If-Match': `"${member.version}"` },
    });
    expect(removal.status()).toBe(204); removed = true;
    await waitForBoardDelivery(context.request, fixture.boardId);
    for (const client of clients) {
      await expect(client.page.getByRole('img', { name: 'Card cover', exact: true })).toHaveCount(0, { timeout: 20_000 });
      await expect(client.page.getByRole('group', { name: 'Attachment Private original.png', exact: true })).toHaveCount(0, { timeout: 20_000 });
      await expect(client.page.getByRole('button', { name: 'Review Card cover', exact: true })).toHaveCount(0);
      await expect(client.page).toHaveURL(new RegExp(`/cards/${fixture.cardId}$`)); expect(client.navigations).toBe(0);
      for (const route of [`/boards/${fixture.boardId}`, coverPath, coverPath + '/image', `/cards/${fixture.cardId}/attachments`,
        ...['download-options', 'download', 'preview'].map(suffix => `/cards/${fixture.cardId}/attachments/${originalCover.attachmentId}/${suffix}`)]) {
        expect((await client.context.request.get(route)).status()).toBe(404);
      }
      const deniedWrite = await client.context.request.put(coverPath, { headers: { ...headers, 'Idempotency-Key': randomUUID() },
        data: { cardVersion: 9, attachmentId: null, attachmentVersion: null, publicVisibilityConfirmed: false } });
      expect(deniedWrite.status()).toBe(404);
    }
    const retainedCoverResponse = await context.request.get(coverPath); expect(retainedCoverResponse.status()).toBe(200);
    expect(await retainedCoverResponse.json()).toEqual(originalCover);
    expect((await context.request.get(coverPath + '/image')).status()).toBe(200);
    const afterMembershipResponse = await context.request.get(organizationMemberPath); expect(afterMembershipResponse.status()).toBe(200);
    expect(await afterMembershipResponse.json()).toEqual(beforeMembership);
  } finally {
    try {
      if (removed) {
        const restored = await context.request.patch(`/boards/${fixture.boardId}/members/${peerId}`, {
          headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { role: 'MEMBER' },
        }); expect(restored.status()).toBe(200);
        await waitForBoardDelivery(context.request, fixture.boardId);
      }
    } finally { for (const client of clients) await client.context.close(); }
  }
});

// Mandatory alternate phase: the shell has selected its actual Worker-published
// source as a private cover. The following HTTP phase recovers this same command.
test('PRD-14 actual source archive withdraws the selected cover from distinct member desktop, phone and reconnecting sessions', async ({ page, context, browser }) => {
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
  let unavailable = false; let interruptedSocket: WebSocketRoute | undefined;
  try {
    for (const { width, interrupted } of [{ width: 1280, interrupted: false }, { width: 390, interrupted: false }, { width: 390, interrupted: true }]) {
      const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
      const peer = await peerContext.newPage();
      const client = { context: peerContext, page: peer, sequence: 0n, navigations: 0, interrupted, reads: () => 0 }; clients.push(client);
      if (interrupted) await peerContext.routeWebSocket('**/boards/live*', route => {
        if (unavailable) { route.close({ code: 1013 }); return; }
        interruptedSocket = route; route.connectToServer();
      });
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
      client.reads = peerReads;
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
    const interrupted = clients.find(client => client.interrupted)!;
    expect(interruptedSocket).toBeDefined(); unavailable = true;
    await interruptedSocket!.close({ code: 1012 });
    await expect(interrupted.page.getByText('Live updates connected.', { exact: true })).not.toBeVisible();
    const interruptedSequence = interrupted.sequence;
    const archivedResponse = await context.request.post(`/cards/${fixture.cardId}/attachments/${cover.attachmentId}/archive`, {
      headers: { ...headers, 'Idempotency-Key': fixture.archiveKey }, data: { cardVersion: 9, version: 3 },
    });
    expect(archivedResponse.status()).toBe(200);
    expect(await archivedResponse.json()).toMatchObject({ cardVersion: 10, attachment: { id: cover.attachmentId, version: 4, lifecycleState: 1 } });
    await waitForBoardDelivery(context.request, fixture.boardId);
    await expect(page.getByRole('img', { name: 'Card cover', exact: true })).toHaveCount(0, { timeout: 20_000 });
    // Preserve both continuously connected viewport proofs before admitting
    // the extra interrupted peer. Its socket cannot receive the archive event.
    for (const client of clients.filter(client => !client.interrupted)) {
      await expect.poll(() => client.sequence > BigInt(sync.cursor), { timeout: 20_000 }).toBe(true);
      await expect(client.page.getByRole('img', { name: 'Card cover', exact: true })).toHaveCount(0, { timeout: 20_000 });
    }
    expect(interrupted.sequence).toBe(interruptedSequence);
    const beforeReconnectReads = interrupted.reads(); unavailable = false;
    await expect(interrupted.page.getByText('Live updates connected.', { exact: true })).toBeVisible({ timeout: 45_000 });
    await expect.poll(interrupted.reads, { timeout: 20_000 }).toBeGreaterThan(beforeReconnectReads);
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

for (const { width, loseReply, rejected } of [
  { width: 1280, loseReply: false, rejected: false }, { width: 390, loseReply: false, rejected: false },
  { width: 1280, loseReply: true, rejected: false }, { width: 390, loseReply: true, rejected: false },
  { width: 1280, loseReply: false, rejected: true }, { width: 390, loseReply: false, rejected: true },
]) {
  test(`PRD-14 real browser file upload${loseReply ? ' original receipt recovery' : ''}, ${rejected ? 'Worker quarantine and refused delivery' : 'Worker publication, preview and download'} at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000);
    expect(process.env.CI).toBe('true');
    const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE; expect(path).toBeTruthy();
    const fixture = JSON.parse(readFileSync(path!, 'utf8')) as { email: string; password: string; organizationId: string };
    expect(fixture.organizationId).toMatch(/^[0-9a-f-]{36}$/);
    await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    expect((await context.request.post('/auth/login', { headers, data: { email: fixture.email, password: fixture.password } })).status()).toBe(200);
    const boardResponse = await context.request.post('/boards', { headers: { ...headers, 'Idempotency-Key': randomUUID() },
      data: { organizationId: fixture.organizationId, name: `Browser upload ${width}${loseReply ? ' recovery' : ''}${rejected ? ' quarantine' : ''}`, visibility: 'PRIVATE' } });
    expect(boardResponse.status()).toBe(201); const board = (await boardResponse.json()).id;
    const listResponse = await context.request.post(`/boards/${board}/lists`, { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { name: 'Browser uploads' } });
    expect(listResponse.status()).toBe(201); const list = (await listResponse.json()).id;
    const cardResponse = await context.request.post(`/lists/${list}/cards`, { headers: { ...headers, 'Idempotency-Key': randomUUID() }, data: { title: `Browser upload ${width}` } });
    expect(cardResponse.status()).toBe(201); const card = (await cardResponse.json()).id;
    for (const id of [board, list, card]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
    await waitForBoardDelivery(context.request, board);
    const cardPath = `/app/${fixture.organizationId}/boards/${board}/cards/${card}`;
    const reads = trackBoardReads(page, board, cardPath), admittedCardVersion = trackCardVersion(page, board, card, cardPath);
    let receivedSequence = 0n;
    page.on('websocket', socket => {
      if (new URL(socket.url()).pathname !== '/boards/live') return;
      socket.on('framereceived', frame => {
        for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
          const message = JSON.parse(raw);
          if (message.type !== 2 || !Array.isArray(message.item?.events)) continue;
          for (const event of message.item.events) {
            if (event.organizationId === fixture.organizationId && event.boardId === board
              && typeof event.sequence === 'string' && /^[0-9]{1,19}$/.test(event.sequence)
              && BigInt(event.sequence) > receivedSequence) receivedSequence = BigInt(event.sequence);
          }
        }
      });
    });
    async function admitPublishedFile(version: number) {
      await waitForBoardDelivery(context.request, board);
      const syncResponse = await context.request.get(`/boards/${board}/sync`); expect(syncResponse.status()).toBe(200);
      const sync = await syncResponse.json(); expect(sync).toMatchObject({ hasMore: false, pending: false, resetRequired: false });
      expect(sync.cursor).toMatch(/^[0-9]{1,19}$/);
      // Server readiness alone does not prove that the browser received the
      // final scan/publication events that trigger protected Card revalidation.
      await expect.poll(() => receivedSequence >= BigInt(sync.cursor), { timeout: 20_000 }).toBe(true);
      await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
      await expect.poll(admittedCardVersion).toBe(version);
    }
    await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    const original = Buffer.concat([Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==', 'base64'), Buffer.from(rejected ? 'STRATAAI_CI_HARMLESS_REJECT_FIXTURE' : 'PRIVATE BROWSER ORIGINAL TRAILING METADATA')]);
    const name = `Browser original ${width}.png`, uploadPath = `/cards/${card}/attachments`;
    const writes: Record<string, string>[] = [];
    page.on('request', request => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === uploadPath) writes.push(request.headers());
    });
    await pressAdmittedAction(page.getByRole('button', { name: 'Add file attachment', exact: true }));
    const input = page.getByLabel('File to attach', { exact: true }); await expect(input).toBeEnabled();
    await input.setInputFiles({ name, mimeType: 'image/png', buffer: original });
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    const lost = loseReply ? await loseUploadReply(context, page, uploadPath) : undefined;
    const uploadResponsePromise = lost ? lost.reply : page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === uploadPath)
      .then(async response => { expect(response.status()).toBe(200); return response.json(); });
    await pressAdmittedAction(page.getByRole('button', { name: 'Upload selected file', exact: true }));
    const uploaded = await uploadResponsePromise as { cardVersion: number; attachment: { id: string } };
    expect(uploaded).toMatchObject({ cardVersion: 2, attachment: { displayName: name, mimeType: 'image/png', sizeBytes: original.length, version: 1, scanStatus: 1 } });
    expect(uploaded.attachment.id).toMatch(/^[0-9a-f-]{36}$/);
    // Chromium's request-body mirror omits File bodies. Prove full byte identity
    // through admitted size/hash and the actual browser download below.
    expect(writes).toHaveLength(1);
    expect(writes[0]).toMatchObject({ 'content-type': 'application/octet-stream', 'x-card-version': '1',
      'x-attachment-size': String(original.length), 'x-attachment-name': Buffer.from(name).toString('base64'),
      'x-attachment-sha256': createHash('sha256').update(original).digest('hex') });
    expect(writes[0]['idempotency-key']).toMatch(/^[0-9a-f-]{36}$/);
    if (loseReply) await expect(page.getByRole('button', { name: 'Retry original file upload', exact: true })).toBeEnabled();
    else await expect(page.getByText('File attached. Safety scan pending.', { exact: true })).toBeVisible();
    if (rejected) {
      await expect.poll(async () => {
        const response = await context.request.get(uploadPath); expect(response.status()).toBe(200);
        const current = await response.json();
        return current.items.some((item: { id: string; scanStatus: number }) => item.id === uploaded.attachment.id && item.scanStatus === 3);
      }, { timeout: 90_000 }).toBe(true);
      await admitPublishedFile(3);
      await pressAdmittedAction(page.getByRole('button', { name: 'Show attachments', exact: true }));
      const group = page.getByRole('group', { name: `File attachment ${name}`, exact: true }); await expect(group).toBeVisible();
      await expect(group.getByText('File rejected by the safety scan. File access is unavailable.', { exact: true })).toBeVisible();
      await expect(group.getByRole('button', { name: 'Show image preview', exact: true })).toHaveCount(0);
      await expect(group.getByRole('button', { name: 'Check file download access', exact: true })).toHaveCount(0);
      await expect(group.getByRole('link')).toHaveCount(0); await expect(group.getByRole('img')).toHaveCount(0);
      const currentResponse = await context.request.get(uploadPath); expect(currentResponse.status()).toBe(200);
      const current = await currentResponse.json(); expect(current.cardVersion).toBe(3); expect(current.items).toHaveLength(1);
      expect(current.items[0]).toMatchObject({ id: uploaded.attachment.id, version: 2, scanStatus: 3 });
      expect(current.items[0].scannedAt).toBeTruthy(); expect(current.items[0]).not.toHaveProperty('storageKey');
      for (const suffix of ['download-options', 'download', 'preview']) {
        const denied = await context.request.get(`${uploadPath}/${uploaded.attachment.id}/${suffix}`);
        expect(denied.status()).toBe(404); expect(denied.headers()['content-disposition']).toBeUndefined();
        expect((await denied.body()).includes(Buffer.from('STRATAAI_CI_HARMLESS_REJECT_FIXTURE'))).toBe(false);
      }
      const candidatesResponse = await context.request.get(`/cards/${card}/cover/candidates`); expect(candidatesResponse.status()).toBe(200);
      expect((await candidatesResponse.json()).items).toEqual([]);
      const coverResponse = await context.request.get(`/cards/${card}/cover`); expect(coverResponse.status()).toBe(200);
      const beforeCover = await coverResponse.json();
      expect(beforeCover).toMatchObject({ cardVersion: 3, attachmentId: null, attachmentVersion: null });
      const beforeSyncResponse = await context.request.get(`/boards/${board}/sync`); expect(beforeSyncResponse.status()).toBe(200);
      const beforeSync = await beforeSyncResponse.json();
      expect(beforeSync).toMatchObject({ pending: false, hasMore: false, resetRequired: false });
      // UI exclusion is not authorization. Submit both the real Rejected
      // revision and a forged published revision through the ordinary API.
      for (const attachmentVersion of [2, 3]) {
        const refused = await context.request.put(`/cards/${card}/cover`, {
          headers: { ...headers, 'Idempotency-Key': randomUUID() },
          data: { cardVersion: 3, attachmentId: uploaded.attachment.id, attachmentVersion, publicVisibilityConfirmed: false },
        });
        expect(refused.status()).toBe(404); expect(await refused.json()).toMatchObject({ code: 'card_not_found' });
      }
      const afterCoverResponse = await context.request.get(`/cards/${card}/cover`); expect(afterCoverResponse.status()).toBe(200);
      expect(await afterCoverResponse.json()).toEqual(beforeCover);
      const afterAttachmentResponse = await context.request.get(uploadPath); expect(afterAttachmentResponse.status()).toBe(200);
      expect(await afterAttachmentResponse.json()).toEqual(current);
      const afterSyncResponse = await context.request.get(`/boards/${board}/sync`); expect(afterSyncResponse.status()).toBe(200);
      const afterSync = await afterSyncResponse.json();
      expect(afterSync).toMatchObject({ cursor: beforeSync.cursor, pending: false, hasMore: false, resetRequired: false });
      expect((await context.request.get(`/cards/${card}/cover/image`)).status()).toBe(404);
      expect(writes).toHaveLength(1);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      return;
    }
    await expect.poll(async () => {
      const response = await context.request.get(`/cards/${card}/cover/candidates`); expect(response.status()).toBe(200);
      return (await response.json()).items.some((item: { attachmentId: string; attachmentVersion: number }) => item.attachmentId === uploaded.attachment.id && item.attachmentVersion === 3);
    }, { timeout: 90_000 }).toBe(true);
    await admitPublishedFile(4);
    if (loseReply) {
      await expect(input).toBeDisabled();
      for (const name of ['Save card', 'Manage attachments', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
      const retry = page.getByRole('button', { name: 'Retry original file upload', exact: true }); await expect(retry).toBeFocused();
      const recoveredResponsePromise = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === uploadPath);
      await pressAdmittedAction(retry);
      const recoveredResponse = await recoveredResponsePromise; expect(recoveredResponse.status()).toBe(200);
      expect(await recoveredResponse.json()).toEqual(uploaded);
      await expect(page.getByText('File attached. Safety scan pending.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2);
      for (const name of ['idempotency-key', 'content-type', 'x-card-version', 'x-attachment-size', 'x-attachment-name', 'x-attachment-sha256']) expect(writes[1][name]).toBe(writes[0][name]);
      const currentResponse = await context.request.get(uploadPath); expect(currentResponse.status()).toBe(200);
      const current = await currentResponse.json(); expect(current.cardVersion).toBe(4); expect(current.items).toHaveLength(1);
      expect(current.items[0]).toMatchObject({ id: uploaded.attachment.id, version: 3, scanStatus: 2 });
      await expect.poll(admittedCardVersion).toBe(4);
    }
    await pressAdmittedAction(page.getByRole('button', { name: 'Show attachments', exact: true }));
    const group = page.getByRole('group', { name: `File attachment ${name}`, exact: true }); await expect(group).toBeVisible();
    await expect(group.getByText('Safety scan complete.', { exact: true })).toBeVisible();
    const previewPath = `${uploadPath}/${uploaded.attachment.id}/preview`;
    const previewResponsePromise = page.waitForResponse(response => new URL(response.url()).pathname === previewPath && response.request().method() === 'GET');
    await pressAdmittedAction(group.getByRole('button', { name: 'Show image preview', exact: true }));
    const previewResponse = await previewResponsePromise; expect(previewResponse.status()).toBe(200);
    const previewBytes = await previewResponse.body();
    expect(previewBytes.subarray(0, 8)).toEqual(Buffer.from('89504e470d0a1a0a', 'hex'));
    expect(previewBytes.subarray(-12)).toEqual(Buffer.from('0000000049454e44ae426082', 'hex'));
    expect(previewBytes.includes(Buffer.from('PRIVATE BROWSER ORIGINAL'))).toBe(false); expect(previewBytes).not.toEqual(original);
    const preview = group.getByRole('img', { name: `Sanitized preview of ${name}`, exact: true }); await expect(preview).toBeVisible();
    await expect.poll(() => preview.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    await pressAdmittedAction(group.getByRole('button', { name: 'Check file download access', exact: true }));
    const downloadLink = group.getByRole('link', { name: `Download ${name} (opens in a new tab)`, exact: true }); await expect(downloadLink).toBeVisible();
    await expect(downloadLink).toHaveAttribute('rel', 'noopener noreferrer'); await expect(downloadLink).toHaveAttribute('referrerpolicy', 'no-referrer');
    const downloadPromise = page.waitForEvent('download', { timeout: 20_000 });
    await pressAdmittedAction(downloadLink); const download = await downloadPromise;
    expect(download.suggestedFilename()).toBe(name); expect(await download.failure()).toBeNull();
    const downloadedPath = await download.path(); expect(downloadedPath).toBeTruthy(); expect(readFileSync(downloadedPath!)).toEqual(original);
    expect((await context.request.get(`${uploadPath}/${uploaded.attachment.id}/download`)).status()).toBe(200);
    expect(writes).toHaveLength(loseReply ? 2 : 1);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
  });
}
