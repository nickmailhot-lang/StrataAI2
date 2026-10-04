import { validateBoardStarSync } from './boardStarSync';
const org = '11111111-1111-1111-1111-111111111111';
const board = '22222222-2222-2222-2222-222222222222';
const user = '33333333-3333-3333-3333-333333333333';
const event = { organizationId: org, boardId: board, actorId: user,
  entityId: '44444444-4444-4444-4444-444444444444', eventId: '55555555-5555-5555-5555-555555555555',
  version: 1, createdAt: '2026-10-04T00:00:00.1234567Z',
  entityType: 'UserBoardPreference', eventType: 'BOARD_STARRED', metadata: {} };
const head = { organizationId: org, boardId: board, userId: user, cursor: '0',
  events: [], hasMore: false, resetRequired: false };
function read(value: unknown, cursor?: string, seen: ReadonlyMap<string,string> = new Map()) {
  return validateBoardStarSync(value, org, board, user, cursor, seen);
}
it('accepts the initial snapshot and a transition racing the initial handoff', () => {
  expect(read(head)?.cursor).toBe('0');
  expect(read({ ...head, cursor: '1', events: [event] })?.eventIds).toHaveLength(1);
  expect(read({ ...head, cursor: '9223372036854775807' })?.cursor).toBe('9223372036854775807');
  expect(read({ ...head, cursor: '9223372036854775808' })).toBeNull();
});
it('deduplicates exact replay and rejects a changed replay before admitting any IDs', () => {
  const page = { ...head, cursor: '1', events: [event] };
  const first = read(page, '0')!; const seen = new Map(first.eventIds);
  expect(read(page, '1', seen)?.eventIds).toEqual([]);
  expect(read({ ...page, events: [{ ...event, createdAt: '2026-10-04T00:00:00.1234568Z' }] }, '1', seen)).toBeNull();
  expect(read(page, '1')).toBeNull();
  expect(read({ ...head, cursor: '2' }, '1', seen)).toBeNull();
});
it.each([
  { ...head, organizationId: user }, { ...head, boardId: org }, { ...head, userId: org },
  { ...head, privateValue: true }, { ...head, cursor: '-1' }, { ...head, hasMore: true },
  { ...head, cursor: '1', events: [{ ...event, actorId: org }] },
  { ...head, cursor: '1', events: [{ ...event, metadata: { starred: true } }] },
  { ...head, cursor: '1', events: [{ ...event, version: 2 }] },
  { ...head, cursor: '1', events: [{ ...event, version: 1.5 }] },
  { ...head, cursor: '1', events: [{ ...event, createdAt: '2026-02-30T00:00:00Z' }] },
  { ...head, cursor: '1', events: [event, event] },
])('rejects malformed or foreign private pages: %j', value => {
  expect(read(value, '0')).toBeNull();
});
it('admits only a backwards reset with an existing cursor', () => {
  const reset = { ...head, cursor: '1', events: [event], resetRequired: true };
  expect(read(reset, '2')?.eventIds).toHaveLength(1);
  expect(read(reset)).toBeNull(); expect(read(reset, '1')).toBeNull();
  expect(read(head, '1')).toBeNull();
});
