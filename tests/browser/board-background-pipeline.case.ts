import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { focusAdmittedControl } from './keyboardAdmission';

// Explicit alternate config: never discovered by the ordinary .spec.ts suite.
// The CI shell fixture supplies a real uploaded/published image and owns the
// exact API/Worker lifecycle. Only the first committed response is dropped.
test.afterEach(async ({ context }) => {
  // A failed assertion must not make the next viewport inherit PUBLIC state.
  // Restore only this shell-owned fixture using current revisions and ordinary
  // authenticated commands; the failed case remains failed.
  const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE;
  expect(path).toBeTruthy();
  const fixture = JSON.parse(readFileSync(path!, 'utf8')) as { email: string; password: string; organizationId: string; boardId: string; cardId: string };
  expect(fixture.boardId).toMatch(/^[0-9a-f-]{36}$/);
  expect((await context.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' },
    data: { email: fixture.email, password: fixture.password } })).status()).toBe(200);
  // Retire a selected cover left by a failed assertion using its current
  // canonical revision. Keep the original case failed and isolate the next one.
  const coverPath = `/cards/${fixture.cardId}/cover`;
  const coverResponse = await context.request.get(coverPath); expect(coverResponse.status()).toBe(200);
  const cover = await coverResponse.json();
  if (cover.attachmentId !== null) {
    expect((await context.request.put(coverPath, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { cardVersion: cover.cardVersion, attachmentId: null, attachmentVersion: null, publicVisibilityConfirmed: false },
    })).status()).toBe(200);
    const retired = await context.request.get(coverPath); expect(retired.status()).toBe(200);
    expect(await retired.json()).toMatchObject({ cardVersion: cover.cardVersion + 1, attachmentId: null });
  }
  const read = async () => {
    const response = await context.request.get(`/boards/${fixture.boardId}`); expect(response.status()).toBe(200);
    const board = (await response.json()).board;
    expect(board).toMatchObject({ id: fixture.boardId, organizationId: fixture.organizationId, lifecycleState: 'active' });
    expect(Number.isSafeInteger(board.version) && board.version > 0).toBe(true); return board;
  };
  let board = await read();
  if (board.backgroundType !== 'COLOR' || board.backgroundValue !== null) {
    expect((await context.request.patch(`/boards/${fixture.boardId}`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { name: board.name, version: board.version, backgroundType: 'COLOR', backgroundValue: null },
    })).status()).toBe(200);
    board = await read();
  }
  if (board.visibility !== 'PRIVATE') {
    expect((await context.request.patch(`/boards/${fixture.boardId}/visibility`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { visibility: 'PRIVATE', version: board.version },
    })).status()).toBe(200);
  }
  expect(await read()).toMatchObject({ visibility: 'PRIVATE', backgroundType: 'COLOR', backgroundValue: null });
});

