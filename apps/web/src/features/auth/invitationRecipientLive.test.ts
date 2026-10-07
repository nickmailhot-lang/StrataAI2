import { createInvitationRecipientConnection, watchInvitationRecipient } from './invitationRecipientLive';
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture(start = vi.fn().mockResolvedValue(undefined)) {
  const observers: Observer[] = []; let reconnecting = () => {}; let reconnected = () => {}; let close = () => {};
  const dispose = vi.fn(); const stop = vi.fn().mockResolvedValue(undefined);
  const stream = vi.fn(() => ({ subscribe: (value: Observer) => { observers.push(value); return { dispose }; } }));
  const connection = { start, stop, stream, onreconnecting: (v: () => void) => { reconnecting = v; },
    onreconnected: (v: () => void) => { reconnected = v; }, onclose: (v: () => void) => { close = v; } };
  const invalidate = vi.fn(); const cleanup = watchInvitationRecipient({ subject: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', invalidate,
    connection: connection as unknown as ReturnType<typeof createInvitationRecipientConnection> });
  return { observers, stream, stop, dispose, invalidate, cleanup,
    next: (v: unknown) => observers.at(-1)!.next(v), reconnecting: () => reconnecting(), reconnected: () => reconnected(), close: () => close() };
}
const reset = { cursor: 'protected_head', events: [], hasMore: false, resetRequired: true };
const event = { eventId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', eventType: 'INVITATION_REVOKED',
  sequence: '42', createdAt: '2026-10-07T00:00:00.1234567Z' };
const page = { ...reset, cursor: 'protected_transition', resetRequired: false, events: [event] };
afterEach(() => { vi.useRealTimers(); });

it('bootstraps at the captured head, ignores rotated heartbeats and resumes exact acknowledged cursor', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', null);
  f.next(reset); expect(f.invalidate.mock.calls).toEqual([['reset']]);
  f.next({ ...reset, cursor: 'rotated_head', resetRequired: false });
  expect(f.invalidate).toHaveBeenCalledTimes(1);
  f.next(page); expect(f.invalidate.mock.calls).toEqual([['reset'], ['change']]);
  const old = f.observers[0]; f.reconnecting(); expect(f.invalidate).toHaveBeenLastCalledWith('reconnecting');
  old.next({ ...page, cursor: 'late_private', invitationId: event.eventId });
  expect(f.stop).not.toHaveBeenCalled(); f.reconnected();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', page.cursor);
  f.next({ ...page, cursor: 'rotated_after_reconnect', events: [] });
  expect(f.invalidate).toHaveBeenCalledTimes(3);
  f.cleanup(); f.next(page); await vi.advanceTimersByTimeAsync(30_000);
  expect(f.invalidate).toHaveBeenCalledTimes(3); expect(f.dispose).toHaveBeenCalled(); expect(f.stop).toHaveBeenCalledTimes(1);
});
it('withdraws synchronously on private/malformed data and retries the last admitted cursor', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve(); f.next(reset);
  f.next({ ...page, events: [{ ...event, email: 'private@example.test' }] });
  expect(f.invalidate).toHaveBeenLastCalledWith('unavailable');
  await vi.advanceTimersByTimeAsync(1000); expect(f.stop).toHaveBeenCalled();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', reset.cursor);
  f.cleanup();
});
it('refuses duplicate original identities and recovers an expired cursor by an empty reset', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve(); f.next(reset); f.next(page);
  f.reconnecting(); f.reconnected(); f.next(reset);
  f.next({ ...page, cursor: 'new_scope_event', events: [{ ...event, sequence: '7' }] });
  expect(f.invalidate).toHaveBeenLastCalledWith('change');
  f.next({ ...page, cursor: 'duplicate', events: [{ ...event, sequence: '8' }] });
  expect(f.invalidate).toHaveBeenLastCalledWith('unavailable'); f.cleanup();
});
it('backs off failed starts and cancels retries on cleanup', async () => {
  vi.useFakeTimers(); const start = vi.fn().mockRejectedValue(new Error('unavailable'));
  const f = fixture(start); await vi.advanceTimersByTimeAsync(0);
  expect(f.invalidate).toHaveBeenLastCalledWith('unavailable'); expect(start).toHaveBeenCalledTimes(1);
  await vi.advanceTimersByTimeAsync(1000); expect(start).toHaveBeenCalledTimes(2);
  await vi.advanceTimersByTimeAsync(1999); expect(start).toHaveBeenCalledTimes(2);
  f.cleanup(); await vi.advanceTimersByTimeAsync(30_000); expect(start).toHaveBeenCalledTimes(2);
});
it('stops a late successful connection after disposal without subscribing', async () => {
  let resolve!: () => void; const start = vi.fn(() => new Promise<void>(done => { resolve = done; }));
  const f = fixture(start); f.cleanup(); resolve(); await Promise.resolve();
  expect(f.stream).not.toHaveBeenCalled(); expect(f.invalidate).not.toHaveBeenCalled(); expect(f.stop).toHaveBeenCalled();
});
