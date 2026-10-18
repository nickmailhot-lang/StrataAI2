export function focusBoardReturn(target: HTMLElement | null | undefined) {
  if (!target) return;
  target.focus({ preventScroll: true });
  const original = target.getBoundingClientRect();
  if (!original.width || !original.height) return;

  // Native nearest scrolling also pans a partly clipped Card by a few pixels.
  // Preserve that visible local context; reveal locally only when the target
  // has become wholly hidden, for example after moving to another List.
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

  // A tall Board can leave the return target outside the document viewport.
  // Move only the document here, keeping canvas and List offsets intact.
  const bounds = target.getBoundingClientRect();
  const header = document.querySelector('header')?.getBoundingClientRect();
  const top = Math.max(0, Math.min(window.innerHeight, header?.bottom ?? 0));
  const delta = bounds.top < top ? bounds.top - top : bounds.bottom > window.innerHeight
    ? Math.min(bounds.bottom - window.innerHeight, bounds.top - top) : 0;
  if (delta) window.scrollBy({ top: delta, left: 0, behavior: 'instant' });
}
