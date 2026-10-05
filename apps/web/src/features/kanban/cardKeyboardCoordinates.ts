import type { KeyboardCoordinateGetter } from '@dnd-kit/core';

export const cardKeyboardCoordinates: KeyboardCoordinateGetter = (event, { active, currentCoordinates, context }) => {
  const horizontal = event.code === 'ArrowLeft' || event.code === 'ArrowRight';
  const direction = event.code === 'ArrowLeft' || event.code === 'ArrowUp' ? -1
    : event.code === 'ArrowRight' || event.code === 'ArrowDown' ? 1 : 0;
  if (!direction || !context.collisionRect) return;
  event.preventDefault();
  const current = context.collisionRect;
  const center = { x: current.left + current.width / 2, y: current.top + current.height / 2 };
  const targets = context.droppableContainers.getEnabled().flatMap(container => {
    if (container.id === active) return [];
    if (!String(container.id).startsWith('card:') && !String(container.id).startsWith('card-end:')) return [];
    // Windowed rows can move when neighboring measurements refine, without
    // resizing this target. Read the committed position at the key boundary;
    // the scheduled droppable measurement can still describe its old layout.
    const rect = container.node.current?.getBoundingClientRect() ?? context.droppableRects.get(container.id); if (!rect) return [];
    const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2;
    // Smooth scrolling rounds fractional row positions to physical pixels.
    // A target within the settled center tolerance is the current destination,
    // not another adjacent move; selecting it again can interrupt the scroll.
    if (horizontal ? (x - center.x) * direction <= 2 : Math.abs(x - center.x) > 1 || (y - center.y) * direction <= 2) return [];
    return [{ x, y }];
  }).sort((a, b) => horizontal
    ? Math.abs(a.x - center.x) - Math.abs(b.x - center.x) || Math.abs(a.y - center.y) - Math.abs(b.y - center.y)
    : Math.abs(a.y - center.y) - Math.abs(b.y - center.y));
  const target = targets[0];
  return target ? { x: currentCoordinates.x + target.x - center.x, y: currentCoordinates.y + target.y - center.y } : undefined;
};
