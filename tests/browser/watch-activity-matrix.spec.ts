import AxeBuilder from '@axe-core/playwright';
import { execFileSync } from 'node:child_process';
import { expect, test } from './releaseTest';
import { scopedBoardWorker, waitForBoardDelivery } from './scopedBoardWorker';
import { focusAdmittedControl } from './keyboardAdmission';

type PrivateEnvelope = {
  eventId: string; eventType: string; actorId: string; recipientId: string;
  organizationId: string; boardId: string; entityType: string; entityId: string;
  version: number; sequence: string; createdAt: string; metadata: Record<string, unknown>;
};

test('PRD-17-TC-01/06/07/08/10/11/12: all configured watch producers reach private native inboxes', async ({ page, context, browser, baseURL }) => {
  test.setTimeout(180_000);
  const headers = { 'X-StrataAI-Request': '1' }; const peer = await browser.newContext({ baseURL });
  let phone: typeof peer | undefined; let restoreWorker = () => {};
  try {
    const suffix = crypto.randomUUID(); const email = `watch-matrix-issuer-${suffix}@example.test`;
    for (const [index, client] of [context, peer].entries()) {
      const credentials = { email: index ? email : `watch-matrix-recipient-${suffix}@example.test`, password: 'watch-matrix-correct-horse-battery', displayName: 'Watch matrix participant' };
      const registered = await client.request.post('/auth/register', { headers, data: credentials }); expect(registered.status()).toBe(201);
      if (process.env.STRATAAI_E2E_VERIFY_WATCH_ACCOUNTS === '1') {
        const pending = await client.request.post('/auth/login', { headers, data: credentials }); expect(pending.status()).toBe(403);
        expect((await pending.json()).code).toBe('email_verification_required'); const account = await registered.json();
        if (account.verificationToken) expect((await client.request.post('/auth/verify-email', { headers, data: { token: account.verificationToken } })).status()).toBe(200);
        else {
          // Identity mail has separate provider acceptance. This activates only
          // the freshly registered disposable account when its token is private.
          expect(process.env.CI).toBe('true'); expect(account.user.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
          execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
            'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], {
            input: `UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,now()) WHERE id='${account.user.id}';`, stdio: 'pipe',
          });
        }
      }
      expect((await client.request.post('/auth/login', { headers, data: credentials })).status()).toBe(200);
    }
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Configured watch matrix' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201); expect((await peer.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const issuer = (await (await peer.request.get('/me')).json()).id; const recipient = (await (await context.request.get('/me')).json()).id;
    const createdBoard = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Configured activity', visibility: 'PRIVATE' } });
    expect(createdBoard.status()).toBe(201); const board = (await createdBoard.json()).id;
    expect((await context.request.patch(`/boards/${board}/members/${issuer}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    const lists: string[] = [];
    for (const name of ['Source', 'Destination']) {
      const reply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name } }); expect(reply.status()).toBe(201); lists.push((await reply.json()).id);
    }
    restoreWorker = scopedBoardWorker(org); await waitForBoardDelivery(context.request, board);
    await page.setViewportSize({ width: 1280, height: 844 }); await page.goto(`/app/${org}/boards/${board}`);
    async function activate(button: ReturnType<typeof page.getByRole>) {
      await button.page().bringToFront(); await focusAdmittedControl(button); await button.press('Enter', { timeout: 5_000 });
    }
    await activate(page.getByRole('button', { name: 'Board watching', exact: true }));
    const watch = page.getByRole('dialog', { name: 'Board watching', exact: true });
    await expect(watch.getByText('You are not watching this Board.', { exact: true })).toBeVisible();
    await activate(watch.getByRole('button', { name: 'Check current watching', exact: true }));
    await expect(watch.getByRole('button', { name: 'Check current watching', exact: true })).toBeFocused();
    await expect(page.getByRole('region', { name: 'Board workspace', exact: true, includeHidden: true })).toHaveAttribute('aria-busy', 'false');
    const watched = page.waitForResponse(reply => new URL(reply.url()).pathname === `/watch/BOARD/${board}` && reply.request().method() === 'PUT', { timeout: 5_000 });
    await activate(watch.getByRole('button', { name: 'Watch Board', exact: true })); expect((await watched).status()).toBe(200);
    await expect(watch.getByText('You are watching this Board.', { exact: true })).toBeVisible();
    await activate(watch.getByRole('button', { name: 'Done watching', exact: true })); await expect(watch).toHaveCount(0);
    const actorWatch = await peer.request.put(`/watch/BOARD/${board}?version=0`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} });
    expect(actorWatch.status()).toBe(200); expect(await actorWatch.json()).toMatchObject({ watching: true, version: 1 });
    phone = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 }, storageState: await context.storageState() });
    const mobile = await phone.newPage(); const clients = [page, mobile];
    const live = clients.map(client => {
      const observed = { snapshots: 0, cursor: '', events: [] as PrivateEnvelope[] };
      client.on('websocket', socket => {
        if (new URL(socket.url()).pathname !== '/notifications/live') return;
        socket.on('framereceived', frame => {
          if (typeof frame.payload !== 'string') return;
          for (const text of frame.payload.split('\u001e').filter(Boolean)) {
            const message = JSON.parse(text); if (message.type !== 2 || !message.item) continue;
            expect(message.item.organizationId).toBe(org); expect(message.item.recipientId).toBe(recipient); observed.snapshots++; observed.cursor = message.item.cursor;
            for (const event of message.item.events) {
              expect(Object.keys(event).sort()).toEqual(['eventId', 'eventType', 'actorId', 'recipientId', 'organizationId', 'boardId',
                'entityType', 'entityId', 'version', 'sequence', 'createdAt', 'metadata'].sort());
              expect(['NOTIFICATION_CREATED', 'NOTIFICATION_READ']).toContain(event.eventType);
              expect(event).toMatchObject({ organizationId: org, recipientId: recipient,
                actorId: event.eventType === 'NOTIFICATION_CREATED' ? issuer : recipient, boardId: board,
                entityType: 'Notification', version: event.eventType === 'NOTIFICATION_CREATED' ? 1 : 2, metadata: {} });
              expect(event.sequence).toMatch(/^[1-9][0-9]*$/); utc(event.createdAt);
              observed.events.push(event);
            }
          }
        });
      }); return observed;
    });
    for (const [index, client] of clients.entries()) {
      await client.goto(`/app/${org}/notifications`); await expect(client.getByRole('article')).toHaveCount(0);
      await expect.poll(() => live[index].snapshots).toBeGreaterThan(0); expect(live[index].events).toEqual([]);
    }
    const commands: { route: string; method: string; key: string; data: object; body: string }[] = [];
    async function command(method: string, route: string, data: object, status = 200) {
      const key = crypto.randomUUID(); const reply = await peer.request.fetch(route, { method, headers: { ...headers, 'Idempotency-Key': key }, data });
      expect(reply.status()).toBe(status); const body = await reply.text(); commands.push({ route, method, key, data, body }); return JSON.parse(body);
    }
    async function delivered(count: number) {
      await waitForBoardDelivery(peer.request, board);
      for (const [index, client] of clients.entries()) {
        await expect(client.getByRole('article')).toHaveCount(count, { timeout: 25_000 }); await expect.poll(() => live[index].events.length).toBe(count);
      }
    }
    const card = (await command('POST', `/lists/${lists[0]}/cards`, { title: 'Configured watched work' }, 201)).id; await delivered(1);
    const copied = (await command('POST', `/cards/${card}/copy`, { sourceBoardId: board, destinationListId: lists[1], title: 'Copied watched work', expectedVersion: 1 })).id; await delivered(2);
    expect((await command('PATCH', `/cards/${card}`, { title: 'Updated watched work', description: '', version: 1 })).version).toBe(2); await delivered(3);
    expect((await command('POST', `/cards/${card}/move`, { sourceBoardId: board, destinationListId: lists[1], expectedVersion: 2 })).version).toBe(3); await delivered(4);
    expect((await command('PUT', `/cards/${card}/members/${issuer}?version=3`, {})).card.version).toBe(4); await delivered(5);
    expect((await command('DELETE', `/cards/${card}/members/${issuer}?version=4`, {})).card.version).toBe(5); await delivered(6);
    const labelReply = await peer.request.post(`/boards/${board}/labels`, { headers, data: { name: 'Matrix label', color: 'blue' } }); expect(labelReply.status()).toBe(201);
    const label = (await labelReply.json()).id;
    expect((await command('PUT', `/cards/${card}/labels/${label}?version=5`, {})).card.version).toBe(6); await delivered(7);
    expect((await command('DELETE', `/cards/${card}/labels/${label}?version=6`, {})).card.version).toBe(7); await delivered(8);
    const dates = { startAt: null, dueAt: '2040-01-02T12:00:00Z', dueTimezone: 'UTC', dueHasTime: true };
    for (const [index, dueComplete] of [false, true, false].entries()) {
      expect((await command('PATCH', `/cards/${card}/dates`, { ...dates, dueComplete, version: 7 + index })).card.version).toBe(8 + index); await delivered(9 + index);
    }
    expect((await command('POST', `/cards/${card}/archive`, { version: 10 })).version).toBe(11); await waitForBoardDelivery(peer.request, board);
    for (const client of clients) await expect(client.getByRole('article')).toHaveCount(1, { timeout: 25_000 });
    const hidden = await context.request.get(`/organizations/${org}/notifications`); expect(hidden.status()).toBe(200);
    expect((await hidden.json()).items.map((item: { entityId: string }) => item.entityId)).toEqual([copied]);
    const hiddenSync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(hiddenSync.status()).toBe(200);
    const hiddenJournal = await hiddenSync.json(); expect(hiddenJournal.cursor).toBe('12'); expect(hiddenJournal.events).toHaveLength(1);
    // Let each actual stream advance past the hidden archive intent before restore.
    for (const observed of live) { await expect.poll(() => observed.cursor).toBe('12'); expect(observed.events).toHaveLength(11); }
    expect((await command('POST', `/cards/${card}/restore`, { version: 11 })).version).toBe(12); await waitForBoardDelivery(peer.request, board);
    for (const client of clients) await expect(client.getByRole('article')).toHaveCount(13, { timeout: 25_000 });
    const inbox = await context.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200); const rows = (await inbox.json()).items;
    const types = ['CARD_CREATED', 'CARD_COPIED', 'CARD_UPDATED', 'CARD_MOVED', 'CARD_MEMBER_ADDED', 'CARD_MEMBER_REMOVED', 'LABEL_ADDED', 'LABEL_REMOVED', 'CARD_DATE_CHANGED', 'CARD_DUE_COMPLETED', 'CARD_DUE_REOPENED', 'CARD_ARCHIVED', 'CARD_RESTORED'];
    expect(rows.map((row: { type: string }) => row.type).sort()).toEqual([...types].sort());
    for (const row of rows) expect(row).toMatchObject({ actorId: issuer, recipientId: recipient, boardId: board, readAt: null,
      entityId: row.type === 'CARD_COPIED' ? copied : card, entityLink: `/app/${org}/boards/${board}/cards/${row.type === 'CARD_COPIED' ? copied : card}` });
    expect(process.env.CI).toBe('true'); for (const id of [org, recipient]) expect(id).toMatch(/^[0-9a-f-]{36}$/);
    function query(sql: string) {
      return execFileSync('docker', ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c', 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'], { input: sql, encoding: 'utf8', stdio: 'pipe' }).trim();
    }
    const stored = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(n) ORDER BY created_at DESC,id DESC) FROM card_assignment_notifications n WHERE tenant_id='${org}' AND recipient_id='${recipient}';`));
    function utc(value: string) {
      expect(value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/);
      return value.replace(/\+00:00$/, 'Z').replace(/(\.\d*?)0+Z$/, '$1Z').replace(/\.Z$/, 'Z');
    }
    expect(stored).toHaveLength(13);
    for (const row of rows) {
      const persisted = stored.find((item: { id: string }) => item.id === row.id);
      expect(persisted).toMatchObject({ actor_id: issuer, recipient_id: recipient, board_id: board, card_id: row.entityId, notification_type: row.type, read_at: null });
      expect(utc(persisted.created_at)).toBe(utc(row.createdAt));
    }
    expect(query(`SELECT count(*) FROM card_assignment_notifications n JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE n.tenant_id='${org}' AND n.recipient_id='${recipient}' AND e.actor_id=n.actor_id AND e.board_id=n.board_id AND e.entity_id=n.card_id AND e.entity_version=n.card_version AND e.event_type=n.notification_type AND e.created_at=n.created_at;`)).toBe('13');
    const sync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200); const journal = (await sync.json()).events; expect(journal).toHaveLength(13);
    const persistedJournal = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM notification_events e WHERE tenant_id='${org}' AND recipient_id='${recipient}';`));
    expect(persistedJournal).toHaveLength(13);
    for (const [index, event] of (journal as PrivateEnvelope[]).entries()) {
      const persisted = persistedJournal[index]; const notification = stored.find((item: { id: string }) => item.id === event.entityId);
      expect(event).toEqual({ eventId: persisted.event_id, eventType: persisted.event_type, actorId: persisted.actor_id,
        recipientId: persisted.recipient_id, organizationId: persisted.tenant_id, boardId: persisted.board_id,
        entityType: 'Notification', entityId: persisted.notification_id, version: persisted.version,
        sequence: String(persisted.sequence), createdAt: event.createdAt, metadata: persisted.metadata });
      expect(event).toMatchObject({ actorId: issuer, recipientId: recipient, organizationId: org, boardId: board,
        eventType: 'NOTIFICATION_CREATED', version: 1, sequence: String(index + 1), metadata: {} });
      expect(utc(event.createdAt)).toBe(utc(persisted.created_at)); expect(utc(event.createdAt)).toBe(utc(notification.created_at));
    }
    const archiveId = rows.find((row: { type: string }) => row.type === 'CARD_ARCHIVED').id;
    const visibleEvents = journal.filter((event: { entityId: string }) => event.entityId !== archiveId);
    for (const [index, client] of clients.entries()) {
      await expect.poll(() => live[index].events.length).toBe(12);
      const canonical = (event: PrivateEnvelope) => ({ ...event, createdAt: utc(event.createdAt) });
      expect(live[index].events.map(canonical)).toEqual(visibleEvents.map(canonical));
      for (const caption of ['Card created', 'Card copied', 'Card updated', 'Card moved', 'Card member added', 'Card member removed',
        'Label added', 'Label removed', 'Card dates changed', 'Due date completed', 'Due date reopened', 'Card archived', 'Card restored'])
        await expect(client.getByText(`${caption} · Unread`, { exact: true })).toBeVisible();
      const links = await client.getByRole('link', { name: 'Open Card', exact: true }).evaluateAll(elements => elements.map(element => element.getAttribute('href')));
      expect(links).toHaveLength(13); expect(links.filter(href => href === `/app/${org}/boards/${board}/cards/${card}`)).toHaveLength(12);
      expect(links.filter(href => href === `/app/${org}/boards/${board}/cards/${copied}`)).toHaveLength(1);
      expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    function history() { return query(`SELECT md5(jsonb_build_object('cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='${org}'),'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}'),'journal',(SELECT jsonb_agg(to_jsonb(e) ORDER BY recipient_id,sequence) FROM notification_events e WHERE tenant_id='${org}'))::text);`); }
    const baseline = history();
    for (const original of commands) {
      const replay = await peer.request.fetch(original.route, { method: original.method, headers: { ...headers, 'Idempotency-Key': original.key }, data: original.data });
      expect(replay.status()).toBe(original.route.startsWith('/lists/') ? 201 : 200); expect(await replay.text()).toBe(original.body);
    }
    expect(history()).toBe(baseline); expect((await (await context.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual(rows);
    expect((await (await peer.request.get(`/organizations/${org}/notifications`)).json()).items).toEqual([]);
    expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${issuer}';`)).toBe('0');
    // Recover one real committed bulk read with the original selection/key,
    // while the other native client observes its thirteen private transitions.
    const attempts: { key: string; body: string }[] = []; let readAcknowledgment = '';
    const readPath = `/organizations/${org}/notifications/read`;
    await mobile.route(`**${readPath}`, async route => {
      if (route.request().method() !== 'POST') return route.continue();
      attempts.push({ key: route.request().headers()['idempotency-key'], body: route.request().postData()! });
      if (attempts.length > 1) expect(attempts[attempts.length - 1]).toEqual(attempts[0]);
      const committed = await route.fetch(); expect(committed.status()).toBe(200);
      if (attempts.length === 1) {
        readAcknowledgment = await committed.text();
        await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ code: 'service_unavailable' }) });
      } else { expect(await committed.text()).toBe(readAcknowledgment); await route.fulfill({ response: committed }); }
    });
    await activate(mobile.getByRole('button', { name: 'Select unread on this page', exact: true }));
    await expect(mobile.getByRole('checkbox', { checked: true })).toHaveCount(13);
    await activate(mobile.getByRole('button', { name: 'Mark selected read', exact: true }));
    const retryRead = mobile.getByRole('button', { name: 'Retry mark read', exact: true }); await expect(retryRead).toBeEnabled();
    await expect(page.getByText('0 unread on this page.', { exact: true })).toBeVisible({ timeout: 25_000 });
    await activate(retryRead); await expect(retryRead).toHaveCount(0);
    await expect(mobile.getByRole('button', { name: 'Refresh notifications', exact: true })).toBeFocused();
    expect(attempts).toHaveLength(2); expect(attempts[1]).toEqual(attempts[0]);
    expect([...JSON.parse(attempts[0].body).ids].sort()).toEqual(rows.map((row: { id: string }) => row.id).sort());
    await mobile.unroute(`**${readPath}`);
    const readReply = await context.request.get(`/organizations/${org}/notifications`); expect(readReply.status()).toBe(200);
    const readRows = (await readReply.json()).items; expect(readRows).toHaveLength(13);
    expect(readRows.map((row: { readAt: string | null }) => ({ ...row, readAt: null }))).toEqual(rows);
    expect(new Set(readRows.map((row: { readAt: string }) => utc(row.readAt))).size).toBe(1);
    const readSync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(readSync.status()).toBe(200);
    const readJournal = (await readSync.json()).events as PrivateEnvelope[]; expect(readJournal).toHaveLength(26);
    expect(readJournal.slice(0, 13)).toEqual(journal);
    const transitions = readJournal.slice(13); expect(transitions.map(event => event.entityId).sort()).toEqual(rows.map((row: { id: string }) => row.id).sort());
    const storedReadJournal = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM notification_events e WHERE tenant_id='${org}' AND recipient_id='${recipient}' AND event_type='NOTIFICATION_READ';`));
    const storedReadRows = JSON.parse(query(`SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}' AND recipient_id='${recipient}';`));
    expect(storedReadJournal).toHaveLength(13); expect(storedReadRows).toHaveLength(13);
    for (const [index, event] of transitions.entries()) {
      const persisted = storedReadJournal[index]; const notification = storedReadRows.find((item: { id: string }) => item.id === event.entityId);
      const visible = readRows.find((item: { id: string }) => item.id === event.entityId);
      expect(event).toEqual({ eventId: persisted.event_id, eventType: 'NOTIFICATION_READ', actorId: recipient,
        recipientId: recipient, organizationId: org, boardId: board, entityType: 'Notification', entityId: persisted.notification_id,
        version: 2, sequence: String(index + 14), createdAt: event.createdAt, metadata: {} });
      expect(persisted).toMatchObject({ actor_id: recipient, recipient_id: recipient, tenant_id: org, board_id: board, version: 2, event_type: 'NOTIFICATION_READ', metadata: {} });
      expect(utc(event.createdAt)).toBe(utc(persisted.created_at)); expect(utc(event.createdAt)).toBe(utc(notification.read_at));
      expect(utc(visible.readAt)).toBe(utc(notification.read_at));
    }
    for (const [index, client] of clients.entries()) {
      await expect(client.getByText('0 unread on this page.', { exact: true })).toBeVisible(); await expect(client.getByRole('article')).toHaveCount(13);
      await expect(client.getByRole('button', { name: 'Mark read', exact: true })).toHaveCount(0);
      await expect.poll(() => live[index].events.length).toBe(25);
      const canonical = (event: PrivateEnvelope) => ({ ...event, createdAt: utc(event.createdAt) });
      expect(live[index].events.slice(12).map(canonical)).toEqual(transitions.map(canonical));
      expect((await new AxeBuilder({ page: client }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze()).violations).toEqual([]);
      expect(await client.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    }
    const afterRead = history();
    const noOpRead = await context.request.post(readPath, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: JSON.parse(attempts[0].body) });
    expect(noOpRead.status()).toBe(200); expect(await noOpRead.text()).toBe(readAcknowledgment); expect(history()).toBe(afterRead);
    const finalSync = await context.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(finalSync.status()).toBe(200);
    expect((await finalSync.json()).events).toEqual(readJournal);
  } finally { try { restoreWorker(); } finally { await phone?.close(); await peer.close(); } }
});
