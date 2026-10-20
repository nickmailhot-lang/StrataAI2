import { observeFocusedCardVisibility } from './focusedCardVisibility';
import { revealBoardTarget } from './focusBoardReturn';

vi.mock('./focusBoardReturn', () => ({ revealBoardTarget: vi.fn() }));

let callbacks: ResizeObserverCallback[];
let disconnect: ReturnType<typeof vi.fn>;
let cleanup: (() => void) | undefined;
beforeEach(() => {
  vi.mocked(revealBoardTarget).mockClear();
  callbacks = [];
  disconnect = vi.fn();
  vi.stubGlobal('ResizeObserver', class {
    constructor(callback: ResizeObserverCallback) { callbacks.push(callback); }
    observe = vi.fn();
    disconnect = disconnect;
  });
});
afterEach(() => { cleanup?.(); cleanup = undefined; document.body.replaceChildren(); vi.unstubAllGlobals(); });
function setup(canReveal = () => true) {
  const card = document.createElement('a'); card.href = '#card'; card.textContent = 'Inspect roof';
  const other = document.createElement('button'); other.textContent = 'Other control';
  document.body.append(card, other);
  cleanup = observeFocusedCardVisibility(new Map([['card', card]]), canReveal);
  card.focus();
  const resize = () => callbacks.at(-1)?.([], {} as ResizeObserver);
  return { card, other, resize, scroll: vi.mocked(revealBoardTarget) };
}
it('reveals only the focused registered Card on metadata resize without moving focus', () => {
  const { card, resize, scroll } = setup(); scroll.mockClear(); resize();
  expect(scroll).toHaveBeenCalledExactlyOnceWith(card);
  expect(document.activeElement).toBe(card);
});
it('ignores queued resize after focus moves elsewhere, removal, or cleanup', () => {
  const { card, other, resize, scroll } = setup(); scroll.mockClear(); other.focus(); resize();
  expect(scroll).not.toHaveBeenCalled();
  card.focus(); scroll.mockClear(); card.remove(); resize(); expect(scroll).not.toHaveBeenCalled();
  cleanup?.(); other.focus(); resize(); expect(scroll).not.toHaveBeenCalled();
});
it('does not compete with an active Card or List drag', () => {
  let dragging = true;
  const { card, resize, scroll } = setup(() => !dragging); resize(); expect(scroll).not.toHaveBeenCalled();
  expect(document.activeElement).toBe(card);
  dragging = false; resize(); expect(scroll).toHaveBeenCalledTimes(1);
});
it.each(['wheel', 'touchmove', 'pointerdown', 'keydown'])(
  'yields to manual %s until the Card is focused again', type => {
    const { card, other, resize, scroll } = setup(); scroll.mockClear();
    const event = type === 'keydown' ? new KeyboardEvent(type, { key: 'PageDown', bubbles: true }) : new Event(type, { bubbles: true });
    other.dispatchEvent(event); window.dispatchEvent(new Event('resize')); resize(); expect(scroll).not.toHaveBeenCalled();
    expect(disconnect).toHaveBeenCalled();
    other.focus(); card.focus(); scroll.mockClear(); resize(); expect(scroll).toHaveBeenCalledTimes(1);
  });
it('supports initial focus without ResizeObserver and removes all handlers on cleanup', () => {
  vi.stubGlobal('ResizeObserver', undefined);
  const { card, other, scroll } = setup(); expect(scroll).toHaveBeenCalledTimes(1);
  cleanup?.(); scroll.mockClear(); other.focus(); card.focus(); expect(scroll).not.toHaveBeenCalled();
});
it('cancels a queued viewport paint after manual scrolling and ignores an already delivered callback', () => {
  let frame: FrameRequestCallback | undefined;
  vi.stubGlobal('requestAnimationFrame', vi.fn((callback: FrameRequestCallback) => { frame = callback; return 7; }));
  const cancel = vi.fn(); vi.stubGlobal('cancelAnimationFrame', cancel);
  const { card, scroll } = setup(); scroll.mockClear();
  window.dispatchEvent(new Event('resize')); expect(scroll).toHaveBeenCalledTimes(1);
  document.dispatchEvent(new Event('wheel')); expect(cancel).toHaveBeenCalledWith(7);
  frame?.(0); expect(scroll).toHaveBeenCalledTimes(1); expect(document.activeElement).toBe(card);
});
