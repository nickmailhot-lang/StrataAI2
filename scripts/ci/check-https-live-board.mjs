import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
let stage = 'configuration';

export function sessionCookie(jar) {
  const entries = jar.split(/\r?\n/).map(row => row.replace(/^#HttpOnly_/, ''))
    .filter(row => row && !row.startsWith('#')).map(row => row.split('\t'))
    .filter(fields => fields[5] === 'strataai_session');
  assert.equal(entries.length, 1, 'Exactly one synthetic session is required');
  const [domain, , path, secure, , name, value] = entries[0];
  assert.equal(domain.replace(/^\./, ''), 'localhost');
  assert.equal(path, '/'); assert.equal(secure, 'TRUE');
  assert.match(value, /^[A-Za-z0-9_-]{20,200}$/);
  return `${name}=${value}`;
}

export function expectedCardEvent(event, scope, version) {
  return event?.organizationId === scope.organization && event.boardId === scope.board
    && event.entityId === scope.card && event.actorId === scope.actor
    && event.entityType === 'Card' && event.eventType === 'CARD_UPDATED' && event.version === version;
}

export function assertAnonymousDenied(probe) {
  assert.ok(probe.handshake && probe.closed, 'Denial must follow successful TLS/SignalR admission');
  assert.equal(probe.protocolFailure, false, 'Malformed private output is not a safe denial');
  assert.equal(probe.buffer, '', 'Partial private output is not a safe denial');
  assert.equal(probe.frames.length, 0);
}

export function checkPage(page, scope) {
  assert.deepEqual(Object.keys(page).sort(), ['cursor', 'events', 'hasMore', 'pending', 'resetRequired']);
  assert.match(page.cursor, /^(0|[1-9][0-9]{0,18})$/);
  assert.ok(BigInt(page.cursor) <= 9223372036854775807n);
  for (const field of ['hasMore', 'pending', 'resetRequired']) assert.equal(typeof page[field], 'boolean');
  assert.ok(Array.isArray(page.events) && page.events.length <= 100);
  const identities = new Set(); let sequence = 0n;
  for (const event of page.events) {
    assert.deepEqual(Object.keys(event).sort(), ['actorId','boardId','createdAt','entityId','entityType','eventId','eventType','metadata','organizationId','sequence','version']);
    assert.equal(event.organizationId, scope.organization); assert.equal(event.boardId, scope.board);
    assert.match(event.eventId, uuid); assert.notEqual(event.eventId, '00000000-0000-0000-0000-000000000000');
    assert.ok(!identities.has(event.eventId)); identities.add(event.eventId);
    assert.match(event.sequence, /^[1-9][0-9]{0,18}$/);
    assert.ok(BigInt(event.sequence) > sequence && BigInt(event.sequence) <= BigInt(page.cursor)); sequence = BigInt(event.sequence);
    assert.ok(Number.isFinite(Date.parse(event.createdAt)));
    assert.deepEqual(event.metadata, {});
  }
}

class Watch {
  constructor(origin, cookie, scope, cursor = '0') {
    this.frames = []; this.handshake = false; this.closed = false; this.failure = null; this.buffer = ''; this.protocolFailure = false;
    this.socket = new WebSocket(origin.replace(/^https:/, 'wss:') + '/boards/live', {
      headers: { Origin: origin, ...(cookie ? { Cookie: cookie } : {}) },
    });
    this.socket.addEventListener('open', () => this.send({ protocol: 'json', version: 1 }));
    this.socket.addEventListener('message', event => {
      try {
        assert.equal(typeof event.data, 'string'); this.buffer += event.data;
        assert.ok(this.buffer.length < 262144);
        let end;
        while ((end = this.buffer.indexOf('\x1e')) >= 0) {
          const message = JSON.parse(this.buffer.slice(0, end)); this.buffer = this.buffer.slice(end + 1);
          if (!this.handshake) {
            assert.deepEqual(message, {}); this.handshake = true;
            this.send({ type: 4, invocationId: 'tls-proof', target: 'Watch', arguments: [scope.board, cursor] });
          } else if (message.type === 2) {
            assert.equal(message.invocationId, 'tls-proof'); checkPage(message.item, scope);
            assert.ok(this.frames.length < 200); this.frames.push(message.item);
          } else if (message.type === 7 || message.type === 3) {
            this.socket.close();
          } else assert.equal(message.type, 6, 'Only SignalR ping messages may be ignored');
        }
      } catch {
        this.protocolFailure = true;
        this.failure = new Error('Invalid live response'); this.socket.close();
      }
    });
    this.socket.addEventListener('error', () => { this.failure ??= new Error('Live transport failed'); });
    this.socket.addEventListener('close', () => {
      this.closed = true; if (this.buffer) this.protocolFailure = true; clearInterval(this.heartbeat);
    });
    this.heartbeat = setInterval(() => { if (this.socket.readyState === WebSocket.OPEN) this.send({ type: 6 }); }, 10000);
  }
  send(message) { this.socket.send(JSON.stringify(message) + '\x1e'); }
  async wait(predicate, timeout = 20000, allowDenied = false) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
      if (predicate()) return;
      if (!allowDenied && (this.failure || this.closed)) throw new Error('Live stream ended before expected evidence');
      await delay(25);
    }
    throw new Error('Live evidence deadline exceeded');
  }
  async close() {
    clearInterval(this.heartbeat); this.socket.close();
    await this.wait(() => this.closed, 5000, true);
  }
}

