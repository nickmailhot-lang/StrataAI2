import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { clearDeletionRecovery, parseDeletionObservation, readDeletionRecovery, saveDeletionRecovery } from './deletionObservation';
import type { DeletionIntent, DeletionObservation, DeletionRecovery } from './deletionObservation';

type Review = { organization: { id: string; name: string; status: number; version: number }; role: number };
type Intent = DeletionIntent;
export function OrganizationDeletePage() {
  const { organizationId } = useParams();
  return <Deletion key={organizationId} organizationId={organizationId ?? ''} />;
}
function Deletion({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate(); const [review, setReview] = useState<Review>(); const [actor, setActor] = useState<string>();
  const [open, setOpen] = useState(false); const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>(); const [acknowledged, setAcknowledged] = useState(false);
  const [accepted, setAccepted] = useState<Intent>(); const [observation, setObservation] = useState<DeletionObservation>();
  const [unverifiedRecovery, setUnverifiedRecovery] = useState<DeletionRecovery>();
  const checkButton = useRef<HTMLButtonElement>(null); const completionFocus = useRef(false); const resumeFocus = useRef(false);
  const [intent, setIntent] = useState<Intent>(); const intentRef = useRef<Intent | undefined>(undefined);
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined);
  const cancel = useRef<HTMLButtonElement>(null); const reload = useRef<HTMLButtonElement>(null);
  const launcher = useRef<HTMLButtonElement>(null); const retry = useRef<HTMLButtonElement>(null);
  const notice = useRef<HTMLDivElement>(null); const recoveryFocus = useRef(false);
  const root = `/organizations/${encodeURIComponent(organizationId)}`;
  useEffect(() => { mounted.current = true; const saved = readDeletionRecovery(organizationId); if (saved) void resume(saved); else void load(); return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; }; }, []);
  useEffect(() => {
    if (!busy && acknowledged && recoveryFocus.current) {
      if (document.activeElement === document.body) notice.current?.focus();
      recoveryFocus.current = false;
    }
  }, [busy, acknowledged]);
  useEffect(() => {
    if (!busy && observation?.state === 'COMPLETED' && completionFocus.current) {
      if (document.activeElement === document.body) notice.current?.focus();
      completionFocus.current = false;
    }
  }, [busy, observation]);
  useEffect(() => {
    if (!busy && intent && resumeFocus.current) {
      if (document.activeElement === document.body) retry.current?.focus();
      resumeFocus.current = false;
    }
  }, [busy, intent]);
  async function resume(saved: DeletionRecovery) {
    const controller = begin(); if (!controller) return;
    setUnverifiedRecovery(saved);
    try {
      const profile = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(profile.status)) { unavailable(profile.status); return; }
      if (profile.status !== 200 || !isNotificationProfile(profile.body)) throw new Error('Account unavailable');
      if (profile.body.id !== saved.actor) { unavailable(401, true); return; }
      setUnverifiedRecovery(undefined);
      if (saved.acknowledged) { recoveryFocus.current = true; setAccepted(saved); setAcknowledged(true); }
      else { resumeFocus.current = true; intentRef.current = saved; setIntent(saved); setError('Retry the original deletion request to recover its acknowledgment.'); }
    } catch { if (current(controller)) setError('Unable to verify the account for this deletion request. Refresh to try again.'); }
    finally { finish(controller); }
  }
  async function checkStatus() {
    if (!accepted || observation?.state === 'COMPLETED') return;
    const original = accepted; const controller = begin(); if (!controller) return;
    try {
      const before = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(before.status)) { unavailable(before.status); return; }
      if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Account unavailable');
      if (before.body.id !== original.actor) { unavailable(401, true); return; }
      const result = await request(`${root}/deletion-requests/${original.key}?expectedActorId=${original.actor}`, {}, controller);
      if (!current(controller)) return;
      if ([401, 403, 404].includes(result.status)) { unavailable(result.status); return; }
      const snapshot = parseDeletionObservation(result.body, original);
      if (result.status !== 200 || !snapshot) throw new Error('Completion unconfirmed');
      const after = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(after.status)) { unavailable(after.status); return; }
      if (after.status !== 200 || !isNotificationProfile(after.body)) throw new Error('Account unavailable');
      if (after.body.id !== original.actor) { unavailable(401, true); return; }
      setObservation(snapshot);
    } catch { if (current(controller)) setError('Unable to confirm deletion status. Check the same request again.'); }
    finally { finish(controller); }
  }
  function begin() {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller;
  }
  function current(controller: AbortController) { return mounted.current && pending.current === controller && !controller.signal.aborted; }
  function finish(controller: AbortController) { if (current(controller)) { pending.current = undefined; setBusy(false); } }
  async function request(path: string, options: RequestInit, controller: AbortController) {
    return boundedWorkRead(async signal => {
      const result = await apiFetch(path, { ...options, signal });
      const body: unknown = result.status === 401 ? undefined : await result.json().catch(() => undefined);
      return { status: result.status, body };
    }, controller.signal);
  }
  function unavailable(status: number, switched = false) {
    if (switched || [403, 404].includes(status)) clearDeletionRecovery(organizationId);
    setAccepted(undefined); setObservation(undefined); setAcknowledged(false);
    setUnverifiedRecovery(undefined);
    intentRef.current = undefined; setIntent(undefined); setReview(undefined); setActor(undefined); setOpen(false);
    setError('Organization deletion is unavailable to this account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function load() {
    if (intentRef.current || acknowledged) return;
    const saved = unverifiedRecovery ?? readDeletionRecovery(organizationId);
    if (saved) { await resume(saved); return; }
    const controller = begin(); if (!controller) return; setReview(undefined); setActor(undefined); setOpen(false);
    try {
      const before = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(before.status)) { unavailable(before.status); return; }
      if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Account unavailable');
      const result = await request(root, {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(result.status)) { unavailable(result.status); return; }
      const value = result.body as Review | undefined;
      if (result.status !== 200 || value?.organization?.id !== organizationId || typeof value.organization.name !== 'string'
        || !value.organization.name.trim() || value.organization.name.length > 160 || value.organization.status !== 0
        || !Number.isSafeInteger(value.organization.version) || value.organization.version < 1 || value.organization.version > Number.MAX_SAFE_INTEGER - 2 || ![0, 1, 2].includes(value.role))
        throw new Error('Invalid review');
      const after = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(after.status)) { unavailable(after.status); return; }
      if (after.status !== 200 || !isNotificationProfile(after.body)) throw new Error('Account unavailable');
      if (before.body.id !== after.body.id) { unavailable(401); return; }
      if (value.role !== 0) { setError('Only a current Organization Owner can request deletion.'); return; }
      setActor(after.body.id); setReview(value);
    } catch { if (current(controller)) setError('Unable to review current deletion permission. Try loading it again.'); }
    finally { finish(controller); }
  }
  async function remove(recover = false) {
    if (acknowledged || (recover ? !intentRef.current : !open || !review || !actor || !!intentRef.current)) return;
    const controller = begin(); if (!controller) return;
    const original = intentRef.current ?? { key: crypto.randomUUID(), actor: actor!, version: review!.organization.version };
    intentRef.current = original; setIntent(original); saveDeletionRecovery(organizationId, original, false);
    try {
      const before = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(before.status)) { unavailable(before.status); return; }
      if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Account unavailable');
      if (before.body.id !== original.actor) { unavailable(401, true); return; }
      const result = await request(`${root}?version=${original.version}&expectedActorId=${encodeURIComponent(original.actor)}`,
        { method: 'DELETE', headers: { 'Idempotency-Key': original.key } }, controller); if (!current(controller)) return;
      setOpen(false); setReview(undefined); setActor(undefined);
      if (result.status === 202) {
        if (result.body !== undefined && (!result.body || typeof result.body !== 'object'
          || Object.keys(result.body).length !== 1 || (result.body as { requestId?: unknown }).requestId !== original.key)) throw new Error('Wrong acknowledgment');
        intentRef.current = undefined; setIntent(undefined); setAccepted(original); setAcknowledged(true);
        saveDeletionRecovery(organizationId, original, true); return;
      }
      if ([401, 403, 404].includes(result.status)) { unavailable(result.status); return; }
      if ([400, 409, 429].includes(result.status)) {
        clearDeletionRecovery(organizationId);
        intentRef.current = undefined; setIntent(undefined);
        setError('The deletion request was refused. Review current permission and version before considering another request.'); return;
      }
      throw new Error('Missing acknowledgment');
    } catch {
      if (current(controller)) {
        setOpen(false); setReview(undefined); setActor(undefined);
        setError('The deletion request is not yet acknowledged. Retry the original request; it may already have succeeded.');
      }
    } finally { finish(controller); }
  }
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography component="h1" variant="h4">Request Organization deletion</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {acknowledged && <Alert ref={notice} tabIndex={-1} severity={observation?.state === 'COMPLETED' ? 'success' : 'info'} role="status">
      {observation?.state === 'COMPLETED' ? 'Organization deletion confirmed complete.' : observation?.state === 'PENDING'
        ? 'Deletion is still in progress. Completion has not been confirmed.' : 'Deletion request acknowledged. Deletion has not been confirmed complete.'}
    </Alert>}
    {acknowledged && accepted && observation?.state !== 'COMPLETED' && <Button ref={checkButton} disabled={busy}
      onClick={() => { completionFocus.current = document.activeElement === checkButton.current; void checkStatus(); }}>Check deletion status</Button>}
    <Button component={Link} to="/app">Your Organizations</Button>
    {!acknowledged && <Button ref={reload} disabled={busy || !!intent} onClick={() => void load()}>{unverifiedRecovery ? 'Retry account verification' : 'Review current deletion permission'}</Button>}
    {intent && <Button ref={retry} disabled={busy} onClick={() => { recoveryFocus.current = document.activeElement === retry.current; void remove(true); }}>
      Retry original deletion request
    </Button>}
    {review && <><Typography>{review.organization.name}</Typography>
      <Typography>Requesting deletion removes normal access to this Organization and its Boards and suspends reminders. Completion is a separate step.</Typography>
      <Button ref={launcher} disabled={busy || !!intent} onClick={() => setOpen(true)}>Review deletion request</Button></>}
    <Dialog open={open} aria-labelledby="organization-deletion-title" onClose={() => { if (!busy && !intentRef.current) setOpen(false); }}
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: () => {
        if (retry.current) retry.current.focus(); else if (notice.current) notice.current.focus();
        else (launcher.current ?? reload.current)?.focus();
      } } }}>
      <DialogTitle id="organization-deletion-title">Request deletion of {review?.organization.name}?</DialogTitle>
      <DialogContent><Typography>Normal Organization and Board access will stop. Confirm only if you intend to request deletion of this Organization.</Typography></DialogContent>
      <DialogActions><Button ref={cancel} autoFocus disabled={busy || !!intent} onClick={() => setOpen(false)}>Cancel deletion request</Button>
        <Button disabled={busy || !!intent} onClick={() => void remove()}>Confirm deletion request</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
