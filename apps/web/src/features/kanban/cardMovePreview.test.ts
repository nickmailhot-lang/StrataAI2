import { previewCardMove } from './cardMovePreview';
import type { BoardSnapshot } from '../../api/workManagement';

const card = (id: string) => ({ id, title: id, description: null, rank: '500000000000000000000000000000', version: 3 });
const snapshot: BoardSnapshot = {
  board: { id: 'board', organizationId: 'org', name: 'Board', description: null, lifecycleState: 'active' },
  access: { canView: true, canEdit: true, canMove: true, canAdminister: false },
  lists: [ { list: { id: 'source', name: 'Source', rank: '1', lifecycleState: 'active' }, cards: [card('moving'), card('stays')] },
    { list: { id: 'dest', name: 'Destination', rank: '2', lifecycleState: 'active' }, cards: [card('first'), card('anchor')] } ],
};
it('presents relative cross-list placement without altering canonical versions, ranks or collections', () => {
  const before = JSON.stringify(snapshot);
  const preview = previewCardMove(snapshot, { cardId: 'moving', destination: 'dest', before: 'anchor' });
  expect(preview.lists.map(column => column.cards.map(value => value.id))).toEqual([['stays'], ['first', 'moving', 'anchor']]);
  expect(preview.lists[1].cards[1]).toBe(snapshot.lists[0].cards[0]);
  expect(JSON.stringify(snapshot)).toBe(before);
  expect(previewCardMove(snapshot)).toBe(snapshot);
});
it('supports same-list prepend and append without duplicating the moving card', () => {
  expect(previewCardMove(snapshot, { cardId: 'stays', destination: 'source', before: 'moving' }).lists[0].cards.map(value => value.id)).toEqual(['stays', 'moving']);
  expect(previewCardMove(snapshot, { cardId: 'moving', destination: 'source', before: '' }).lists[0].cards.map(value => value.id)).toEqual(['stays', 'moving']);
});
it('does not speculate across unavailable scope, permission, or vanished positions', () => {
  const move = { cardId: 'moving', destination: 'dest', before: 'anchor' };
  for (const value of [{ ...move, destination: 'foreign' }, { ...move, before: 'missing' }, { ...move, before: 'moving' }, { ...move, cardId: 'missing' }]) {
    expect(previewCardMove(snapshot, value)).toBe(snapshot);
  }
  const denied = { ...snapshot, access: { ...snapshot.access, canMove: false } };
  expect(previewCardMove(denied, move)).toBe(denied);
  const archived = { ...snapshot, board: { ...snapshot.board, lifecycleState: 'archived' } };
  expect(previewCardMove(archived, move)).toBe(archived);
});
