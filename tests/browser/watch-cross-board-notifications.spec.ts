import AxeBuilder from '@axe-core/playwright';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { focusAdmittedControl } from './keyboardAdmission';
import type { Locator } from '@playwright/test';
import { execFileSync } from 'node:child_process';

async function activate(button: Locator) {
  await button.page().bringToFront();
  await focusAdmittedControl(button);
  await button.press('Enter', { timeout: 5_000 });
}

for (const recipientRole of ['OWNER', 'MEMBER'] as const) {
test(`PRD-17-TC-01/07/08/11/12: ${recipientRole} cross-Board watch relationships and direct Card identity reach desktop and phone inboxes`, async ({ page, context, browser, baseURL }) => {
  test.setTimeout(180_000);
  const headers = { 'X-StrataAI-Request': '1' };
  const peer = await browser.newContext({ baseURL });
  const administrator = recipientRole === 'OWNER' ? context : await browser.newContext({ baseURL });
  let phone: typeof peer | undefined; let restoreWorker = () => {};
  try {
    const suffix = crypto.randomUUID(); const email = `watch-issuer-${suffix}@example.test`;
    const recipientEmail = `watch-recipient-${suffix}@example.test`;
    const clients = recipientRole === 'OWNER' ? [context, peer] : [context, peer, administrator];
    for (const [index, client] of clients.entries()) {
      const account = { email: index === 0 ? recipientEmail : index === 1 ? email : `watch-administrator-${suffix}@example.test`, password: 'watched-notifications-correct-horse', displayName: index ? 'Watch activity issuer' : 'Watch activity recipient' };
      expect((await client.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
      expect((await client.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
    }
    const orgReply = await administrator.request.post('/organizations', { headers, data: { name: 'Watched notifications' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const invitation = await administrator.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await peer.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const issuer = (await (await peer.request.get('/me')).json()).id;
    const recipient = (await (await context.request.get('/me')).json()).id;
    if (recipientRole === 'MEMBER') {
      const invited = await administrator.request.post(`/organizations/${org}/invitations`, { headers,
        data: { email: recipientEmail, surface: 'INTERNAL', targetRole: 'MEMBER' } });
      expect(invited.status()).toBe(201);
      expect((await context.request.post(`/me/invitations/${(await invited.json()).id}/accept`, { headers })).status()).toBe(200);
    }

    const boards: string[] = []; const lists: string[] = [];
    for (const name of ['Source watch Board', 'Destination watch Board']) {
      const b = await administrator.request.post('/boards', { headers, data: { organizationId: org, name, visibility: 'PRIVATE' } });
      expect(b.status()).toBe(201); const board = (await b.json()).id; boards.push(board);
      expect((await administrator.request.patch(`/boards/${board}/members/${issuer}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      if (recipientRole === 'MEMBER') expect((await administrator.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
      const l = await administrator.request.post(`/boards/${board}/lists`, { headers, data: { name: name + ' List' } });
      expect(l.status()).toBe(201); lists.push((await l.json()).id);
    }
    const c = await administrator.request.post(`/lists/${lists[0]}/cards`, { headers, data: { title: 'Cross-Board watched Card' } });
    expect(c.status()).toBe(201); const card = (await c.json()).id;
    restoreWorker = scopedBoardWorker(org);
    for (const board of boards) await waitForBoardDelivery(context.request, board);
    async function watch(kind: 'Board' | 'List' | 'Card', board: string, watching: boolean) {
      await page.bringToFront(); await page.goto(`/app/${org}/boards/${board}${kind === 'Card' ? `/cards/${card}` : ''}`);
      await activate(page.getByRole('button', { name: `${kind} watching`, exact: true }));
      const dialog = page.getByRole('dialog', { name: `${kind} watching`, exact: true });
      await expect(dialog.getByText(`You are ${watching ? 'not ' : ''}watching this ${kind}.`, { exact: true })).toBeVisible();
      await activate(dialog.getByRole('button', { name: 'Check current watching', exact: true }));
      await expect(dialog.getByRole('button', { name: 'Check current watching', exact: true })).toBeFocused();
      await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
      const entity = kind === 'Board' ? board : kind === 'List' ? lists[boards.indexOf(board)] : card;
      const changed = page.waitForResponse(reply => new URL(reply.url()).pathname === `/watch/${kind.toUpperCase()}/${entity}`
        && reply.request().method() === (watching ? 'PUT' : 'DELETE'), { timeout: 5_000 });
      await activate(dialog.getByRole('button', { name: `${watching ? 'Watch' : 'Unwatch'} ${kind}`, exact: true }));
      expect((await changed).status()).toBe(200);
      await expect(dialog.getByText(`You are ${watching ? '' : 'not '}watching this ${kind}.`, { exact: true })).toBeVisible();
      await expect(dialog.getByRole('button', { name: 'Check current watching', exact: true })).toBeFocused();
      await activate(dialog.getByRole('button', { name: 'Done watching', exact: true }));
      await expect(dialog).toHaveCount(0);
    }
    await watch('Board', boards[0], true); await watch('List', boards[0], true);
    phone = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
    const desktop = await context.newPage(); await desktop.setViewportSize({ width: 1280, height: 844 }); const mobile = await phone.newPage();
    const live = [desktop, mobile].map(client => {
      const observed = { snapshots: 0, events: [] as { eventId: string; eventType: string }[] };
      client.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/notifications/live') return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text); if (message.type !== 2 || !message.item) continue;
            const item = message.item;
            expect(item.organizationId).toBe(org); expect(item.recipientId).toBe(recipient); observed.snapshots++;
            for (const event of item.events) {
              expect(event.organizationId).toBe(org); expect(event.recipientId).toBe(recipient);
              expect(event.entityType).toBe('Notification'); expect(event.metadata).toEqual({});
              observed.events.push({ eventId: event.eventId, eventType: event.eventType });
            }
          }
        });
      });
      return observed;
    });
    for (const [index, client] of [desktop, mobile].entries()) {
      await client.goto(`/app/${org}/notifications`);
      await expect(client.getByText('0 unread on this page.', { exact: true })).toBeVisible();
      await expect.poll(() => live[index].snapshots).toBeGreaterThan(0); expect(live[index].events).toEqual([]);
    }
    async function move(source: string, destination: string, version: number) {
      const response = await peer.request.post(`/cards/${card}/move`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
        data: { sourceBoardId: source, destinationListId: destination, expectedVersion: version } });
      expect(response.status()).toBe(200); expect((await response.json()).version).toBe(version + 1);
      for (const board of boards) await waitForBoardDelivery(peer.request, board);
    }
    async function inboxRows() {
      const reply = await context.request.get(`/organizations/${org}/notifications`); expect(reply.status()).toBe(200);
      expect(reply.headers()['cache-control']).toContain('no-store'); return (await reply.json()).items;
    }
    async function delivered(count: number) {
      for (const [index, client] of [desktop, mobile].entries()) {
        await expect(client.getByRole('article')).toHaveCount(count, { timeout: 25_000 });
        await expect.poll(() => live[index].events.length).toBe(count);
        expect(live[index].events.map(event => event.eventType)).toEqual(Array(count).fill('NOTIFICATION_CREATED'));
      }
    }
    // A move into an unwatched destination uses no former source relationship.
    await move(boards[0], lists[1], 1); expect(await inboxRows()).toEqual([]);
    for (const observed of live) expect(observed.events).toEqual([]);
    await watch('Board', boards[1], true);
    expect((await peer.request.patch(`/cards/${card}`, { headers, data: { title: 'Destination Board edit', description: '', version: 2 } })).status()).toBe(200);
    await waitForBoardDelivery(peer.request, boards[1]); await delivered(1);
    expect((await inboxRows())[0]).toMatchObject({ entityId: card, boardId: boards[1], actorId: issuer, type: 'CARD_UPDATED' });
    // Source Board+List overlap on the returning move yields one delivery.
    await move(boards[1], lists[0], 3); await delivered(2);
    await watch('Card', boards[0], true);
    const beforeWatch = await context.request.get(`/watch/CARD/${card}`); expect(beforeWatch.status()).toBe(200); const direct = await beforeWatch.json();
    expect(direct.watching).toBe(true); expect(direct.version).toBe(1);
    await watch('Board', boards[0], false); await watch('List', boards[0], false);
    // A direct Card watch and destination Board overlap after crossing Boards.
    await move(boards[0], lists[1], 4); await delivered(3);
    const afterWatch = await context.request.get(`/watch/CARD/${card}`); expect(afterWatch.status()).toBe(200);
    expect(await afterWatch.json()).toMatchObject({ subscriptionId: direct.subscriptionId, watching: true, version: 1 });
    await watch('Board', boards[1], false);
    expect((await peer.request.patch(`/cards/${card}`, { headers, data: { title: 'Direct Card only edit', description: '', version: 5 } })).status()).toBe(200);
    await waitForBoardDelivery(peer.request, boards[1]); await delivered(4);
    const rows = await inboxRows(); expect(rows).toHaveLength(4);
    expect(rows.filter((row: { type: string }) => row.type === 'CARD_MOVED')).toHaveLength(2);
    expect(rows.filter((row: { type: string }) => row.type === 'CARD_UPDATED')).toHaveLength(2);
    for (const row of rows) expect(row).toMatchObject({ entityId: card, actorId: issuer, recipientId: recipient,
      entityLink: `/app/${org}/boards/${boards[1]}/cards/${card}` });
    const sync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200);
    const journal = (await sync.json()).events; expect(journal).toHaveLength(4);
    for (const observed of live) expect(observed.events.map(event => event.eventId)).toEqual(journal.map((event: { eventId: string }) => event.eventId));
    expect((await (await peer.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
    for (const client of [desktop, mobile]) {
      for (const link of await client.getByRole('link', { name: 'Open Card', exact: true }).all())
        await expect(link).toHaveAttribute('href', `/app/${org}/boards/${boards[1]}/cards/${card}`);
      expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    if (recipientRole === 'MEMBER') {
      expect(process.env.CI).toBe('true');
      for (const id of [org, recipient]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
      function privateHistory() {
        return execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
          'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], {
          input: `SELECT md5(jsonb_build_object(
            'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}' AND recipient_id='${recipient}'),
            'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM notification_events e WHERE tenant_id='${org}' AND recipient_id='${recipient}'))::text);
            SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}';
            SELECT count(*) FROM notification_events WHERE tenant_id='${org}' AND recipient_id='${recipient}';`, encoding: 'utf8', stdio: 'pipe',
        }).trim();
      }
      const history = privateHistory(); expect(history).toMatch(/^[0-9a-f]{32}\r?\n4\r?\n4$/);
      expect((await administrator.request.delete(`/boards/${boards[1]}/members/${recipient}`, { headers })).status()).toBe(204);
      expect((await context.request.get(`/watch/CARD/${card}`)).status()).toBe(404);
      expect(await inboxRows()).toEqual([]);
      for (const client of [desktop, mobile]) await expect(client.getByRole('article')).toHaveCount(0, { timeout: 25_000 });
      expect((await peer.request.patch(`/cards/${card}`, { headers, data: { title: 'Edit after recipient departure', description: '', version: 6 } })).status()).toBe(200);
      await waitForBoardDelivery(peer.request, boards[1]); expect(await inboxRows()).toEqual([]);
      expect(privateHistory()).toBe(history);
      for (const observed of live) expect(observed.events.map(event => event.eventId)).toEqual(journal.map((event: { eventId: string }) => event.eventId));
    }
  } finally { try { restoreWorker(); } finally { await phone?.close(); await peer.close(); if (administrator !== context) await administrator.close(); } }
});
}