for (const width of [1280, 390]) {
  test(`PRD-04 real public consent, publication recovery and image-backed copy at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000);
    expect(process.env.CI).toBe('true');
    const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE;
    expect(path).toBeTruthy();
    const fixture = JSON.parse(readFileSync(path!, 'utf8')) as { email: string; password: string; organizationId: string; boardId: string; cardId: string; peer: { email: string; password: string } };
    for (const id of [fixture.organizationId, fixture.boardId, fixture.cardId]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
    await page.setViewportSize({ width, height: 844 });
    const login = await context.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' },
      data: { email: fixture.email, password: fixture.password } });
    expect(login.status()).toBe(200);
    const board = fixture.boardId, org = fixture.organizationId;
    const beforeVisibility = await context.request.get(`/boards/${board}`);
    expect(beforeVisibility.status()).toBe(200);
    const before = (await beforeVisibility.json()).board;
    expect(before.visibility).toBe('PRIVATE');
    const publicVisibility = await context.request.patch(`/boards/${board}/visibility`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { visibility: 'PUBLIC', version: before.version },
    });
    expect(publicVisibility.status()).toBe(200);
    const candidates = await context.request.get(`/cards/${fixture.cardId}/cover/candidates`);
    expect(candidates.status()).toBe(200);
    const candidate = (await candidates.json()).items.find((item: { displayName: string }) => item.displayName === 'Private original.png');
    expect(candidate).toBeTruthy();
    // UI consent cannot be the security boundary: a direct client omitting
    // consent must leave the actual public Board and its revision unchanged.
    const deniedSelection = await context.request.post(`/boards/${board}/background/image`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { cardId: fixture.cardId, attachmentId: candidate.attachmentId,
        attachmentVersion: candidate.attachmentVersion, boardVersion: before.version + 1 },
    });
    expect(deniedSelection.status()).toBe(400);
    expect(await deniedSelection.json()).toMatchObject({ code: 'background_public_confirmation_required' });
    const unchanged = await context.request.get(`/boards/${board}`);
    expect(unchanged.status()).toBe(200);
    expect((await unchanged.json()).board).toMatchObject({ version: before.version + 1,
      backgroundType: before.backgroundType, backgroundValue: before.backgroundValue });
    await waitForBoardDelivery(context.request, board);
    const cardPath = `/app/${org}/boards/${board}/cards/${fixture.cardId}`;
    const reads = trackBoardReads(page, board, cardPath);
    const admittedCardVersion = trackCardVersion(page, board, fixture.cardId, cardPath);
    await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    // The source was really uploaded, scanned and decoded by the shell-owned
    // Worker. Exercise cover disclosure/receipts without fabricating metadata
    // or image replies; only the first successful command response is lost.
    const coverPath = `/cards/${fixture.cardId}/cover`;
    const initialCoverResponse = await context.request.get(coverPath); expect(initialCoverResponse.status()).toBe(200);
    const initialCover = await initialCoverResponse.json(); expect(initialCover.attachmentId).toBeNull();
    const refusedCover = await context.request.put(coverPath, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { attachmentId: candidate.attachmentId, attachmentVersion: candidate.attachmentVersion,
        cardVersion: initialCover.cardVersion, publicVisibilityConfirmed: false },
    });
    expect(refusedCover.status()).toBe(400);
    expect(await refusedCover.json()).toMatchObject({ code: 'cover_public_confirmation_required' });
    const coverAfterRefusal = await context.request.get(coverPath); expect(coverAfterRefusal.status()).toBe(200);
    expect(await coverAfterRefusal.json()).toEqual(initialCover);
    const coverWrites: { key: string | undefined; body: string | null }[] = [];
    await page.route('**' + coverPath, async route => {
      if (route.request().method() !== 'PUT') return route.continue();
      coverWrites.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      const result = await route.fetch(); expect(result.status()).toBe(200);
      if (coverWrites.length === 1) await route.abort('failed');
      else await route.fulfill({ response: result });
    });
    const coverReview = page.getByRole('button', { name: 'Review Card cover', exact: true });
    await focusAdmittedControl(coverReview); await page.keyboard.press('Enter');
    await focusAdmittedControl(page.getByRole('button', { name: 'Use Private original.png as cover', exact: true })); await page.keyboard.press('Enter');
    const coverConfirm = page.getByRole('button', { name: 'Confirm Card cover', exact: true });
    await expect(coverConfirm).toBeDisabled();
    const coverConsent = page.getByRole('checkbox', { name: 'I understand this cover image will be publicly visible', exact: true });
    await expect(coverConsent).toBeFocused(); await focusAdmittedControl(coverConsent); await page.keyboard.press('Space');
    await focusAdmittedControl(coverConfirm); await page.keyboard.press('Enter');
    const coverRetry = page.getByRole('button', { name: 'Retry original cover change', exact: true });
    await expect(coverRetry).toBeEnabled(); await expect(coverRetry).toBeFocused();
    await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(initialCover.cardVersion + 1);
    for (const name of ['Save card', 'Manage attachments', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await focusAdmittedControl(coverRetry); await page.keyboard.press('Enter'); await expect(page.getByText('Card cover updated.', { exact: true })).toBeVisible();
    expect(coverWrites).toHaveLength(2); expect(coverWrites[1]).toEqual(coverWrites[0]);
    expect(coverWrites[0].key).toMatch(/^[0-9a-f-]{36}$/i);
    expect(JSON.parse(coverWrites[0].body!)).toEqual({ attachmentId: candidate.attachmentId,
      attachmentVersion: candidate.attachmentVersion, cardVersion: initialCover.cardVersion, publicVisibilityConfirmed: true });
    const coverImage = page.getByRole('img', { name: 'Card cover', exact: true }); await expect(coverImage).toBeVisible();
    await expect.poll(() => coverImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    await expect(coverReview).toBeFocused();
    const selectedCoverResponse = await context.request.get(coverPath); expect(selectedCoverResponse.status()).toBe(200);
    expect(await selectedCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 1,
      attachmentId: candidate.attachmentId, attachmentVersion: candidate.attachmentVersion });
    await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(initialCover.cardVersion + 1);
    const visitor = await browser.newContext({ baseURL: new URL(page.url()).origin });
    try {
      const publicCoverImage = await visitor.request.get(coverPath + '/image'); expect(publicCoverImage.status()).toBe(200);
      expect(publicCoverImage.headers()['content-type']).toBe('image/png');
      const coverBytes = await publicCoverImage.body(); expect(coverBytes.subarray(0, 8)).toEqual(Buffer.from('89504e470d0a1a0a', 'hex'));
      expect(coverBytes.includes(Buffer.from('PRIVATE ORIGINAL'))).toBe(false);
      expect((await visitor.request.get(coverPath)).status()).toBe(401);
    } finally { await visitor.close(); }
    // A distinct admitted Board member must retire already-rendered
    // disclosure through actual Worker delivery, without navigation or reload.
    const peerContext = await browser.newContext({ baseURL: new URL(page.url()).origin, viewport: { width, height: 844 } });
    try {
      expect((await peerContext.request.post('/auth/login', { headers: { 'X-StrataAI-Request': '1' },
        data: { email: fixture.peer.email, password: fixture.peer.password } })).status()).toBe(200);
      const ownerProfile = await context.request.get('/me'); expect(ownerProfile.status()).toBe(200);
      const peerProfile = await peerContext.request.get('/me'); expect(peerProfile.status()).toBe(200);
      expect((await peerProfile.json()).id).not.toBe((await ownerProfile.json()).id);
      await waitForBoardDelivery(context.request, board);
      const peer = await peerContext.newPage(); let peerSequence = 0n;
      peer.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/boards/live') return;
        socket.on('framereceived', frame => {
          for (const raw of frame.payload.toString().split('\x1e').filter(Boolean)) {
            const message = JSON.parse(raw);
            if (message.type !== 2 || !Array.isArray(message.item?.events)) continue;
            for (const event of message.item.events) {
              if (event.organizationId === org && event.boardId === board && typeof event.sequence === 'string'
                && /^[0-9]{1,19}$/.test(event.sequence) && BigInt(event.sequence) > peerSequence) peerSequence = BigInt(event.sequence);
            }
          }
        });
      });
      await peer.goto(cardPath);
      const peerImage = peer.getByRole('img', { name: 'Card cover', exact: true }); await expect(peerImage).toBeVisible();
      await expect.poll(() => peerImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
      await expect(peer.getByText('Live updates connected.', { exact: true })).toBeVisible();
      let peerNavigations = 0; peer.on('framenavigated', frame => { if (frame === peer.mainFrame()) peerNavigations++; });
      const priorSyncResponse = await context.request.get(`/boards/${board}/sync`); expect(priorSyncResponse.status()).toBe(200);
      const priorSync = await priorSyncResponse.json(); expect(priorSync).toMatchObject({ hasMore: false, pending: false, resetRequired: false });
      expect(priorSync.cursor).toMatch(/^[0-9]{1,19}$/);
      await focusAdmittedControl(coverReview); await page.keyboard.press('Enter');
      await focusAdmittedControl(page.getByRole('button', { name: 'Remove Card cover', exact: true })); await page.keyboard.press('Enter');
      await expect(coverConsent).toHaveCount(0);
      await focusAdmittedControl(page.getByRole('button', { name: 'Confirm cover removal', exact: true })); await page.keyboard.press('Enter');
      await expect(page.getByText('Card cover removed.', { exact: true })).toBeVisible(); await expect(coverImage).toHaveCount(0);
      await expect(coverReview).toBeFocused(); expect(coverWrites).toHaveLength(3);
      const removedCoverResponse = await context.request.get(coverPath); expect(removedCoverResponse.status()).toBe(200);
      expect(await removedCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 2, attachmentId: null });
      await expect.poll(admittedCardVersion).toBeGreaterThanOrEqual(initialCover.cardVersion + 2);
      await waitForBoardDelivery(context.request, board);
      await expect.poll(() => peerSequence > BigInt(priorSync.cursor), { timeout: 20_000 }).toBe(true);
      await expect(peerImage).toHaveCount(0, { timeout: 20_000 }); await expect(peer).toHaveURL(new RegExp(`/cards/${fixture.cardId}$`));
      expect(peerNavigations).toBe(0);
      const peerCoverResponse = await peerContext.request.get(coverPath); expect(peerCoverResponse.status()).toBe(200);
      expect(await peerCoverResponse.json()).toMatchObject({ cardVersion: initialCover.cardVersion + 2, attachmentId: null });
      expect((await peerContext.request.get(coverPath + '/image')).status()).toBe(404);
      expect((await context.request.get(coverPath + '/image')).status()).toBe(404);
    } finally { await peerContext.close(); }
    await page.unroute('**' + coverPath);
    const attempts: { key: string | undefined; body: string | null }[] = [];
    await page.route(`**/boards/${board}/background/image`, async route => {
      expect(route.request().method()).toBe('POST');
      const result = await route.fetch(); expect(result.status()).toBe(200);
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      if (attempts.length === 1) await route.abort('failed');
      else { expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); await route.fulfill({ response: result }); }
    });
    const review = page.getByRole('button', { name: 'Review Board background images', exact: true });
    await focusAdmittedControl(review); await page.keyboard.press('Enter');
    await focusAdmittedControl(page.getByRole('button', { name: 'Use Private original.png as Board background', exact: true })); await page.keyboard.press('Enter');
    const confirm = page.getByRole('button', { name: 'Confirm Board background image', exact: true });
    await expect(confirm).toBeDisabled();
    const consent = page.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible', exact: true });
    await expect(consent).toBeFocused(); await focusAdmittedControl(consent); await page.keyboard.press('Space');
    await focusAdmittedControl(confirm); await page.keyboard.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original Board background change', exact: true });
    await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
    for (const name of ['Save card', 'Review Card cover', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await focusAdmittedControl(retry); await page.keyboard.press('Enter'); await expect(page.getByText('Board background updated.', { exact: true })).toBeVisible();
    await expect(review).toBeFocused(); expect(attempts).toHaveLength(2);
    expect(JSON.parse(attempts[0].body!)).toMatchObject({ publicVisibilityConfirmed: true, boardVersion: before.version + 1 });
    const sourceResponse = await context.request.get(`/boards/${board}`); expect(sourceResponse.status()).toBe(200);
    const source = (await sourceResponse.json()).board;
    expect(source.backgroundType).toBe('IMAGE'); expect(source.backgroundValue).toMatch(/^[0-9a-f-]{36}$/);
    const image = page.locator(`img[src="/boards/${board}/background/image?boardVersion=${source.version}"]`);
    await expect.poll(() => image.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    await expect(image).toHaveAttribute('alt', '');
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    // Wait for native actionability through the current Card/Board read. A
    // keypress can target a control that became disabled since an assertion.
    const boardPath = `/app/${org}/boards/${board}`;
    const closedDetailReads = trackBoardReads(page, board, boardPath);
    await page.getByRole('button', { name: 'Close', exact: true }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    // Closing detail only changes the route. Explicitly refresh its Board so
    // readiness is based on a completed read, rather than an assumed request.
    await page.getByRole('button', { name: 'Refresh board', exact: true }).click();
    await expect.poll(closedDetailReads).toBeGreaterThanOrEqual(1);
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true })).toHaveAttribute('aria-busy', 'false');
    const copy = page.getByRole('button', { name: 'Copy Board', exact: true }); await copy.click();
    await expect(page.getByRole('dialog', { name: 'Copy Board', exact: true })).toBeVisible();
    let copyWrites = 0;
    page.on('request', request => { if (request.method() === 'POST'
      && new URL(request.url()).pathname === `/boards/${board}/copy`) copyWrites++; });
    const copyResponse = page.waitForResponse(response => response.request().method() === 'POST'
      && new URL(response.url()).pathname === `/boards/${board}/copy`);
    // A current Board read can disable confirmation after an enabled assertion.
    // Native click waits for actual actionability before dispatching one command.
    const create = page.getByRole('button', { name: 'Create Board copy', exact: true }); await create.click();
    const copyReceipt = await copyResponse; expect(copyReceipt.status()).toBe(201); expect(copyWrites).toBe(1);
    const acknowledgedCopy = await copyReceipt.json();
    const open = page.getByRole('link', { name: 'Open copied Board', exact: true }); await expect(open).toBeVisible(); await expect(open).toBeFocused();
    const href = await open.getAttribute('href'); const target = href!.split('/').at(-1)!; expect(target).not.toBe(board);
    expect(acknowledgedCopy).toMatchObject({ id: target, organizationId: org, visibility: 'PRIVATE', version: 1, backgroundType: 'IMAGE' });
    await waitForBoardDelivery(context.request, target);
    const copiedResponse = await context.request.get(`/boards/${target}`); expect(copiedResponse.status()).toBe(200);
    const copied = (await copiedResponse.json()).board;
    expect(copied).toMatchObject({ visibility: 'PRIVATE', backgroundType: 'IMAGE', version: 1 });
    expect(copied.backgroundValue).not.toBe(source.backgroundValue);
    await open.press('Enter'); await expect(page).toHaveURL(new RegExp(`/boards/${target}$`));
    const copiedImage = page.locator(`img[src="/boards/${target}/background/image?boardVersion=1"]`);
    await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect((await new AxeBuilder({ page }).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations).toEqual([]);
    // Retire the source selection through the actual command, then discard all
    // browser state. The destination must still admit its independent owner.
    const cleared = await context.request.patch(`/boards/${board}`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { name: source.name, version: source.version, backgroundType: 'COLOR', backgroundValue: null },
    });
    expect(cleared.status()).toBe(200);
    expect(await cleared.json()).toMatchObject({ version: source.version + 1, backgroundType: 'COLOR', backgroundValue: null });
    expect((await context.request.get(`/boards/${board}/background/image`)).status()).toBe(404);
    const copiedPath = `/app/${org}/boards/${target}`;
    const freshReads = trackBoardReads(page, target, copiedPath);
    await page.reload(); await expect.poll(freshReads).toBeGreaterThanOrEqual(2);
    await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
    await expect(page.getByRole('button', { name: 'Copy Board', exact: true })).toBeEnabled();
    const retained = await context.request.get(`/boards/${target}`);
    expect(retained.status()).toBe(200);
    expect((await retained.json()).board).toMatchObject({ version: 1, backgroundType: 'IMAGE', backgroundValue: copied.backgroundValue });
    // Restore the source visibility for the next independent viewport case
    // and the subsequent private HTTP lifecycle/concurrency assertions.
    const privateVisibility = await context.request.patch(`/boards/${board}/visibility`, {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { visibility: 'PRIVATE', version: source.version + 1 },
    });
    expect(privateVisibility.status()).toBe(200);
    // The destination is still open with genuine private image bytes. A real
    // session withdrawal must retire rendered disclosure without a reload.
    await expect(page.getByText('Live updates connected.', { exact: true })).toBeVisible();
    expect((await context.request.post('/auth/logout', {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() }, data: {},
    })).status()).toBe(204);
    await expect(copiedImage).toHaveCount(0, { timeout: 10_000 });
    await expect(page.getByRole('button', { name: 'Copy Board', exact: true })).toHaveCount(0);
    expect((await context.request.get(`/boards/${target}/background/image`)).status()).toBe(404);
    expect((await context.request.post('/auth/login', {
      headers: { 'X-StrataAI-Request': '1', 'Idempotency-Key': randomUUID() },
      data: { email: fixture.email, password: fixture.password },
    })).status()).toBe(200);
    const readmitted = await context.request.get(`/boards/${target}`); expect(readmitted.status()).toBe(200);
    expect((await readmitted.json()).board).toEqual(copied);
    const admittedReads = trackBoardReads(page, target, copiedPath);
    await page.goto(copiedPath); await expect.poll(admittedReads).toBeGreaterThanOrEqual(2);
    await expect.poll(() => copiedImage.evaluate(node => (node as HTMLImageElement).naturalWidth)).toBe(1);
  });
}
