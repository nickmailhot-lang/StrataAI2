import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { activityEvent, activityResult } from '../kanban/activityTelemetry';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { watchOperationalLifecycle } from './operationalLifecycleLive';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { clearLifecycleIntent, lifecycleLabel, lifecycleUuid, parseLifecycleAcknowledgment, parseLifecycleReview, readLifecycleIntent, saveLifecycleIntent } from './operationalLifecycle';
import type { LifecycleAcknowledgment, LifecycleIntent, LifecycleReview, OperationalState } from './operationalLifecycle';

export function OrganizationOperationalLifecyclePage() {
  const { organizationId } = useParams(); const canonical = (organizationId ?? '').toLowerCase();
  return <Lifecycle key={canonical} organizationId={canonical} />;
}
function Lifecycle({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate(); const [review, setReview] = useState<LifecycleReview>();
  const [target, setTarget] = useState<OperationalState>('SUSPENDED'); const [open, setOpen] = useState(false);
  const [confirmation, setConfirmation] = useState<{ review: LifecycleReview; target: OperationalState }>();
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const [intent, setIntent] = useState<LifecycleIntent>(); const original = useRef<LifecycleIntent | undefined>(undefined);
  const [ack, setAck] = useState<LifecycleAcknowledgment>(); const [denied, setDenied] = useState(false);
  const [liveStatus, setLiveStatus] = useState<string>(); const [liveUnavailable, setLiveUnavailable] = useState(false);
  const latestReview = useRef<LifecycleReview | undefined>(undefined); latestReview.current = review;
  const liveActor = review?.actorId; const opened = useRef(false);
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined);
  const cancel = useRef<HTMLButtonElement>(null); const launcher = useRef<HTMLButtonElement>(null);
  const retry = useRef<HTMLButtonElement>(null); const reload = useRef<HTMLButtonElement>(null); const notice = useRef<HTMLDivElement>(null);
  const focus = useRef<'retry' | 'reload' | 'notice' | undefined>(undefined);
  function focusResult() {
    const element = focus.current === 'retry' ? retry.current : focus.current === 'reload' ? reload.current : focus.current === 'notice' ? notice.current : undefined;
    if (element && !element.matches(':disabled')) { element.focus(); focus.current = undefined; }
  }
  const root = `/organizations/${encodeURIComponent(organizationId)}/operational-lifecycle`;
  useEffect(() => { mounted.current = true; original.current = readLifecycleIntent(organizationId); void load();
    return () => { mounted.current = false; pending.current?.abort(); }; }, []);
  useEffect(() => {
    if (!liveActor) return;
    let fresh: LifecycleReview | undefined;
    return watchOperationalLifecycle({ organizationId, actorId: liveActor,
      admit: async (page, signal) => {
        fresh = undefined;
        const body = await workRequest<unknown>(root, { signal, headers: { 'X-StrataAI-Expected-Actor': liveActor } });
        const value = parseLifecycleReview(body, organizationId, liveActor);
        if (!value || value.version < page.version || value.version === page.version && value.status !== page.status)
          throw new WorkRequestError(409, null);
        fresh = value;
      },
      update: () => {
        if (!mounted.current || !fresh || pending.current || original.current) return;
        const previous = latestReview.current;
        setLiveUnavailable(false);
        if (!previous || previous.version !== fresh.version || previous.status !== fresh.status || previous.name !== fresh.name) {
          setOpen(false); setConfirmation(undefined); setReview(fresh);
          setTarget(fresh.status === 'ACTIVE' ? 'SUSPENDED' : 'ACTIVE');
          setLiveStatus('Organization lifecycle changed. Current permission and revision have been checked. Review a new change before confirming.');
        } else setLiveStatus(undefined);
      },
      reconnected: () => { if (mounted.current) activityEvent('organization_lifecycle_read', 'reconnect'); },
      unavailable: () => {
        if (!mounted.current) return;
        activityEvent('organization_lifecycle_read', 'exception'); setOpen(false); setLiveUnavailable(true);
        setLiveStatus('Live updates are unavailable. Load the current lifecycle before reviewing another change.');
      },
      denied: (status, switched) => { if (mounted.current) unavailable(status, switched); },
    });
  }, [organizationId, liveActor]);
  useEffect(() => {
    if (busy || !focus.current) return;
    if (document.activeElement === document.body || !document.activeElement?.isConnected) focusResult();
  }, [busy, intent, error, ack]);
  function begin() {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller;
  }
  function current(controller: AbortController) { return mounted.current && pending.current === controller && !controller.signal.aborted; }
  function finish(controller: AbortController) { if (current(controller)) { pending.current = undefined; setBusy(false); } }
  async function request(path: string, options: RequestInit, controller: AbortController) {
    return boundedWorkRead(async signal => {
      const response = await apiFetch(path, { ...options, signal });
      const body: unknown = response.status === 401 ? undefined : await response.json().catch(() => undefined);
      return { status: response.status, body };
    }, controller.signal);
  }
  function unavailable(status: number, switched = false) {
    if (switched || status === 403 || status === 404) { clearLifecycleIntent(organizationId); original.current = undefined; }
    setReview(undefined); setIntent(undefined); setAck(undefined); setOpen(false); setDenied(true);
    setError('Organization lifecycle is unavailable to this account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function account(controller: AbortController, expected?: string): Promise<string | undefined> {
    const response = await request('/me', {}, controller); if (!current(controller)) return;
    if ([401, 403, 404].includes(response.status)) { unavailable(response.status); return; }
    if (response.status !== 200 || !isNotificationProfile(response.body)) throw new Error('Account unconfirmed');
    if (expected && expected !== response.body.id) { unavailable(401, true); return; }
    return response.body.id;
  }
  async function admitted(controller: AbortController, expected?: string): Promise<LifecycleReview | undefined> {
    const actor = await account(controller, expected); if (!actor || !current(controller)) return;
    const response = await request(root, { headers: { 'X-StrataAI-Expected-Actor': actor } }, controller);
    if (!current(controller)) return;
    if ([401, 403, 404].includes(response.status)) { unavailable(response.status); return; }
    const value = parseLifecycleReview(response.body, organizationId, actor);
    if (response.status !== 200 || !value) throw new Error('Review unconfirmed');
    if (!await account(controller, actor) || !current(controller)) return;
    return value;
  }
  async function load(retryRead = false) {
    if (!lifecycleUuid(organizationId)) { setDenied(true); setError('Organization lifecycle is unavailable to this account.'); return; }
    const controller = begin(); if (!controller) return; setOpen(false); setReview(undefined);
    const started = performance.now(); activityEvent('organization_lifecycle_read', retryRead ? 'retry' : 'use');
    try {
      const value = await admitted(controller, original.current?.actorId); if (!current(controller)) return;
      activityResult('organization_lifecycle_read', !!value, started); if (!value) return;
      if (!opened.current) { opened.current = true; activityEvent('organization_lifecycle_disclosure', 'open'); }
      setDenied(false); setLiveUnavailable(false); setLiveStatus(undefined);
      if (original.current) { setIntent(original.current); focus.current = 'retry'; setError('The original lifecycle change is unconfirmed. Recover the same request before starting another change.'); }
      else { setReview(value); setTarget(value.status === 'ACTIVE' ? 'SUSPENDED' : 'ACTIVE'); }
    } catch (reason) { if (current(controller)) { activityResult('organization_lifecycle_read', false, started);
      if (!(reason instanceof WorkRequestError)) activityEvent('organization_lifecycle_read', 'exception');
      setError('Unable to review current lifecycle permission. Try loading it again.'); focus.current = 'reload'; } }
    finally { finish(controller); }
  }
  async function change(recover = false) {
    if (recover ? !original.current : !open || !confirmation || !!original.current || confirmation.target === confirmation.review.status) return;
    const controller = begin(); if (!controller) return;
    let submitted = false; let recorded = false;
    const started = performance.now(); activityEvent('organization_lifecycle_change', recover ? 'retry' : 'use');
    const result = (success: boolean) => { if (!recorded && current(controller)) { recorded = true; activityResult('organization_lifecycle_change', success, started); } };
    const captured = original.current ?? { organizationId, actorId: confirmation!.review.actorId, key: crypto.randomUUID(),
      status: confirmation!.target, previousStatus: confirmation!.review.status, version: confirmation!.review.version, actingCapacity: confirmation!.review.actingCapacity };
    try {
      const value = await admitted(controller, captured.actorId); if (!current(controller)) return; if (!value) { result(false); return; }
      if (!recover && (value.version !== captured.version || value.status !== captured.previousStatus || value.actingCapacity !== captured.actingCapacity
        || value.name !== confirmation?.review.name)) {
        result(false); activityEvent('organization_lifecycle_change', 'conflict');
        setOpen(false); setReview(undefined); setError('Organization changed elsewhere. Load the current revision and review a new change.'); focus.current = 'reload'; return;
      }
      if (!saveLifecycleIntent(captured)) { result(false); activityEvent('organization_lifecycle_change', 'exception'); setOpen(false); setError('Unable to retain this request for recovery. Enable browser session storage and review again.'); return; }
      original.current = captured; setIntent(captured); setOpen(false); setReview(undefined); setAck(undefined);
      submitted = true;
      const response = await request(root, { method: 'PATCH', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': captured.key,
        'X-StrataAI-Expected-Actor': captured.actorId }, body: JSON.stringify({ version: captured.version, status: captured.status }) }, controller);
      if (!current(controller)) return;
      if ([401, 403, 404].includes(response.status)) { result(false); unavailable(response.status); return; }
      if (response.status === 400 || response.status === 409) {
        result(false); if (response.status === 409) activityEvent('organization_lifecycle_change', 'conflict');
        clearLifecycleIntent(organizationId); original.current = undefined; setIntent(undefined);
        setError('The lifecycle change was refused. Load the current revision and review a new change.'); focus.current = 'reload'; return;
      }
      const source = parseLifecycleAcknowledgment(response.body, captured);
      if (response.status !== 200 || !source) throw new Error('Acknowledgment unconfirmed');
      if (!await account(controller, captured.actorId) || !current(controller)) { result(false); return; }
      result(true);
      // Verify current administrative authority after acknowledgment IO. A later
      // revision is allowed; recovering a receipt never represents current state.
      if (!await admitted(controller, captured.actorId) || !current(controller)) return;
      clearLifecycleIntent(organizationId); original.current = undefined; setIntent(undefined); setAck(source); focus.current = 'notice';
    } catch (reason) {
      if (current(controller)) {
        const acknowledged = recorded; result(false);
        if (!(reason instanceof WorkRequestError)) activityEvent(acknowledged ? 'organization_lifecycle_read' : 'organization_lifecycle_change', 'exception');
        setOpen(false); setReview(undefined);
        if (submitted || original.current) { setIntent(original.current); setError('The lifecycle change is unconfirmed. Retry the original request to recover its acknowledgment.'); focus.current = 'retry'; }
        else { setError('Unable to verify current lifecycle permission. Load it again before confirming.'); focus.current = 'reload'; }
      }
    } finally { finish(controller); }
  }
  return <Container component="main" maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography variant="h4" component="h1">Organization lifecycle</Typography>
    <Button component={Link} to="/app">Back to organizations</Button>
    <Button component={Link} to="/app/lifecycle">Organization lifecycle management</Button>
    {busy && <Typography role="status">Checking current lifecycle and permission…</Typography>}
    {error && <Alert severity="warning">{error}</Alert>}
    {liveStatus && !denied && <Typography role="status">{liveStatus}</Typography>}
    {ack && <Alert severity="success" role="status" tabIndex={-1} ref={notice}>Original change acknowledged: {lifecycleLabel(ack.status)}, revision {ack.version}.
      {' '}This acknowledgment may precede a later change. Load the current lifecycle before another action.</Alert>}
    {review && !denied && <>
      <Typography>Current lifecycle: <strong>{lifecycleLabel(review.status)}</strong>. Revision {review.version}.</Typography>
      <Typography>Organization: <strong>{review.name}</strong></Typography>
      <Typography>Suspension and deactivation prevent new operational activity. Existing records are retained. Reactivation enables eligible operational activity again.</Typography>
      <TextField select label="Requested lifecycle" value={target} onChange={event => setTarget(event.target.value as OperationalState)} disabled={busy}>
        {(['ACTIVE', 'SUSPENDED', 'DEACTIVATED'] as const).filter(value => value !== review.status).map(value => <MenuItem key={value} value={value}>{lifecycleLabel(value)}</MenuItem>)}
      </TextField>
      <Button ref={launcher} variant="contained" disabled={busy || liveUnavailable || !!intent || target === review.status} onClick={() => { setConfirmation({ review, target }); setOpen(true); }}>Review lifecycle change</Button>
    </>}
    {intent && !denied && <Button ref={retry} variant="contained" disabled={busy} onClick={() => void change(true)}>Retry original lifecycle change</Button>}
    {!denied && <Button ref={reload} disabled={busy || !!intent} onClick={() => void load(true)}>Load current lifecycle</Button>}
    <Dialog open={open} disableRestoreFocus onClose={() => { if (!busy) setOpen(false); }} aria-labelledby="lifecycle-review-title" aria-describedby="lifecycle-review-description"
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: () => { if (focus.current) focusResult(); else launcher.current?.focus(); } } }} fullWidth maxWidth="sm">
      <DialogTitle id="lifecycle-review-title">Confirm lifecycle change</DialogTitle>
      <DialogContent><Typography id="lifecycle-review-description">Change {confirmation?.review.name} from {confirmation && lifecycleLabel(confirmation.review.status)} to {confirmation && lifecycleLabel(confirmation.target)} at revision {confirmation?.review.version}.
        {' '}Existing records are retained. {confirmation?.target === 'ACTIVE' ? 'Eligible operational activity will resume.' : 'New operational activity will be prevented.'}</Typography></DialogContent>
      <DialogActions><Button ref={cancel} disabled={busy} onClick={() => setOpen(false)}>Cancel lifecycle change</Button>
        <Button variant="contained" disabled={busy} onClick={() => void change()}>Confirm lifecycle change</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
