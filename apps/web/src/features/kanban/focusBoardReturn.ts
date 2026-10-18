export function focusBoardReturn(target: HTMLElement | null | undefined) {
  if (!target) return;
  target.focus({ preventScroll: true });
  const original = target.getBoundingClientRect();
  if (!original.width || !original.height) return;

  // Native nearest also pans a partly visible horizontal context. Keep that
  // context; a wholly hidden or relocated Card still needs its native reveal.
  for (let parent = target.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
    const style = getComputedStyle(parent), bounds = parent.getBoundingClientRect();
    if (!bounds.width || !bounds.height) continue;
    const clippedX = /^(auto|scroll|hidden|clip)$/.test(style.overflowX);
    const clippedY = /^(auto|scroll|hidden|clip)$/.test(style.overflowY);
    if (clippedX && (original.right <= bounds.left || original.left >= bounds.right)
      || clippedY && (original.bottom <= bounds.top || original.top >= bounds.bottom)) {
      target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
      break;
    }
  }

  // Large text can leave a focused Card partly below or above a local viewport.
  // Reveal it on Y alone, remeasuring after each inner container has moved.
  for (let parent = target.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
    if (!/^(auto|scroll|hidden|clip)$/.test(getComputedStyle(parent).overflowY) || !parent.clientHeight) continue;
    const origin = parent.getBoundingClientRect().top + parent.clientTop;
    const edge = origin + parent.clientHeight, bounds = target.getBoundingClientRect();
    const delta = bounds.top < origin ? bounds.top - origin : bounds.bottom > edge
      ? Math.min(bounds.bottom - edge, bounds.top - origin) : 0;
    if (delta) parent.scrollTop += delta;
  }

  // A tall Board can leave the return target outside the document viewport.
  // Move only the document here, keeping canvas and List offsets intact.
  const bounds = target.getBoundingClientRect();
  const header = document.querySelector('header')?.getBoundingClientRect();
  const top = Math.max(0, Math.min(window.innerHeight, header?.bottom ?? 0));
  const delta = bounds.top < top ? bounds.top - top : bounds.bottom > window.innerHeight
    ? Math.min(bounds.bottom - window.innerHeight, bounds.top - top) : 0;
  if (delta) window.scrollBy({ top: delta, left: 0, behavior: 'instant' });
}
