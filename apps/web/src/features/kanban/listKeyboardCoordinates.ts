import type { KeyboardCoordinateGetter } from '@dnd-kit/core';

export class ListKeyboardNavigation {
  private destination?: string | number;
  start() { this.destination = undefined; }
  finish() { this.destination = undefined; }
  coordinates: KeyboardCoordinateGetter = (event, args) => {
    const next = listKeyboardTarget(event, args, this.destination);
    if (!next) return;
    this.destination = next.id;
    return next.coordinates;
  };
}

// Move one available column per key, regardless of responsive column width.
export const listKeyboardCoordinates: KeyboardCoordinateGetter = (event, args) => listKeyboardTarget(event, args)?.coordinates;
function listKeyboardTarget(event: KeyboardEvent, { active, currentCoordinates, context }: Parameters<KeyboardCoordinateGetter>[1], destination?: string | number) {
  const direction = event.code === 'ArrowRight' ? 1 : event.code === 'ArrowLeft' ? -1 : 0;
  if (!direction || !context.collisionRect) return;
  event.preventDefault();
  // The sensor collision rect can lag the source's separate scroll delta.
  // Use the rendered source and target in the same committed coordinate frame.
  const current = context.droppableContainers.getEnabled().find(container => container.id === active)
    ?.node?.current?.getBoundingClientRect() ?? context.collisionRect;
  const center = { x: current.left + current.width / 2, y: current.top + current.height / 2 };
  // A scroll frame can leave the translated source briefly behind its selected
  // destination. Advance from that destination, rather than selecting it again.
  // Geometry still uses the rendered source for the exact translation delta.
  const previous = context.droppableContainers.getEnabled().find(container => container.id === destination);
  const previousRect = previous?.node?.current?.getBoundingClientRect()
    ?? (previous ? context.droppableRects.get(previous.id) : undefined);
  const origin = previousRect ? previousRect.left + previousRect.width / 2 : center.x;
  const targets = context.droppableContainers.getEnabled().flatMap(container => {
    if (container.id === active) return [];
    if (String(container.id).startsWith('card:') || String(container.id).startsWith('card-end:')) return [];
    // Windowed columns move while smooth scrolling settles. Use their
    // committed position at this key boundary rather than a queued measure.
    const rect = container.node?.current?.getBoundingClientRect() ?? context.droppableRects.get(container.id);
    if (!rect) return [];
    const x = rect.left + rect.width / 2;
    return (x - origin) * direction > 2 ? [{ id: container.id, x, y: rect.top + rect.height / 2 }] : [];
  }).sort((a, b) => Math.abs(a.x - origin) - Math.abs(b.x - origin));
  const target = targets[0];
  return target ? { id: target.id, coordinates: { x: currentCoordinates.x + target.x - center.x, y: currentCoordinates.y + target.y - center.y } } : undefined;
}
