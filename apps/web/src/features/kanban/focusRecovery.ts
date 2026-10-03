// MUI returns focus to its marked Dialog paper when a focused control is
// disabled/removed. Treat only that same dialog's fallback as lost owned
// focus; another control or another dialog remains the user's destination.
export function ownsRecoveryFocus(target: EventTarget | null, owner: HTMLElement | null): boolean {
  if (!owner) return false;
  return target === null || target === document.body || target === owner
    || target === owner.closest('[role="dialog"][data-mui-focusable]');
}
