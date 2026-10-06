import { expect, test, type Page } from './releaseTest';
import { scopedBoardWorker } from './scopedBoardWorker';

type Probe = { socket: WebSocket; pages: Array<{ organizationId: string; userId: string; page: {
  cursor: string; resetRequired: boolean; events: Array<Record<string, unknown>>;
} }>; closed: boolean };
declare global { interface Window { metadataProbe?: Probe } }

async function subscribe(page: Page, organizationId: string, cursor: string | null) {
  return page.evaluate(async ({ organizationId, cursor }) => {
    window.metadataProbe?.socket.close();
    const response = await fetch('/organizations/live/metadata/negotiate?negotiateVersion=1', {
      method: 'POST', credentials: 'same-origin', headers: { 'X-StrataAI-Request': '1' },
    });
    if (!response.ok) throw new Error('Metadata negotiation refused');
    const negotiated = await response.json();
    const url = new URL('/organizations/live/metadata', location.href);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    url.searchParams.set('id', negotiated.connectionToken);
    const socket = new WebSocket(url);
    const probe: Probe = { socket, pages: [], closed: false }; window.metadataProbe = probe;
    socket.onclose = () => { probe.closed = true; };
    let handshake = false;
    socket.onmessage = message => {
      for (const raw of String(message.data).split('\x1e').filter(Boolean)) {
        const frame = JSON.parse(raw);
        if (!handshake) {
          if (frame.error) throw new Error('Metadata handshake refused');
          handshake = true;
          socket.send(JSON.stringify({ type: 4, invocationId: 'metadata', target: 'Watch', arguments: [organizationId, cursor] }) + '\x1e');
        } else if (frame.type === 2) probe.pages.push(frame.item);
      }
    };
    socket.onopen = () => socket.send('{"protocol":"json","version":1}\x1e');
    return url.toString();
  }, { organizationId, cursor });
}

for (const width of [1280, 390]) {
test(`PRD-03: genuine metadata streaming resumes original source identity and stops after logout at ${width}px`, async ({ page, context }) => {
  test.setTimeout(240_000);
  await page.setViewportSize({ width, height: 844 });
  const probe = await context.newPage();
  const closedStreams = new Set<string>();
  probe.on('websocket', socket => {
    if (new URL(socket.url()).pathname === '/organizations/live/metadata')
      socket.on('close', () => closedStreams.add(socket.url()));
  });
  const headers = { 'X-StrataAI-Request': '1' };
  const account = { email: `metadata-stream-${Date.now()}@example.test`, password: 'metadata-stream-correct-horse', displayName: 'Metadata stream Owner' };
  expect((await context.request.post('/auth/register', { headers, data: account })).status()).toBe(201);
  expect((await context.request.post('/auth/login', { headers, data: account })).status()).toBe(200);
  const created = await context.request.post('/organizations', { headers, data: { name: 'Canonical stream Organization' } });
  expect(created.status()).toBe(201); const organizationId = (await created.json()).organization.id;
  const me = await context.request.get('/me'); expect(me.status()).toBe(200); const actor = (await me.json()).id;
  let restoreWorker = () => {};
  try {
    restoreWorker = scopedBoardWorker(organizationId);
    await page.goto(`/app/${organizationId}`);
    // Keep the transport probe outside the app's logout navigation. Its close
    // must come from the server, rather than page teardown or route cleanup.
    await probe.goto('/api/runtime');
    await subscribe(probe, organizationId, null);
    await expect.poll(() => probe.evaluate(() => window.metadataProbe?.pages.length ?? 0), { timeout: 30_000 }).toBeGreaterThan(0);
    const initial = await probe.evaluate(() => window.metadataProbe!.pages[0]);
    expect(initial.organizationId).toBe(organizationId); expect(initial.userId).toBe(actor);
    expect(initial.page.resetRequired).toBe(true); expect(initial.page.events).toEqual([]);
    // Read current metadata after reset, then change it through the ordinary API.
    expect((await context.request.get(`/organizations/${organizationId}`)).status()).toBe(200);
    const update = await context.request.patch(`/organizations/${organizationId}`, { headers,
      data: { name: 'New canonical Organization name', version: 1 } });
    expect(update.status()).toBe(200);
    await expect(page.getByRole('heading', { name: 'New canonical Organization name', exact: true })).toBeVisible({ timeout: 30_000 });
    await expect.poll(() => probe.evaluate(() => window.metadataProbe!.pages.flatMap(p => p.page.events)
      .filter(e => e.eventType === 'ORGANIZATION_UPDATED').length), { timeout: 30_000 }).toBe(1);
    const event = await probe.evaluate(() => window.metadataProbe!.pages.flatMap(p => p.page.events)
      .find(e => e.eventType === 'ORGANIZATION_UPDATED')!);
    expect(Object.keys(event).sort()).toEqual(['actorId', 'boardId', 'createdAt', 'entityId', 'entityType', 'eventId', 'eventType', 'metadata', 'organizationId', 'version']);
    expect(event.actorId).toBe(actor); expect(event.organizationId).toBe(organizationId);
    expect(event.entityId).toBe(organizationId); expect(event.entityType).toBe('Organization');
    expect(event.boardId).toBeNull(); expect(event.metadata).toEqual({}); expect(event.version).toBe(2);
    const replay = await context.request.get(`/organizations/${organizationId}/metadata-events`, { params: { cursor: initial.page.cursor, expectedActorId: actor } });
    expect(replay.status()).toBe(200); expect((await replay.json()).events).toEqual([event]);
    const resumedUrl = await subscribe(probe, organizationId, initial.page.cursor);
    await expect.poll(() => probe.evaluate(() => window.metadataProbe!.pages.flatMap(p => p.page.events)
      .map(e => e.eventId)), { timeout: 30_000 }).toEqual([event.eventId]);
    expect((await context.request.post('/auth/logout', { headers })).status()).toBe(204);
    await expect.poll(() => closedStreams.has(resumedUrl), { timeout: 15_000 }).toBe(true);
    const refused = await context.request.get(`/organizations/${organizationId}/metadata-events`);
    expect(refused.status()).toBe(401);
  } finally {
    await probe.evaluate(() => window.metadataProbe?.socket.close()).catch(() => {});
    await probe.close().catch(() => {});
    restoreWorker();
  }
});
}
