import { createIdentityConnection, watchIdentity } from './identityLive';

type Observer = { next(value: unknown): void; error(error: Error): void; complete(): void };
function fakeConnection() {
  const observers: Observer[] = [];
  let reconnecting = () => {}, reconnected = () => {};
  const subscription = { dispose: vi.fn() };
  const connection = {
    start: vi.fn(async () => {}), stop: vi.fn(async () => {}),
    stream: vi.fn(() => ({ subscribe: (observer: Observer) => { observers.push(observer); return subscription; } })),
    onreconnecting: (callback: () => void) => { reconnecting = callback; },
    onreconnected: (callback: () => void) => { reconnected = callback; }, onclose: vi.fn(),
  };
  return { connection, observers, subscription, reconnecting: () => reconnecting(), reconnected: () => reconnected() };
}
const profile = { id: 'subject', version: 1 };
const isProfile = (value: unknown): value is typeof profile => !!value && typeof value === 'object'
  && typeof (value as typeof profile).id === 'string' && Number.isSafeInteger((value as typeof profile).version);
const initial = { profile, cursor: 1, latestSequence: 1, hasMore: false, events: [] };

describe('PRD-02/60 identity live recovery', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());
  function mount(fake = fakeConnection(), initialVersion?: number) {
    const invalidate = vi.fn();
    const stop = watchIdentity({ subject: 'subject', isProfile, invalidate, initialVersion,
      connection: fake.connection as unknown as ReturnType<typeof createIdentityConnection> });
    return { ...fake, invalidate, stop };
  }
  it('keeps an already admitted head quiet and recovers newer revisions and same-version session events', async () => {
    const live = mount(fakeConnection(), 1);
    await vi.advanceTimersByTimeAsync(0); live.observers[0].next(initial);
    await vi.advanceTimersByTimeAsync(100); expect(live.invalidate).not.toHaveBeenCalled();
    live.observers[0].next({ ...initial, profile: { ...profile, version: 2 } });
    await vi.advanceTimersByTimeAsync(100); expect(live.invalidate).toHaveBeenCalledTimes(1);
    live.observers[0].next({ profile: { ...profile, version: 2 }, cursor: 2, latestSequence: 2, hasMore: false,
      events: [{ eventId: '11111111-1111-1111-1111-111111111111', sequence: 2, eventType: 'SESSION_REVOKED',
        actorId: profile.id, entityType: 'User', entityId: profile.id, version: 2,
        organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-09T20:00:00Z' }] });
    await vi.advanceTimersByTimeAsync(100); expect(live.invalidate).toHaveBeenCalledTimes(2);
    expect(live.connection.stop).not.toHaveBeenCalled(); live.stop();
  });
  it('refuses a head older than the admitted revision without acknowledging its cursor', async () => {
    const live = mount(fakeConnection(), 2);
    await vi.advanceTimersByTimeAsync(0); live.observers[0].next(initial);
    await vi.advanceTimersByTimeAsync(1100);
    expect(live.invalidate).toHaveBeenCalledTimes(1); expect(live.connection.stop).toHaveBeenCalledTimes(1);
    expect(live.connection.stream).toHaveBeenLastCalledWith('Watch', null); live.stop();
  });
  it.each([0, -1, Number.NaN])('retains conservative startup recovery for an invalid admitted revision %s', async version => {
    const live = mount(fakeConnection(), version);
    await vi.advanceTimersByTimeAsync(0); live.observers[0].next(initial);
    await vi.advanceTimersByTimeAsync(100); expect(live.invalidate).toHaveBeenCalledTimes(1); live.stop();
  });
  it('resumes the accepted cursor and fences callbacks from the previous connection generation', async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.observers[0].next(initial);
    await vi.advanceTimersByTimeAsync(100);
    expect(live.invalidate).toHaveBeenCalledTimes(1);
    live.reconnecting(); live.reconnected();
    expect(live.connection.stream).toHaveBeenLastCalledWith('Watch', '1');
    live.observers[0].next({ ...initial, profile: { id: 'other', version: 99 } });
    live.observers[1].next(initial);
    await vi.advanceTimersByTimeAsync(100);
    expect(live.connection.stop).not.toHaveBeenCalled();
    live.stop();
  });
  it('rejects a cross-subject page and recovers through HTTP without advancing its cursor', async () => {
    const live = mount();
    await vi.advanceTimersByTimeAsync(0);
    live.observers[0].next(initial);
    await vi.advanceTimersByTimeAsync(100);
    live.observers[0].next({ ...initial, profile: { id: 'other', version: 1 } });
    await vi.advanceTimersByTimeAsync(1100);
    expect(live.connection.stop).toHaveBeenCalledTimes(1);
    expect(live.invalidate).toHaveBeenCalledTimes(2);
    expect(live.connection.stream).toHaveBeenLastCalledWith('Watch', '1');
    live.stop();
  });
  it('retries an initial connection failure and disposes all queued recovery work', async () => {
    const fake = fakeConnection();
    fake.connection.start.mockRejectedValueOnce(new Error('offline'));
    const live = mount(fake);
    await vi.advanceTimersByTimeAsync(1100);
    expect(live.connection.start).toHaveBeenCalledTimes(2);
    live.stop();
    const before = live.invalidate.mock.calls.length;
    await vi.advanceTimersByTimeAsync(40_000);
    expect(live.invalidate).toHaveBeenCalledTimes(before);
    expect(live.subscription.dispose).toHaveBeenCalled();
  });
});
