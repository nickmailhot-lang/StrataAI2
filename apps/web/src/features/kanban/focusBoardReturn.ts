export function focusBoardReturn(target: HTMLElement | null | undefined) {
  target?.focus({ preventScroll: true });
  target?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
}
