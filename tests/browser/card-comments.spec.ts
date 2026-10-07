import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';

// Real comment commands/storage. Only the first already-committed HTTP reply
// is replaced with 503 to exercise recovery; command bodies and receipts are
// produced by the actual API. Execution requires the release-image fixture.
for (const width of [1280, 390]) {
  test(`PRD-15 native author commands, two-client recovery and redaction at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(120_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const credentials = { email: `comments-${width}-${Date.now()}@example.test`, password: 'comments-fixture-battery-horse', displayName: 'Comment author', locale: 'en-US', timezone: 'Pacific/Honolulu' };
    expect((await context.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
    expect((await context.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    const profile = await (await context.request.get('/me')).json(); const actor = profile.id;
    expect(profile).toMatchObject({ locale: credentials.locale, timezone: credentials.timezone });
    const org = (await (await context.request.post('/organizations', { headers, data: { name: 'Comment Organization' } })).json()).organization.id;
    const board = (await (await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Comment Board' } })).json()).id;
    const list = (await (await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Comment List' } })).json()).id;
    const card = (await (await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Comment Card' } })).json()).id;
    const path = `/cards/${card}/comments`; const writes: { key: string | undefined; body: string | null }[] = [];
    await page.route('**' + path, async route => {
      if (route.request().method() !== 'POST') return route.continue();
      writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
      const actual = await route.fetch(); expect(actual.status()).toBe(200);
      if (writes.length === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
      return route.fulfill({ response: actual });
    });
    const peer = await browser.newContext({ baseURL: new URL((await context.request.get('/me')).url()).origin, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      expect((await peer.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      const peerPage = await peer.newPage(); restoreWorker = scopedBoardWorker(org);
      await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`; const reads = trackBoardReads(page, board, cardPath);
      const cardVersion = trackCardVersion(page, board, card, cardPath);
      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const peerReads = trackBoardReads(peerPage, board, cardPath); await peerPage.goto(cardPath); await expect.poll(peerReads).toBeGreaterThanOrEqual(2);
      const peerReview = peerPage.getByRole('button', { name: 'Review Card comments', exact: true });
      await expect(peerReview).toBeEnabled(); await peerReview.press('Enter'); await expect(peerPage.getByText('No comments on this page. Add the first comment.', { exact: true })).toBeVisible();
      const review = page.getByRole('button', { name: 'Review Card comments', exact: true });
      await expect(review).toBeEnabled(); await review.press('Enter');
      await page.getByRole('button', { name: 'Add comment', exact: true }).press('Enter');
      const text = page.getByRole('textbox', { name: 'New comment', exact: true }); await expect(text).toBeFocused();
      await page.keyboard.insertText('Literal <script>🙂');
      await page.getByRole('button', { name: 'Save comment', exact: true }).press('Enter');
      const retry = page.getByRole('button', { name: 'Retry original comment change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      for (const name of ['Add checklist', 'Save card', 'Add link attachment', 'Manage attachments', 'Review Card cover', 'Close'])
        await expect(page.getByRole('button', { name, exact: true })).toBeDisabled();
      await expect.poll(cardVersion).toBe(2);
      await waitForBoardDelivery(context.request, board);
      // A received snapshot is not yet rendered admission: the current actor
      // and any queued live refresh still have to finish before retrying.
      await expect(page.getByRole('alert').filter({ hasText: 'This Card changed. Review the latest comments.' })).toBeVisible();
      // Card details is modal, so its background Board is hidden from the
      // accessibility tree while still owning the current read admission.
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      await expect(retry).toBeEnabled(); await retry.press('Enter'); await expect(page.getByText('Comment added.', { exact: true })).toBeVisible();
      await expect(peerPage.getByText('Literal <script>🙂', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      expect(JSON.parse(writes[0].body!)).toEqual({ content: 'Literal <script>🙂', cardVersion: 1 });
      const stored = (await (await context.request.get(path)).json()).items;
      expect(stored).toHaveLength(1); expect(stored[0]).toMatchObject({ authorId: actor, version: 1, content: 'Literal <script>🙂' });
      const displayed = await page.evaluate(({ instant, locale, timezone }) => new Intl.DateTimeFormat(locale, {
        timeZone: timezone, year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
      }).format(new Date(instant)), { instant: stored[0].createdAt, locale: credentials.locale, timezone: credentials.timezone });
      await expect(page.locator('section[aria-label="Card comments"]').getByText(`You · ${displayed}`, { exact: true })).toBeVisible();
      const comment = stored[0].id;
      await expect(review).toBeEnabled(); await review.press('Enter');
      await expect(page.getByText('Literal <script>🙂', { exact: true })).toBeVisible();
      await expect(page.locator('section[aria-label="Card comments"]').getByText(`You · ${displayed}`, { exact: true })).toBeVisible();
      expect(await page.locator('section[aria-label="Card comments"] script').count()).toBe(0);
      const beforePreference = await (await peer.request.get('/me')).json();
      expect((await peer.request.patch('/me', { headers, data: { timezone: 'Asia/Tokyo', version: beforePreference.version } })).status()).toBe(200);
      const updatedDisplay = await page.evaluate(({ instant, locale }) => new Intl.DateTimeFormat(locale, {
        timeZone: 'Asia/Tokyo', year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZoneName: 'short',
      }).format(new Date(instant)), { instant: stored[0].createdAt, locale: credentials.locale });
      // Both clean dialogs recover a preference changed by another session,
      // without a Review click or reload. Display changes cannot mutate history.
      for (const client of [page, peerPage]) {
        const comments = client.locator('section[aria-label="Card comments"]');
        await expect(comments.getByText(`You · ${updatedDisplay}`, { exact: true })).toBeVisible({ timeout: 20_000 });
        await expect(comments.getByText(`You · ${displayed}`, { exact: true })).toHaveCount(0);
      }
      expect((await (await context.request.get(path)).json()).items).toEqual(stored);
      expect(writes).toHaveLength(2);
      await page.getByRole('button', { name: 'Edit comment', exact: true }).press('Enter');
      const edit = page.getByRole('textbox', { name: 'Edit your comment', exact: true }); await expect(edit).toBeFocused();
      await edit.press('ControlOrMeta+A'); await page.keyboard.insertText('Edited plaintext');
      await peer.setOffline(true);
      await page.getByRole('button', { name: 'Save comment', exact: true }).press('Enter');
      await expect(page.getByText('Comment saved.', { exact: true })).toBeVisible();
      await peer.setOffline(false);
      await expect(peerPage.getByText('Edited plaintext', { exact: true })).toBeVisible({ timeout: 30_000 });
      await expect(peerPage.getByText('Literal <script>🙂', { exact: true })).toHaveCount(0);
      await expect(review).toBeEnabled(); await review.press('Enter');
      await page.getByRole('button', { name: 'Remove comment body', exact: true }).press('Enter');
      const confirm = page.getByRole('button', { name: 'Confirm comment removal', exact: true }); await expect(confirm).toBeDisabled();
      const consent = page.getByRole('checkbox', { name: 'I confirm removal of my comment body', exact: true });
      await expect(consent).toBeFocused(); await consent.press('Space'); await confirm.press('Enter');
      await expect(page.getByText('Comment body removed.', { exact: true }).first()).toBeVisible();
      await expect(peerPage.getByText('Comment body removed.', { exact: true })).toBeVisible();
      await expect(peerPage.getByText('Edited plaintext', { exact: true })).toHaveCount(0);
      await expect(review).toBeEnabled(); await review.press('Enter');
      const tombstone = (await (await context.request.get(path)).json()).items;
      expect(tombstone).toHaveLength(1); expect(tombstone[0]).toMatchObject({ id: comment, authorId: actor, content: null, version: 3, deletedBy: actor });
      expect(tombstone[0].editedAt).not.toBeNull(); expect(tombstone[0].deletedAt).not.toBeNull();
      expect(await page.getByRole('button', { name: 'Edit comment', exact: true }).count()).toBe(0);
      expect(await page.getByText('Edited plaintext', { exact: true }).count()).toBe(0);
      const oldReceipt = await context.request.post(path, { headers: { ...headers, 'Idempotency-Key': writes[0].key! }, data: JSON.parse(writes[0].body!) });
      expect(oldReceipt.status()).toBe(404); expect(await oldReceipt.text()).not.toContain('Literal');
      await expect.poll(async () => {
        const sync = await (await context.request.get(`/boards/${board}/sync`)).json();
        return sync.events.filter((event: { eventType: string }) => event.eventType.startsWith('COMMENT_')).map((event: { eventType: string }) => event.eventType);
      }, { timeout: 30_000 }).toEqual(['COMMENT_ADDED', 'COMMENT_EDITED', 'COMMENT_DELETED']);
      const snapshot = await (await context.request.get(`/boards/${board}`)).json();
      expect(snapshot.lists.flatMap((column: { cards: { id: string; version: number }[] }) => column.cards).find((row: { id: string }) => row.id === card).version).toBe(4);
      expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
    } finally { try { restoreWorker(); } finally { await peer.close(); } }
  });
}
