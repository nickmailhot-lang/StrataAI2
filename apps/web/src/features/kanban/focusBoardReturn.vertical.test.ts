import { focusBoardReturn } from './focusBoardReturn';

const viewport = { width: window.innerWidth, height: window.innerHeight };
const rectangle = (x: number, y: number, width: number, height: number) => ({
  x, y, width, height, left: x, right: x + width, top: y, bottom: y + height, toJSON: () => ({}),
}) as DOMRect;
function fixture({ y = 756, columnTop = 148, columnHeight = 700, border = 0, canvasHeight = 800 } = {}) {
  Object.defineProperties(window, { innerWidth: { value: 390, configurable: true }, innerHeight: { value: 1000, configurable: true } });
  const header = document.createElement('header'), canvas = document.createElement('div');
  const column = document.createElement('section'), card = document.createElement('button');
  header.style.position = 'fixed'; canvas.style.overflowX = 'auto'; canvas.style.overflowY = 'auto'; column.style.overflowY = 'auto';
  canvas.append(column); column.append(card); document.body.append(header, canvas);
  canvas.scrollLeft = 20; canvas.scrollTop = 0; column.scrollTop = 100;
  Object.defineProperties(canvas, { clientHeight: { value: canvasHeight, configurable: true } });
  Object.defineProperties(column, { clientTop: { value: border, configurable: true }, clientHeight: { value: columnHeight, configurable: true } });
  vi.spyOn(header, 'getBoundingClientRect').mockImplementation(() => rectangle(0, 0, 390, 56));
  vi.spyOn(canvas, 'getBoundingClientRect').mockImplementation(() => rectangle(16, 100, 358, canvasHeight));
  vi.spyOn(column, 'getBoundingClientRect').mockImplementation(() => rectangle(-4, columnTop - canvas.scrollTop, 320, columnHeight + border * 2));
  vi.spyOn(card, 'getBoundingClientRect').mockImplementation(() => rectangle(12, y - (column.scrollTop - 100) - canvas.scrollTop, 284, 136));
  vi.spyOn(window, 'scrollBy').mockImplementation(() => {});
  card.scrollIntoView = vi.fn();
  return { card, canvas, column };
}
function insideClient(card: HTMLElement, parent: HTMLElement) {
  const target = card.getBoundingClientRect(), origin = parent.getBoundingClientRect().top + parent.clientTop;
  expect(target.top).toBeGreaterThanOrEqual(origin);
  expect(target.bottom).toBeLessThanOrEqual(origin + parent.clientHeight);
}
afterEach(() => {
  vi.restoreAllMocks(); document.body.replaceChildren();
  Object.defineProperties(window, { innerWidth: { value: viewport.width, configurable: true }, innerHeight: { value: viewport.height, configurable: true } });
});

it('PRD-09-VS-01-T-03/AC-01/02: reveal the original 44px lower-edge clip without panning horizontal context', () => {
  const { card, canvas, column } = fixture(); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(144); expect(canvas.scrollLeft).toBe(20); expect(canvas.scrollTop).toBe(0);
  insideClient(card, column); insideClient(card, canvas); expect(window.scrollBy).not.toHaveBeenCalled();
});
it('PRD-09-VS-01-T-03/AC-02: reveal partial clipping above the List while preserving the document and canvas', () => {
  const { card, canvas, column } = fixture({ y: 120 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(72); expect(canvas.scrollLeft).toBe(20); expect(canvas.scrollTop).toBe(0);
  insideClient(card, column); insideClient(card, canvas); expect(window.scrollBy).not.toHaveBeenCalled();
});
it('PRD-09-VS-01-T-03/AC-02: use the scroll client box rather than its border box', () => {
  const { card, canvas, column } = fixture({ border: 2 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(142); expect(canvas.scrollLeft).toBe(20);
  insideClient(card, column); insideClient(card, canvas); expect(window.scrollBy).not.toHaveBeenCalled();
});
it('PRD-09-VS-01-T-03/AC-02: remeasure after inner scrolling so every nested vertical viewport reveals the Card', () => {
  const { card, canvas, column } = fixture({ y: 750, columnTop: 150, canvasHeight: 700 }); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(136); expect(canvas.scrollTop).toBe(50); expect(canvas.scrollLeft).toBe(20);
  insideClient(card, column); insideClient(card, canvas); expect(window.scrollBy).not.toHaveBeenCalled();
});

it('PRD-09-VS-01-T-03/AC-02: a wholly hidden vertical target must not pan a partly visible horizontal context', () => {
  const { card, canvas, column } = fixture({ y: 900 });
  card.scrollIntoView = vi.fn(() => { canvas.scrollLeft = 16; });
  focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(canvas.scrollLeft).toBe(20);
  expect(column.scrollTop).toBe(288); insideClient(card, column); insideClient(card, canvas);
  expect(card.scrollIntoView).not.toHaveBeenCalled();
});
