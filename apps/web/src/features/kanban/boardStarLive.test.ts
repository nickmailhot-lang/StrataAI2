import { watchBoardStars, createBoardStarConnection } from './boardStarLive';
const org = '11111111-1111-1111-1111-111111111111';
const board = '22222222-2222-2222-2222-222222222222';
const user = '33333333-3333-3333-3333-333333333333';
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture() {
  let observer: Observer; let reconnect: () => void = () => {};
  const dispose = vi.fn(); const stop = vi.fn().mockResolvedValue(undefined);
  const stream = vi.fn(() => ({ subscribe: (value: Observer) => { observer = value; return { dispose }; } }));
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop, stream,
    onreconnecting: vi.fn(), onreconnected: (value: () => void) => { reconnect = value; }, onclose: vi.fn() };
  const invalidate = vi.fn(); const observe = vi.fn();
  const cleanup = watchBoardStars({ organizationId: org, boardId: board, userId: user, invalidate, observe,
    connection: connection as unknown as ReturnType<typeof createBoardStarConnection> });
  return { stream, stop, dispose, invalidate, observe, cleanup, next: (value: unknown) => observer.next(value), reconnect: () => reconnect() };
}
const head = { organizationId: org, boardId: board, userId: user, cursor: '9007199254740993',
  events: [], hasMore: false, resetRequired: false };
afterEach(() => { vi.useRealTimers(); });
it('refreshes initial handoff, ignores duplicate heartbeat, resumes exact cursor and cleans up', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', board, null);
  f.next(head); await vi.advanceTimersByTimeAsync(100);
  f.next(head); await vi.advanceTimersByTimeAsync(100);
  expect(f.invalidate).toHaveBeenCalledTimes(1);
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', board, head.cursor);
  expect(f.observe.mock.calls).toEqual([['reconnect']]);
  f.cleanup(); f.next(head); await vi.advanceTimersByTimeAsync(5000);
  expect(f.invalidate).toHaveBeenCalledTimes(1);
  expect(f.dispose).toHaveBeenCalled(); expect(f.stop).toHaveBeenCalled();
});
it('rejects foreign actor delivery before advancing the cursor', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  f.next({ ...head, userId: org }); await vi.advanceTimersByTimeAsync(100);
  expect(f.stop).toHaveBeenCalled(); expect(f.invalidate).toHaveBeenCalledTimes(1);
  expect(f.observe.mock.calls).toEqual([['exception']]);
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', board, null);
  f.cleanup(); await vi.advanceTimersByTimeAsync(5000);
  expect(f.invalidate).toHaveBeenCalledTimes(1);
});
