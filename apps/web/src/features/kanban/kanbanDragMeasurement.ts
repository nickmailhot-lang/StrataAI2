import { getClientRect } from '@dnd-kit/core';

// Windowing pins the source's layout origin. dnd-kit's separate scroll delta
// moves that source during a drag. Replacing its client rect halfway through
// smooth scrolling rebases the scroll delta in a later effect and loses the
// intervening scroll movement. Keep that origin for the current drag only.
export class KanbanDragMeasurement {
  private activeId?: string;
  private source?: { node: HTMLElement; rect: ReturnType<typeof getClientRect> };

  start(id: string) { this.activeId = id.replace(/^card:/, ''); this.source = undefined; }
  finish() { this.activeId = undefined; this.source = undefined; }

  measure = (node: HTMLElement) => {
    const rect = getClientRect(node, { ignoreTransform: true });
    const row = node.closest<HTMLElement>('[data-board-window-id]');
    if (!this.activeId || row?.dataset.boardWindowId !== this.activeId) return rect;
    // A real dimension change still needs a fresh measurement. Row mounting
    // and ancestor scrolling alone must not replace the drag's origin.
    if (this.source?.node === node && this.source.rect.width === rect.width && this.source.rect.height === rect.height) return this.source.rect;
    this.source = { node, rect }; return rect;
  };
}
