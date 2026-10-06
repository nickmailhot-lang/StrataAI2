import { createOrganizationLifecycleConnection, validateOrganizationLifecycle, watchOrganizationLifecycle } from './organizationLifecycleLive';
const built = vi.hoisted(() => ({ connection: null as unknown }));
vi.mock('@microsoft/signalr', async importOriginal => ({
  ...await importOriginal<typeof import('@microsoft/signalr')>(),
  HubConnectionBuilder: class {
    withUrl() { return this; } withAutomaticReconnect() { return this; } configureLogging() { return this; }
    build() { return built.connection; }
  },
}));
const org = '11111111-1111-4111-8111-111111111111', user = '22222222-2222-4222-8222-222222222222';
const owner = '33333333-3333-4333-8333-333333333333';
const profile = { id: user, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const event = { eventId: '44444444-4444-4444-8444-444444444444', eventType: 'ORGANIZATION_DELETED', actorId: owner,
  organizationId: org, boardId: null, entityType: 'Organization', entityId: org, version: 3, metadata: {}, createdAt: '2026-10-06T12:00:00Z' };
const completed = { organizationId: org, userId: user, page: { state: 'COMPLETED', events: [event] } };
const pending = { ...completed, page: { state: 'PENDING', events: [] } };
const reply = (value: unknown) => new Response(JSON.stringify(value));
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture(injected = true) {
  let observer: Observer; let reconnect = () => {}; let reconnecting = () => {};
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn(() => ({ subscribe: (value: Observer) => { observer = value; return { dispose: vi.fn() }; } })),
    onreconnected: (callback: () => void) => { reconnect = callback; },
    onreconnecting: (callback: () => void) => { reconnecting = callback; }, onclose: vi.fn() };
  built.connection = connection;
  const update = vi.fn(), unavailable = vi.fn(), accountUnavailable = vi.fn();
  const cleanup = watchOrganizationLifecycle({ organizationId: org, userId: user, update, unavailable, accountUnavailable,
    ...(injected ? { connection: connection as unknown as ReturnType<typeof createOrganizationLifecycleConnection> } : {}) });
  return { connection, update, unavailable, accountUnavailable, cleanup, next: (value: unknown) => observer.next(value),
    previous: () => observer, reconnect: () => reconnect(), reconnecting: () => reconnecting() };
}
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
it('accepts only the actual terminal envelope and separate pending state', () => {
  expect(validateOrganizationLifecycle(completed, org, user)?.event).toEqual(event);
  expect(validateOrganizationLifecycle(pending, org, user)?.state).toBe('PENDING');
});
it.each([
  { ...completed, userId: owner }, { ...completed, organizationId: owner },
  { ...completed, requestId: event.eventId }, { ...completed, page: { state: 'COMPLETED', events: [] } },
  { ...completed, page: { state: 'COMPLETED', events: [null] } },
  { ...completed, page: { state: 'PENDING', events: [event] } },
  ...[{ name: 'Private name' }, { eventType: 'ORGANIZATION_UPDATED' }, { actorId: 'invalid' }, { entityId: owner },
    { boardId: owner }, { entityType: 'Board' }, { version: 2 }, { metadata: { requestId: event.eventId } },
    { createdAt: '2026-02-30T12:00:00Z' }].map(change => ({ ...completed, page: { state: 'COMPLETED', events: [{ ...event, ...change }] } })),
])('refuses wrong admission, content leakage and fabricated terminal semantics', input => {
  expect(validateOrganizationLifecycle(input, org, user)).toBeNull();
});
it('deduplicates immutable terminal history and restores it only after reconnect admission', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => reply(profile))); const f = fixture();
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledWith('Watch', org));
  f.next(completed); await vi.waitFor(() => expect(f.update).toHaveBeenCalledWith('COMPLETED', event));
  f.next(completed); await new Promise(resolve => setTimeout(resolve, 0)); expect(f.update).toHaveBeenCalledTimes(1);
  const old = f.previous(); f.reconnecting(); expect(f.unavailable).toHaveBeenCalledTimes(1);
  f.reconnect(); old.next({ ...completed, userId: owner }); f.next(completed);
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(2));
  expect(f.accountUnavailable).not.toHaveBeenCalled(); f.cleanup();
});
it('retires the connection without delivering a frame after actual profile replacement', async () => {
  let current = profile; vi.stubGlobal('fetch', vi.fn(async () => reply(current))); const f = fixture();
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled()); current = { ...profile, id: owner };
  f.next(completed); await vi.waitFor(() => expect(f.accountUnavailable).toHaveBeenCalledTimes(1));
  expect(f.update).not.toHaveBeenCalled(); expect(f.connection.stop).toHaveBeenCalled(); f.cleanup();
});
it('refuses changed original source attribution after reconnect', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => reply(profile))); const f = fixture();
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled()); f.next(completed);
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(1)); f.reconnect();
  f.next({ ...completed, page: { state: 'COMPLETED', events: [{ ...event, actorId: user }] } });
  await vi.waitFor(() => expect(f.unavailable).toHaveBeenCalledTimes(1)); expect(f.update).toHaveBeenCalledTimes(1); f.cleanup();
});
it('withholds a valid source when the account changes between the frame admission checks', async () => {
  let reads = 0;
  vi.stubGlobal('fetch', vi.fn(async () => reply(++reads < 3 ? profile : { ...profile, id: owner })));
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(completed);
  await vi.waitFor(() => expect(f.accountUnavailable).toHaveBeenCalledTimes(1));
  expect(reads).toBe(3); expect(f.update).not.toHaveBeenCalled(); f.cleanup();
});
it('fences a frame whose profile read finishes after reconnect has replaced its subscription', async () => {
  let reads = 0; let finish!: (value: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async () => ++reads === 2 ? new Promise<Response>(resolve => { finish = resolve; }) : reply(profile)));
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(completed); await vi.waitFor(() => expect(finish).toBeDefined());
  f.reconnecting(); f.reconnect();
  finish(reply({ ...profile, id: owner }));
  f.next(completed); await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(1));
  expect(f.update).toHaveBeenCalledWith('COMPLETED', event);
  expect(f.accountUnavailable).not.toHaveBeenCalled(); f.cleanup();
});
it('skips Demo without a socket or simulated terminal result', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => reply({ service: 'strataai-api', mode: 'demo' }))); const f = fixture(false);
  await new Promise(resolve => setTimeout(resolve, 0)); expect(f.connection.start).not.toHaveBeenCalled();
  expect(f.update).not.toHaveBeenCalled(); expect(f.unavailable).not.toHaveBeenCalled(); f.cleanup();
});
