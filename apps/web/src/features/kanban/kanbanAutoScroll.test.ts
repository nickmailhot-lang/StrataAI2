import { expect, it, vi } from 'vitest';
import type { Modifier } from '@dnd-kit/core';
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

function viewport(axis: string, rect: DOMRect) {
  const element = surface(axis); document.body.append(element);
  vi.spyOn(element, 'getBoundingClientRect').mockReturnValue(rect);
  Object.defineProperties(element, { clientWidth: { value: rect.width }, clientHeight: { value: rect.height },
    scrollWidth: { value: rect.width + 10_000 }, scrollHeight: { value: rect.height + 10_000 } });
  element.scrollLeft = 1000; element.scrollTop = 1000;
  const scrollBy = vi.fn((x: number, y: number) => { element.scrollLeft += x; element.scrollTop += y; });
  Object.defineProperty(element, 'scrollBy', { value: scrollBy });
  return { element, scrollBy };
}
function observe(scroll: KanbanAutoScroll, elements: Element[], rectangles = elements.map(element => element.getBoundingClientRect())) {
  const transform = { x: 12, y: 0, scaleX: 1, scaleY: 1 };
  expect(scroll.observe({ active: null, activatorEvent: null, activeNodeRect: null, draggingNodeRect: null,
    containerNodeRect: null, over: null, overlayNodeRect: null, windowRect: null,
    scrollableAncestors: elements, scrollableAncestorRects: rectangles, transform } satisfies Parameters<Modifier>[0])).toBe(transform);
}
function startPointer(scroll: KanbanAutoScroll, x: number, y: number) {
  const source = document.createElement('button'); document.body.append(source);
  source.addEventListener('pointerdown', event => scroll.start(true, event));
  source.dispatchEvent(new MouseEvent('pointerdown', { clientX: x, clientY: y })); return source;
}
it('uses current Board geometry despite a previous column rectangle and stops on native center input before a React move', () => {
  vi.useFakeTimers(); const scroll = new KanbanAutoScroll(), board = viewport('horizontal', new DOMRect(24, 100, 342, 400));
  const source = startPointer(scroll, 195, 300);
  try {
    observe(scroll, [board.element], [new DOMRect(798, 100, 320, 400)]);
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 36, clientY: 300 }));
    expect(scroll.canScroll(board.element)).toBe(true); expect(scroll.libraryCanScroll(board.element)).toBe(false);
    vi.advanceTimersByTime(20);
    expect(board.scrollBy).toHaveBeenCalledTimes(4);
    expect(board.scrollBy.mock.calls.every(([x, y]) => x < 0 && Math.abs(x) <= 10 && y === 0)).toBe(true);
    expect(board.scrollBy.mock.calls[0][0]).toBeCloseTo(-8.24561403508772);
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 195, clientY: 300 }));
    scroll.move({ x: 345343, y: -53534 }); vi.advanceTimersByTime(50);
    expect(board.scrollBy).toHaveBeenCalledTimes(4);
    scroll.finish(); vi.advanceTimersByTime(100); expect(board.scrollBy).toHaveBeenCalledTimes(4);
  } finally { scroll.finish(); source.remove(); board.element.remove(); vi.useRealTimers(); }
});
it('tracks native direction across nested Card and Board viewports and leaves keyboard scrolling with dnd-kit', () => {
  vi.useFakeTimers(); const scroll = new KanbanAutoScroll();
  const cards = viewport('vertical', new DOMRect(24, 100, 342, 200)), board = viewport('horizontal', new DOMRect(24, 100, 342, 400));
  const source = startPointer(scroll, 195, 200);
  try {
    observe(scroll, [cards.element, board.element]);
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 200, clientY: 295 })); vi.advanceTimersByTime(10);
    expect(cards.scrollBy).toHaveBeenCalledTimes(2); expect(board.scrollBy).not.toHaveBeenCalled();
    expect(cards.scrollBy.mock.calls.every(([x, y]) => x === 0 && y > 0 && y <= 10)).toBe(true);
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 36, clientY: 200 })); vi.advanceTimersByTime(10);
    expect(cards.scrollBy).toHaveBeenCalledTimes(2); expect(board.scrollBy).toHaveBeenCalledTimes(2);
    scroll.start(true); scroll.move({ x: 0, y: 300 });
    expect(scroll.libraryCanScroll(cards.element)).toBe(true); expect(scroll.libraryCanScroll(board.element)).toBe(false);
    vi.advanceTimersByTime(50); expect(board.scrollBy).toHaveBeenCalledTimes(2);
  } finally { scroll.finish(); source.remove(); cards.element.remove(); board.element.remove(); vi.useRealTimers(); }
});
it('bounds native edge speed outside the viewport and ignores clipped, hidden, detached and unowned surfaces', () => {
  vi.useFakeTimers(); const scroll = new KanbanAutoScroll();
  const cards = viewport('vertical', new DOMRect(0, 100, 300, 200)), board = viewport('horizontal', new DOMRect(272, 100, 472, 400));
  const source = startPointer(scroll, 280, 100);
  try {
    observe(scroll, [cards.element, board.element]);
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 100, clientY: 295 }));
    // The Card surface extends under the sidebar, but its parent clips that
    // region. Vertical movement cannot scroll invisible Card content there.
    scroll.move({ x: 0, y: 9000 }); vi.advanceTimersByTime(10);
    expect(cards.scrollBy).not.toHaveBeenCalled();
    expect(board.scrollBy).not.toHaveBeenCalled();
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: -1000, clientY: 200 })); vi.advanceTimersByTime(10);
    expect(board.scrollBy).toHaveBeenCalledTimes(2);
    expect(board.scrollBy.mock.calls.every(([x, y]) => x === -10 && y === 0)).toBe(true);
    board.element.removeAttribute('data-kanban-scroll'); board.scrollBy.mockClear(); vi.advanceTimersByTime(10);
    expect(board.scrollBy).not.toHaveBeenCalled();
    board.element.setAttribute('data-kanban-scroll', 'true');
    vi.mocked(board.element.getBoundingClientRect).mockReturnValue(new DOMRect()); vi.advanceTimersByTime(10);
    expect(board.scrollBy).not.toHaveBeenCalled();
    board.element.remove(); vi.advanceTimersByTime(10); expect(board.scrollBy).not.toHaveBeenCalled();
  } finally { scroll.finish(); source.remove(); cards.element.remove(); board.element.remove(); vi.useRealTimers(); }
});
