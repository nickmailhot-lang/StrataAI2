import AxeBuilder from '@axe-core/playwright';
import type { Locator } from '@playwright/test';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { focusAdmittedControl, openAdmittedReview, pressAdmittedAction, pressObservedRequest } from './keyboardAdmission';

async function visibleButtonFocus(control: Locator) {
  await expect(control).toBeFocused(); await expect(control).toBeInViewport();
  await expect.poll(() => control.evaluate(element => {
    const box = element.getBoundingClientRect(), style = getComputedStyle(element);
    return document.activeElement === element && element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2))
      && style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
  })).toBe(true);
}

// Existing picker focus implementation; real production UI/cookies/API/Postgres.
// Hold only an actual successful lookup response to observe asynchronous ownership.
// No supplied response/identity/receipt; original mention recovery files stay complete.
for (const viewport of [
  { width: 320, height: 844, zoom: false }, { width: 390, height: 844, zoom: false },
  { width: 768, height: 1024, zoom: false }, { width: 1280, height: 844, zoom: false },
  { width: 768, height: 1024, zoom: true },
]) {
  test(`PRD-15 owned lookup return, independent focus and persisted selection at ${viewport.width}px${viewport.zoom ? ' with 200% text' : ''}`, async ({ page, context, browser }, info) => {
    test.setTimeout(150_000);
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    if (viewport.zoom) await context.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => { document.documentElement.style.fontSize = '32px'; }, { once: true });
    });
    const recipientContext = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {}; let releaseLookup = () => {};
    try {
      const headers = { 'X-StrataAI-Request': '1' }, suffix = `${viewport.width}-${viewport.zoom}-${Date.now()}`;
      const email = `lookup-recipient-${suffix}@example.test`;
      for (const [index, client] of [context, recipientContext].entries()) await registerNotificationAccount(client.request, {
        email: index ? email : `lookup-author-${suffix}@example.test`, password: 'lookup-browser-battery-horse',
        displayName: index ? 'Lookup teammate' : 'Lookup author',
      });
      const actor = (await (await context.request.get('/me')).json()).id;
      const recipient = (await (await recipientContext.request.get('/me')).json()).id;
      const original = await (await recipientContext.request.get('/me/mention-handle')).json();
      const handle = `lookup_${viewport.width}_${Date.now()}`;
      expect((await recipientContext.request.patch('/me/mention-handle', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { handle, userVersion: original.userVersion, handleVersion: original.handleVersion } })).status()).toBe(200);
      const currentHandle = await (await recipientContext.request.get('/me/mention-handle')).json();
      expect(currentHandle.handle).toBe(handle);
      const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Lookup focus Organization' } });
      expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await recipientContext.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      restoreWorker = scopedBoardWorker(org);
      const boardResult = await context.request.post('/boards', { headers,
        data: { organizationId: org, name: 'Lookup focus Board', visibility: 'PRIVATE' } });
      expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
      expect((await context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Lookup focus List' } })).json()).id;
      const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Lookup focus Card' } })).json()).id;
      await waitForBoardDelivery(context.request, board);
      const boardPath = `/app/${org}/boards/${board}`, cardPath = `${boardPath}/cards/${card}`;
      const optionsPath = `/cards/${card}/mention-options`, commentsPath = `/cards/${card}/comments`;
      const lookups: { prefix: string | null; after: string | null }[] = [];
      const lookupReceipts: unknown[] = [], receipts: unknown[] = [], errors: string[] = [], responseErrors: string[] = [];
      const writes: { path: string; method: string; key: string | undefined; csrf: string | undefined; body: string | null }[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => {
        const url = new URL(request.url());
        if (url.pathname === optionsPath && request.method() === 'GET') lookups.push({ prefix: url.searchParams.get('prefix'), after: url.searchParams.get('after') });
        if ((url.pathname === commentsPath || url.pathname.startsWith(commentsPath + '/')) && request.method() !== 'GET') writes.push({
          path: url.pathname, method: request.method(), key: request.headers()['idempotency-key'],
          csrf: request.headers()['x-strataai-request'], body: request.postData(),
        });
      });
      page.on('response', response => {
        const path = new URL(response.url()).pathname;
        const destination = path === optionsPath ? lookupReceipts : path === commentsPath && response.request().method() === 'POST' ? receipts : undefined;
        if (destination) void response.json().then(body => destination.push({ status: response.status(), body })).catch(error => responseErrors.push(String(error)));
      });
      const reads = trackBoardReads(page, board, boardPath);
      const entryVersion = trackCardVersion(page, board, card, boardPath), detailVersion = trackCardVersion(page, board, card, cardPath);
      await page.goto(boardPath);
      if (viewport.zoom) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
      await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const cardLink = page.getByRole('link', { name: 'Lookup focus Card', exact: true });
      await focusAdmittedControl(cardLink); await cardLink.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Card details', exact: true });
      const comments = dialog.locator('section[aria-label="Card comments"]');
      const review = comments.getByRole('button', { name: 'Review Card comments', exact: true });
      const add = comments.getByRole('button', { name: 'Add comment', exact: true });
      await openAdmittedReview(review, add);
      const picker = comments.locator('section[aria-label="Mention a teammate"]');
      const prefix = picker.getByRole('textbox', { name: 'Teammate username prefix', exact: true });
      await openAdmittedReview(add, prefix);
      const find = picker.getByRole('button', { name: 'Find teammates', exact: true });
      const field = comments.getByRole('textbox', { name: 'New comment', exact: true });
      const close = dialog.getByRole('button', { name: 'Close', exact: true });
      const independentDestination = dialog.locator('summary').filter({ hasText: /^Appearance actions$/ });
      const choice = picker.getByRole('button', { name: `Mention Lookup teammate (@${handle})`, exact: true });
      async function captureOwnedFocus(control: Locator, name: string) {
        const sample = () => control.evaluate(element => {
          const box = element.getBoundingClientRect(), style = getComputedStyle(element);
          return { active: document.activeElement === element, focusVisible: element.matches(':focus-visible'),
            activeTag: document.activeElement?.tagName, outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth,
            centerHit: element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2)) };
        });
        const before = await sample(); await page.screenshot({ path: info.outputPath(name + '.png') });
        const after = await sample();
        await info.attach(name + '-native-state', { body: Buffer.from(JSON.stringify({ before, after })), contentType: 'application/json' });
        expect(after.active && after.centerHit && after.focusVisible && after.outlineStyle !== 'none' && parseFloat(after.outlineWidth) > 0).toBe(true);
        await visibleButtonFocus(control);
      }
      let actualResponsesHeld = 0;
      await page.route('**' + optionsPath + '?*', async route => {
        expect(route.request().method()).toBe('GET');
        const actual = await route.fetch(); expect(actual.status()).toBe(200);
        actualResponsesHeld++;
        await new Promise<void>(resolve => { releaseLookup = resolve; });
        await route.fulfill({ response: actual });
      });
      for (const independent of [false, true]) {
        const count = independent ? 2 : 1;
        const literal = `  ${handle.toUpperCase()}  `; expect(literal.length).toBeLessThanOrEqual(40);
        await prefix.fill(literal); expect(lookups).toHaveLength(count - 1); expect(writes).toEqual([]);
        await pressObservedRequest(find, () => lookups.length, count);
        await expect.poll(() => actualResponsesHeld).toBe(count);
        await expect(find).toBeDisabled(); await expect(close).toBeDisabled(); await expect(prefix).toHaveValue(literal);
        await expect(picker.getByRole('status')).toHaveText('Checking current teammates…');
        expect(writes).toEqual([]); await expect(choice).toHaveCount(0);
        if (independent) { await focusAdmittedControl(independentDestination); await visibleButtonFocus(independentDestination); }
        if (independent) await captureOwnedFocus(independentDestination, 'lookup-deliberate-focus-pending');
        else await page.screenshot({ path: info.outputPath('lookup-owned-focus-pending.png') });
        releaseLookup();
        await expect(choice).toBeEnabled(); await expect(prefix).toHaveValue(handle);
        await expect.poll(() => lookupReceipts.length).toBe(count);
        await visibleButtonFocus(independent ? independentDestination : find);
        await captureOwnedFocus(independent ? independentDestination : find, independent ? 'lookup-deliberate-focus-preserved' : 'lookup-owned-focus-returned');
        expect(writes).toEqual([]);
      }
      expect(lookups).toEqual([{ prefix: handle, after: null }, { prefix: handle, after: null }]);
      for (const receipt of lookupReceipts) expect(receipt).toMatchObject({ status: 200, body: { organizationId: org, boardId: board, cardId: card,
        cardVersion: 1, prefix: handle, items: [{ userId: recipient, handle, handleVersion: currentHandle.handleVersion, displayName: 'Lookup teammate' }], nextCursor: null } });
      await pressAdmittedAction(choice);
      await expect(field).toHaveValue(`@${handle}`); await expect(field).toBeFocused(); expect(writes).toEqual([]);
      await expect.poll(() => field.evaluate(element => {
        const label = element.closest('.MuiFormControl-root')?.querySelector('label');
        return label ? getComputedStyle(label).transform : '';
      })).toBe('matrix(0.75, 0, 0, 0.75, 14, -9)');
      await page.screenshot({ path: info.outputPath('selected-lookup-draft.png') });
      await pressObservedRequest(comments.getByRole('button', { name: 'Save comment', exact: true }), () => writes.length, 1);
      await expect(comments.getByText('Comment added.', { exact: true })).toBeVisible(); expect(writes).toHaveLength(1);
      expect(writes[0]).toMatchObject({ path: commentsPath, method: 'POST', csrf: '1' }); expect(writes[0].key).toMatch(/^[0-9a-f-]{36}$/);
      expect(JSON.parse(writes[0].body!)).toEqual({ content: `@${handle}`, cardVersion: 1,
        mentionSelections: [{ userId: recipient, handle, handleVersion: currentHandle.handleVersion }] });
      await expect.poll(() => receipts.length).toBe(1);
      expect(receipts[0]).toMatchObject({ status: 200, body: { organizationId: org, boardId: board, cardId: card,
        cardVersion: 2, changed: true, comment: { authorId: actor, content: `@${handle}`, version: 1 } } });
      const stored = await (await context.request.get(commentsPath)).json();
      expect(stored.cardVersion).toBe(2); expect(stored.items).toHaveLength(1);
      expect(stored.items[0]).toMatchObject({ authorId: actor, content: `@${handle}`, version: 1, deletedAt: null });
      await expect.poll(() => Math.max(entryVersion(), detailVersion())).toBe(2);
      await openAdmittedReview(review, comments.getByRole('button', { name: 'Edit comment', exact: true }));
      await expect(comments.getByText(`@${handle}`, { exact: true })).toBeVisible();
      await expect.poll(async () => {
        const inbox = await recipientContext.request.get(`/organizations/${org}/notifications`);
        expect(inbox.headers()['cache-control']).toContain('no-store');
        return (await inbox.json()).items.filter((item: { type: string; entityId: string }) => item.type === 'MENTION_CREATED' && item.entityId === card);
      }, { timeout: 25_000 }).toEqual([expect.objectContaining({ type: 'MENTION_CREATED', recipientId: recipient, entityId: card, entityLink: cardPath })]);
      expect((await (await context.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
      expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: info.outputPath('persisted-selected-comment.png') });
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Close', exact: true }));
      await expect(dialog).not.toBeVisible(); await expect(page).toHaveURL(new RegExp(boardPath + '$')); await visibleButtonFocus(cardLink);
      await page.screenshot({ path: info.outputPath('returned-prefix-card.png') });
      await focusAdmittedControl(cardLink); await cardLink.press('Enter');
      await openAdmittedReview(review, comments.getByRole('button', { name: 'Edit comment', exact: true }));
      await expect(comments.getByText(`@${handle}`, { exact: true })).toBeVisible();
      expect(lookups).toHaveLength(2); expect(writes).toHaveLength(1); expect(receipts).toHaveLength(1); expect(responseErrors).toEqual([]); expect(errors).toEqual([]);
    } finally { try { releaseLookup(); restoreWorker(); } finally { await recipientContext.close(); } }
  });
}
