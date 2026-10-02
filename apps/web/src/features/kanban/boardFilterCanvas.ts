import type { BoardSnapshot, WorkCard } from '../../api/workManagement';

export type FilterCard = WorkCard & { listId: string };
export type BoardCanvasFilter = { snapshot: BoardSnapshot; items: FilterCard[] };
export function filterPageMatchesSnapshot(snapshot: BoardSnapshot, items: FilterCard[]): boolean {
  return items.every(item => snapshot.lists.some(column => column.list.id === item.listId && column.list.lifecycleState === 'active'
    && column.cards.some(card => card.id === item.id && card.version === item.version && card.title === item.title
      && card.description === item.description && card.rank === item.rank)));
}
export function filteredBoardCanvas(snapshot: BoardSnapshot, filter: BoardCanvasFilter): BoardSnapshot {
  const current = filter.snapshot === snapshot && filterPageMatchesSnapshot(snapshot, filter.items);
  const ids = new Set(current ? filter.items.map(card => card.id) : []);
  return { ...snapshot, lists: snapshot.lists.map(column => ({ ...column, cards: column.cards.filter(card => ids.has(card.id)) })) };
}
