import { WorkRequestError } from '../../api/workManagement';
import { createOperationalLifecycleConnection, watchOperationalLifecycle } from './operationalLifecycleLive';
const organization = '11111111-1111-4111-8111-111111111111', actor = '22222222-2222-4222-8222-222222222222';
const other = '33333333-3333-4333-8333-333333333333';
const profile = { id: actor, version: 1, status: 'ACTIVE', emailVerified: true, locale: 'en-CA', timezone: 'America/Vancouver' };
const event = { eventId: '44444444-4444-4444-8444-444444444444', organizationId: organization, actorId: other,
  entityId: organization, entityType: 'Organization', version: 2, previousStatus: 'ACTIVE', status: 'SUSPENDED',
  eventType: 'ORGANIZATION_SUSPENDED', actingCapacity: 'ORGANIZATION_OWNER', createdAt: '2026-10-10T12:00:00Z' };
const page = { organizationId: organization, actorId: actor, status: 'SUSPENDED', version: 2, afterVersion: 0,
  nextVersion: null, pending: false, events: [event] };
const frame = (value: unknown = page) => ({ organizationId: organization, userId: actor, page: value });
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture(admit: Parameters<typeof watchOperationalLifecycle>[0]['admit'] = vi.fn(async () => {})) {
  let observer!: Observer; let reconnect = () => {}; let reconnecting = () => {}; let close = () => {};
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined),
    stream: vi.fn(() => ({ subscribe: (value: Observer) => { observer = value; return { dispose: vi.fn() }; } })),
    onreconnected: (callback: () => void) => { reconnect = callback; },
    onreconnecting: (callback: () => void) => { reconnecting = callback; }, onclose: (callback: () => void) => { close = callback; } };
  const update = vi.fn(), unavailable = vi.fn(), denied = vi.fn(), reconnected = vi.fn();
  const cleanup = watchOperationalLifecycle({ organizationId: organization, actorId: actor, admit, update, unavailable, denied, reconnected,
    connection: connection as unknown as ReturnType<typeof createOperationalLifecycleConnection> });
  return { connection, update, unavailable, denied, reconnected, admit, cleanup, next: (value: unknown) => observer.next(value),
    previous: () => observer, reconnect: () => reconnect(), reconnecting: () => reconnecting(), close: () => close() };
}
beforeEach(() => vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(profile)))));
afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); });
it('delivers only after current surface admission and resumes from the delivered fact', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledWith('Watch', organization, 0));
  f.next(frame()); await vi.waitFor(() => expect(f.update).toHaveBeenCalledWith(page));
  expect(f.admit).toHaveBeenCalledWith(page, expect.any(AbortSignal));
  f.reconnecting(); f.reconnect(); expect(f.connection.stream).toHaveBeenLastCalledWith('Watch', organization, 2);
  f.next(frame({ ...page, afterVersion: 2, events: [] }));
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(2)); f.cleanup();
});
it('retains the source cursor across a pending gap, even if parent state is newer', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(frame({ ...page, status: 'ACTIVE', version: 4, pending: true, nextVersion: 2 }));
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(1));
  f.reconnecting(); f.reconnect(); expect(f.connection.stream).toHaveBeenLastCalledWith('Watch', organization, 2); f.cleanup();
});
it('withdraws updates when the account changes after surface admission', async () => {
  let current = profile;
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(current))));
  const f = fixture(vi.fn(async () => { current = { ...profile, id: other }; }));
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled()); f.next(frame());
  await vi.waitFor(() => expect(f.denied).toHaveBeenCalledTimes(1));
  expect(f.update).not.toHaveBeenCalled(); expect(f.connection.stop).toHaveBeenCalled(); f.cleanup();
});
it('never advances or delivers after current consumer permission is withdrawn', async () => {
  const f = fixture(vi.fn(async () => { throw new WorkRequestError(404, null); }));
  await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled()); f.next(frame());
  await vi.waitFor(() => expect(f.denied).toHaveBeenCalledTimes(1));
  expect(f.update).not.toHaveBeenCalled(); f.reconnect(); expect(f.connection.stream).toHaveBeenCalledTimes(1); f.cleanup();
});
it('fences delayed admission and old frames after a reconnect generation replaces them', async () => {
  let finish!: () => void;
  const admit = vi.fn().mockImplementationOnce(() => new Promise<void>(resolve => { finish = resolve; })).mockResolvedValue(undefined);
  const f = fixture(admit); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(frame()); await vi.waitFor(() => expect(finish).toBeDefined()); const previous = f.previous();
  f.reconnecting(); f.reconnect(); finish(); previous.next({ ...frame(), userId: other }); f.next(frame());
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(1));
  expect(f.denied).not.toHaveBeenCalled(); expect(f.connection.stream).toHaveBeenLastCalledWith('Watch', organization, 0); f.cleanup();
});
it('serializes back-to-back pages using source positions without reconnect', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(frame({ ...page, version: 3, status: 'ACTIVE', nextVersion: 2 }));
  f.next(frame({ ...page, version: 3, status: 'ACTIVE', afterVersion: 2, events: [{ ...event,
    eventId: '66666666-6666-4666-8666-666666666666', version: 3, previousStatus: 'SUSPENDED',
    status: 'ACTIVE', eventType: 'ORGANIZATION_REACTIVATED' }] }));
  await vi.waitFor(() => expect(f.update).toHaveBeenCalledTimes(2)); expect(f.unavailable).not.toHaveBeenCalled(); f.cleanup();
});
it('withholds malformed or foreign replay before surface admission', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next({ ...frame(), userId: other }); await vi.waitFor(() => expect(f.unavailable).toHaveBeenCalledTimes(1));
  expect(f.update).not.toHaveBeenCalled(); expect(f.admit).not.toHaveBeenCalled(); f.cleanup();
});
it('cleanup aborts pending surface admission and fences its eventual result', async () => {
  let finish!: () => void; let signal!: AbortSignal;
  const admit = vi.fn(async (_page: unknown, value: AbortSignal) => {
    signal = value; await new Promise<void>(resolve => { finish = resolve; });
  });
  const f = fixture(admit); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  f.next(frame()); await vi.waitFor(() => expect(finish).toBeDefined()); f.cleanup();
  expect(signal.aborted).toBe(true); finish(); await new Promise(resolve => setTimeout(resolve, 0));
  expect(f.update).not.toHaveBeenCalled(); expect(f.denied).not.toHaveBeenCalled(); expect(f.unavailable).not.toHaveBeenCalled();
});
it('bounds pending replay backlog and resumes from the last delivered source after overflow', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalled());
  for (let index = 0; index < 65; index++) f.next(frame());
  expect(f.unavailable).toHaveBeenCalledTimes(1); expect(f.update).not.toHaveBeenCalled();
  f.reconnect(); expect(f.connection.stream).toHaveBeenLastCalledWith('Watch', organization, 0); f.cleanup();
});

