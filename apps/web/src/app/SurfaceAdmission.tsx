import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Alert, Box, Button, CircularProgress, Stack } from '@mui/material';
import { ThemeProvider, useTheme } from '@mui/material/styles';
import { Link, useParams } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../api/workManagement';
import { isNotificationProfile } from '../features/notifications/notificationInbox';
import { lifecycleUuid } from '../features/organizations/operationalLifecycle';
import { watchOperationalLifecycle } from '../features/organizations/operationalLifecycleLive';
import { forgetOrganizationRetentionPolicyChanges } from '../features/records/retentionPolicyRecovery';
import { forgetOrganizationRetentionSourceClassifications } from '../features/records/retentionSourceRecovery';

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
  const currentResult = useRef<Result | undefined>(undefined);
  currentResult.current = result;
  const [retry, setRetry] = useState(0);
  const generation = useRef(0);
  const refreshAdmission = useRef<(() => void) | undefined>(undefined);
  // Mount the boundary before its children so a Portal's first layout effect
  // can resolve this container instead of falling back to document.body.
  const [content, setContent] = useState<HTMLDivElement | null>(null);
  const container = useCallback(() => content, [content]);
  const admitted = result?.scope === scope && result.status === 'admitted';
  useEffect(() => {
    // The outer boundary can retire the policy page before its own denial
    // effect runs. Clear only this Organization after definitive internal denial;
    // transport uncertainty and independent Portal grants retain original work.
    if (surface === 'INTERNAL' && organizationId && result?.scope === scope && result.status === 'denied') {
      forgetOrganizationRetentionPolicyChanges(organizationId);
      forgetOrganizationRetentionSourceClassifications(organizationId);
    }
  }, [surface, organizationId, scope, result]);
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
      const currentGeneration = generation.current;
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
        if (!retired && currentGeneration === generation.current) setResult({ scope, status: 'admitted' });
      } catch (reason) {
        if (!retired && currentGeneration === generation.current) {
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
    refreshAdmission.current = refresh;
    refresh();
    const interval = setInterval(refresh, 10_000);
    window.addEventListener('focus', refresh);
    return () => { retired = true; clearInterval(interval); window.removeEventListener('focus', refresh);
      if (refreshAdmission.current === refresh) refreshAdmission.current = undefined;
      pending?.abort(); };
  }, [organizationId, surface, scope, retry]);
  useEffect(() => {
    // Portal grants are independent; an internal-member stream cannot admit it.
    if (surface !== 'INTERNAL' || !lifecycleUuid(organizationId)) return;
    let retired = false; let stop: (() => void) | undefined;
    const controller = new AbortController();
    const withdraw = (status: 'denied' | 'error') => {
      if (retired) return;
      // Stream availability does not decide initial navigation admission.
      // Let the bounded HTTP check finish, including a denial that mounts an
      // independently authorized PUBLIC Board view. Withdraw an existing
      // grant immediately when its live transport becomes uncertain.
      const previous = currentResult.current;
      if (status === 'error' && (previous?.scope !== scope
        || previous.status !== 'admitted' && !previous.previouslyAdmitted)) return;
      // Transport uncertainty withdraws presentation, but is not a newer
      // authority decision. An already pending bounded HTTP check must still
      // resolve, especially a denial that admits independent lifecycle recovery.
      if (status === 'denied') ++generation.current;
      setResult(previous => ({ scope,
        // A stream transport failure cannot turn a definitive surface denial
        // into an unknown grant. Keep the shell withdrawn while the independent
        // denied route performs its own lifecycle/Board authorization.
        status: status === 'error' && previous?.scope === scope && previous.status === 'denied' ? 'denied' : status,
        previouslyAdmitted: status === 'error' && previous?.scope === scope
          && (previous.status === 'admitted' || previous.previouslyAdmitted === true) }));
      if (status === 'error' && previous?.scope === scope && previous.status === 'admitted')
        refreshAdmission.current?.();
    };
    void boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal).then(profile => {
      if (retired || !isNotificationProfile(profile)) return;
      stop = watchOperationalLifecycle({ organizationId, actorId: profile.id,
        admit: async (page, signal) => {
          if (page.status !== 'ACTIVE') return;
          const admission = await workRequest<{ organizationId: string; surface: string }>(
            `/organizations/${encodeURIComponent(organizationId)}/surface-access?surface=INTERNAL`, { signal,
              headers: { 'X-StrataAI-Expected-Actor': profile.id } });
          if (admission?.organizationId !== organizationId || admission.surface !== 'INTERNAL') throw new Error('Invalid admission');
        },
        update: page => {
          if (retired) return;
          ++generation.current;
          setResult({ scope, status: page.status === 'ACTIVE' ? 'admitted' : 'denied' });
        },
        unavailable: () => withdraw('error'), denied: () => withdraw('denied'),
      });
    }).catch(error => {
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) withdraw('denied');
      // Existing bounded polling remains the fallback if stream bootstrap fails.
    });
    return () => { retired = true; controller.abort(); stop?.(); };
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
