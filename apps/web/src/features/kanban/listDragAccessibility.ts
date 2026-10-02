import type { Announcements } from '@dnd-kit/core';
import type { BoardSnapshot } from '../../api/workManagement';

export const listDragInstructions = {
  draggable: 'To move a list or card, press Space or Enter. Use Left and Right for list positions, or the arrow keys for card positions. Press Space or Enter to drop, or Escape to cancel. Check the move status for confirmation.',
};

export function listDragAnnouncements(snapshot: BoardSnapshot): Announcements {
  const lists = snapshot.lists.filter(column => column.list.lifecycleState === 'active');
  const name = (id: string | number) => lists.find(column => column.list.id === id)?.list.name;
  const cardName = (id: string | number) => lists.flatMap(column => column.cards).find(card => `card:${card.id}` === id)?.title;
  const cardPosition = (id: string | number) => {
    const column = lists.find(value => `card-end:${value.list.id}` === id);
    return column ? `at the end of ${column.list.name}` : cardName(id) ? `before ${cardName(id)}` : undefined;
  };
  const position = (id: string | number) => id === 'list-end' ? 'at the end of the Board'
    : name(id) ? `before ${name(id)}` : undefined;
  return {
    onDragStart: ({ active }) => cardName(active.id) ? `Dragging ${cardName(active.id)} card. Use arrow keys to choose a position.`
      : name(active.id) ? `Dragging ${name(active.id)} list. Use Left and Right to choose a position.` : undefined,
    onDragOver: ({ active, over }) => {
      if (cardName(active.id)) return over && over.id !== active.id && cardPosition(over.id) ? `${cardName(active.id)} card can be dropped ${cardPosition(over.id)}.` : 'Card is outside a new available position.';
      if (!name(active.id)) return;
      if (!over) return 'Outside the available list positions. Dropping here will not move the list.';
      if (over.id === active.id) return `${name(active.id)} list is at its starting position.`;
      const target = position(over.id);
      return target ? `${name(active.id)} list can be dropped ${target}.` : undefined;
    },
    onDragEnd: ({ active, over }) => {
      if (cardName(active.id)) return over && over.id !== active.id && cardPosition(over.id) ? `Drop requested for ${cardName(active.id)} card ${cardPosition(over.id)}. Check the move status for confirmation.` : 'Card drag ended without a move.';
      if (!name(active.id)) return;
      if (!over || over.id === active.id) return `Drag ended. ${name(active.id)} list was not moved.`;
      const target = position(over.id);
      return target ? `Drop requested for ${name(active.id)} list ${target}. Check the move status for confirmation.` : undefined;
    },
    onDragCancel: ({ active }) => cardName(active.id) ? `Drag cancelled. ${cardName(active.id)} card was not moved.`
      : name(active.id) ? `Drag cancelled. ${name(active.id)} list was not moved.` : undefined,
  };
}
