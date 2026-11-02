import AxeBuilder from '@axe-core/playwright';
import type { APIRequestContext, Locator, Page, TestInfo } from '@playwright/test';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { focusAdmittedControl, pressAdmittedAction } from './keyboardAdmission';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';

type EventRow = { eventId: string; organizationId: string; boardId: string; actorId: string; actorLabel: string;
  eventType: string; entityType: string; entityId: string; version: string; createdAt: string; metadata: object; currentBoardId: string };
type HistoryPage = { organizationId: string; kind: string; targetId: string; items: EventRow[]; nextCursor: string | null };
const labels: Record<string, string> = { CARD_UPDATED: 'updated a Card', CARD_CREATED: 'created a Card',
  BOARD_CREATED: 'created the Board', LIST_CREATED: 'created a List' };

// Observe the product's result focus. Never focus the destination or scroll it
// into view to make these assertions pass; admission applies only to actions.
async function nativeState(target: Locator) {
  return target.evaluate(element => {
    const box = element.getBoundingClientRect(), style = getComputedStyle(element);
    const x = box.x + box.width / 2, y = box.y + box.height / 2;
    return { active: document.activeElement === element, activeTag: document.activeElement?.tagName,
      focusVisible: element.matches(':focus-visible'), outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth,
      centerHit: x >= 0 && y >= 0 && x < innerWidth && y < innerHeight && element.contains(document.elementFromPoint(x, y)) };
  });
}
async function visibleFocus(target: Locator, outlineWidth = '2px') {
  await expect(target).toBeFocused(); await expect(target).toBeInViewport();
  await expect.poll(() => nativeState(target)).toMatchObject({ active: true, focusVisible: true,
    outlineStyle: 'solid', outlineWidth, centerHit: true });
}
async function captureFocus(page: Page, info: TestInfo, target: Locator, name: string, outlineWidth = '2px') {
  await visibleFocus(target, outlineWidth); const before = await nativeState(target);
  expect(before).toMatchObject({ active: true, focusVisible: true, outlineStyle: 'solid', outlineWidth, centerHit: true });
  await page.screenshot({ path: info.outputPath(name + '.png'), fullPage: false });
  const after = await nativeState(target);
  await info.attach(name + '-native-state', { body: JSON.stringify({ before, after }), contentType: 'application/json' });
  expect(after).toMatchObject({ active: true, focusVisible: true, outlineStyle: 'solid', outlineWidth, centerHit: true });
  await visibleFocus(target, outlineWidth);
}
async function readHistory(request: APIRequestContext, path: string) {
  const response = await request.get(path); expect(response.status()).toBe(200);
  return await response.json() as HistoryPage;
}
async function matchRows(history: Locator, expected: HistoryPage) {
  await expect(history.getByRole('listitem')).toHaveCount(expected.items.length);
  await expect(history.locator('time')).toHaveCount(expected.items.length);
  await expect.poll(() => history.getByRole('listitem').evaluateAll(elements => elements.map(element => ({
    caption: element.querySelector('span')?.textContent, at: element.querySelector('time')?.getAttribute('datetime'),
  })))).toEqual(expected.items.map(row => ({ caption: `${row.actorLabel} ${labels[row.eventType] ?? row.eventType.toLowerCase().replaceAll('_', ' ')}.`, at: row.createdAt })));
  expect(await history.locator('script').count()).toBe(0);
}
// Preserve the original first-page admission: reopen only the read-only newest
// page after foreground recovery; never advance an already admitted older page.
async function openFirstPage(launcher: Locator, history: Locator) {
  const newest = history.getByRole('button', { name: 'Newest activity', exact: true });
  await expect(async () => {
    if (await history.getByRole('listitem').count() === 50) return;
    const action = await launcher.isVisible() ? launcher : await newest.isVisible() ? newest : undefined;
    if (action) {
      await expect(action).toBeEnabled({ timeout: 500 }); await action.focus({ timeout: 500 });
      await expect(action).toBeFocused({ timeout: 500 }); await expect(action).toBeEnabled({ timeout: 500 });
      await action.press('Enter', { timeout: 500 });
    }
    await expect(history.getByRole('listitem')).toHaveCount(50, { timeout: 500 });
  }).toPass({ timeout: 5_000 });
}

