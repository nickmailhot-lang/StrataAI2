import { focusBoardReturn } from './focusBoardReturn';
import { observeFocusedCardVisibility } from './focusedCardVisibility';

const viewport = window.innerHeight, scrollDescriptor = Object.getOwnPropertyDescriptor(window, 'scrollY');
let cleanup: (() => void) | undefined;
const rect = (x: number, y: number, width: number, height: number) => ({ x, y, width, height, left: x, right: x + width, top: y, bottom: y + height, toJSON: () => ({}) }) as DOMRect;
afterEach(() => {
  cleanup?.(); cleanup = undefined; vi.restoreAllMocks(); vi.unstubAllGlobals(); document.body.replaceChildren();
  Object.defineProperty(window, 'innerHeight', { value: viewport, configurable: true });
  if (scrollDescriptor) Object.defineProperty(window, 'scrollY', scrollDescriptor);
});
it.each([{ edge: 'viewport bottom', y: 784.75, columnY: 500 }, { edge: 'fixed header', y: 79.25, columnY: -100 }])(
  'uses identical entry/return reveal policy at a fractional %s boundary', ({ y, columnY }) => {
    Object.defineProperty(window, 'innerHeight', { value: 844, configurable: true });
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
    const header = document.createElement('header'), canvas = document.createElement('div'), column = document.createElement('section');
    const card = document.createElement('a'), dialogControl = document.createElement('button'); card.href = '#card';
    canvas.style.overflowX = 'auto'; column.style.overflowY = 'auto'; canvas.append(column); column.append(card); document.body.append(header, canvas, dialogControl);
    canvas.scrollLeft = 20; column.scrollTop = 100;
    const state = { scroll: 35 };
    Object.defineProperty(window, 'scrollY', { get: () => state.scroll, configurable: true });
    Object.defineProperty(column, 'clientHeight', { value: 500 });
    vi.spyOn(header, 'getBoundingClientRect').mockReturnValue(rect(0, 0, 390, 80));
    vi.spyOn(canvas, 'getBoundingClientRect').mockImplementation(() => rect(16, columnY + 35 - state.scroll, 358, 500));
    vi.spyOn(column, 'getBoundingClientRect').mockImplementation(() => rect(-4, columnY + 35 - state.scroll, 320, 500));
    vi.spyOn(card, 'getBoundingClientRect').mockImplementation(() => rect(40, y + 35 - state.scroll, 280, 60));
    // Model integral CSSOM scrolling at DPR1; the CI trace retains integer35→36.
    // The synthetic fractional rectangles reproduce a policy mismatch, not an
    // unrecorded claim about the exact CI floating-point rectangle.
    vi.spyOn(window, 'scrollBy').mockImplementation((value: ScrollToOptions | number = 0, top?: number) => {
      state.scroll += Math.round(typeof value === 'number' ? top ?? 0 : value.top ?? 0);
    });
    cleanup = observeFocusedCardVisibility(new Map([['card', card]]), () => true);
    card.focus(); const entry = window.scrollY;
    dialogControl.focus(); focusBoardReturn(card);
    expect(window.scrollY).toBe(entry); expect(card).toHaveFocus();
    expect(canvas.scrollLeft).toBe(20); expect(column.scrollTop).toBe(100);
  });
