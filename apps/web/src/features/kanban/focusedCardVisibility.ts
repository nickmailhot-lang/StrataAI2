import { revealBoardTarget } from './focusBoardReturn';

// A single Board observer follows only the focused Card. Metadata can change its
// height after focus; user scrolling and drag controllers retain ownership.
export function observeFocusedCardVisibility(
  links: ReadonlyMap<string, HTMLAnchorElement>,
  canReveal: () => boolean,
): () => void {
  let focused: HTMLAnchorElement | undefined;
  let observer: ResizeObserver | undefined;
  let resizeFrame: number | undefined;
  const retire = () => {
    if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
    resizeFrame = undefined;
    observer?.disconnect();
    observer = undefined;
    focused = undefined;
  };
  const reveal = () => {
    if (focused?.isConnected && document.activeElement === focused && canReveal()) {
      revealBoardTarget(focused);
    }
  };
  const followFocus = () => {
    retire();
    focused = [...links.values()].find(link => link === document.activeElement);
    if (!focused) return;
    if (typeof ResizeObserver !== 'undefined') {
      observer = new ResizeObserver(reveal);
      // Header wrapping and local viewport changes can move an unchanged Card.
      for (let node: HTMLElement | null = focused; node; node = node.parentElement) observer.observe(node);
      const header = document.querySelector('header');
      if (header) observer.observe(header);
    }
    reveal();
  };
  const pointer = (event: PointerEvent) => {
    if (!(event.target instanceof Node) || !focused?.contains(event.target)) retire();
  };
  const key = (event: KeyboardEvent) => {
    if (['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'PageUp', 'PageDown', 'Home', 'End', ' '].includes(event.key)) retire();
  };
  const viewportResize = () => {
    if (!focused) return;
    reveal();
    if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => { resizeFrame = undefined; reveal(); });
  };
  document.addEventListener('focusin', followFocus);
  document.addEventListener('focusout', retire);
  document.addEventListener('wheel', retire, { passive: true, capture: true });
  document.addEventListener('touchmove', retire, { passive: true, capture: true });
  document.addEventListener('pointerdown', pointer, true);
  document.addEventListener('keydown', key, true);
  window.addEventListener('resize', viewportResize);
  followFocus();
  return () => {
    retire();
    document.removeEventListener('focusin', followFocus);
    document.removeEventListener('focusout', retire);
    document.removeEventListener('wheel', retire, true);
    document.removeEventListener('touchmove', retire, true);
    document.removeEventListener('pointerdown', pointer, true);
    document.removeEventListener('keydown', key, true);
    window.removeEventListener('resize', viewportResize);
  };
}
