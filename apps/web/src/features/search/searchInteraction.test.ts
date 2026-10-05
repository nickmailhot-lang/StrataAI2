import { expect, it } from 'vitest';
import { ChangedSearchInteractionActor, parseSearchInteraction, SearchInteractionAcknowledgments } from './searchInteraction';
const id = '11111111-1111-4111-8111-111111111111';
const other = '22222222-2222-4222-8222-222222222222';
const source = () => ({ eventId: id, entityId: id, actorId: id, eventType: 'SEARCH_EXECUTED', entityType: 'Search',
  version: 1, organizationId: null, boardId: null, metadata: {}, createdAt: '2026-10-05T12:00:00.123456+00:00' });
it('admits an exact canonical original for the current actor with database precision', () => {
  expect(parseSearchInteraction(source(), id)).toEqual(source());
  expect(parseSearchInteraction({ ...source(), actorId: id.toUpperCase() }, id).actorId).toBe(id);
});
it('rejects missing fields, arbitrary metadata, fabricated tenant scope and invalid identity/clock/version', () => {
  const missing = source() as Record<string, unknown>; delete missing.createdAt;
  for (const value of [null, missing, { ...source(), criteria: 'private' }, { ...source(), metadata: { q: 'private' } },
    { ...source(), metadata: [] }, { ...source(), organizationId: id }, { ...source(), boardId: id },
    { ...source(), entityId: other }, { ...source(), version: 2 }, { ...source(), eventType: 'BOARD_FILTER_CHANGED' },
    { ...source(), eventId: '00000000-0000-0000-0000-000000000000' }, { ...source(), createdAt: 'bad' },
    { ...source(), createdAt: '2026-10-05T12:00:00+01:00' }, { ...source(), createdAt: '2026-02-30T12:00:00Z' },
    { ...source(), createdAt: '0001-01-01T00:00:00Z' }]) expect(() => parseSearchInteraction(value, id)).toThrow();
  expect(() => parseSearchInteraction({ ...source(), actorId: other }, id)).toThrow(ChangedSearchInteractionActor);
});
it('deduplicates acknowledgments by original EventId and isolates account changes and explicit clearing', () => {
  const consumer = new SearchInteractionAcknowledgments();
  const first = parseSearchInteraction(source(), id);
  expect(consumer.consume(first)).toBe(true); expect(consumer.consume(first)).toBe(false);
  expect(() => consumer.consume({ ...first, createdAt: '2026-10-05T12:00:01Z' })).toThrow();
  expect(consumer.consume(parseSearchInteraction({ ...source(), actorId: other }, other))).toBe(true);
  expect(consumer.consume(first)).toBe(true);
  consumer.clear(); expect(consumer.consume(first)).toBe(true);
});
