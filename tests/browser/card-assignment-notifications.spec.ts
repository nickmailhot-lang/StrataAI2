import { registerNotificationAccount } from './notificationAccountFixture';
import { expectPersistedNotificationDelivery, retainPrivateNotification, type PrivateNotificationEnvelope } from './persistedNotificationDelivery';
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

// Real membership, native assignment commands and private recipient delivery.
// Only a successfully committed first response is replaced.
for (const width of [1280, 390]) {
  test(`PRD-11/17 native assignment receipt, reassignment and private inbox at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(150_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const recipientContext = await browser.newContext({ baseURL: test.info().project.use.baseURL });
    let restoreWorker = () => {};
    try {
      const email = `assignment-recipient-${width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, recipientContext].entries()) {
        const credentials = { email: index ? email : `assignment-owner-${width}-${Date.now()}@example.test`,
          password: 'assignment-browser-battery-horse', displayName: index ? 'Assignment recipient' : 'Assignment author' };
        await registerNotificationAccount(client.request, credentials);
      }
      const orgResult = await context.request.post('/organizations', { headers, data: { name: 'Assignment delivery' } });
      expect(orgResult.status()).toBe(201); const org = (await orgResult.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await recipientContext.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const recipient = (await (await recipientContext.request.get('/me')).json()).id;
      const actor = (await (await context.request.get('/me')).json()).id;
      const boardResult = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Assignment Board', visibility: 'PRIVATE' } });
      expect(boardResult.status()).toBe(201); const board = (await boardResult.json()).id;
      expect((await context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const listResult = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Assignment List' } });
      expect(listResult.status()).toBe(201); const list = (await listResult.json()).id;
      const cardResult = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Native assigned Card' } });
      expect(cardResult.status()).toBe(201); const card = (await cardResult.json()).id;
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`;
      const inbox = await recipientContext.newPage(); await inbox.setViewportSize({ width, height: 844 });
      const liveEvents: PrivateNotificationEnvelope[] = []; let liveSnapshots = 0;
      inbox.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/notifications/live') return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text); if (message.type !== 2 || !message.item) continue;
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
      await inbox.goto(`/app/${org}/notifications`);
      await expect(inbox.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      await expect.poll(() => liveSnapshots).toBeGreaterThan(0);
      const reads = trackBoardReads(page, board, cardPath); const revision = trackCardVersion(page, board, card, cardPath);
      await page.bringToFront(); await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const edit = page.getByRole('button', { name: 'Edit Card assignees', exact: true });
      async function options(version: number) {
        await waitForBoardDelivery(context.request, board); await expect.poll(revision).toBe(version);
        await page.bringToFront(); await pressAdmittedAction(edit);
        await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      }
      await options(1);
      const path = `/cards/${card}/members/${recipient}`;
      const attempts: { url: string; method: string; key: string | undefined; body: string | null }[] = [];
      await page.route('**' + path + '?*', async route => {
        const request = route.request(); expect(request.method()).toBe('PUT');
        attempts.push({ url: request.url(), method: request.method(), key: request.headers()['idempotency-key'], body: request.postData() });
        const committed = await route.fetch(); expect(committed.status()).toBe(200);
        if (attempts.length === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
        return route.fulfill({ response: committed });
      });
      await pressAdmittedAction(page.getByRole('button', { name: 'Assign Assignment recipient', exact: true }));
      const retry = page.getByRole('button', { name: 'Retry assignee change', exact: true });
      await expect(retry).toBeEnabled(); await pressAdmittedAction(retry);
      await expect(page.getByRole('region', { name: 'Edit Card assignees', exact: true })).toHaveCount(0);
      await expect(edit).toBeFocused(); expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
      expect(attempts[0].key).toMatch(/^[0-9a-f-]{36}$/); expect(new URL(attempts[0].url).searchParams.get('version')).toBe('1');
      await page.unroute('**' + path + '?*');
      await expect.poll(() => liveEvents.map(event => event.eventType)).toEqual(['NOTIFICATION_CREATED']);
      await expect(inbox.getByText('Assigned to you · Unread', { exact: true })).toBeVisible();
      await expect(inbox.getByRole('article')).toHaveCount(1);
      await expect(inbox.getByRole('link', { name: 'Open Card', exact: true })).toHaveAttribute('href', cardPath);
      async function recipientRows() {
        const reply = await recipientContext.request.get(`/organizations/${org}/notifications`); expect(reply.status()).toBe(200);
        expect(reply.headers()['cache-control']).toContain('no-store'); return (await reply.json()).items;
      }
      const first = await recipientRows(); expect(first).toHaveLength(1);
      expect(first[0]).toMatchObject({ actorId: actor, recipientId: recipient, entityId: card, type: 'CARD_ASSIGNED', entityLink: cardPath });
      await options(2); await pressAdmittedAction(page.getByRole('button', { name: 'Unassign Assignment recipient', exact: true }));
      await expect(edit).toBeFocused(); expect(await recipientRows()).toEqual(first);
      await options(3); await pressAdmittedAction(page.getByRole('button', { name: 'Assign Assignment recipient', exact: true }));
      await expect(edit).toBeFocused();
      await expect.poll(() => liveEvents.map(event => event.eventType)).toEqual(['NOTIFICATION_CREATED', 'NOTIFICATION_CREATED']);
      await expect(inbox.getByRole('article')).toHaveCount(2);
      const second = await recipientRows(); expect(second).toHaveLength(2); expect(new Set(second.map((row: { id: string }) => row.id)).size).toBe(2);
      await options(4); await pressAdmittedAction(page.getByRole('button', { name: 'Assign Assignment author', exact: true }));
      await expect(edit).toBeFocused(); await expect.poll(revision).toBe(5);
      expect((await (await context.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
      expect(await recipientRows()).toEqual(second);
      const sync = await recipientContext.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200);
      expect(liveEvents.map(event => event.eventId)).toEqual((await sync.json()).events.map((event: { eventId: string }) => event.eventId));
      await expectPersistedNotificationDelivery(recipientContext.request, org, recipient, [liveEvents]);
      for (const client of [page, inbox]) {
        expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
        expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      }
      expect((await context.request.delete(`/boards/${board}/members/${recipient}`, { headers })).status()).toBe(204);
      expect(await recipientRows()).toEqual([]); await expect(inbox.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
      expect(liveEvents.map(event => event.eventType)).toEqual(['NOTIFICATION_CREATED', 'NOTIFICATION_CREATED']);
    } finally { try { restoreWorker(); } finally { await recipientContext.close(); } }
  });
}
