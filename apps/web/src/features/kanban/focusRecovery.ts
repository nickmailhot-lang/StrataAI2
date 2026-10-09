// MUI can return focus to its Dialog paper, trap container or sentinel when
// a focused control is disabled/removed. Treat only that same dialog's fallback as lost owned
// focus; another control or another dialog remains the user's destination.
export function ownsRecoveryFocus(target: EventTarget | null, owner: HTMLElement | null, capturedDialog: HTMLElement | null = null): boolean {
  if (!owner) return false;
  // Access refresh can remove the owner while its dialog remains mounted.
  // A connected owner always identifies its own current dialog directly.
  const dialog = owner.closest<HTMLElement>('[role="dialog"][data-mui-focusable]')
    ?? (!owner.isConnected && capturedDialog?.isConnected ? capturedDialog : null);
  const root = dialog?.closest('.MuiDialog-root');
  const ownSentinel = root != null && target instanceof HTMLElement
    && target.matches('div[data-testid="sentinelStart"], div[data-testid="sentinelEnd"]')
    && target.closest('.MuiDialog-root') === root;
  return target === null || target === document.body || target === owner
    || ownSentinel || target === dialog
    || target === dialog?.closest('.MuiDialog-container[role="presentation"]');
}

// Move an activated control's focus to its own dialog before disabling or
// removing it. The dialog trap then retains an owned fallback, instead of
// choosing a different control when the browser blurs a disabled button.
export function parkRecoveryFocus(owner: HTMLElement | null): void {
  if (!owner || document.activeElement !== owner) return;
  owner.closest<HTMLElement>('[role="dialog"][data-mui-focusable]')?.focus({ preventScroll: true });
}
