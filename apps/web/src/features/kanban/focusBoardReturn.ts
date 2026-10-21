export function focusBoardReturn(target: HTMLElement | null | undefined) {
  if (!target) return;
  target.focus({ preventScroll: true });
  revealBoardTarget(target);
}

// Share the same local/document reveal policy with metadata resizes, which must
// never move focus or undo the canvas context preserved by Card return.
export function revealBoardTarget(target: HTMLElement) {
  const original = target.getBoundingClientRect();
  if (!original.width || !original.height) return;
  const targetStyle = getComputedStyle(target);
  const startClearance = Math.max(0, Number.parseFloat(targetStyle.scrollMarginTop) || 0);
  const endClearance = Math.max(0, Number.parseFloat(targetStyle.scrollMarginBottom) || 0);

  // Native nearest also pans a partly visible horizontal context. Keep that
  // context; a wholly hidden horizontal target still needs its native reveal.
  // Vertical hiding is handled independently below so it cannot pan that X.
  for (let parent = target.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
    const style = getComputedStyle(parent), bounds = parent.getBoundingClientRect();
    if (!bounds.width || !bounds.height) continue;
    const clippedX = /^(auto|scroll|hidden|clip)$/.test(style.overflowX);
    if (clippedX && (original.right <= bounds.left || original.left >= bounds.right)) {
      target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
      break;
    }
  }

  // Large text can leave a focused Card partly below or above a local viewport.
  // Reveal it on Y alone, remeasuring after each inner container has moved.
  for (let parent = target.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
    if (!/^(auto|scroll|hidden|clip)$/.test(getComputedStyle(parent).overflowY) || !parent.clientHeight) continue;
    const viewportTop = parent.getBoundingClientRect().top + parent.clientTop;
    const origin = viewportTop + startClearance;
    const edge = Math.max(origin, viewportTop + parent.clientHeight - endClearance), bounds = target.getBoundingClientRect();
    const delta = bounds.top < origin ? bounds.top - origin : bounds.bottom > edge
      ? Math.min(bounds.bottom - edge, bounds.top - origin) : 0;
    if (delta) parent.scrollTop += delta;
  }

  // A tall Board can leave the return target outside the document viewport.
  // Move only the document here, keeping canvas and List offsets intact.
  const bounds = target.getBoundingClientRect();
  const header = document.querySelector('header')?.getBoundingClientRect();
  const top = Math.max(0, Math.min(window.innerHeight, (header?.bottom ?? 0) + startClearance));
  const bottom = Math.max(top, window.innerHeight - endClearance);
  const delta = bounds.top < top ? bounds.top - top : bounds.bottom > bottom
    ? Math.min(bounds.bottom - bottom, bounds.top - top) : 0;
  if (delta) window.scrollBy({ top: delta, left: 0, behavior: 'instant' });
}
