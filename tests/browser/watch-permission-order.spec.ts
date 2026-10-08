import { expect, test } from './releaseTest';
import { commandOrderQuery as query, inObservedBoardOrder } from './observedBoardCommandOrder';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expectPersistedNotificationDelivery } from './persistedNotificationDelivery';

for (const role of ['MEMBER', 'ADMIN', 'ORGANIZATION_READER', 'PUBLIC_READER'] as const) {
test(`PRD-05/17: ${role} permission withdrawal and actual activity obey observed Board lock order`, async ({ context, browser, baseURL }) => {
  test.setTimeout(180_000);
  const member = await browser.newContext({ baseURL });
  const headers = { 'X-StrataAI-Request': '1' };
  const reader = role.endsWith('_READER');
  try {
    const suffix = crypto.randomUUID(), email = `permission-recipient-${suffix}@example.test`;
    const ownerAccount = await registerNotificationAccount(context.request, { email: `permission-owner-${suffix}@example.test`, password: 'permission-order-correct-horse', displayName: 'Permission owner' });
    const recipientAccount = await registerNotificationAccount(member.request, { email, password: 'permission-order-correct-horse', displayName: 'Permission watcher' });
    const owner = ownerAccount.user.id, recipient = recipientAccount.user.id;
    const orgReply = await context.request.post('/organizations', { headers, data: { name: 'Permission ordering' } });
    expect(orgReply.status()).toBe(201); const org = (await orgReply.json()).organization.id;
    const invitation = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invitation.status()).toBe(201);
    expect((await member.request.post(`/me/invitations/${(await invitation.json()).id}/accept`, { headers })).status()).toBe(200);
    const visibility = role === 'PUBLIC_READER' ? 'PUBLIC' : reader ? 'ORGANIZATION' : 'PRIVATE';
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Permission Board', visibility } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    const grant = () => context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role } });
    if (!reader) expect((await grant()).status()).toBe(200);
    const admitted = await member.request.get(`/boards/${board}`); expect(admitted.status()).toBe(200);
    const access = (await admitted.json()).access; expect(access.canView).toBe(true);
    expect(access.canEdit).toBe(!reader);
    if (role === 'ADMIN') expect(access.canAdminister).toBe(true);
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Permission List' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Permission Card' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    for (const id of [org, board, list, card, owner, recipient]) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    const watches = [];
    for (const [type, entity] of [['CARD', card], ['LIST', list], ['BOARD', board]]) {
      const path = `/watch/${type}/${entity}`;
      const response = await member.request.put(`${path}?version=0`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} });
      expect(response.status()).toBe(200); const body = await response.json();
      expect(body).toMatchObject({ watching: true, version: 1 }); watches.push({ path, body });
    }
    const watchState = () => query(`SELECT md5(jsonb_agg(to_jsonb(w) ORDER BY id)::text) FROM watch_subscriptions w WHERE tenant_id='${org}' AND user_id='${recipient}';`);
    const originalWatches = watchState();
    const privateState = () => query(`SELECT md5(jsonb_build_object(
      'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='${org}'),
      'watches',(SELECT jsonb_agg(to_jsonb(w) ORDER BY id) FROM watch_subscriptions w WHERE tenant_id='${org}'),
      'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='${org}'),
      'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='${org}'),
      'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='${org}'),
      'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}'),
      'journal',(SELECT jsonb_agg(to_jsonb(e) ORDER BY recipient_id,sequence) FROM notification_events e WHERE tenant_id='${org}'),
      'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s WHERE tenant_id='${org}'),
      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='${org}'))::text);`);
    const retainedState = () => query(`SELECT md5(jsonb_build_object('notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}'),'journal',(SELECT jsonb_agg(to_jsonb(e) ORDER BY recipient_id,sequence) FROM notification_events e WHERE tenant_id='${org}'),'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s WHERE tenant_id='${org}'))::text);`);
    let version = 1;
    for (const withdrawalFirst of [true, false]) {
      const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
      const boardVersion = (await current.json()).board.version;
      const withdraw = () => reader
        ? context.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility: 'PRIVATE', version: boardVersion } })
        : context.request.delete(`/boards/${board}/members/${recipient}`, { headers });
      const sourceBody = { title: `Permission ${role} ${withdrawalFirst ? 'withdrawal' : 'activity'} first`, description: null, version };
      const key = crypto.randomUUID();
      const source = () => context.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': key }, data: sourceBody });
      const replies = await inObservedBoardOrder(org, board, withdrawalFirst ? withdraw : source, withdrawalFirst ? source : withdraw);
      expect(replies[withdrawalFirst ? 0 : 1].status()).toBe(reader ? 200 : 204);
      const sourceReply = replies[withdrawalFirst ? 1 : 0]; expect(sourceReply.status()).toBe(200);
      const sourceText = await sourceReply.text(); version++;
      expect(JSON.parse(sourceText)).toMatchObject({ id: card, version, title: sourceBody.title });
      expect(query(`SELECT count(*) FROM work_events WHERE tenant_id='${org}' AND board_id='${board}' AND actor_id='${owner}' AND entity_type='Card' AND entity_id='${card}' AND entity_version=${version} AND event_type='CARD_UPDATED';`)).toBe('1');
      expect(query(`SELECT count(*) FROM card_assignment_notifications n JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE n.tenant_id='${org}' AND n.recipient_id='${recipient}' AND n.actor_id='${owner}' AND n.notification_type='CARD_UPDATED' AND e.entity_id='${card}' AND e.entity_version=${version} AND e.event_type='CARD_UPDATED';`)).toBe(withdrawalFirst ? '0' : '1');
      expect(watchState()).toBe(originalWatches);
      const inbox = await member.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200); expect((await inbox.json()).items).toEqual([]);
      const sync = await member.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200); expect((await sync.json()).events).toEqual([]);
      for (const watch of watches) expect((await member.request.get(watch.path)).status()).toBe(404);
      const committed = privateState(), retained = retainedState();
      if (!withdrawalFirst) {
        const id = query(`SELECT id FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}';`);
        expect(id).toMatch(/^[0-9a-f-]{36}$/);
        for (const bulk of [false, true]) {
          const denied = await member.request.post(`/organizations/${org}/notifications/${bulk ? 'read' : `${id}/read`}`, {
            headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, ...(bulk ? { data: { ids: [id] } } : {}) });
          expect(denied.status()).toBe(404); expect((await denied.json()).code).toBe('notification_not_found');
        }
      }
      const replay = await source(); expect(replay.status()).toBe(200); expect(await replay.text()).toBe(sourceText);
      expect(privateState()).toBe(committed);
      if (reader) {
        const deniedBoard = await context.request.get(`/boards/${board}`); expect(deniedBoard.status()).toBe(200);
        expect((await context.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility, version: (await deniedBoard.json()).board.version } })).status()).toBe(200);
      } else expect((await grant()).status()).toBe(200);
      for (const watch of watches) {
        const restored = await member.request.get(watch.path); expect(restored.status()).toBe(200); expect(await restored.json()).toEqual({ ...watch.body, changed: false });
      }
      expect(watchState()).toBe(originalWatches); expect(retainedState()).toBe(retained);
      if (!withdrawalFirst) await expectPersistedNotificationDelivery(member.request, org, recipient, []);
      else {
        const empty = await member.request.get(`/organizations/${org}/notifications`); expect(empty.status()).toBe(200); expect((await empty.json()).items).toEqual([]);
      }
    }
    expect(query(`SELECT version FROM cards WHERE tenant_id='${org}' AND id='${card}';`)).toBe('3');
    expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}';`)).toBe('1');
    // Each read race uses fresh notifications from actual source commands. Bulk
    // selects two distinct sources, so denial must preserve the entire selection.
    for (const bulk of [false, true]) for (const withdrawalFirst of [true, false]) {
      const ids: string[] = [];
      const sources: { body: { title: string; description: null; version: number }; key: string; text: string }[] = [];
      for (let index = 0; index < (bulk ? 2 : 1); index++) {
        const body = { title: `Read order ${role} ${bulk} ${withdrawalFirst} ${index}`, description: null, version };
        const key = crypto.randomUUID();
        const response = await context.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': key }, data: body });
        expect(response.status()).toBe(200); const text = await response.text(); version++;
        expect(JSON.parse(text)).toMatchObject({ id: card, version, title: body.title }); sources.push({ body, key, text });
        const id = query(`SELECT n.id FROM card_assignment_notifications n JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE n.tenant_id='${org}' AND n.recipient_id='${recipient}' AND e.entity_id='${card}' AND e.entity_version=${version} AND e.event_type='CARD_UPDATED';`);
        expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/); ids.push(id);
      }
      expect(new Set(ids).size).toBe(ids.length);
      const selection = ids.map(id => `'${id}'`).join(',');
      expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND id IN (${selection}) AND read_at IS NULL;`)).toBe(String(ids.length));
      const current = await context.request.get(`/boards/${board}`); expect(current.status()).toBe(200);
      const boardVersion = (await current.json()).board.version;
      const withdraw = () => reader
        ? context.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility: 'PRIVATE', version: boardVersion } })
        : context.request.delete(`/boards/${board}/members/${recipient}`, { headers });
      const key = crypto.randomUUID();
      const read = () => member.request.post(`/organizations/${org}/notifications/${bulk ? 'read' : `${ids[0]}/read`}`, {
        headers: { ...headers, 'Idempotency-Key': key }, ...(bulk ? { data: { ids } } : {}) });
      const before = retainedState();
      const receiptCount = () => Number(query(`SELECT count(*) FROM work_command_replays WHERE tenant_id='${org}' AND actor_id='${recipient}';`));
      const beforeReceipts = receiptCount();
      const replies = await inObservedBoardOrder(org, board, withdrawalFirst ? withdraw : read, withdrawalFirst ? read : withdraw);
      expect(replies[withdrawalFirst ? 0 : 1].status()).toBe(reader ? 200 : 204);
      const readReply = replies[withdrawalFirst ? 1 : 0]; expect(readReply.status()).toBe(withdrawalFirst ? 404 : 200);
      let originalReadText: string | undefined;
      if (withdrawalFirst) {
        expect((await readReply.json()).code).toBe('notification_not_found');
        expect(retainedState()).toBe(before); expect(receiptCount()).toBe(beforeReceipts);
        expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND id IN (${selection}) AND read_at IS NULL;`)).toBe(String(ids.length));
      } else {
        originalReadText = await readReply.text();
        const result = JSON.parse(originalReadText); expect(result.organizationId).toBe(org);
        expect(result.items.map((item: { id: string }) => item.id).sort()).toEqual([...ids].sort());
        expect(new Set(result.items.map((item: { readAt: string }) => item.readAt)).size).toBe(1);
        expect(receiptCount()).toBe(beforeReceipts + 1);
      }
      expect(query(`SELECT count(*) FROM notification_events WHERE tenant_id='${org}' AND recipient_id='${recipient}' AND notification_id IN (${selection}) AND event_type='NOTIFICATION_READ';`)).toBe(withdrawalFirst ? '0' : String(ids.length));
      const committed = privateState(), retained = retainedState();
      const deniedRetry = await read(); expect(deniedRetry.status()).toBe(404); expect((await deniedRetry.json()).code).toBe('notification_not_found');
      const inbox = await member.request.get(`/organizations/${org}/notifications`); expect(inbox.status()).toBe(200); expect((await inbox.json()).items).toEqual([]);
      const sync = await member.request.get(`/organizations/${org}/notifications/sync?after=0`); expect(sync.status()).toBe(200); expect((await sync.json()).events).toEqual([]);
      for (const source of sources) {
        const replay = await context.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': source.key }, data: source.body });
        expect(replay.status()).toBe(200); expect(await replay.text()).toBe(source.text);
      }
      expect(privateState()).toBe(committed); expect(watchState()).toBe(originalWatches);
      if (reader) {
        const hidden = await context.request.get(`/boards/${board}`); expect(hidden.status()).toBe(200);
        expect((await context.request.patch(`/boards/${board}/visibility`, { headers, data: { visibility, version: (await hidden.json()).board.version } })).status()).toBe(200);
      } else expect((await grant()).status()).toBe(200);
      expect(retainedState()).toBe(retained); expect(watchState()).toBe(originalWatches);
      const authorized = await read(); expect(authorized.status()).toBe(200); const text = await authorized.text();
      if (originalReadText !== undefined) expect(text).toBe(originalReadText);
      else originalReadText = text;
      const acknowledgment = JSON.parse(text); expect(acknowledgment.organizationId).toBe(org);
      expect(acknowledgment.items.map((item: { id: string }) => item.id).sort()).toEqual([...ids].sort());
      expect(new Set(acknowledgment.items.map((item: { readAt: string }) => item.readAt)).size).toBe(1);
      expect(receiptCount()).toBe(beforeReceipts + 1);
      expect(query(`SELECT count(DISTINCT read_at) FROM card_assignment_notifications WHERE tenant_id='${org}' AND id IN (${selection});`)).toBe('1');
      expect(query(`SELECT count(*) FROM notification_events WHERE tenant_id='${org}' AND recipient_id='${recipient}' AND notification_id IN (${selection}) AND event_type='NOTIFICATION_READ';`)).toBe(String(ids.length));
      await expectPersistedNotificationDelivery(member.request, org, recipient, []);
      const acknowledged = privateState();
      const retry = await read(); expect(retry.status()).toBe(200); expect(await retry.text()).toBe(originalReadText);
      expect(privateState()).toBe(acknowledged);
    }
    expect(query(`SELECT version FROM cards WHERE tenant_id='${org}' AND id='${card}';`)).toBe('9');
    expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}';`)).toBe('7');
    expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}' AND read_at IS NOT NULL;`)).toBe('6');
    expect(query(`SELECT count(*) FROM notification_events WHERE tenant_id='${org}' AND recipient_id='${recipient}';`)).toBe('13');
  } finally { await member.close(); }
});
}
