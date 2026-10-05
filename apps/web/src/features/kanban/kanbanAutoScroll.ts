// A translated draggable can create overflow in the other axis. Admit only
// the scroll surface responsible for the current drag direction.
export class KanbanAutoScroll {
  private axis?: 'horizontal' | 'vertical';
  start(card: boolean) { this.axis = card ? 'vertical' : 'horizontal'; }
  move(delta: { x: number; y: number }) {
    if (this.axis && (delta.x || delta.y)) this.axis = Math.abs(delta.x) > Math.abs(delta.y) ? 'horizontal' : 'vertical';
  }
  finish() { this.axis = undefined; }
  canScroll = (element: Element) => !!this.axis && element.hasAttribute('data-kanban-scroll')
    && element.getAttribute('data-kanban-scroll-axis') === this.axis;
}
