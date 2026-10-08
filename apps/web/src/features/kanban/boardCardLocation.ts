import type { BoardSnapshot } from '../../api/workManagement';

export function boardCardLocation(snapshot: BoardSnapshot | undefined, cardId: string | undefined) {
  if (!snapshot || !cardId) return undefined;
  for (const column of snapshot.lists) {
    const card = column.cards.find(item => item.id === cardId);
    if (card) return { card, list: column.list };
  }
  return undefined;
}
