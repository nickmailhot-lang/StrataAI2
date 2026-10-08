import { Alert, AlertTitle, Button, Container, Stack, Typography } from '@mui/material';
import type { ClientOnErrorFunction } from 'react-router-dom';
import type { RootOptions } from 'react-dom/client';
import { activityEvent } from '../features/kanban/activityTelemetry';
import { renderRootFailure } from './rootFailure';

export const observeViewFailure: ClientOnErrorFunction = (_error, info) => {
  if (!info.errorInfo) return; // HTTP/loader errors already have their own observations.
  try {
    const match = /^\/app\/[^/]+\/boards\/[^/]+(?:\/(cards)\/[^/]+)?\/?$/.exec(info.location.pathname);
    activityEvent(match ? match[1] ? 'card_render' : 'board_render' : 'application_render', 'exception');
  } catch { /* Diagnostic failure must not replace the original error boundary. */ }
};

// React otherwise logs the caught Error before the router's callback runs.
// Keep development diagnostics; production never prints the private Error object.
export function viewFailureRootOptions(production = import.meta.env.PROD, rootElement?: HTMLElement): RootOptions {
  const diagnostic = () => {
    try { console.error('A view could not be rendered.'); } catch { /* Best-effort diagnostic only. */ }
  };
  return production ? {
    onCaughtError: diagnostic, // Router observes caught renders; avoid double counting.
    onUncaughtError: () => {
      diagnostic();
      try { activityEvent('application_root_exception', 'exception'); } catch { /* Best effort only. */ }
      if (rootElement) {
        try { renderRootFailure(rootElement); } catch { /* A failed recovery must not leak the original diagnostic. */ }
      }
    },
    onRecoverableError: () => {
      diagnostic();
      try { activityEvent('application_recovery_exception', 'exception'); } catch { /* Best effort only. */ }
    },
  } : {};
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
