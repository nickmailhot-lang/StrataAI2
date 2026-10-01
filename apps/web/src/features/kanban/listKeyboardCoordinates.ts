import type { KeyboardCoordinateGetter } from '@dnd-kit/core';

// Move one available column per key, regardless of responsive column width.
export const listKeyboardCoordinates: KeyboardCoordinateGetter = (event, { currentCoordinates, context }) => {
  const direction = event.code === 'ArrowRight' ? 1 : event.code === 'ArrowLeft' ? -1 : 0;
  if (!direction || !context.collisionRect) return;
  event.preventDefault();
  const current = context.collisionRect;
  const center = { x: current.left + current.width / 2, y: current.top + current.height / 2 };
  const targets = context.droppableContainers.getEnabled().flatMap(container => {
    const rect = context.droppableRects.get(container.id);
    if (!rect) return [];
    const x = rect.left + rect.width / 2;
    return (x - center.x) * direction > 1 ? [{ x, y: rect.top + rect.height / 2 }] : [];
  }).sort((a, b) => Math.abs(a.x - center.x) - Math.abs(b.x - center.x));
  const target = targets[0];
  return target ? { x: currentCoordinates.x + target.x - center.x, y: currentCoordinates.y + target.y - center.y } : undefined;
};
