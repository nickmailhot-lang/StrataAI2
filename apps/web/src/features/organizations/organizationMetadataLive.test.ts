import { createOrganizationMetadataConnection, watchOrganizationMetadata } from './organizationMetadataLive';
const built = vi.hoisted(() => ({ connection: null as unknown }));
vi.mock('@microsoft/signalr', async importOriginal => ({
  ...await importOriginal<typeof import('@microsoft/signalr')>(),
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return built.connection; }
  },
}));

const org = '11111111-1111-4111-8111-111111111111', user = '22222222-2222-4222-8222-222222222222';
const source = { eventId: '44444444-4444-4444-8444-444444444444', actorId: user, organizationId: org,
  boardId: null, entityType: 'Organization', entityId: org, eventType: 'ORGANIZATION_UPDATED', version: 2,
  metadata: {}, createdAt: '2026-10-06T12:00:00Z' };
const initial = { organizationId: org, userId: user, page: { cursor: 'protected-metadata_A', events: [],
  hasMore: false, pending: false, resetRequired: true } };
const delivered = { ...initial, page: { ...initial.page, resetRequired: false, events: [source] } };
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture() {
  let observer: Observer; let reconnect = () => {};
  const stop = vi.fn().mockResolvedValue(undefined), dispose = vi.fn();
  const stream = vi.fn(() => ({ subscribe: (value: Observer) => { observer = value; return { dispose }; } }));
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop, stream, onreconnecting: vi.fn(),
    onreconnected: (value: () => void) => { reconnect = value; }, onclose: vi.fn() };
  const invalidate = vi.fn(), reset = vi.fn(), unavailable = vi.fn();
  const cleanup = watchOrganizationMetadata({ organizationId: org, userId: user, invalidate, reset, unavailable,
    connection: connection as unknown as ReturnType<typeof createOrganizationMetadataConnection> });
  return { stream, stop, dispose, invalidate, reset, unavailable, cleanup, next: (value: unknown) => observer.next(value),
    previous: () => observer, reconnect: () => reconnect() };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
function runtimeFixture() {
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn(() => ({ subscribe: vi.fn(() => ({ dispose: vi.fn() })) })),
    onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn() };
  built.connection = connection;
  const unavailable = vi.fn();
  const cleanup = watchOrganizationMetadata({ organizationId: org, userId: user,
    invalidate: vi.fn(), reset: vi.fn(), unavailable });
  return { connection, unavailable, cleanup };
}
it('opens the authenticated canonical stream after the Demo descriptor is confirmed', async () => {
  const response = new Response(JSON.stringify({ service: 'strataai-api', mode: 'demo' }));
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response)); const f = runtimeFixture();
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledWith('Watch', org, null));
  expect(response.bodyUsed).toBe(true);
  expect(f.connection.start).toHaveBeenCalledTimes(1); expect(f.unavailable).not.toHaveBeenCalled(); f.cleanup();
});
it('opens the authenticated stream only after the Production descriptor is confirmed', async () => {
  const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ service: 'strataai-api', mode: 'production' })));
  vi.stubGlobal('fetch', fetcher); const f = runtimeFixture();
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledWith('Watch', org, null));
  expect(fetcher).toHaveBeenCalledWith('/api/runtime', expect.anything());
  expect(f.connection.start).toHaveBeenCalledTimes(1); f.cleanup();
});
it('fences a late runtime response after unmount before any stream or unavailable callback', async () => {
  let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })));
  const f = runtimeFixture();
  await vi.waitFor(() => expect(finish).toBeDefined());
  f.cleanup();
  finish(new Response(JSON.stringify({ service: 'strataai-api', mode: 'production' })));
  await new Promise(resolve => setTimeout(resolve, 0));
  expect(f.connection.start).not.toHaveBeenCalled(); expect(f.unavailable).not.toHaveBeenCalled();
});
it('resets to a fresh snapshot, deduplicates canonical sources and retains the exact cursor through reconnect', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', org, null);
  f.next(initial); f.next(delivered); f.next(delivered); await vi.advanceTimersByTimeAsync(100);
  expect(f.reset).toHaveBeenCalledTimes(1); expect(f.invalidate).toHaveBeenCalledTimes(1);
  const old = f.previous(); f.reconnect();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', org, initial.page.cursor);
  old.next({ ...delivered, userId: org }); expect(f.unavailable).not.toHaveBeenCalled();
  f.cleanup(); f.next(initial); await vi.advanceTimersByTimeAsync(5000);
  expect(f.reset).toHaveBeenCalledTimes(1); expect(f.dispose).toHaveBeenCalled(); expect(f.stop).toHaveBeenCalled();
});
it('advances an empty heartbeat cursor without inventing a metadata change', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve(); f.next(initial);
  f.next({ ...initial, page: { ...initial.page, cursor: 'protected-metadata_B', resetRequired: false } });
  await vi.advanceTimersByTimeAsync(100); expect(f.invalidate).not.toHaveBeenCalled();
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', org, 'protected-metadata_B'); f.cleanup();
});
it('rejects another account before retaining its cursor or invalidating metadata', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  f.next({ ...delivered, userId: org }); await vi.advanceTimersByTimeAsync(100);
  expect(f.unavailable).toHaveBeenCalledTimes(1); expect(f.stop).toHaveBeenCalled();
  expect(f.invalidate).not.toHaveBeenCalled(); expect(f.reset).not.toHaveBeenCalled();
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', org, null); f.cleanup();
});
it('rejects reused source identity with changed attribution and cancels pending refresh on unmount', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve(); f.next(delivered);
  f.next({ ...delivered, page: { ...delivered.page, events: [{ ...source, actorId: org }] } });
  await vi.advanceTimersByTimeAsync(0); expect(f.unavailable).toHaveBeenCalledTimes(1);
  f.cleanup(); await vi.advanceTimersByTimeAsync(30_000); expect(f.invalidate).not.toHaveBeenCalled();
});
