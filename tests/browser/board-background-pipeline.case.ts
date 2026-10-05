import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { expect, test } from './releaseTest';
import { waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

// Explicit alternate config: never discovered by the ordinary .spec.ts suite.
// The CI shell fixture supplies a real uploaded/published image and owns the
// exact API/Worker lifecycle. Only the first committed response is dropped.
for (const width of [1280, 390]) {
  test(`PRD-04 real public consent, publication recovery and image-backed copy at ${width}px`, async ({ page, context }) => {
    test.setTimeout(150_000);
    expect(process.env.CI).toBe('true');
    const path = process.env.STRATAAI_ATTACHMENT_BROWSER_FIXTURE;
    expect(path).toBeTruthy();
    const fixture = JSON.parse(readFileSync(path!, 'utf8')) as { email: string; password: string; organizationId: string; boardId: string; cardId: string };
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
    await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
    const attempts: { key: string | undefined; body: string | null }[] = [];
    await page.route(`**/boards/${board}/background/image`, async route => {
      expect(route.request().method()).toBe('POST');
      const result = await route.fetch(); expect(result.status()).toBe(200);
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      if (attempts.length === 1) await route.abort('failed');
      else { expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]); await route.fulfill({ response: result }); }
    });
    const review = page.getByRole('button', { name: 'Review Board background images', exact: true });
    await expect(review).toBeEnabled(); await review.press('Enter');
    await page.getByRole('button', { name: 'Use Private original.png as Board background', exact: true }).press('Enter');
    const confirm = page.getByRole('button', { name: 'Confirm Board background image', exact: true });
    await expect(confirm).toBeDisabled();
    const consent = page.getByRole('checkbox', { name: 'I understand this Board background image will be publicly visible', exact: true });
    await expect(consent).toBeFocused(); await consent.press('Space');
    await expect(confirm).toBeEnabled(); await confirm.press('Enter');
    const retry = page.getByRole('button', { name: 'Retry original Board background change', exact: true });
    await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
    for (const name of ['Save card', 'Review Card cover', 'Close']) await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
    await retry.press('Enter'); await expect(page.getByText('Board background updated.', { exact: true })).toBeVisible();
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
