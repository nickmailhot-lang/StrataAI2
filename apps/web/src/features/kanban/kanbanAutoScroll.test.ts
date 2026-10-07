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

it('keeps native pointer direction when destination scroll compensation changes the drag delta', () => {
  const scroll = new KanbanAutoScroll(), board = surface('horizontal'), cards = surface('vertical');
  const source = document.createElement('button'); document.body.append(source);
  source.addEventListener('pointerdown', event => scroll.start(true, event));
  source.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, clientX: 100, clientY: 100 }));
  document.dispatchEvent(new MouseEvent('pointermove', { clientX: 500, clientY: 100 }));
  scroll.move({ x: 400, y: 343243 });
  expect(scroll.canScroll(board)).toBe(true); expect(scroll.canScroll(cards)).toBe(false);
  document.dispatchEvent(new MouseEvent('pointermove', { clientX: 110, clientY: 500 }));
  scroll.move({ x: 343243, y: 400 });
  expect(scroll.canScroll(board)).toBe(false); expect(scroll.canScroll(cards)).toBe(true);
  scroll.finish();
  document.dispatchEvent(new MouseEvent('pointermove', { clientX: 900, clientY: 100 }));
  expect(scroll.canScroll(board)).toBe(false); expect(scroll.canScroll(cards)).toBe(false);
  source.remove();
});
