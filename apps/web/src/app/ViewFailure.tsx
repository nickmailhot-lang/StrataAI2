import { Alert, AlertTitle, Button, Container, Stack, Typography } from '@mui/material';
import type { ClientOnErrorFunction } from 'react-router-dom';
import type { RootOptions } from 'react-dom/client';
import { activityEvent } from '../features/kanban/activityTelemetry';

export const observeViewFailure: ClientOnErrorFunction = (_error, info) => {
  if (!info.errorInfo) return; // HTTP/loader errors already have their own observations.
  try {
    const match = /^\/app\/[^/]+\/boards\/[^/]+(?:\/(cards)\/[^/]+)?\/?$/.exec(info.location.pathname);
    activityEvent(match ? match[1] ? 'card_render' : 'board_render' : 'application_render', 'exception');
  } catch { /* Diagnostic failure must not replace the original error boundary. */ }
};

// React otherwise logs the caught Error before the router's callback runs.
// Keep development diagnostics; production never prints the private Error object.
export function viewFailureRootOptions(production = import.meta.env.PROD): RootOptions {
  return production ? { onCaughtError: () => {
    try { console.error('A view could not be rendered.'); } catch { /* Best-effort diagnostic only. */ }
  } } : {};
}

export function ViewFailure() {
  return <Container maxWidth="sm" sx={{ py: 4 }}><Stack spacing={2}>
    <Alert severity="error"><AlertTitle>This view is unavailable.</AlertTitle>
      A submitted change may still have completed. Check the current state before trying again.
    </Alert>
    <Typography>Reloading may discard unsaved changes.</Typography>
    <Button variant="contained" onClick={() => window.location.reload()}>Reload this page</Button>
  </Stack></Container>;
}
