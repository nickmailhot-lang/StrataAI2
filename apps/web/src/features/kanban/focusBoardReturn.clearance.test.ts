import { focusBoardReturn } from './focusBoardReturn';

const viewport = window.innerHeight;
const rect = (top: number, height: number) => ({ x: 40, y: top, width: 280, height, left: 40, right: 320, top, bottom: top + height, toJSON: () => ({}) }) as DOMRect;
afterEach(() => { vi.restoreAllMocks(); document.body.replaceChildren(); Object.defineProperty(window, 'innerHeight', { value: viewport, configurable: true }); });
function fixture(y: number, columnY: number) {
  Object.defineProperty(window, 'innerHeight', { value: 844, configurable: true });
  const header = document.createElement('header'), column = document.createElement('section'), card = document.createElement('button');
  column.style.overflowY = 'auto'; column.scrollTop = 100;
  card.style.scrollMarginTop = '4px'; card.style.scrollMarginBottom = '4px';
  column.append(card); document.body.append(header, column);
  let pageShift = 0;
  Object.defineProperty(column, 'clientHeight', { value: 500 });
  vi.spyOn(header, 'getBoundingClientRect').mockReturnValue(rect(0, 80));
  vi.spyOn(column, 'getBoundingClientRect').mockImplementation(() => rect(columnY - pageShift, 500));
  vi.spyOn(card, 'getBoundingClientRect').mockImplementation(() => rect(y - (column.scrollTop - 100) - pageShift, 60));
  vi.spyOn(window, 'scrollBy').mockImplementation((options: ScrollToOptions | number = 0, top?: number) => {
    pageShift += typeof options === 'number' ? top ?? 0 : options.top ?? 0;
  });
  return { card, column };
}
it('keeps the focused outline clear of the fixed header without changing List scroll', () => {
  const { card, column } = fixture(80, -100); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(100);
  expect(card.getBoundingClientRect().top).toBe(84);
});
it('keeps the focused outline inside the document bottom edge', () => {
  const { card, column } = fixture(784, 500); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(100);
  expect(card.getBoundingClientRect().bottom).toBe(840);
});
it('keeps outline clearance inside the local List viewport before moving the document', () => {
  const { card, column } = fixture(590, 150); focusBoardReturn(card);
  expect(card).toHaveFocus(); expect(column.scrollTop).toBe(104);
  expect(window.scrollBy).not.toHaveBeenCalled();
});
