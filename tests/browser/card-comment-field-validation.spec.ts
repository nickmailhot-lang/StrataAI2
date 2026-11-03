import AxeBuilder from '@axe-core/playwright';
import type { Locator } from '@playwright/test';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { focusAdmittedControl, openAdmittedReview, pressAdmittedAction, pressObservedRequest } from './keyboardAdmission';

const validation = 'Enter valid comment text, up to 10000 characters.';

async function visibleFocus(control: Locator) {
  await expect(control).toBeFocused(); await expect(control).toBeInViewport();
  await expect.poll(() => control.evaluate(element => {
    const box = element.getBoundingClientRect();
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
    return hit !== null && element.contains(hit);
  })).toBe(true);
  await expect.poll(() => control.evaluate(element => {
    const fieldset = element.closest('.MuiOutlinedInput-root')?.querySelector('fieldset');
    const style = getComputedStyle(fieldset ?? element);
    const box = element.getBoundingClientRect();
    const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
    const indicated = fieldset ? parseFloat(style.borderTopWidth) >= 2
      : style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
    return document.activeElement === element && hit !== null && element.contains(hit) && indicated;
  })).toBe(true);
}

// Real production UI, cookies, API and PostgreSQL. No reply/identity routes or
// supplied receipts. The complete original card-comments recovery/redaction
// scenarios remain separate and unchanged alongside the required Board baseline.
for (const viewport of [
  { name: '320px', width: 320, height: 844, textZoom: false },
  { name: '390px', width: 390, height: 844, textZoom: false },
  { name: '768px', width: 768, height: 1024, textZoom: false },
  { name: '1280px', width: 1280, height: 844, textZoom: false },
  { name: '768px with 200% text', width: 768, height: 1024, textZoom: true },
]) {
  test(`PRD-15 native new/edit comment field validation, explicit save and return at ${viewport.name}`, async ({ page, context }, info) => {
    test.setTimeout(120_000);
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    if (viewport.textZoom) await context.addInitScript(() => {
      document.addEventListener('DOMContentLoaded', () => { document.documentElement.style.fontSize = '32px'; }, { once: true });
    });
    let restoreWorker = () => {};
    try {
      const headers = { 'X-StrataAI-Request': '1' };
      const credentials = { email: `comment-field-${viewport.width}-${viewport.textZoom}-${Date.now()}@example.test`,
        password: 'comment-field-browser-battery-horse', displayName: 'Comment field author' };
      expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
      expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      const actor = (await (await context.request.get('/me')).json()).id;
      const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Comment field Organization' } })).json()).organization.id;
      restoreWorker = scopedBoardWorker(org);
      const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Comment field Board' } })).json()).id;
      const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Comment field List' } })).json()).id;
      const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Comment field Card' } })).json()).id;
      await waitForBoardDelivery(context.request, board);
      const boardPath = `/app/${org}/boards/${board}`, cardPath = `${boardPath}/cards/${card}`, commentsPath = `/cards/${card}/comments`;
      const reads: string[] = [], errors: string[] = [], responseErrors: string[] = [];
      const writes: { method: string; path: string; key: string | undefined; csrf: string | undefined; body: string | null }[] = [];
      const receipts: { method: string; path: string; key: string | undefined; status: number; body: unknown }[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('request', request => {
        const url = new URL(request.url());
        if (url.pathname === commentsPath && request.method() === 'GET') reads.push(url.pathname + url.search);
        if ((url.pathname === commentsPath || url.pathname.startsWith(commentsPath + '/')) && request.method() !== 'GET') writes.push({
          method: request.method(), path: url.pathname, key: request.headers()['idempotency-key'],
          csrf: request.headers()['x-strataai-request'], body: request.postData(),
        });
      });
      page.on('response', response => {
        const request = response.request(), path = new URL(response.url()).pathname;
        if ((path === commentsPath || path.startsWith(commentsPath + '/')) && ['POST', 'PATCH'].includes(request.method())) {
          void response.json().then(body => receipts.push({ method: request.method(), path,
            key: request.headers()['idempotency-key'], status: response.status(), body }))
            .catch(error => responseErrors.push(String(error)));
        }
      });
      const boardReads = trackBoardReads(page, board, boardPath);
      const entryVersion = trackCardVersion(page, board, card, boardPath), detailVersion = trackCardVersion(page, board, card, cardPath);
      const cardVersion = () => Math.max(entryVersion(), detailVersion());
      await page.goto(boardPath);
      if (viewport.textZoom) expect(await page.evaluate(() => getComputedStyle(document.documentElement).fontSize)).toBe('32px');
      await expect.poll(boardReads).toBeGreaterThanOrEqual(2); await expect.poll(cardVersion).toBe(1);
      const cardLink = page.getByRole('link', { name: 'Comment field Card', exact: true });
      await focusAdmittedControl(cardLink); await cardLink.press('Enter');
      const dialog = page.getByRole('dialog', { name: 'Card details', exact: true });
      await expect(dialog).toBeVisible(); await expect.poll(cardVersion).toBe(1);
      const comments = dialog.locator('section[aria-label="Card comments"]');
      const review = comments.getByRole('button', { name: 'Review Card comments', exact: true });
      const add = comments.getByRole('button', { name: 'Add comment', exact: true });
      await openAdmittedReview(review, add);
      let commentId = '';
      for (const edit of [false, true]) {
        const field = comments.getByRole('textbox', { name: edit ? 'Edit your comment' : 'New comment', exact: true });
        const launcher = edit ? comments.getByRole('button', { name: 'Edit comment', exact: true }) : add;
        await openAdmittedReview(launcher, field); await visibleFocus(field);
        const admittedReads = [...reads], previousWrites = edit ? 1 : 0;
        for (const invalid of [' \n\t ', 'Hello\u0001']) {
          await field.fill(invalid);
          await pressAdmittedAction(comments.getByRole('button', { name: 'Save comment', exact: true }));
          await expect(field).toHaveAttribute('aria-invalid', 'true'); await expect(field).toHaveAccessibleDescription(validation);
          await expect(comments.getByRole('alert')).toHaveText(validation); await expect(field).toHaveValue(invalid);
          await visibleFocus(field); expect(reads).toEqual(admittedReads); expect(writes).toHaveLength(previousWrites);
          await expect(comments.getByRole('button', { name: 'Retry original comment change', exact: true })).toHaveCount(0);
        }
        await page.screenshot({ path: info.outputPath(edit ? 'invalid-edit-comment.png' : 'invalid-new-comment.png') });
        const content = edit ? 'Edited comment\nSecond line' : 'Repaired comment\nSecond line';
        await field.fill(edit ? '  Edited comment\r\nSecond line  ' : '  Repaired comment\r\nSecond line  ');
        await expect(field).not.toHaveAttribute('aria-invalid', 'true'); await expect(comments.getByRole('alert')).toHaveCount(0);
        await expect(field).not.toHaveAccessibleDescription(validation);
        expect(reads).toEqual(admittedReads); expect(writes).toHaveLength(previousWrites);
        await pressObservedRequest(comments.getByRole('button', { name: 'Save comment', exact: true }), () => writes.length, previousWrites + 1);
        await expect(comments.getByText(edit ? 'Comment saved.' : 'Comment added.', { exact: true })).toBeVisible();
        expect(writes).toHaveLength(previousWrites + 1);
        const command = writes[previousWrites];
        expect(command.method).toBe(edit ? 'PATCH' : 'POST'); expect(command.path).toBe(commentsPath + (edit ? '/' + commentId : ''));
        expect(command.key).toMatch(/^[0-9a-f-]{36}$/); expect(command.csrf).toBe('1');
        expect(JSON.parse(command.body!)).toEqual({ content, cardVersion: edit ? 2 : 1, ...(edit ? { version: 1 } : {}) });
        // Work commands use real cookies/CSRF. Bind author ownership to the
        // observed server receipt and the independent canonical read.
        await expect.poll(() => receipts.length).toBe(previousWrites + 1); expect(responseErrors).toEqual([]);
        expect(receipts[previousWrites]).toMatchObject({ method: command.method, path: command.path, key: command.key, status: 200,
          body: { organizationId: org, boardId: board, cardId: card, cardVersion: edit ? 3 : 2, changed: true,
            comment: { authorId: actor, content, version: edit ? 2 : 1, ...(edit ? { id: commentId } : {}) } } });
        const stored = await (await context.request.get(commentsPath)).json();
        expect(stored.cardVersion).toBe(edit ? 3 : 2); expect(stored.items).toHaveLength(1);
        expect(stored.items[0]).toMatchObject({ authorId: actor, content, version: edit ? 2 : 1, deletedAt: null, deletedBy: null });
        if (edit) { expect(stored.items[0].id).toBe(commentId); expect(stored.items[0].editedAt).not.toBeNull(); }
        else { commentId = stored.items[0].id; expect(stored.items[0].editedAt).toBeNull(); }
        await expect.poll(cardVersion).toBe(edit ? 3 : 2);
        await visibleFocus(review);
        await openAdmittedReview(review, comments.getByRole('button', { name: 'Edit comment', exact: true }));
        await expect(comments.getByText(content, { exact: true })).toBeVisible();
      }
      expect(writes).toHaveLength(2); expect(writes[1].key).not.toBe(writes[0].key);
      await expect.poll(async () => {
        const sync = await (await context.request.get(`/boards/${board}/sync`)).json();
        return sync.events.filter((event: { eventType: string }) => event.eventType.startsWith('COMMENT_')).map((event: { eventType: string }) => event.eventType);
      }, { timeout: 30_000 }).toEqual(['COMMENT_ADDED', 'COMMENT_EDITED']);
      expect((await new AxeBuilder({ page }).include('[role="dialog"]').analyze()).violations).toEqual([]);
      await page.screenshot({ path: info.outputPath('persisted-comments.png') });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await pressAdmittedAction(dialog.getByRole('button', { name: 'Close', exact: true }));
      await expect(dialog).not.toBeVisible(); await expect(page).toHaveURL(new RegExp(boardPath + '$')); await visibleFocus(cardLink);
      await page.screenshot({ path: info.outputPath('returned-board-card.png') });
      await focusAdmittedControl(cardLink); await cardLink.press('Enter');
      await openAdmittedReview(review, comments.getByRole('button', { name: 'Edit comment', exact: true }));
      await expect(comments.getByText('Edited comment\nSecond line', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(receipts).toHaveLength(2); expect(responseErrors).toEqual([]); expect(errors).toEqual([]);
    } finally { restoreWorker(); }
  });
}
