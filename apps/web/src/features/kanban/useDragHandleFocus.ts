import { useLayoutEffect, useRef } from 'react';

// A protected refresh can blur a disabled native button. Retain only its own
// focus, and resume after current admission; user navigation retires the request.
export function useDragHandleFocus(id: string, disabled: boolean, available: boolean) {
  const handle = useRef<HTMLButtonElement | null>(null);
  const ownsFocus = useRef(false), requested = useRef(false), identity = useRef(id);
  useLayoutEffect(() => {
    const retire = (event: Event) => {
      const target = event.target;
      if (target instanceof Node && !handle.current?.contains(target)) {
        ownsFocus.current = false; requested.current = false;
      }
    };
    document.addEventListener('focusin', retire);
    document.addEventListener('pointerdown', retire);
    return () => {
      document.removeEventListener('focusin', retire);
      document.removeEventListener('pointerdown', retire);
      ownsFocus.current = false; requested.current = false;
    };
  }, []);
  useLayoutEffect(() => {
    if (identity.current !== id || !available) {
      identity.current = id; ownsFocus.current = false; requested.current = false; return;
    }
    if (disabled) { requested.current ||= ownsFocus.current; return; }
    if (!requested.current) return;
    requested.current = false;
    const node = handle.current, active = document.activeElement;
    if (node && !node.disabled && (active === null || active === document.body || active === node))
      node.focus({ preventScroll: true });
  }, [id, disabled, available]);
  return { handle, onFocus: () => { ownsFocus.current = available && !disabled; } };
}
