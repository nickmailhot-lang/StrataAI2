import { focusBoardReturn } from './focusBoardReturn';

const viewport = { width: window.innerWidth, height: window.innerHeight };
const rectangle = (x: number, y: number, width: number, height: number) => ({
  x, y, width, height, left: x, right: x + width, top: y, bottom: y + height, toJSON: () => ({}),
}) as DOMRect;
function fixture({ x = 12, y = 300, containerY = 150 } = {}) {
  Object.defineProperties(window, { innerWidth: { value: 390, configurable: true }, innerHeight: { value: 844, configurable: true } });
  const header = document.createElement('header'), canvas = document.createElement('div');
  const column = document.createElement('section'), card = document.createElement('button');
  header.style.position = 'fixed'; canvas.style.overflowX = 'auto'; column.style.overflowY = 'auto';
  canvas.append(column); column.append(card); document.body.append(header, canvas);
  canvas.scrollLeft = 20; column.scrollTop = 100;
  const state = { x, y, pageShift: 0 };
  vi.spyOn(header, 'getBoundingClientRect').mockImplementation(() => rectangle(0, 0, 390, 80));
  vi.spyOn(canvas, 'getBoundingClientRect').mockImplementation(() => rectangle(16, containerY - state.pageShift, 358, 500));
  vi.spyOn(column, 'getBoundingClientRect').mockImplementation(() => rectangle(-4, containerY - state.pageShift, 320, 500));
  vi.spyOn(card, 'getBoundingClientRect').mockImplementation(() => rectangle(state.x, state.y - state.pageShift, 284, 60));
  vi.spyOn(window, 'scrollBy').mockImplementation((value: ScrollToOptions | number = 0, top?: number) => {
    state.pageShift += typeof value === 'number' ? top ?? 0 : value.top ?? 0;
  });
  // Model native nearest scrolling of a partly clipped Card. The production
  // baseline independently reproduces this exact20→16 ancestor movement.
  card.scrollIntoView = vi.fn(() => {
    if (state.x < 16) { canvas.scrollLeft -= 16 - state.x; state.x = 16; }
    else if (state.x >= 374) { canvas.scrollLeft += state.x - 40; state.x = 40; }
    const bounds = card.getBoundingClientRect();
    if (bounds.bottom > window.innerHeight) window.scrollBy({ top: bounds.bottom - window.innerHeight });
    else if (bounds.top < 0) window.scrollBy({ top: bounds.top });
  });
  return { card, canvas, column, state };
}
afterEach(() => {
  vi.restoreAllMocks(); document.body.replaceChildren();
  Object.defineProperties(window, { innerWidth: { value: viewport.width, configurable: true }, innerHeight: { value: viewport.height, configurable: true } });
});

it('PRD-09-VS-01-T-02: visible Card return preserves exact canvas and List scroll even when its edge is partly clipped', () => {
  const { card, canvas, column, state } = fixture(); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100); expect(state.pageShift).toBe(0);
});
it('PRD-09-VS-01-T-02: reveal below the viewport moves only the document and preserves local context', () => {
  const { card, canvas, column, state } = fixture({ y: 1000, containerY: 900 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100);
  expect(state.pageShift).toBe(216); expect(card.getBoundingClientRect().bottom).toBe(844);
});
it('PRD-09-VS-01-T-02: reveal above the fixed header preserves canvas and List scroll', () => {
  const { card, canvas, column, state } = fixture({ y: 40, containerY: -100 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100);
  expect(state.pageShift).toBe(-40); expect(card.getBoundingClientRect().top).toBe(80);
});
it('PRD-09-VS-01-T-02: a wholly hidden relocated Card can still reveal its local container', () => {
  const { card, canvas, column } = fixture({ x: 500 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(480); expect(column.scrollTop).toBe(100);
  expect(card.getBoundingClientRect().left).toBe(40);
});
it('PRD-09-VS-01-T-02: absent fallback has no focus or scroll side effect', () => {
  const { state } = fixture(); expect(() => focusBoardReturn(null)).not.toThrow();
  expect(window.scrollBy).not.toHaveBeenCalled(); expect(state.pageShift).toBe(0);
});