it('reports reconnection only for a live connection and does not resurrect disposed subscriptions', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledTimes(1));
  expect(f.reconnected).not.toHaveBeenCalled(); f.reconnecting(); f.reconnect();
  expect(f.reconnected).toHaveBeenCalledTimes(1); expect(f.connection.stream).toHaveBeenCalledTimes(2);
  f.cleanup(); f.reconnect(); expect(f.reconnected).toHaveBeenCalledTimes(1);
  expect(f.connection.stream).toHaveBeenCalledTimes(2);
});

it('observes a successful restart after closed transport, excluding failed attempts and disposed callbacks', async () => {
  const f = fixture(); await vi.waitFor(() => expect(f.connection.stream).toHaveBeenCalledTimes(1));
  vi.useFakeTimers(); f.connection.start.mockRejectedValueOnce(new Error('Transport unavailable')); f.close();
  await vi.advanceTimersByTimeAsync(1000); expect(f.reconnected).not.toHaveBeenCalled();
  expect(f.connection.stream).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(2000); expect(f.reconnected).toHaveBeenCalledTimes(1);
  expect(f.connection.stream).toHaveBeenCalledTimes(2);
  f.cleanup(); f.close(); await vi.advanceTimersByTimeAsync(30000);
  expect(f.reconnected).toHaveBeenCalledTimes(1); expect(f.connection.stream).toHaveBeenCalledTimes(2);
});
