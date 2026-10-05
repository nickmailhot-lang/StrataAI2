import type { KeyboardCoordinateGetter } from '@dnd-kit/core';

// Move one available column per key, regardless of responsive column width.
export const listKeyboardCoordinates: KeyboardCoordinateGetter = (event, { active, currentCoordinates, context }) => {
  const direction = event.code === 'ArrowRight' ? 1 : event.code === 'ArrowLeft' ? -1 : 0;
  if (!direction || !context.collisionRect) return;
  event.preventDefault();
  const current = context.collisionRect;
  const center = { x: current.left + current.width / 2, y: current.top + current.height / 2 };
  const targets = context.droppableContainers.getEnabled().flatMap(container => {
    if (container.id === active) return [];
    if (String(container.id).startsWith('card:') || String(container.id).startsWith('card-end:')) return [];
    // Windowed columns move while smooth scrolling settles. Use their
    // committed position at this key boundary rather than a queued measure.
    const rect = container.node?.current?.getBoundingClientRect() ?? context.droppableRects.get(container.id);
    if (!rect) return [];
    const x = rect.left + rect.width / 2;
    return (x - center.x) * direction > 2 ? [{ x, y: rect.top + rect.height / 2 }] : [];
  }).sort((a, b) => Math.abs(a.x - center.x) - Math.abs(b.x - center.x));
  const target = targets[0];
  return target ? { x: currentCoordinates.x + target.x - center.x, y: currentCoordinates.y + target.y - center.y } : undefined;
};
