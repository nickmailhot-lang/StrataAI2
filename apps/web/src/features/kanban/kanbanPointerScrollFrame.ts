import { getScrollableAncestors, type Modifier } from '@dnd-kit/core';

// Pointer collisions use pointerWithin. Preserve the original source scroll
// frame when dnd-kit switches its accounting to a hovered destination's parents.
export class KanbanPointerScrollFrame {
  private source?: { id: string; ancestors: Element[] };
  start(event: Event) {
    this.finish();
    if (event.type.startsWith('key') || !(event.target instanceof Element)) return;
    const node = event.target.closest('[data-card-drag-id]');
    const id = node?.getAttribute('data-card-drag-id');
    if (node && id) this.source = { id: `card:${id}`, ancestors: getScrollableAncestors(node) };
  }
  finish() { this.source = undefined; }
  private offsets(elements: Element[]) {
    return elements.reduce((sum, element) => {
      const window = element.ownerDocument.defaultView;
      const documentScroll = element === element.ownerDocument.scrollingElement;
      return { x: sum.x + (documentScroll && window ? window.scrollX : element.scrollLeft),
        y: sum.y + (documentScroll && window ? window.scrollY : element.scrollTop) };
    }, { x: 0, y: 0 });
  }
  modify: Modifier = ({ active, scrollableAncestors, transform }) => {
    if (!this.source || active?.id !== this.source.id) return transform;
    const original = this.offsets(this.source.ancestors), current = this.offsets(scrollableAncestors);
    return { ...transform, x: transform.x + original.x - current.x, y: transform.y + original.y - current.y };
  };
}
