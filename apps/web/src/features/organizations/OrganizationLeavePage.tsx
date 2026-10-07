import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';

class DepartureUnavailable extends Error { constructor(readonly status: number) { super('Departure unavailable'); } }
type Review = { organization: { id: string; name: string; status: number }; role: number };
export function OrganizationLeavePage() {
  const { organizationId } = useParams();
  return <Departure key={organizationId} organizationId={organizationId ?? ''} />;
}
function Departure({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate(); const [review, setReview] = useState<Review>();
  const [open, setOpen] = useState(false); const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [retryKey, setRetryKey] = useState<string>(); const [recovered, setRecovered] = useState(false);
  const [actorId, setActorId] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined);
  const cancel = useRef<HTMLButtonElement>(null); const action = useRef<HTMLButtonElement>(null);
  const confirmation = useRef<HTMLDivElement>(null);
  const retry = useRef<HTMLButtonElement>(null); const recoveryFocus = useRef(false);
  const root = `/organizations/${encodeURIComponent(organizationId)}`;
  useEffect(() => { mounted.current = true; void load(); return () => {
    mounted.current = false; pending.current?.abort(); pending.current = undefined;
  }; }, []);
  useEffect(() => {
    if (!busy && recovered && recoveryFocus.current) {
      if (document.activeElement === document.body) confirmation.current?.focus();
      recoveryFocus.current = false;
    }
  }, [busy, recovered]);
  function begin() {
    if (pending.current) return undefined;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller;
  }
  function current(controller: AbortController) { return mounted.current && pending.current === controller; }
  function finish(controller: AbortController) { if (current(controller)) { pending.current = undefined; setBusy(false); } }
  async function request(path: string, options: RequestInit, signal: AbortSignal) {
    signal.throwIfAborted();
    const response = await apiFetch(path, { ...options, signal });
    const body = response.status === 204 || response.status === 401 ? undefined : await response.json().catch(() => undefined) as unknown;
    signal.throwIfAborted();
    return { status: response.status, body };
  }
  async function profile(signal: AbortSignal) {
    const me = await request('/me', {}, signal);
    if ([401, 403, 404].includes(me.status)) throw new DepartureUnavailable(me.status);
    if (me.status !== 200 || !isNotificationProfile(me.body)) throw new Error('Invalid account');
    return me.body;
  }
  function unavailable(status: number) {
    setReview(undefined); setActorId(undefined); setOpen(false); setRetryKey(undefined); setNotice(undefined); setRecovered(false);
    setError('This Organization is unavailable to your account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function load() {
    if (retryKey) return;
    const controller = begin(); if (!controller) return;
    setReview(undefined); setActorId(undefined); setOpen(false); setNotice(undefined); setRecovered(false);
    try {
      const admitted = await boundedWorkRead(async signal => {
        const before = await profile(signal);
        const result = await request(root, {}, signal);
        if ([401, 403, 404].includes(result.status)) throw new DepartureUnavailable(result.status);
        const value = result.body as Review | undefined;
        if (result.status !== 200 || value?.organization?.id !== organizationId || typeof value.organization.name !== 'string'
          || !value.organization.name.trim() || value.organization.status !== 0 || ![0, 1, 2].includes(value.role)) throw new Error('Invalid review');
        const after = await profile(signal);
        if (after.id !== before.id) throw new DepartureUnavailable(401);
        return { actor: after.id, value };
      }, controller.signal);
      if (!current(controller)) return;
      setActorId(admitted.actor); setReview(admitted.value);
    } catch (error) { if (current(controller)) {
      if (error instanceof DepartureUnavailable) unavailable(error.status);
      else setError('Unable to review current membership. Try loading it again.');
    } } finally { finish(controller); }
  }
  async function leave(recover = false) {
    if (!actorId || (recover ? !retryKey : !review || !open || !!retryKey)) return;
    const controller = begin(); if (!controller) return;
    const key = retryKey ?? crypto.randomUUID(); let submitted = false;
    setNotice(undefined); setRecovered(false);
    try {
      const result = await boundedWorkRead(async signal => {
        const before = await profile(signal);
        if (before.id !== actorId) throw new DepartureUnavailable(401);
        if (!current(controller)) throw new Error('Departure retired');
        submitted = true;
        const response = await request(`${root}/leave`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify({ expectedActorId: actorId }) }, signal);
        if ([401, 403, 404].includes(response.status)) throw new DepartureUnavailable(response.status);
        const after = await profile(signal);
        if (after.id !== actorId) throw new DepartureUnavailable(401);
        return response;
      }, controller.signal);
      if (!current(controller)) return;
      setOpen(false); setReview(undefined);
      if (result.status === 204) {
        setRetryKey(undefined); setRecovered(recover);
        setNotice(recover ? 'Original departure acknowledged. Review current membership to check later access.' : 'You left the Organization.'); return;
      }
      const code = (result.body as { code?: unknown } | undefined)?.code;
      setRetryKey([400, 409, 429].includes(result.status) ? undefined : key);
      setError(result.status === 409 && code === 'sole_owner'
        ? 'The last usable owner cannot leave. Another usable owner must remain.'
        : [400, 409, 429].includes(result.status) ? 'The departure was refused. Review current membership before considering another departure.'
          : 'Your departure could not be confirmed. Retry the original departure to recover its acknowledgment.');
    } catch (error) {
      if (current(controller)) {
        if (error instanceof DepartureUnavailable) { unavailable(error.status); return; }
        setOpen(false); setReview(undefined);
        if (submitted || retryKey) { setRetryKey(key); setError('Your departure could not be confirmed. Retry the original departure to recover its acknowledgment.'); }
        else { setActorId(undefined); setError('Your account could not be confirmed. No departure was sent. Review current membership before trying again.'); }
      }
    } finally { finish(controller); }
  }
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography component="h1" variant="h4">Leave Organization</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {notice && <Alert ref={confirmation} tabIndex={-1} severity="success" role="status">{notice}</Alert>}
    <Button component={Link} to="/app">Your Organizations</Button>
    {(!notice || recovered) && <Button ref={action} disabled={busy || !!retryKey} onClick={() => void load()}>Review current membership</Button>}
    {retryKey && <Button ref={retry} disabled={busy} onClick={() => {
      recoveryFocus.current = document.activeElement === retry.current; void leave(true);
    }}>Retry original departure</Button>}
    {review && <><Typography>{review.organization.name}</Typography>
      <Typography>Leaving removes your membership and Organization Card assignments. The Organization and its work remain available to other authorized members.</Typography>
      <Button disabled={busy} onClick={() => setOpen(true)}>Review departure</Button></>}
    <Dialog open={open} aria-labelledby="departure-title" onClose={() => { if (!busy) setOpen(false); }}
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: () => {
        if (retry.current) retry.current.focus(); else if (action.current) action.current.focus(); else confirmation.current?.focus();
      } } }}>
      <DialogTitle id="departure-title">Leave {review?.organization.name}?</DialogTitle>
      <DialogContent><Typography>You will lose access through this membership. The server will refuse departure if you are the last usable owner.</Typography></DialogContent>
      <DialogActions><Button ref={cancel} autoFocus disabled={busy} onClick={() => setOpen(false)}>Cancel departure</Button>
        <Button disabled={busy} onClick={() => void leave()}>Confirm departure</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
