import { afterEach, expect, it } from 'vitest';
import { KanbanPointerScrollFrame } from './kanbanPointerScrollFrame';
afterEach(() => document.body.replaceChildren());
function setup() {
  const parent = document.createElement('div'); parent.style.overflow = 'auto'; parent.scrollTop = 343243;
  const node = document.createElement('div'); node.dataset.cardDragId = 'source'; parent.append(node); document.body.append(parent);
  const frame = new KanbanPointerScrollFrame(); node.addEventListener('pointerdown', event => frame.start(event));
  node.dispatchEvent(new Event('pointerdown', { bubbles: true })); return { frame, parent };
}
const apply = (frame: KanbanPointerScrollFrame, ancestors: Element[], id = 'card:source') => frame.modify({
  active: { id }, scrollableAncestors: ancestors, transform: { x: -110, y: 0, scaleX: 1, scaleY: 1 },
} as unknown as Parameters<typeof frame.modify>[0]);
it('compensates removed source and added destination offsets while preserving actual source scrolling', () => {
  const { frame, parent } = setup(); const target = document.createElement('div'); target.scrollTop = 269;
  expect(apply(frame, [parent])).toMatchObject({ x: -110, y: 0 });
  expect(apply(frame, [target])).toMatchObject({ x: -110, y: 342974 });
  // dnd-kit's current-total minus initial-total delta must cancel completely.
  expect(apply(frame, [target]).y + target.scrollTop - parent.scrollTop).toBe(0);
  parent.scrollTop += 100; expect(apply(frame, [target]).y).toBe(343074);
  expect(apply(frame, [parent]).y).toBe(0);
});
it('does not affect another active item, keyboard drags or a finished pointer gesture', () => {
  const { frame } = setup(); expect(apply(frame, [], 'another').y).toBe(0);
  frame.finish(); expect(apply(frame, []).y).toBe(0);
  const node = document.querySelector('[data-card-drag-id]')!; node.addEventListener('keydown', event => frame.start(event));
  node.dispatchEvent(new KeyboardEvent('keydown', { key: 'Space' })); expect(apply(frame, []).y).toBe(0);
});
