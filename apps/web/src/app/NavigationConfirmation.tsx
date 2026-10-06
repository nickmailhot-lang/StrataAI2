import { useEffect, useRef, useState } from 'react';
import { Alert, Button } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../api/workManagement';
import { notificationUuid as uuid } from '../features/notifications/notificationInbox';
import { ChangedNavigationActor, NavigationAcknowledgments, type NavigationTarget } from './navigationInteraction';
import { createNavigationIntent, submitNavigationIntent, validateNavigationTarget, type NavigationIntent } from './navigationObservation';

// Mount once for an admitted navigation visit. Later entity edits do not create
// another open event or replace an unresolved original revision.
export function NavigationConfirmation({ target, admitted = true }: { target: NavigationTarget; admitted?: boolean }) {
  const [originalTarget] = useState(() => ({ ...target }));
  const intent = useRef<NavigationIntent | undefined>(undefined);
  const [consumer] = useState(() => new NavigationAcknowledgments());
  const [status, setStatus] = useState<'pending' | 'done' | 'retry' | 'unavailable'>('pending');
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!admitted) return;
    const controller = new AbortController();
    async function observe() {
      // Invalid route/snapshot identities cannot authorize an account read or
      // a personal observation, including malformed cached client records.
      try { validateNavigationTarget(originalTarget); }
      catch { setStatus('unavailable'); return; }
      try {
        if (!intent.current) {
          const profile = await boundedWorkRead(signal => workRequest<{ id: unknown }>('/me', { signal }), controller.signal);
          if (!uuid(profile?.id)) throw new ChangedNavigationActor();
          intent.current = createNavigationIntent(profile.id, originalTarget);
        }
        await submitNavigationIntent(intent.current, controller.signal, consumer);
        if (!controller.signal.aborted) setStatus('done');
      } catch (reason) {
        if (controller.signal.aborted) return;
        const denied = reason instanceof ChangedNavigationActor || reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status);
        setStatus(denied ? 'unavailable' : 'retry');
      }
    }
    void observe();
    return () => controller.abort();
  }, [admitted, originalTarget, consumer, retry]);
  if (!admitted || status !== 'retry') return null;
  return <Alert severity="info" action={<Button color="inherit" onClick={() => { setStatus('pending'); setRetry(value => value + 1); }}>Retry navigation confirmation</Button>}>
    Navigation could not be confirmed. You can retry.
  </Alert>;
}
