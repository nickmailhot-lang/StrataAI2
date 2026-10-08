import { expectPersistedNotificationDelivery, retainPrivateNotification, type PrivateNotificationEnvelope } from './persistedNotificationDelivery';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

// Real cookies, lookup, identity revision changes, comment/inbox writes and
// original receipts. Only a successfully committed first reply is replaced.
for (const width of [1280, 390]) {
  test(`PRD-15 native selected teammate revision, lost reply and private inbox at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const recipientContext = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {};
    try {
      const email = `mention-recipient-${width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, recipientContext].entries()) {
        const credentials = { email: index ? email : `mention-owner-${width}-${Date.now()}@example.test`,
          password: 'mention-browser-battery-horse', displayName: index ? 'Mention recipient' : 'Mention author' };
        expect((await client.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      }
      const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Selected mentions' } });
      expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await recipientContext.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const recipient = (await (await recipientContext.request.get('/me')).json()).id;
      const original = await (await recipientContext.request.get('/me/mention-handle')).json();
      const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Mention Board', visibility: 'PRIVATE' } });
      expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
      expect((await context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const listResult = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Mention List' } });
      expect(listResult.status()).toBe(201); const list = (await listResult.json()).id;
      const cardResult = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Selected teammate Card' } });
      expect(cardResult.status()).toBe(201); const card = (await cardResult.json()).id;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`; const reads = trackBoardReads(page, board, cardPath);
      const nativeInbox = await recipientContext.newPage(); await nativeInbox.setViewportSize({ width, height: 844 });
      const liveEvents: PrivateNotificationEnvelope[] = []; let liveSnapshots = 0;
      nativeInbox.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/notifications/live') return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text);
            if (message.type !== 2 || !message.item) continue;
            const item = message.item;
            expect(item.organizationId).toBe(org); expect(item.recipientId).toBe(recipient); liveSnapshots++;
            for (const event of item.events) {
              expect(event.organizationId).toBe(org); expect(event.recipientId).toBe(recipient);
              expect(event.entityType).toBe('Notification'); expect(event.metadata).toEqual({});
              liveEvents.push(retainPrivateNotification(event, org, recipient));
            }
          }
        });
      });
      await nativeInbox.goto(`/app/${org}/notifications`);
      await expect(nativeInbox.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      await expect.poll(() => liveSnapshots).toBeGreaterThan(0);

      await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const review = page.getByRole('button', { name: 'Review Card comments', exact: true });
      async function select() {
        await expect(review).toBeEnabled(); await review.press('Enter');
        await page.getByRole('button', { name: 'Add comment', exact: true }).press('Enter');
        await page.getByRole('textbox', { name: 'Teammate username prefix', exact: true }).fill(original.handle);
        await page.getByRole('button', { name: 'Find teammates', exact: true }).press('Enter');
        const choice = page.getByRole('button', { name: `Mention Mention recipient (@${original.handle})`, exact: true });
        await expect(choice).toBeEnabled(); await choice.press('Enter');
        const field = page.getByRole('textbox', { name: 'New comment', exact: true });
        await expect(field).toHaveValue(`@${original.handle}`); await expect(field).toBeFocused();
      }
      await select();
      expect((await new AxeBuilder({ page }).include('section[aria-label="Card comments"]').analyze()).violations).toEqual([]);
      // Reclaiming the same handle does not restore the reviewed revision.
      for (const handle of [`selected_${width}_${Date.now()}`, original.handle]) {
        const current = await (await recipientContext.request.get('/me/mention-handle')).json();
        const changed = await recipientContext.request.patch('/me/mention-handle', { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
          data: { handle, userVersion: current.userVersion, handleVersion: current.handleVersion } });
        expect(changed.status()).toBe(200);
      }
      const reclaimed = await (await recipientContext.request.get('/me/mention-handle')).json();
      expect(reclaimed.handleVersion).toBe(original.handleVersion + 2);
      await pressAdmittedAction(page.getByRole('button', { name: 'Save comment', exact: true }));
      await expect(page.getByText('This comment change is unavailable. Load the latest Card before starting another change.', { exact: true })).toBeVisible();
      expect((await (await context.request.get(`/cards/${card}/comments`)).json()).items).toEqual([]);
      expect((await (await recipientContext.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
      expect(liveEvents).toEqual([]); await expect(nativeInbox.getByRole('article')).toHaveCount(0);
      await page.getByRole('button', { name: 'Discard comment review and load latest', exact: true }).press('Enter');
      await select();
      const writes: { key: string | undefined; body: string | null }[] = [];
      const path = `/cards/${card}/comments`;
      await page.route('**' + path, async route => {
        if (route.request().method() !== 'POST') return route.continue();
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const actual = await route.fetch(); expect(actual.status()).toBe(200);
        if (writes.length === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
        return route.fulfill({ response: actual });
      });
      await pressAdmittedAction(page.getByRole('button', { name: 'Save comment', exact: true }));
      const retry = page.getByRole('button', { name: 'Retry original comment change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused();
      await pressAdmittedAction(retry); await expect(page.getByText('Comment added.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      expect(JSON.parse(writes[0].body!)).toEqual({ content: `@${original.handle}`, cardVersion: 1,
        mentionSelections: [{ userId: recipient, handle: original.handle, handleVersion: reclaimed.handleVersion }] });
      await expect.poll(() => liveEvents.filter(event => event.eventType === 'NOTIFICATION_CREATED').length).toBe(1);
      await expect(nativeInbox.getByText('Mentioned you in a comment · Unread', { exact: true })).toBeVisible({ timeout: 25_000 });
      await expect(nativeInbox.getByRole('article')).toHaveCount(1);
      await expect(nativeInbox.getByRole('link', { name: 'Open Card', exact: true })).toHaveAttribute('href', cardPath);
      expect((await new AxeBuilder({ page: nativeInbox }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await nativeInbox.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await pressAdmittedAction(nativeInbox.getByRole('button', { name: 'Mark read', exact: true }));
      await expect(nativeInbox.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      await expect.poll(() => liveEvents.filter(event => event.eventType === 'NOTIFICATION_READ').length).toBe(1);
      const inbox = await recipientContext.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200);
      expect(inbox.headers()['cache-control']).toContain('no-store');
      const notifications = (await inbox.json()).items; expect(notifications).toHaveLength(1);
      expect(notifications[0]).toMatchObject({ type: 'MENTION_CREATED', recipientId: recipient, entityId: card, entityLink: cardPath });
      expect(notifications[0].readAt).not.toBeNull();
      await expectPersistedNotificationDelivery(recipientContext.request, org, recipient, [liveEvents]);
      expect((await (await context.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect((await context.request.delete(`/boards/${board}/members/${recipient}`, { headers })).status()).toBe(204);
      expect((await (await recipientContext.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
      await expect(nativeInbox.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
      expect(liveEvents.filter(event => event.eventType === 'NOTIFICATION_CREATED')).toHaveLength(1);
      expect(liveEvents.filter(event => event.eventType === 'NOTIFICATION_READ')).toHaveLength(1);
    } finally { try { restoreWorker(); } finally { await recipientContext.close(); } }
  });
}
