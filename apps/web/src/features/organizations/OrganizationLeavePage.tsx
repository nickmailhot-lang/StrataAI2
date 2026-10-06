import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';

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
  async function request(path: string, options: RequestInit, controller: AbortController) {
    return boundedWorkRead(async signal => {
      const response = await apiFetch(path, { ...options, signal });
      return { status: response.status, body: response.status === 204 || response.status === 401
        ? undefined : await response.json().catch(() => undefined) as unknown };
    }, controller.signal);
  }
  function unavailable(status: number) {
    setReview(undefined); setActorId(undefined); setOpen(false); setRetryKey(undefined); setError('This Organization is unavailable to your account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function load() {
    if (retryKey) return;
    const controller = begin(); if (!controller) return;
    setReview(undefined); setActorId(undefined); setOpen(false); setNotice(undefined); setRecovered(false);
    try {
      const me = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(me.status)) { unavailable(me.status); return; }
      const actor = (me.body as { id?: unknown } | undefined)?.id;
      if (me.status !== 200 || typeof actor !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(actor)
        || actor === '00000000-0000-0000-0000-000000000000') throw new Error('Invalid account');
      const result = await request(root, {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(result.status)) { unavailable(result.status); return; }
      const value = result.body as Review | undefined;
      if (result.status !== 200 || value?.organization?.id !== organizationId || typeof value.organization.name !== 'string'
        || !value.organization.name.trim() || value.organization.status !== 0 || ![0, 1, 2].includes(value.role)) throw new Error('Invalid review');
      setActorId(actor); setReview(value);
    } catch { if (current(controller)) setError('Unable to review current membership. Try loading it again.'); }
    finally { finish(controller); }
  }
  async function leave(recover = false) {
    if (!actorId || (recover ? !retryKey : !review || !open || !!retryKey)) return;
    const controller = begin(); if (!controller) return;
    const key = retryKey ?? crypto.randomUUID();
    try {
      const result = await request(`${root}/leave`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': key }, body: JSON.stringify({ expectedActorId: actorId }) }, controller);
      if (!current(controller)) return;
      setOpen(false); setReview(undefined);
      if (result.status === 204) {
        setRetryKey(undefined); setRecovered(recover);
        setNotice(recover ? 'Original departure acknowledged. Review current membership to check later access.' : 'You left the Organization.'); return;
      }
      if ([401, 403, 404].includes(result.status)) { unavailable(result.status); return; }
      const code = (result.body as { code?: unknown } | undefined)?.code;
      setRetryKey([400, 409, 429].includes(result.status) ? undefined : key);
      setError(result.status === 409 && code === 'sole_owner'
        ? 'The last usable owner cannot leave. Another usable owner must remain.'
        : [400, 409, 429].includes(result.status) ? 'The departure was refused. Review current membership before considering another departure.'
          : 'Your departure could not be confirmed. Retry the original departure to recover its acknowledgment.');
    } catch {
      if (current(controller)) { setRetryKey(key); setOpen(false); setReview(undefined); setError('Your departure could not be confirmed. Retry the original departure to recover its acknowledgment.'); }
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
