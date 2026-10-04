import { describe, expect, it } from 'vitest';
import { parseSearchPage } from './globalSearch';
const id = '11111111-1111-4111-8111-111111111111';
const document = () => ({ sourceKind: 'CARD', card: { id, organizationId: id, boardId: id, listId: id,
  title: 'Card', version: 1, lifecycleState: 'active', dueAt: null, dueComplete: false },
  boardName: 'Board', listName: 'List', labels: [], members: [], hasMoreLabels: false, hasMoreMembers: false });
describe('global search response admission', () => {
  it('accepts canonical contextual Cards and an empty continuation page', () => {
    expect(parseSearchPage({ items: [document()], nextCursor: 'opaque' }).items[0].title).toBe('Card');
    expect(parseSearchPage({ items: [], nextCursor: 'next' }, 'previous').nextCursor).toBe('next');
  });
  it('rejects oversized or duplicate result pages and repeated continuation', () => {
    expect(() => parseSearchPage({ items: Array.from({ length: 51 }, document), nextCursor: null })).toThrow();
    expect(() => parseSearchPage({ items: [document(), document()], nextCursor: null })).toThrow();
    expect(() => parseSearchPage({ items: [], nextCursor: 'same' }, 'same')).toThrow();
  });
  it('admits bounded expanded multilingual cursors and rejects oversized tokens', () => {
    expect(parseSearchPage({ items: [], nextCursor: 'x'.repeat(8192) }).nextCursor?.length).toBe(8192);
    expect(() => parseSearchPage({ items: [], nextCursor: 'x'.repeat(8193) })).toThrow();
  });
  it.each(['deleted', 'unknown'])('rejects unsupported lifecycle %s', lifecycleState => {
    const item = document(); item.card.lifecycleState = lifecycleState;
    expect(() => parseSearchPage({ items: [item], nextCursor: null })).toThrow();
  });
  it('rejects malformed dates, routing, revision and protected context', () => {
    for (const change of [{ dueAt: 'invalid' }, { id: 'invalid' }, { version: 0 }]) {
      const item = document();
      expect(() => parseSearchPage({ items: [{ ...item, card: { ...item.card, ...change } }], nextCursor: null })).toThrow();
    }
    expect(() => parseSearchPage({ items: [{ ...document(), members: [{ userId: 'bad', displayName: 'Name' }] }], nextCursor: null })).toThrow();
  });
});
