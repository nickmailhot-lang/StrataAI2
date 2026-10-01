import type { BoardSnapshot } from '../../api/workManagement';
export type ListMovePreview = { listId: string; before: string };
export function previewListMove(snapshot: BoardSnapshot, move?: ListMovePreview): BoardSnapshot {
  if (!move || !snapshot.access.canMove || snapshot.board.lifecycleState !== 'active') return snapshot;
  const moving = snapshot.lists.find(column => column.list.id === move.listId && column.list.lifecycleState === 'active');
  if (!moving || move.before === move.listId) return snapshot;
  const remaining = snapshot.lists.filter(column => column !== moving);
  const index = move.before ? remaining.findIndex(column => column.list.id === move.before && column.list.lifecycleState === 'active') : remaining.length;
  if (index < 0) return snapshot;
  return { ...snapshot, lists: [...remaining.slice(0, index), moving, ...remaining.slice(index)] };
}