async function run(originValue, cookiePath, organization, board, card, actor) {
  const origin = new URL(originValue);
  assert.equal(origin.protocol, 'https:'); assert.equal(origin.hostname, 'localhost');
  assert.equal(origin.pathname, '/'); assert.ok(origin.port);
  assert.equal(origin.username + origin.password + origin.search + origin.hash, '');
  assert.notEqual(process.env.NODE_TLS_REJECT_UNAUTHORIZED, '0');
  const scope = { organization, board, card, actor };
  for (const value of Object.values(scope)) assert.match(value, uuid);
  const cookie = sessionCookie(readFileSync(cookiePath, 'utf8'));
  const url = origin.origin;
  async function request(method, path, status, body, requestOrigin = url) {
    const reply = await fetch(url + path, {
      method, headers: { Cookie: cookie, Origin: requestOrigin, 'X-StrataAI-Request': '1',
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body), redirect: 'error', signal: AbortSignal.timeout(10000),
    });
    assert.equal(reply.status, status); return reply;
  }
  const sockets = [];
  try {
    stage = 'authorized initial stream';
    const live = new Watch(url, cookie, scope); sockets.push(live);
    await live.wait(() => live.frames.some(page => !page.pending && !page.hasMore && BigInt(page.cursor) > 0n));
    assert.ok(!live.failure && !live.protocolFailure);
    stage = 'foreign origin refusal';
    const deniedOrigin = await request('POST', '/boards/live/negotiate?negotiateVersion=1', 403, {}, 'https://foreign.example.test');
    assert.equal((await deniedOrigin.json()).code, 'realtime_origin_denied');
    assert.match(deniedOrigin.headers.get('cache-control'), /no-store/);

    stage = 'anonymous stream refusal';
    const anonymous = new Watch(url, '', scope); sockets.push(anonymous);
    await anonymous.wait(() => anonymous.closed, 10000, true);
    assertAnonymousDenied(anonymous);

    stage = 'Worker-delivered live edit';
    const liveTitle = 'Private TLS live edit';
    const changed = await (await request('PATCH', `/cards/${card}`, 200, { title: liveTitle, version: 1 })).json();
    assert.equal(changed.version, 2);
    await live.wait(() => live.frames.some(page => page.events.some(event => expectedCardEvent(event, scope, 2))));
    assert.ok(!live.failure && !live.protocolFailure);
    assert.ok(!JSON.stringify(live.frames).includes(liveTitle));
    const cursor = live.frames.at(-1).cursor;
    await live.close();
    stage = 'persisted cursor recovery';
    const recoveredTitle = 'Private TLS disconnected edit';
    assert.equal((await (await request('PATCH', `/cards/${card}`, 200, { title: recoveredTitle, version: 2 })).json()).version, 3);
    const recovered = new Watch(url, cookie, scope, cursor); sockets.push(recovered);
    await recovered.wait(() => recovered.frames.some(page => page.events.some(event => expectedCardEvent(event, scope, 3))));
    const delivered = recovered.frames.flatMap(page => page.events);
    assert.equal(delivered.some(event => expectedCardEvent(event, scope, 2)), false);
    assert.ok(!JSON.stringify(recovered.frames).includes(recoveredTitle));
    const persisted = await (await request('GET', `/boards/${board}/sync?since=${cursor}`, 200)).json();
    checkPage(persisted, scope);
    assert.equal(delivered.find(event => expectedCardEvent(event, scope, 3)).eventId,
      persisted.events.find(event => expectedCardEvent(event, scope, 3)).eventId);
    assert.ok(BigInt(recovered.frames.at(-1).cursor) > BigInt(cursor));
    const graph = await (await request('GET', `/boards/${board}`, 200)).json();
    const saved = graph.lists.flatMap(list => list.cards).find(item => item.id === card);
    assert.equal(saved.title, recoveredTitle); assert.equal(saved.version, 3);
    stage = 'logout withdrawal';
    assert.ok(!recovered.closed && !recovered.failure && !recovered.protocolFailure);
    assert.equal(recovered.socket.readyState, WebSocket.OPEN);
    await request('POST', '/auth/logout', 204, {});
    await recovered.wait(() => recovered.closed, 8000, true);
    assert.equal(recovered.protocolFailure, false);
    await request('GET', '/me', 401);
    console.log('HTTPS WSS Board passed: Worker-delivered edit, persisted cursor recovery, anonymous/origin refusal, content-free frames and logout withdrawal.');
  } finally {
    await Promise.allSettled(sockets.map(socket => socket.close()));
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const deadline = setTimeout(() => { console.error('HTTPS live Board deadline exceeded.'); process.exit(1); }, 70000);
  try { assert.equal(process.argv.length, 8); await run(...process.argv.slice(2)); }
  catch { console.error(`HTTPS live Board contract failed during ${stage}.`); process.exitCode = 1; }
  finally { clearTimeout(deadline); }
  if (process.exitCode) process.exit(process.exitCode);
}
