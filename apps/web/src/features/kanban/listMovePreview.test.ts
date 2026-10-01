import { previewListMove } from './listMovePreview';
import type { BoardSnapshot } from '../../api/workManagement';
const snapshot: BoardSnapshot = { board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: ['first', 'second', 'moving'].map((id, index) => ({ list: { id, name: id, rank: String(index), lifecycleState: 'active', version: 1 }, cards: [] })) };
it('presents prepend, relative and end ordering without mutating canonical ranks, versions or cards', () => {
  const original = JSON.stringify(snapshot);
  const relative = previewListMove(snapshot, { listId: 'moving', before: 'second' });
  expect(relative.lists.map(column => column.list.id)).toEqual(['first', 'moving', 'second']);
  expect(relative.lists[1]).toBe(snapshot.lists[2]);
  expect(previewListMove(snapshot, { listId: 'moving', before: 'first' }).lists.map(column => column.list.id)).toEqual(['moving', 'first', 'second']);
  expect(previewListMove(snapshot, { listId: 'first', before: '' }).lists.map(column => column.list.id)).toEqual(['second', 'moving', 'first']);
  expect(JSON.stringify(snapshot)).toBe(original);
  expect(previewListMove(snapshot)).toBe(snapshot);
});
it('declines speculation for self, vanished anchors, missing lists and denied or archived scope', () => {
  for (const move of [{ listId: 'moving', before: 'moving' }, { listId: 'moving', before: 'missing' }, { listId: 'missing', before: '' }]) expect(previewListMove(snapshot, move)).toBe(snapshot);
  const denied = { ...snapshot, access: { ...snapshot.access, canMove: false } };
  expect(previewListMove(denied, { listId: 'moving', before: 'first' })).toBe(denied);
  const archived = { ...snapshot, lists: snapshot.lists.map(column => ({ ...column, list: { ...column.list, lifecycleState: 'archived' } })) };
  expect(previewListMove(archived, { listId: 'moving', before: 'first' })).toBe(archived);
});
