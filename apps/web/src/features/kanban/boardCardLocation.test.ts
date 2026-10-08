import { boardCardLocation } from './boardCardLocation';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';

const card: WorkCard = { id: 'card', title: 'Current', description: null, rank: 'rank', version: 3 };
function snapshot(): BoardSnapshot {
  return { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
    access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
    lists: [{ list: { id: 'list', name: 'List', rank: 'rank', lifecycleState: 'active' }, cards: [card] }] };
}

it('does not materialize or scan later Card collections after resolving the current location', () => {
  const value = snapshot(); const readLater = vi.fn(() => Array.from({ length: 5000 }, (_, index) => ({ ...card, id: `later-${index}` })));
  value.lists.push({ list: { id: 'later', name: 'Later', rank: 'rank', lifecycleState: 'active' }, get cards() { return readLater(); } });
  const location = boardCardLocation(value, card.id);
  expect(location?.card).toBe(card); expect(location?.list).toBe(value.lists[0].list);
  expect(readLater).not.toHaveBeenCalled();
});

it('resolves the new canonical parent and revision without mutating prior or archived snapshots', () => {
  const before = snapshot(); const current = snapshot();
  current.lists[0].list = { ...current.lists[0].list, id: 'destination', lifecycleState: 'archived' };
  current.lists[0].cards = [{ ...card, version: 4 }];
  expect(boardCardLocation(current, card.id)).toEqual({ card: current.lists[0].cards[0], list: current.lists[0].list });
  expect(boardCardLocation(before, card.id)).toEqual({ card, list: before.lists[0].list });
  expect(before.lists[0].list.lifecycleState).toBe('active'); expect(before.lists[0].cards[0].version).toBe(3);
});

it('withholds a location for missing scope, absent selection and removed Cards', () => {
  expect(boardCardLocation(undefined, card.id)).toBeUndefined();
  expect(boardCardLocation(snapshot(), undefined)).toBeUndefined();
  expect(boardCardLocation(snapshot(), 'removed')).toBeUndefined();
});
