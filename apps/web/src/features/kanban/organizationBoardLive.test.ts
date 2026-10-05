import { createOrganizationBoardConnection, watchOrganizationBoards } from './organizationBoardLive';
import { validateOrganizationBoardSync } from './organizationBoardSync';
const org = '11111111-1111-4111-8111-111111111111', user = '22222222-2222-4222-8222-222222222222';
const board = '33333333-3333-4333-8333-333333333333', eventId = '44444444-4444-4444-8444-444444444444';
const event = { eventId, boardId: board, eventType: 'BOARD_ARCHIVED', version: 2, createdAt: '2026-10-04T12:00:00Z' };
const head = { organizationId: org, userId: user, page: { cursor: 'protected-cursor_A', events: [], hasMore: false, pending: false, resetRequired: true } };
const delivered = { ...head, page: { ...head.page, resetRequired: false, events: [event] } };
type Observer = { next(value: unknown): void; error(error: unknown): void; complete(): void };
function fixture() {
  let observer: Observer; let reconnect: () => void = () => {};
  const dispose = vi.fn(); const stop = vi.fn().mockResolvedValue(undefined);
  const stream = vi.fn(() => ({ subscribe: (value: Observer) => { observer = value; return { dispose }; } }));
  const connection = { start: vi.fn().mockResolvedValue(undefined), stop, stream, onreconnecting: vi.fn(),
    onreconnected: (value: () => void) => { reconnect = value; }, onclose: vi.fn() };
  const invalidate = vi.fn(), reset = vi.fn(), unavailable = vi.fn();
  const cleanup = watchOrganizationBoards({ organizationId: org, userId: user, invalidate, reset, unavailable,
    connection: connection as unknown as ReturnType<typeof createOrganizationBoardConnection> });
  return { stream, stop, dispose, invalidate, reset, unavailable, cleanup, next: (value: unknown) => observer.next(value),
    previous: () => observer, reconnect: () => reconnect() };
}
afterEach(() => vi.useRealTimers());
it('resets bootstrap, invalidates canonical sources once and resumes the exact opaque cursor', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', org, null);
  f.next(head); expect(f.reset).toHaveBeenCalledTimes(1);
  f.next(delivered); f.next(delivered); await vi.advanceTimersByTimeAsync(100);
  expect(f.invalidate).toHaveBeenCalledTimes(1);
  const old = f.previous(); f.reconnect();
  expect(f.stream).toHaveBeenLastCalledWith('Watch', org, head.page.cursor);
  old.next({ ...head, userId: org }); expect(f.unavailable).not.toHaveBeenCalled();
  f.cleanup(); f.next(head); await vi.advanceTimersByTimeAsync(5000);
  expect(f.reset).toHaveBeenCalledTimes(1); expect(f.dispose).toHaveBeenCalled(); expect(f.stop).toHaveBeenCalled();
});
it('does not refresh for newly encrypted empty heartbeats', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve(); f.next(head);
  f.next({ ...head, page: { ...head.page, cursor: 'protected-cursor_B', resetRequired: false } });
  await vi.advanceTimersByTimeAsync(100); expect(f.invalidate).not.toHaveBeenCalled();
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', org, 'protected-cursor_B'); f.cleanup();
});
it('refuses foreign viewer delivery before retaining its cursor or publishing an invalidation', async () => {
  vi.useFakeTimers(); const f = fixture(); await Promise.resolve();
  f.next({ ...delivered, userId: org }); await vi.advanceTimersByTimeAsync(100);
  expect(f.unavailable).toHaveBeenCalledTimes(1); expect(f.stop).toHaveBeenCalled();
  expect(f.invalidate).not.toHaveBeenCalled(); expect(f.reset).not.toHaveBeenCalled();
  f.reconnect(); expect(f.stream).toHaveBeenLastCalledWith('Watch', org, null); f.cleanup();
});
it.each([
  { ...delivered, organizationId: user },
  { ...delivered, page: { ...delivered.page, cursor: '9007199254740993' } },
  { ...delivered, page: { ...delivered.page, cursor: 'x'.repeat(4097) } },
  { ...delivered, page: { ...delivered.page, events: [{ ...event, actorId: user }] } },
  { ...delivered, page: { ...delivered.page, events: [{ ...event, description: 'Private body' }] } },
  { ...delivered, page: { ...delivered.page, events: [{ ...event, eventType: 'BOARD_STARRED' }] } },
  { ...delivered, page: { ...delivered.page, events: [{ ...event, createdAt: '2026-02-30T12:00:00Z' }] } },
  { ...delivered, page: { ...delivered.page, events: [event, event] } },
  { ...delivered, page: { ...delivered.page, hasMore: true } },
  { ...delivered, page: { ...delivered.page, resetRequired: true } },
])('withholds malformed scope, private fields, bounds, identities or reset semantics', value => {
  expect(validateOrganizationBoardSync(value, org, user, new Map())).toBeNull();
});
it('rejects reused canonical identity with changed attribution-free envelope', () => {
  const valid = validateOrganizationBoardSync(delivered, org, user, new Map())!;
  expect(valid).not.toBeNull();
  expect(validateOrganizationBoardSync({ ...delivered, page: { ...delivered.page, events: [{ ...event, version: 3 }] } },
    org, user, new Map(valid.eventIds))).toBeNull();
});
