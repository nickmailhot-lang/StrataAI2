import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { trackBoardReads, trackCardVersion } from './boardReadTracker';
import { pressAdmittedAction } from './keyboardAdmission';

// Real account invitations, explicit Board membership and Card assignment.
// Only an actual committed first response is replaced to exercise recovery.
for (const width of [1280, 390]) {
  test(`PRD-15 native confirmed groups, original receipt, role scope and rolling quota at ${width}px`, async ({ page, context, browser }) => {
    test.setTimeout(180_000); await page.setViewportSize({ width, height: 844 });
    const headers = { 'X-StrataAI-Request': '1' };
    const teammate = await browser.newContext({ baseURL: test.info().project.use.baseURL, viewport: { width, height: 844 } });
    let restoreWorker = () => {};
    try {
      const email = `mass-member-${width}-${Date.now()}@example.test`;
      for (const [index, client] of [context, teammate].entries()) {
        const credentials = { email: index ? email : `mass-owner-${width}-${Date.now()}@example.test`,
          password: 'mass-mention-battery-horse', displayName: index ? 'Group teammate' : 'Group author' };
        expect((await client.request.post('/auth/register', { headers, data: credentials })).status()).toBe(201);
        expect((await client.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
      }
      const createdOrg = await context.request.post('/organizations', { headers, data: { name: 'Confirmed groups' } });
      expect(createdOrg.status()).toBe(201); const org = (await createdOrg.json()).organization.id;
      const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invitation.status()).toBe(201);
      expect((await teammate.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
      const recipient = (await (await teammate.request.get('/me')).json()).id;
      const createdBoard = await context.request.post('/boards', { headers,
        data: { organizationId: org, name: 'Group Board', visibility: 'PRIVATE' } });
      expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
      expect((await context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const createdList = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Group List' } });
      expect(createdList.status()).toBe(201); const list = (await createdList.json()).id;
      const createdCard = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Confirmed group Card' } });
      expect(createdCard.status()).toBe(201); const card = (await createdCard.json()).id;
      expect((await context.request.put(`/cards/${card}/members/${recipient}?version=1`,
        { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} })).status()).toBe(200);
      restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
      const cardPath = `/app/${org}/boards/${board}/cards/${card}`;
      const inboxPath = `/organizations/${org}/notifications`;
      const initialInbox = await teammate.request.get(inboxPath); expect(initialInbox.status()).toBe(200);
      const initialItems = (await initialInbox.json()).items;
      expect(initialItems).toHaveLength(1); expect(initialItems[0].type).toBe('CARD_ASSIGNED');
      const assignmentId = initialItems[0].id;
      const nativeInbox = await teammate.newPage();
      const mentionEvents: string[] = []; let liveSnapshots = 0;
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
              if (event.entityId !== assignmentId) mentionEvents.push(event.eventType);
            }
          }
        });
      });
      await nativeInbox.goto(`/app/${org}/notifications`);
      await expect(nativeInbox.getByRole('article')).toHaveCount(1);
      await expect.poll(() => liveSnapshots).toBeGreaterThan(0);
      expect(mentionEvents).toEqual([]);
      const cardVersion = trackCardVersion(page, board, card, cardPath);
      const reads = trackBoardReads(page, board, cardPath); await page.goto(cardPath); await expect.poll(reads).toBeGreaterThanOrEqual(2);
      const commentsPath = `/cards/${card}/comments`;
      async function draft(text: string) {
        await waitForBoardDelivery(context.request, board);
        const snapshot = await (await context.request.get(`/boards/${board}`)).json();
        const current = snapshot.lists.flatMap((column: { cards: { id: string; version: number }[] }) => column.cards)
          .find((row: { id: string }) => row.id === card);
        expect(current).toBeDefined(); await expect.poll(cardVersion).toBe(current.version);
        const review = page.getByRole('button', { name: 'Review Card comments', exact: true });
        await expect(review).toBeEnabled(); await review.press('Enter');
        await page.getByRole('button', { name: 'Add comment', exact: true }).press('Enter');
        await page.getByRole('textbox', { name: 'New comment', exact: true }).fill(text);
      }
      async function save(client = page) {
        // Multiple native pages share the keyboard; activate the author page
        // before checking foreground admission and issuing one keypress.
        await client.bringToFront();
        // A keyboard focus transition can start a protected foreground read.
        // Wait for the actual workspace admission, then activate once; never
        // retry a rejected mutation or bypass the current-access boundary.
        await expect(client.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
        await pressAdmittedAction(client.getByRole('button', { name: 'Save comment', exact: true }));
      }
      await draft('@card @board');
      const cardConsent = page.getByRole('checkbox', { name: 'Notify current teammates assigned to this Card (@card)', exact: true });
      const boardConsent = page.getByRole('checkbox', { name: 'Notify all current board participants (@board)', exact: true });
      await expect(cardConsent).not.toBeChecked(); await expect(boardConsent).not.toBeChecked();
      await cardConsent.press('Space'); await boardConsent.press('Space');
      await expect(cardConsent).toBeChecked(); await expect(boardConsent).toBeChecked();
      expect((await new AxeBuilder({ page }).include('section[aria-label="Card comments"]').analyze()).violations).toEqual([]);
      const writes: { key: string | undefined; body: string | null }[] = [];
      await page.route('**' + commentsPath, async route => {
        if (route.request().method() !== 'POST') return route.continue();
        writes.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData() });
        const actual = await route.fetch(); expect(actual.status()).toBe(200);
        if (writes.length === 1) return route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'work_storage_unavailable' }) });
        return route.fulfill({ response: actual });
      });
      await save();
      const retry = page.getByRole('button', { name: 'Retry original comment change', exact: true });
      await expect(retry).toBeEnabled(); await expect(retry).toBeFocused(); await pressAdmittedAction(retry);
      await expect(page.getByText('Comment added.', { exact: true })).toBeVisible();
      expect(writes).toHaveLength(2); expect(writes[1]).toEqual(writes[0]);
      expect(JSON.parse(writes[0].body!)).toEqual({ content: '@card @board', cardVersion: 2, massMentionConfirmation: { card: true, board: true } });
      await page.unroute('**' + commentsPath);
      async function mentions() {
        const response = await teammate.request.get(inboxPath); expect(response.status()).toBe(200);
        expect(response.headers()['cache-control']).toContain('no-store');
        return (await response.json()).items.filter((item: { type: string }) => item.type === 'MENTION_CREATED');
      }
      expect(await mentions()).toHaveLength(1);
      await expect.poll(() => mentionEvents.filter(type => type === 'NOTIFICATION_CREATED').length).toBe(1);
      await expect(nativeInbox.getByText('Mentioned you in a comment · Unread', { exact: true })).toHaveCount(1);
      await expect(nativeInbox.getByRole('article')).toHaveCount(2);
      expect((await (await context.request.get(inboxPath)).json()).items).toEqual([]);
      await draft('Unconfirmed @board'); await expect(boardConsent).not.toBeChecked();
      await save();
      await expect(page.getByText('Comment added.', { exact: true })).toBeVisible(); expect(await mentions()).toHaveLength(1);
      for (const label of ['Second', 'Third']) {
        await draft(`${label} @board`); await boardConsent.press('Space');
        await expect(boardConsent).toBeChecked();
        await save();
        await expect(page.getByText('Comment added.', { exact: true })).toBeVisible();
      }
      await draft('Fourth @board'); await boardConsent.press('Space');
      await save();
      await expect(page.getByText('Group mentions are limited to three deliveries per board in ten minutes. Wait, then review the latest Card.', { exact: true })).toBeVisible();
      await expect(page.getByRole('textbox', { name: 'New comment', exact: true })).toHaveCount(0);
      await expect(page.getByRole('button', { name: 'Retry original comment change', exact: true })).toHaveCount(0);
      const current = await (await context.request.get(commentsPath)).json(); expect(current.cardVersion).toBe(6); expect(current.items).toHaveLength(4);
      expect(await mentions()).toHaveLength(3);
      await expect.poll(() => mentionEvents.filter(type => type === 'NOTIFICATION_CREATED').length).toBe(3);
      await expect(nativeInbox.getByText('Mentioned you in a comment · Unread', { exact: true })).toHaveCount(3);
      await expect(nativeInbox.getByRole('article')).toHaveCount(4);
      for (const link of await nativeInbox.getByRole('link', { name: 'Open Card', exact: true }).all()) {
        await expect(link).toHaveAttribute('href', cardPath);
      }
      expect((await new AxeBuilder({ page: nativeInbox }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await nativeInbox.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      const memberPage = await teammate.newPage(); const memberReads = trackBoardReads(memberPage, board, cardPath);
      await memberPage.goto(cardPath); await expect.poll(memberReads).toBeGreaterThanOrEqual(2);
      await memberPage.getByRole('button', { name: 'Review Card comments', exact: true }).press('Enter');
      await memberPage.getByRole('button', { name: 'Add comment', exact: true }).press('Enter');
      await memberPage.getByRole('textbox', { name: 'New comment', exact: true }).fill('Member self @card @board');
      await expect(memberPage.getByRole('checkbox', { name: 'Notify all current board participants (@board)', exact: true })).toBeDisabled();
      await memberPage.getByRole('checkbox', { name: 'Notify current teammates assigned to this Card (@card)', exact: true }).press('Space');
      await save(memberPage);
      await expect(memberPage.getByText('Comment added.', { exact: true })).toBeVisible(); expect(await mentions()).toHaveLength(3);
      expect((await (await context.request.get(inboxPath)).json()).items).toEqual([]);
      expect((await context.request.delete(`/boards/${board}/members/${recipient}`, { headers })).status()).toBe(204);
      expect((await (await teammate.request.get(inboxPath)).json()).items).toEqual([]);
      await expect(nativeInbox.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
      expect(mentionEvents).toEqual(['NOTIFICATION_CREATED', 'NOTIFICATION_CREATED', 'NOTIFICATION_CREATED']);
    } finally { try { restoreWorker(); } finally { await teammate.close(); } }
  });
}
