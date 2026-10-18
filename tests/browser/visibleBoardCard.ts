import { expect, type Locator } from '@playwright/test';

// Establish the visible-target precondition before testing exact return offsets.
// Card metadata/text size can change row height; keep horizontal context intact.
export async function focusVisibleBoardCard(card: Locator) {
  await card.evaluate(node => {
    const column = node.closest<HTMLElement>('[data-kanban-scroll-axis="vertical"]');
    if (!column) throw new Error('Expected a Card in its local scrollable List.');
    const bounds = node.getBoundingClientRect();
    const top = column.getBoundingClientRect().top + column.clientTop;
    const bottom = top + column.clientHeight;
    column.scrollTop += bounds.top < top ? bounds.top - top : Math.max(0, bounds.bottom - bottom);
    (node as HTMLElement).focus({ preventScroll: true });
  });
  await expect(card).toBeFocused();
  await expect.poll(() => card.evaluate(node => {
    const column = node.closest<HTMLElement>('[data-kanban-scroll-axis="vertical"]')!;
    const bounds = node.getBoundingClientRect(), top = column.getBoundingClientRect().top + column.clientTop;
    return bounds.top >= top && bounds.bottom <= top + column.clientHeight;
  })).toBe(true);
}
