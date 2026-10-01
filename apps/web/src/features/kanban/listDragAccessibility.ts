import type { Announcements } from '@dnd-kit/core';
import type { BoardSnapshot } from '../../api/workManagement';

export const listDragInstructions = {
  draggable: 'To move a list, press Space or Enter. Use Left and Right to choose a list position. Press Space or Enter to drop, or Escape to cancel. Check the move status for confirmation.',
};

export function listDragAnnouncements(snapshot: BoardSnapshot): Announcements {
  const lists = snapshot.lists.filter(column => column.list.lifecycleState === 'active');
  const name = (id: string | number) => lists.find(column => column.list.id === id)?.list.name;
  const position = (id: string | number) => id === 'list-end' ? 'at the end of the Board'
    : name(id) ? `before ${name(id)}` : undefined;
  return {
    onDragStart: ({ active }) => name(active.id) ? `Dragging ${name(active.id)} list. Use Left and Right to choose a position.` : undefined,
    onDragOver: ({ active, over }) => {
      if (!name(active.id)) return;
      if (!over) return 'Outside the available list positions. Dropping here will not move the list.';
      if (over.id === active.id) return `${name(active.id)} list is at its starting position.`;
      const target = position(over.id);
      return target ? `${name(active.id)} list can be dropped ${target}.` : undefined;
    },
    onDragEnd: ({ active, over }) => {
      if (!name(active.id)) return;
      if (!over || over.id === active.id) return `Drag ended. ${name(active.id)} list was not moved.`;
      const target = position(over.id);
      return target ? `Drop requested for ${name(active.id)} list ${target}. Check the move status for confirmation.` : undefined;
    },
    onDragCancel: ({ active }) => name(active.id) ? `Drag cancelled. ${name(active.id)} list was not moved.` : undefined,
  };
}
