import type { BoardSnapshot } from '../../api/workManagement';

export type CardMovePreview = { cardId: string; destination: string; before: string };

// Presentation only: keep canonical versions/ranks and snapshots untouched.
export function previewCardMove(snapshot: BoardSnapshot, move?: CardMovePreview): BoardSnapshot {
  if (!move || !snapshot.access.canMove || snapshot.board.lifecycleState !== 'active') return snapshot;
  const source = snapshot.lists.find(column => column.list.lifecycleState === 'active' && column.cards.some(card => card.id === move.cardId));
  const destination = snapshot.lists.find(column => column.list.id === move.destination && column.list.lifecycleState === 'active');
  const card = source?.cards.find(card => card.id === move.cardId);
  if (!card || !destination || move.before === move.cardId) return snapshot;
  const siblings = destination.cards.filter(value => value.id !== move.cardId);
  const index = move.before ? siblings.findIndex(value => value.id === move.before) : siblings.length;
  if (index < 0) return snapshot;
  const positioned = [...siblings.slice(0, index), card, ...siblings.slice(index)];
  return { ...snapshot, lists: snapshot.lists.map(column => column === destination ? { ...column, cards: positioned }
    : column === source ? { ...column, cards: column.cards.filter(value => value.id !== move.cardId) } : column) };
}
