import { execFileSync, spawn } from 'node:child_process';
import { expect, test } from './releaseTest';
import { registerNotificationAccount } from './notificationAccountFixture';
import { expectPersistedNotificationDelivery } from './persistedNotificationDelivery';

const sqlArgs = ['compose', '-f', 'compose.release.yml', 'exec', '-T', 'postgres', 'sh', '-c',
  'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'];
const query = (sql: string) => execFileSync('docker', sqlArgs, { input: sql, encoding: 'utf8', stdio: 'pipe' }).trim();

test('PRD-17-TC-07/08: actual unwatch and activity commands select Card List Board eligibility in observed lock order', async ({ context, browser, baseURL }) => {
  test.setTimeout(180_000); expect(process.env.CI).toBe('true');
  const member = await browser.newContext({ baseURL }); const headers = { 'X-StrataAI-Request': '1' };
  try {
    const suffix = crypto.randomUUID(); const email = `trigger-member-${suffix}@example.test`;
    const ownerAccount = await registerNotificationAccount(context.request, { email: `trigger-owner-${suffix}@example.test`, password: 'trigger-order-correct-horse', displayName: 'Trigger owner' });
    const owner = ownerAccount.user.id;
    const account = await registerNotificationAccount(member.request, { email, password: 'trigger-order-correct-horse', displayName: 'Trigger watcher' });
    const recipient = account.user.id;
    const organization = await context.request.post('/organizations', { headers, data: { name: 'Trigger order' } });
    expect(organization.status()).toBe(201); const org = (await organization.json()).organization.id;
    const invite = await context.request.post(`/organizations/${org}/invitations`, { headers, data: { email, surface: 'INTERNAL', targetRole: 'MEMBER' } });
    expect(invite.status()).toBe(201);
    expect((await member.request.post(`/me/invitations/${(await invite.json()).id}/accept`, { headers })).status()).toBe(200);
    const boardReply = await context.request.post('/boards', { headers, data: { organizationId: org, name: 'Trigger Board', visibility: 'PRIVATE' } });
    expect(boardReply.status()).toBe(201); const board = (await boardReply.json()).id;
    expect((await context.request.patch(`/boards/${board}/members/${recipient}`, { headers, data: { role: 'MEMBER' } })).status()).toBe(200);
    const listReply = await context.request.post(`/boards/${board}/lists`, { headers, data: { name: 'Trigger List' } });
    expect(listReply.status()).toBe(201); const list = (await listReply.json()).id;
    const cardReply = await context.request.post(`/lists/${list}/cards`, { headers, data: { title: 'Trigger Card' } });
    expect(cardReply.status()).toBe(201); const card = (await cardReply.json()).id;
    for (const id of [org, board, list, card, recipient, owner]) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);
    const waiters = () => JSON.parse(query(`SELECT COALESCE(jsonb_agg(pid ORDER BY pid),'[]') FROM pg_stat_activity WHERE datname=current_database() AND usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';`)) as number[];
    const history = () => query(`SELECT md5(jsonb_build_object(
      'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='${org}'),
      'watches',(SELECT jsonb_agg(to_jsonb(w) ORDER BY id) FROM watch_subscriptions w WHERE tenant_id='${org}'),
      'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='${org}'),
      'journal',(SELECT jsonb_agg(to_jsonb(j) ORDER BY recipient_id,sequence) FROM notification_events j WHERE tenant_id='${org}'),
      'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s WHERE tenant_id='${org}'),
      'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='${org}'),
      'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='${org}'),
      'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='${org}'),
      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='${org}'))::text);`);
    let version = 1; let notifications = 0;
    for (const [type, entity] of [['CARD', card], ['LIST', list], ['BOARD', board]]) {
      const path = `/watch/${type}/${entity}`;
      const enabled = await member.request.put(`${path}?version=0`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} });
      expect(enabled.status()).toBe(200); const originalWatch = await enabled.json(); expect(originalWatch).toMatchObject({ watching: true, version: 1 });
      for (const unwatchFirst of [true, false]) {
        if (!unwatchFirst) {
          const restored = await member.request.put(`${path}?version=2`, { headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() }, data: {} });
          expect(restored.status()).toBe(200); expect(await restored.json()).toMatchObject({ watching: true, version: 3, subscriptionId: originalWatch.subscriptionId, createdAt: originalWatch.createdAt });
        }
        const watchVersion = unwatchFirst ? 1 : 3;
        const watchKey = crypto.randomUUID(), sourceKey = crypto.randomUUID();
        const sourceBody = { title: `Trigger ${type} ${unwatchFirst ? 'unwatch' : 'activity'} first`, description: null, version };
        const source = () => context.request.patch(`/cards/${card}`, { headers: { ...headers, 'Idempotency-Key': sourceKey }, data: sourceBody });
        const unwatch = () => member.request.delete(`${path}?version=${watchVersion}`, { headers: { ...headers, 'Idempotency-Key': watchKey }, data: {} });
        const gate = spawn('docker', sqlArgs, { stdio: 'pipe' }); let output = '', closed = false, exitCode: number | null = null;
        gate.stdout.on('data', chunk => { output += chunk.toString(); }); gate.stderr.on('data', () => {});
        gate.on('close', code => { closed = true; exitCode = code; }); gate.on('error', () => { closed = true; });
        const pending: ReturnType<typeof source>[] = [];
        try {
          gate.stdin.write(`BEGIN; SELECT id FROM boards WHERE tenant_id='${org}' AND id='${board}' FOR UPDATE;\n\\echo trigger_gate_locked\n`);
          await expect.poll(() => output.includes('trigger_gate_locked')).toBe(true);
          pending.push(unwatchFirst ? unwatch() : source());
          await expect.poll(() => waiters().length).toBe(1); const firstPid = waiters()[0];
          pending.push(unwatchFirst ? source() : unwatch());
          await expect.poll(() => waiters().length).toBe(2); const secondPid = waiters().find(pid => pid !== firstPid)!;
          expect(Number.isInteger(firstPid) && firstPid > 0 && Number.isInteger(secondPid) && secondPid > 0).toBe(true);
          await expect.poll(() => query(`SELECT ${firstPid}=ANY(pg_blocking_pids(${secondPid}));`)).toBe('t');
          gate.stdin.end('COMMIT;\n\\q\n'); await expect.poll(() => closed).toBe(true); expect(exitCode).toBe(0);
          const replies = await Promise.all(pending); for (const reply of replies) expect(reply.status()).toBe(200);
          const sourceReply = replies[unwatchFirst ? 1 : 0], watchReply = replies[unwatchFirst ? 0 : 1];
          const sourceText = await sourceReply.text(), watchText = await watchReply.text();
          version++; expect(JSON.parse(sourceText)).toMatchObject({ id: card, version, title: sourceBody.title });
          expect(query(`SELECT count(*) FROM work_events WHERE tenant_id='${org}' AND board_id='${board}' AND actor_id='${owner}' AND entity_type='Card' AND entity_id='${card}' AND entity_version=${version} AND event_type='CARD_UPDATED';`)).toBe('1');
          expect(JSON.parse(watchText)).toMatchObject({ watching: false, version: watchVersion + 1, subscriptionId: originalWatch.subscriptionId, createdAt: originalWatch.createdAt });
          if (!unwatchFirst) notifications++;
          expect(query(`SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='${org}' AND recipient_id='${recipient}';`)).toBe(String(notifications));
          const counts = query(`SELECT count(*) FROM card_assignment_notifications n JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE e.tenant_id='${org}' AND e.entity_id='${card}' AND e.entity_version=${version} AND e.event_type='CARD_UPDATED' AND e.actor_id='${owner}' AND n.actor_id='${owner}' AND n.notification_type='CARD_UPDATED' AND n.recipient_id='${recipient}';`);
          expect(counts).toBe(unwatchFirst ? '0' : '1');
          const committed = history();
          const replayedSource = await source(); expect(replayedSource.status()).toBe(200); expect(await replayedSource.text()).toBe(sourceText);
          const replayedWatch = await unwatch(); expect(replayedWatch.status()).toBe(200); expect(await replayedWatch.text()).toBe(watchText);
          expect(history()).toBe(committed);
        } finally {
          if (!closed) { gate.stdin.end('ROLLBACK;\n\\q\n'); await expect.poll(() => closed, { timeout: 10_000 }).toBe(true); }
          await Promise.allSettled(pending);
        }
      }
    }
    expect(notifications).toBe(3);
    expect(query(`SELECT version FROM cards WHERE tenant_id='${org}' AND id='${card}';`)).toBe('7');
    expect(query(`SELECT count(*) FROM watch_subscriptions WHERE tenant_id='${org}' AND user_id='${recipient}' AND NOT watching AND version=4;`)).toBe('3');
    await expectPersistedNotificationDelivery(member.request, org, recipient, []);
  } finally { await member.close(); }
});