// COMMENT-FR008/009/011, TC11/12, UXA02/03/04/05/06/08. Actual immutable
// events and cookie-authorized reads; original reconnect/revocation scenarios
// and controlled focus cases remain complete, separate files in the runtime.
for (const [width, textZoom] of [[320, 1], [390, 1], [768, 1], [1280, 1], [768, 2]] as const) {
  test(`PRD-15 canonical Board/Card history focus at ${width}px text${textZoom}x`, async ({ page, context }, info) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 900 });
    const headers = { 'X-StrataAI-Request': '1' }, caption = 'History <script>🙂';
    const errors: string[] = [], writes: string[] = []; let restoreWorker = () => {};
    const completedHistoryReads = new Map<string, number>(), transportStatuses: number[] = [];
    page.on('response', async response => {
      const url = new URL(response.url());
      if (response.request().method() === 'POST' && url.pathname === '/boards/live/negotiate')
        transportStatuses.push(response.status());
      if (response.status() === 200 && /^\/(boards|cards)\/[^/]+\/activity$/.test(url.pathname) && !url.search
        && await response.finished() === null)
        completedHistoryReads.set(url.pathname, (completedHistoryReads.get(url.pathname) ?? 0) + 1);
    });
    page.on('pageerror', error => errors.push(error.message));
    try {
      const credentials = { email: `history-focus-${width}-${textZoom}-${Date.now()}@example.test`,
        password: 'history-focus-correct-horse', displayName: caption, locale: 'en-US', timezone: 'UTC' };
      await registerNotificationAccount(context.request, credentials);
      const actor = (await (await context.request.get('/me')).json()).id;
      const organization = await context.request.post('/organizations', { headers, data: { name: 'Canonical history Organization' } });
      expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
      const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Canonical history Board', visibility: 'PRIVATE' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'History List' } });
      expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
      const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'History Card' } });
      expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
      for (let version = 1; version <= 65; version++) {
        const changed = await context.request.patch(`/cards/${card}`, { headers, data: { title: `History Card ${version}`, version } });
        expect(changed.status()).toBe(200); expect((await changed.json()).version).toBe(version + 1);
      }
      const original = await readHistory(context.request, `/cards/${card}/activity`);
      expect(original.items).toHaveLength(50); expect(original.nextCursor).not.toBeNull();
      expect(original.items.every(row => row.actorId === actor && row.actorLabel === caption && row.entityId === card
        && row.boardId === board && row.organizationId === org && row.eventType === 'CARD_UPDATED' && Object.keys(row.metadata).length === 0)).toBe(true);
      page.on('request', request => {
        const path = new URL(request.url()).pathname;
        // The original SignalR handshake is transport setup, not a Board
        // mutation. Account for this exact POST separately; every other
        // non-read business request remains forbidden in this read workflow.
        if (request.method() === 'POST' && path === '/boards/live/negotiate') return;
        if (!['GET', 'HEAD'].includes(request.method()) && /^\/(boards|cards|lists)(\/|$)/.test(path))
          writes.push(request.method() + ' ' + path);
      });
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const boardPath = `/app/${org}/boards/${board}`;
      const reads = trackBoardReads(page, board, boardPath); await page.goto(boardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      if (textZoom === 2) await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
      await expect.poll(() => page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe(textZoom === 2 ? '32px' : '16px');
      const cardLink = page.getByRole('link', { name: 'History Card 65', exact: true });
      let cardContinuation: HistoryPage | undefined;
      for (const kind of ['Board', 'Card'] as const) {
        if (kind === 'Card') await pressAdmittedAction(cardLink);
        const launcher = page.getByRole('button', { name: `Review ${kind} activity`, exact: true });
        const history = page.getByRole('region', { name: `${kind} activity`, exact: true });
        const path = `/${kind === 'Board' ? 'boards' : 'cards'}/${kind === 'Board' ? board : card}/activity`;
        await openFirstPage(launcher, history);
        // The original trace shows two initial UI reads: opening and the
        // identity-head protected reread. Observe both before judging result
        // focus; never focus that result or suppress either authority check.
        await expect.poll(() => completedHistoryReads.get(path) ?? 0).toBeGreaterThanOrEqual(2);
        await expect(history).toHaveAttribute('aria-busy', 'false');
        const first = await readHistory(context.request, path); expect(first.items).toHaveLength(50); expect(first.nextCursor).not.toBeNull();
        expect(first.organizationId).toBe(org); expect(first.kind).toBe(kind.toUpperCase()); expect(first.targetId).toBe(kind === 'Board' ? board : card);
        await matchRows(history, first);
        const older = history.getByRole('button', { name: 'Older activity', exact: true });
        const newer = history.getByRole('button', { name: 'Newer activity', exact: true });
        await captureFocus(page, info, older, `history-first-${kind}-${width}-${textZoom}`);
        const olderPath = path + '?after=' + encodeURIComponent(first.nextCursor!);
        const previous = await readHistory(context.request, olderPath); expect(previous.items.length).toBeGreaterThan(0); expect(previous.items.length).toBeLessThan(50);
        if (kind === 'Card') cardContinuation = previous;
        expect(new Set([...first.items, ...previous.items].map(row => row.eventId)).size).toBe(first.items.length + previous.items.length);
        await expect(async () => {
          if (await newer.isDisabled()) { await focusAdmittedControl(older); await older.press('Enter', { timeout: 500 }); }
          await expect(history.getByRole('listitem')).toHaveCount(previous.items.length, { timeout: 500 });
        }).toPass({ timeout: 5_000 });
        await matchRows(history, previous); await captureFocus(page, info, newer, `history-older-${kind}-${width}-${textZoom}`);
        await pressAdmittedAction(newer); await matchRows(history, first);
        await captureFocus(page, info, older, `history-returned-${kind}-${width}-${textZoom}`);
        expect(await readHistory(context.request, olderPath)).toEqual(previous);
        const scope = kind === 'Card' ? '[role="dialog"]' : '[aria-label="Board activity"]';
        expect((await new AxeBuilder({ page }).include(scope).analyze()).violations).toEqual([]);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
        await pressAdmittedAction(history.getByRole('button', { name: 'Close activity', exact: true }));
        await expect(history).toHaveCount(0); await captureFocus(page, info, launcher, `history-closed-${kind}-${width}-${textZoom}`);
      }
      await pressAdmittedAction(page.getByRole('button', { name: 'Close', exact: true }));
      await captureFocus(page, info, cardLink, `history-card-return-${width}-${textZoom}`, '3px');
      await pressAdmittedAction(cardLink); await expect(page.getByRole('textbox', { name: 'Card title', exact: true })).toHaveValue('History Card 65');
      const { nextCursor: freshCursor, ...freshRows } = await readHistory(context.request, `/cards/${card}/activity`);
      const { nextCursor: originalCursor, ...originalRows } = original;
      // Cursor ciphertext includes an expiry and data protection randomness.
      // Compare every immutable row and scope field, then independently prove
      // that both original and freshly issued tokens still select those same
      // canonical older rows. Token bytes are not an event identity.
      expect(freshRows).toEqual(originalRows); expect(cardContinuation).toBeDefined();
      for (const cursor of [originalCursor, freshCursor]) {
        expect(typeof cursor).toBe('string'); expect(cursor).toMatch(/^[A-Za-z0-9_-]{1,2048}$/);
        expect(await readHistory(context.request, `/cards/${card}/activity?after=${encodeURIComponent(cursor!)}`)).toEqual(cardContinuation);
      }
      expect(transportStatuses.length).toBeGreaterThan(0);
      expect(transportStatuses.every(status => status === 200)).toBe(true);
      expect(writes).toEqual([]); expect(errors).toEqual([]);
    } finally { restoreWorker(); }
  });
}
