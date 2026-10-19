import { useEffect, useRef } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle } from '@mui/material';
import { useBlocker } from 'react-router-dom';

export function UnsavedChangesGuard({ dirty }: { dirty: boolean }) {
  const keepEditing = useRef<HTMLButtonElement>(null);
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (!dirty && blocker.state === 'blocked') blocker.proceed();
  }, [dirty, blocker]);
  useEffect(() => {
    if (!dirty) return;
    const protect = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', protect);
    return () => window.removeEventListener('beforeunload', protect);
  }, [dirty]);
  // Only generic choices leave the admission container. Private draft content
  // stays withdrawn while the user can still cancel or leave a hidden surface.
  return <Dialog container={() => document.body} disableAutoFocus={false} disableEnforceFocus={false}
    slotProps={{ container: { onFocusCapture: event => {
      if (event.target === event.currentTarget) keepEditing.current?.focus();
    } } }}
    open={dirty && blocker.state === 'blocked'} aria-labelledby="unsaved-title" aria-describedby="unsaved-description"
    onClose={() => blocker.reset?.()}>
    <DialogTitle id="unsaved-title">Discard unsaved changes?</DialogTitle>
    <DialogContent id="unsaved-description">Your changes have not been saved. Keep editing to preserve them, or discard them to leave.</DialogContent>
    <DialogActions>
      <Button ref={keepEditing} autoFocus onClick={() => blocker.reset?.()}>Keep editing</Button>
      <Button color="error" onClick={() => blocker.proceed?.()}>Discard changes and leave</Button>
    </DialogActions>
  </Dialog>;
}
