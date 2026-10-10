import { closestCenter, pointerWithin, type CollisionDetection } from '@dnd-kit/core';

export const kanbanCollisions: CollisionDetection = args => {
  const movingCard = String(args.active.id).startsWith('card:');
  const droppableContainers = args.droppableContainers.filter(value => {
    const id = String(value.id);
    const cardTarget = id.startsWith('card:') || id.startsWith('card-end:');
    // A translated source can overlap a newly mounted destination during
    // pointer scrolling. It cannot be its own drop target or obscure that
    // destination. Keyboard traversal retains its starting-position target.
    return movingCard === cardTarget && (!args.pointerCoordinates || value.id !== args.active.id);
  });
  return args.pointerCoordinates ? pointerWithin({ ...args, droppableContainers })
    : closestCenter({ ...args, droppableContainers });
};
