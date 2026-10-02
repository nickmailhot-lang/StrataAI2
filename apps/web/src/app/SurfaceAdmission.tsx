import { useEffect, useState, type ReactNode } from 'react';
import { Alert, Button, CircularProgress, Stack } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { workRequest, WorkRequestError } from '../api/workManagement';

type Result = { scope: string; status: 'admitted' | 'denied' | 'error' };

// ARCH-02-AC-003: this is a current navigation admission check. Each child API
// still independently authorizes its data and commands.
export function SurfaceAdmission({ surface, children, deniedContent }: {
  surface: 'INTERNAL' | 'PORTAL'; children: ReactNode; deniedContent?: ReactNode;
}) {
  const { organizationId } = useParams();
  const scope = `${surface}:${organizationId ?? ''}`;
  const [result, setResult] = useState<Result>();
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let retired = false;
    let pending: AbortController | undefined;
    const read = async () => {
      if (retired || pending) return;
      if (!organizationId) { setResult({ scope, status: 'denied' }); return; }
      const controller = new AbortController(); pending = controller;
      let deadline: ReturnType<typeof setTimeout> | undefined;
      let abort: (() => void) | undefined;
      try {
        const admission = await Promise.race([
          workRequest<{ organizationId: string; surface: string }>(
            `/organizations/${encodeURIComponent(organizationId)}/surface-access?surface=${surface}`, { signal: controller.signal }),
          new Promise<never>((_, reject) => {
            abort = () => reject(new Error('Admission interrupted'));
            controller.signal.addEventListener('abort', abort, { once: true });
            deadline = setTimeout(() => controller.abort(), 5000);
          }),
        ]);
        if (admission?.organizationId !== organizationId || admission.surface !== surface)
          throw new Error('Invalid admission');
        if (!retired) setResult({ scope, status: 'admitted' });
      } catch (reason) {
        if (!retired) setResult({ scope, status: reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status) ? 'denied' : 'error' });
      } finally {
        clearTimeout(deadline);
        if (abort) controller.signal.removeEventListener('abort', abort);
        if (pending === controller) pending = undefined;
      }
    };
    const refresh = () => { void read(); };
    refresh();
    const interval = setInterval(refresh, 10_000);
    window.addEventListener('focus', refresh);
    return () => { retired = true; clearInterval(interval); window.removeEventListener('focus', refresh); pending?.abort(); };
  }, [organizationId, surface, scope, retry]);
  if (!result || result.scope !== scope) return <CircularProgress aria-label="Checking Organization access" />;
  if (result.status === 'admitted') return children;
  if (result.status === 'denied' && deniedContent) return deniedContent;
  return <Stack spacing={2} sx={{ p: 3 }}>
    <Alert severity={result.status === 'denied' ? 'info' : 'error'}>
      {result.status === 'denied' ? 'Access to this Organization surface is unavailable.' : 'Access could not be checked. Try again.'}
    </Alert>
    <Button onClick={() => { setResult(undefined); setRetry(value => value + 1); }}>Check access again</Button>
    <Button component={Link} to="/app">Open organizations</Button>
    <Button component={Link} to="/login">Sign in</Button>
  </Stack>;
}
