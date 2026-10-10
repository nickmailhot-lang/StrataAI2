import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, Box, Button, CircularProgress, Stack } from '@mui/material';
import { ThemeProvider, useTheme } from '@mui/material/styles';
import { Link, useParams } from 'react-router-dom';
import { workRequest, WorkRequestError } from '../api/workManagement';

type Result = { scope: string; status: 'admitted' | 'denied' | 'error'; previouslyAdmitted?: boolean };

// ARCH-02-AC-003: this is a current navigation admission check. Each child API
// still independently authorizes its data and commands.
export function SurfaceAdmission({ surface, children, deniedContent }: {
  surface: 'INTERNAL' | 'PORTAL'; children: ReactNode; deniedContent?: ReactNode;
}) {
  const { organizationId: routeId } = useParams();
  const organizationId = routeId?.toLowerCase();
  const scope = `${surface}:${organizationId ?? ''}`;
  const [result, setResult] = useState<Result>();
  const [retry, setRetry] = useState(0);
  // Mount the boundary before its children so a Portal's first layout effect
  // can resolve this container instead of falling back to document.body.
  const [content, setContent] = useState<HTMLDivElement | null>(null);
  const container = useCallback(() => content, [content]);
  const admitted = result?.scope === scope && result.status === 'admitted';
  const theme = useTheme();
  const surfaceTheme = useMemo(() => ({ ...theme, components: {
    ...theme.components,
    MuiModal: { ...theme.components?.MuiModal, defaultProps: { ...theme.components?.MuiModal?.defaultProps, container,
      disableAutoFocus: admitted ? theme.components?.MuiModal?.defaultProps?.disableAutoFocus : true,
      disableEnforceFocus: admitted ? theme.components?.MuiModal?.defaultProps?.disableEnforceFocus : true } },
    MuiPopover: { ...theme.components?.MuiPopover, defaultProps: { ...theme.components?.MuiPopover?.defaultProps, container } },
    MuiPopper: { ...theme.components?.MuiPopper, defaultProps: { ...theme.components?.MuiPopper?.defaultProps, container } },
  } }), [theme, admitted, container]);
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
        if (!retired) {
          const status = reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status) ? 'denied' : 'error';
          setResult(previous => ({ scope, status, previouslyAdmitted: status === 'error' && previous?.scope === scope
            && (previous.status === 'admitted' || previous.previouslyAdmitted === true) }));
        }
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
  if (result.status === 'denied' && deniedContent) return deniedContent;
  // A failed transport check withdraws the surface without destroying the
  // child's original retry intent and live cursor. Fresh denial still unmounts
  // all protected content; another scope never inherits this recovery state.
  return <>
    {(admitted || result.status === 'error' && result.previouslyAdmitted) &&
      <Box ref={setContent} hidden={!admitted} sx={{ display: admitted ? 'contents' : 'none' }}>
        {content && <ThemeProvider theme={surfaceTheme}>{children}</ThemeProvider>}
      </Box>}
    {!admitted && <Stack spacing={2} sx={{ p: 3 }}>
    <Alert severity={result.status === 'denied' ? 'info' : 'error'}>
      {result.status === 'denied' ? 'Access to this Organization surface is unavailable.' : 'Access could not be checked. Try again.'}
    </Alert>
    <Button onClick={() => {
      setResult(previous => previous?.status === 'error' && previous.previouslyAdmitted ? previous : undefined);
      setRetry(value => value + 1);
    }}>Check access again</Button>
    <Button component={Link} to="/app">Open organizations</Button>
    <Button component={Link} to="/login">Sign in</Button>
    </Stack>}
  </>;
}
