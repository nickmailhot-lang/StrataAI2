import { useEffect } from 'react';
import { Button, Dialog, DialogActions, DialogContent, DialogTitle } from '@mui/material';
import { useBlocker } from 'react-router-dom';

export function UnsavedChangesGuard({ dirty }: { dirty: boolean }) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname);
  useEffect(() => {
    if (!dirty) return;
    const protect = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', protect);
    return () => window.removeEventListener('beforeunload', protect);
  }, [dirty]);
  return <Dialog open={blocker.state === 'blocked'} aria-labelledby="unsaved-title" aria-describedby="unsaved-description"
    onClose={() => blocker.reset?.()}>
    <DialogTitle id="unsaved-title">Discard unsaved changes?</DialogTitle>
    <DialogContent id="unsaved-description">Your changes have not been saved. Keep editing to preserve them, or discard them to leave.</DialogContent>
    <DialogActions>
      <Button autoFocus onClick={() => blocker.reset?.()}>Keep editing</Button>
      <Button color="error" onClick={() => blocker.proceed?.()}>Discard changes and leave</Button>
    </DialogActions>
  </Dialog>;
}
