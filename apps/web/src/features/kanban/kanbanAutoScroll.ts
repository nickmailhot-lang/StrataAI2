// A translated draggable can create overflow in the other axis. Admit only
// the scroll surface responsible for the current drag direction.
import type { Modifier } from '@dnd-kit/core';

export class KanbanAutoScroll {
  private axis?: 'horizontal' | 'vertical';
  private pointer?: { document: Document; window: Window | null; x: number; y: number;
    currentX: number; currentY: number; id?: number };
  private ancestors: Element[] = [];
  private timer?: number;
  observe: Modifier = ({ scrollableAncestors, transform }) => {
    // Element identities are current even while the library's separately
    // measured rectangle array still belongs to the previous hovered target.
    this.ancestors = [...scrollableAncestors]; return transform;
  };
  start(card: boolean, event?: Event) {
    this.finish(); this.axis = card ? 'vertical' : 'horizontal';
    if (event?.type === 'pointerdown' && event instanceof MouseEvent && event.target instanceof Element) {
      const document = event.target.ownerDocument;
      this.pointer = { document, window: document.defaultView, x: event.clientX, y: event.clientY,
        currentX: event.clientX, currentY: event.clientY,
        id: 'pointerId' in event ? (event as PointerEvent).pointerId : undefined };
      this.pointer.document.addEventListener('pointermove', this.pointerMove);
      this.timer = this.pointer.window?.setInterval(this.scrollNativePointer, 5);
    }
  }
  private pointerMove = (event: Event) => {
    const source = this.pointer;
    if (!source || !(event instanceof MouseEvent)
      || (source.id !== undefined && (event as PointerEvent).pointerId !== source.id)) return;
    source.currentX = event.clientX; source.currentY = event.clientY;
    this.chooseAxis({ x: event.clientX - source.x, y: event.clientY - source.y });
  };
  move(delta: { x: number; y: number }) {
    // dnd-kit's public delta includes layout and source/destination scroll
    // offsets. Those offsets cannot change a native pointer's travel direction.
    if (!this.pointer) this.chooseAxis(delta);
  }
  private chooseAxis(delta: { x: number; y: number }) {
    if (this.axis && (delta.x || delta.y)) this.axis = Math.abs(delta.x) > Math.abs(delta.y) ? 'horizontal' : 'vertical';
  }
  private scrollNativePointer = () => {
    const pointer = this.pointer, horizontal = this.axis === 'horizontal';
    if (!pointer?.window || !this.axis) return;
    for (let index = 0; index < this.ancestors.length; index++) {
      const element = this.ancestors[index];
      if (!element.isConnected || !this.canScroll(element)) continue;
      const rect = element.getBoundingClientRect();
      let left = Math.max(0, rect.left), right = Math.min(pointer.window.innerWidth, rect.right);
      let top = Math.max(0, rect.top), bottom = Math.min(pointer.window.innerHeight, rect.bottom);
      for (const parent of this.ancestors.slice(index + 1)) {
        if (!parent.isConnected || parent === pointer.document.scrollingElement) continue;
        const bounds = parent.getBoundingClientRect();
        left = Math.max(left, bounds.left); right = Math.min(right, bounds.right);
        top = Math.max(top, bounds.top); bottom = Math.min(bottom, bounds.bottom);
      }
      if (![left, right, top, bottom].every(Number.isFinite) || right <= left || bottom <= top) continue;
      const point = horizontal ? pointer.currentX : pointer.currentY;
      const cross = horizontal ? pointer.currentY : pointer.currentX;
      if (!Number.isFinite(point) || !Number.isFinite(cross)
        || cross < (horizontal ? top : left) || cross > (horizontal ? bottom : right)) continue;
      const start = horizontal ? left : top, end = horizontal ? right : bottom;
      const threshold = (end - start) * 0.2;
      // Match the normal dnd-kit edge rate, bounded to ten pixels per tick.
      // Recompute from the native point on every tick so center input stops
      // immediately even before a React effect or hovered rectangle catches up.
      const step = point < start + threshold ? -10 * Math.min(1, (start + threshold - point) / threshold)
        : point > end - threshold ? 10 * Math.min(1, (point - end + threshold) / threshold) : 0;
      const offset = horizontal ? element.scrollLeft : element.scrollTop;
      const maximum = horizontal ? element.scrollWidth - element.clientWidth : element.scrollHeight - element.clientHeight;
      if (step < 0 && offset > 0 || step > 0 && offset < maximum) {
        element.scrollBy(horizontal ? step : 0, horizontal ? 0 : step); return;
      }
    }
  };
  finish() {
    this.pointer?.document.removeEventListener('pointermove', this.pointerMove);
    if (this.timer !== undefined) this.pointer?.window?.clearInterval(this.timer);
    this.timer = undefined; this.pointer = undefined; this.axis = undefined; this.ancestors = [];
  }
  canScroll = (element: Element) => !!this.axis && element.hasAttribute('data-kanban-scroll')
    && element.getAttribute('data-kanban-scroll-axis') === this.axis;
  libraryCanScroll = (element: Element) => !this.pointer && this.canScroll(element);
}
