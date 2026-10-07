// A translated draggable can create overflow in the other axis. Admit only
// the scroll surface responsible for the current drag direction.
export class KanbanAutoScroll {
  private axis?: 'horizontal' | 'vertical';
  private pointer?: { document: Document; x: number; y: number; id?: number };
  start(card: boolean, event?: Event) {
    this.finish(); this.axis = card ? 'vertical' : 'horizontal';
    if (event?.type === 'pointerdown' && event instanceof MouseEvent && event.target instanceof Element) {
      this.pointer = { document: event.target.ownerDocument, x: event.clientX, y: event.clientY,
        id: 'pointerId' in event ? (event as PointerEvent).pointerId : undefined };
      this.pointer.document.addEventListener('pointermove', this.pointerMove);
    }
  }
  private pointerMove = (event: Event) => {
    const source = this.pointer;
    if (!source || !(event instanceof MouseEvent)
      || (source.id !== undefined && (event as PointerEvent).pointerId !== source.id)) return;
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
  finish() { this.pointer?.document.removeEventListener('pointermove', this.pointerMove); this.pointer = undefined; this.axis = undefined; }
  canScroll = (element: Element) => !!this.axis && element.hasAttribute('data-kanban-scroll')
    && element.getAttribute('data-kanban-scroll-axis') === this.axis;
}
