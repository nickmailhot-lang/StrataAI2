import { focusBoardReturn } from './focusBoardReturn';
import { observeFocusedCardVisibility } from './focusedCardVisibility';

const viewport = window.innerHeight;
let cleanup: (() => void) | undefined;
afterEach(() => { cleanup?.(); cleanup = undefined; vi.restoreAllMocks(); vi.unstubAllGlobals(); document.body.replaceChildren(); Object.defineProperty(window, 'innerHeight', { value: viewport, configurable: true }); });
const rect = (x: number, y: number, width: number, height: number) => ({ x, y, width, height, left: x, right: x + width, top: y, bottom: y + height, toJSON: () => ({}) }) as DOMRect;
function fixture(x: number) {
  Object.defineProperty(window, 'innerHeight', { value: 844, configurable: true });
  let resized: ResizeObserverCallback | undefined;
  const observed = new Set<Element>();
  vi.stubGlobal('ResizeObserver', class { constructor(callback: ResizeObserverCallback) { resized = callback; } observe(element: Element) { observed.add(element); } disconnect() { observed.clear(); } });
  const header = document.createElement('header'), canvas = document.createElement('div'), column = document.createElement('section'), card = document.createElement('a');
  card.href = '#card'; canvas.style.overflowX = 'auto'; column.style.overflowY = 'auto';
  card.style.scrollMarginTop = '4px'; card.style.scrollMarginBottom = '4px';
  canvas.append(column); column.append(card); document.body.append(header, canvas);
  canvas.scrollLeft = 20; column.scrollTop = 100;
  const state = { x, height: 60, pageShift: 0, headerHeight: 80, columnHeight: 500 };
  Object.defineProperty(column, 'clientHeight', { get: () => state.columnHeight });
  vi.spyOn(header, 'getBoundingClientRect').mockImplementation(() => rect(0, 0, 390, state.headerHeight));
  vi.spyOn(canvas, 'getBoundingClientRect').mockImplementation(() => rect(16, 150 - state.pageShift, 358, 500));
  vi.spyOn(column, 'getBoundingClientRect').mockImplementation(() => rect(-4, 150 - state.pageShift, 320, state.columnHeight));
  vi.spyOn(card, 'getBoundingClientRect').mockImplementation(() => rect(state.x, 200 - (column.scrollTop - 100) - state.pageShift, 280, state.height));
  vi.spyOn(window, 'scrollBy').mockImplementation((options: ScrollToOptions | number = 0, top?: number) => { state.pageShift += typeof options === 'number' ? top ?? 0 : options.top ?? 0; });
  // Model the browser's native nearest pan from the existing return regression.
  card.scrollIntoView = vi.fn(() => {
    if (state.x < 16) { canvas.scrollLeft -= 16 - state.x; state.x = 16; }
    const bounds = card.getBoundingClientRect(), bottom = column.getBoundingClientRect().bottom - 4;
    if (bounds.bottom > bottom) column.scrollTop += bounds.bottom - bottom;
  });
  cleanup = observeFocusedCardVisibility(new Map([['card', card]]), () => true);
  return { card, canvas, column, header, state, resize: (element: Element = card) => { if (observed.has(element)) resized?.([], {} as ResizeObserver); } };
}
it('does not undo the return helper’s deliberate preservation of a partly clipped horizontal context', () => {
  const { card, canvas, column } = fixture(12); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100);
  expect(card.scrollIntoView).not.toHaveBeenCalled();
});
it('reveals growing metadata inside the List with outline clearance while preserving horizontal context', () => {
  const { card, canvas, column, state, resize } = fixture(40); card.focus();
  state.height = 480; resize();
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(134);
  expect(window.scrollBy).not.toHaveBeenCalled();
});
it('keeps the owned focused Card clear when the fixed header grows after metadata admission', () => {
  const { card, canvas, column, header, state, resize } = fixture(40); card.focus();
  state.headerHeight = 220; resize(header);
  expect(card).toHaveFocus(); expect(card.getBoundingClientRect().top).toBe(224);
  expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100);
});
it('keeps the owned focused Card clear when its List viewport shrinks without changing Card size', () => {
  const { card, canvas, column, state, resize } = fixture(40); card.focus();
  state.columnHeight = 100; resize(column);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(114);
  expect(canvas.scrollLeft).toBe(20); expect(window.scrollBy).not.toHaveBeenCalled();
});
it('rechecks the owned Card when the viewport resizes without a Card size notification', () => {
  const { card, canvas, column, state } = fixture(40); card.focus();
  state.columnHeight = 100; window.dispatchEvent(new Event('resize'));
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(114); expect(canvas.scrollLeft).toBe(20);
});
