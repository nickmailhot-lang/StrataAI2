import { useEffect, useRef, useState } from 'react';
import { Alert, Button } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../api/workManagement';
import { notificationUuid as uuid } from '../features/notifications/notificationInbox';
import { ChangedNavigationActor, NavigationAcknowledgments, type NavigationTarget } from './navigationInteraction';
import { createNavigationIntent, submitNavigationIntent, validateNavigationTarget, type NavigationIntent } from './navigationObservation';
import { completeNavigationIntent, restoreNavigationIntent, retainNavigationIntent } from './navigationRecovery';
import { activityEvent, activityResult, type ActivityAction } from '../features/kanban/activityTelemetry';

// Mount once for an admitted navigation visit. Later entity edits do not create
// another open event or replace an unresolved original revision.
export function NavigationConfirmation({ target, admitted = true }: { target: NavigationTarget; admitted?: boolean }) {
  const [originalTarget] = useState(() => ({ ...target }));
  const intent = useRef<NavigationIntent | undefined>(undefined);
  const opened = useRef(false);
  const [consumer] = useState(() => new NavigationAcknowledgments());
  const [status, setStatus] = useState<'pending' | 'done' | 'retry' | 'unavailable'>('pending');
  const [retry, setRetry] = useState(0);
  const action: ActivityAction = originalTarget.kind === 'context' ? 'navigation_context'
    : originalTarget.kind === 'board' ? 'navigation_board' : 'navigation_card';
  useEffect(() => {
    if (!admitted) return;
    const controller = new AbortController();
    async function observe() {
      // Invalid route/snapshot identities cannot authorize an account read or
      // a personal observation, including malformed cached client records.
      try { validateNavigationTarget(originalTarget); }
      catch { setStatus('unavailable'); return; }
      if (!opened.current) { activityEvent(action, 'open'); opened.current = true; }
      const started = performance.now(); activityEvent(action, 'use');
      try {
        if (!intent.current) {
          const profile = await boundedWorkRead(signal => workRequest<{ id: unknown }>('/me', { signal }), controller.signal);
          if (!uuid(profile?.id)) throw new ChangedNavigationActor();
          const original = restoreNavigationIntent(sessionStorage, profile.id, originalTarget)
            ?? createNavigationIntent(profile.id, originalTarget);
          retainNavigationIntent(sessionStorage, original);
          intent.current = original;
        }
        await submitNavigationIntent(intent.current, controller.signal, consumer);
        completeNavigationIntent(sessionStorage, intent.current);
        if (!controller.signal.aborted) { activityResult(action, true, started); setStatus('done'); }
      } catch (reason) {
        if (controller.signal.aborted) return;
        const denied = reason instanceof ChangedNavigationActor || reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status);
        activityResult(action, false, started);
        if (!denied) activityEvent(action, 'exception');
        setStatus(denied ? 'unavailable' : 'retry');
      }
    }
    void observe();
    return () => controller.abort();
  }, [admitted, originalTarget, consumer, retry, action]);
  if (!admitted || status !== 'retry') return null;
  return <Alert severity="info" action={<Button color="inherit" onClick={() => { activityEvent(action, 'retry'); setStatus('pending'); setRetry(value => value + 1); }}>Retry navigation confirmation</Button>}>
    Navigation could not be confirmed. You can retry.
  </Alert>;
}
