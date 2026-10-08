import { cloneElement, forwardRef, useRef, type HTMLAttributes, type ReactElement, type Ref } from 'react';
import { useForkRef } from '@mui/material/utils';
import type { TransitionProps } from '@mui/material/transitions';
import { Transition } from 'react-transition-group';

type ChildProps = HTMLAttributes<HTMLElement> & { ref?: Ref<HTMLElement> };

// Keep the transition lifecycle used by MUI Modal, without Fade's synchronous
// layout read: this dialog deliberately has no visual enter/exit animation.
export const ImmediateDialogTransition = forwardRef<HTMLElement, TransitionProps & { ownerState?: unknown }>(function ImmediateDialogTransition({
  children, in: open, appear = true, enter, exit, onEnter, onEntering, onEntered,
  onExit, onExiting, onExited, style, timeout: _timeout, easing: _easing, addEndListener: _addEndListener,
  disablePrefersReducedMotion: _disableMotion, mountOnEnter: _mountOnEnter, unmountOnExit: _unmountOnExit,
  ownerState: _ownerState, ...childProps
}, forwardedRef) {
  const child = children as ReactElement<ChildProps>;
  const nodeRef = useRef<HTMLElement>(null);
  const ref = useForkRef(nodeRef, child.props.ref, forwardedRef);
  return <Transition<HTMLElement> nodeRef={nodeRef} in={open} appear={appear} enter={enter} exit={exit} timeout={0}
    onEnter={appearing => { if (nodeRef.current) onEnter?.(nodeRef.current, appearing); }}
    onEntering={appearing => { if (nodeRef.current) onEntering?.(nodeRef.current, appearing); }}
    onEntered={appearing => { if (nodeRef.current) onEntered?.(nodeRef.current, appearing); }}
    onExit={() => { if (nodeRef.current) onExit?.(nodeRef.current); }}
    onExiting={() => { if (nodeRef.current) onExiting?.(nodeRef.current); }}
    onExited={() => { if (nodeRef.current) onExited?.(nodeRef.current); }}>
    {state => cloneElement(child, { ...childProps, ref,
      style: { ...style, ...child.props.style, ...(state === 'exited' && !open ? { visibility: 'hidden' } : {}) } })}
  </Transition>;
});
