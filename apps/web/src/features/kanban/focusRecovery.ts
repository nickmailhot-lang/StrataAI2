// MUI can return focus to its marked Dialog paper or outer trap container when
// a focused control is disabled/removed. Treat only that same dialog's fallback as lost owned
// focus; another control or another dialog remains the user's destination.
export function ownsRecoveryFocus(target: EventTarget | null, owner: HTMLElement | null): boolean {
  if (!owner) return false;
  return target === null || target === document.body || target === owner
    || target === owner.closest('[role="dialog"][data-mui-focusable]')
    || target === owner.closest('.MuiDialog-container[role="presentation"]');
}

// Move an activated control's focus to its own dialog before disabling or
// removing it. The dialog trap then retains an owned fallback, instead of
// choosing a different control when the browser blurs a disabled button.
export function parkRecoveryFocus(owner: HTMLElement | null): void {
  if (!owner || document.activeElement !== owner) return;
  owner.closest<HTMLElement>('[role="dialog"][data-mui-focusable]')?.focus({ preventScroll: true });
}
