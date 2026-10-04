import { activityCardLink, parseActivityPage, type ActivityItem } from './activityHistory';
const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const scope = { organizationId: id(1), boardId: id(2), kind: 'CARD' as const, targetId: id(3) };
const item: ActivityItem = { eventId: id(9), organizationId: scope.organizationId, boardId: id(7), actorId: id(8), actorLabel: 'Historical <script>🙂',
  eventType: 'CARD_UPDATED', entityType: 'Card', entityId: scope.targetId, version: '9223372036854775807', createdAt: '2026-10-04T07:00:00.123456Z', metadata: {}, currentBoardId: scope.boardId };
const page = { organizationId: scope.organizationId, kind: scope.kind, targetId: scope.targetId, items: [item], nextCursor: null };
it('retains historical source context and bigint versions while linking the current Card Board', () => {
  expect(parseActivityPage(page, scope).items[0]).toEqual(item);
  expect(activityCardLink(item)).toBe(`/app/${id(1)}/boards/${id(2)}/cards/${id(3)}`);
});
it.each([
  { organizationId: id(99) }, { currentBoardId: id(99) }, { entityId: id(99) }, { eventId: '00000000-0000-0000-0000-000000000000' },
  { actorLabel: 'Bad\u0000caption' }, { actorLabel: '\ud800' }, { version: 1 }, { version: '9223372036854775808' },
  { version: '01' }, { metadata: { content: 'secret' } }, { entityType: 'List' }, { createdAt: '2026-10-04T07:00:00.1234567Z' },
])('rejects malformed/protected activity rows %j', change => {
  expect(() => parseActivityPage({ ...page, items: [{ ...item, ...change }] }, scope)).toThrow();
});
it('rejects cross-target pages, duplicate/unordered sources and cursors on short pages', () => {
  for (const change of [{ kind: 'BOARD' }, { targetId: id(98) }, { nextCursor: 'opaque' },
    { items: [item, item] }, { items: [item, { ...item, eventId: id(10) }] }])
    expect(() => parseActivityPage({ ...page, ...change }, scope)).toThrow();
  expect(() => parseActivityPage(page, scope, item)).toThrow();
  expect(() => parseActivityPage({ ...page, items: [{ ...item, eventId: id(8) }] }, scope, item)).not.toThrow();
});
