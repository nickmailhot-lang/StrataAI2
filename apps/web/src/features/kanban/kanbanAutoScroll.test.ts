import { expect, it } from 'vitest';
import { KanbanAutoScroll } from './kanbanAutoScroll';
function surface(axis: string) { const element = document.createElement('div'); element.setAttribute('data-kanban-scroll', 'true'); element.dataset.kanbanScrollAxis = axis; return element; }
it('routes horizontal Card drags past nested vertical surfaces and supports changing direction', () => {
  const scroll = new KanbanAutoScroll(), board = surface('horizontal'), cards = surface('vertical');
  scroll.start(true); scroll.move({ x: 207, y: 0 });
  expect(scroll.canScroll(cards)).toBe(false); expect(scroll.canScroll(board)).toBe(true);
  scroll.move({ x: 12, y: 300 }); expect(scroll.canScroll(cards)).toBe(true); expect(scroll.canScroll(board)).toBe(false);
  scroll.finish(); expect(scroll.canScroll(cards)).toBe(false); expect(scroll.canScroll(board)).toBe(false);
});
it('starts List movement on the Board and excludes unowned surfaces', () => {
  const scroll = new KanbanAutoScroll(); scroll.start(false); expect(scroll.canScroll(surface('horizontal'))).toBe(true);
  expect(scroll.canScroll(surface('vertical'))).toBe(false); expect(scroll.canScroll(document.createElement('div'))).toBe(false);
});
