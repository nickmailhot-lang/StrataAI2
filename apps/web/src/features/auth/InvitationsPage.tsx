import { useEffect, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Paper, Stack, Typography } from '@mui/material';
import { Link, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';

type Invitation = { id: string; organizationId: string; organizationName: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string; expiresAt: string };
type Page = { items: Invitation[]; nextCursor: string | null };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validPage(value: unknown): value is Page {
  if (!value || typeof value !== 'object') return false;
  const page = value as Partial<Page>;
  if (!Array.isArray(page.items) || page.items.length > 50 || (page.nextCursor !== null && (typeof page.nextCursor !== 'string' || !uuid.test(page.nextCursor)))) return false;
  const seen = new Set<string>();
  for (const item of page.items) {
    if (!item || !uuid.test(item.id) || !uuid.test(item.organizationId) || typeof item.organizationName !== 'string' || !item.organizationName.trim()
      || !['INTERNAL', 'PORTAL'].includes(item.surface) || typeof item.targetRole !== 'string' || !item.targetRole
      || typeof item.expiresAt !== 'string' || !Number.isFinite(Date.parse(item.expiresAt)) || seen.has(item.id)) return false;
    seen.add(item.id);
  }
  return page.nextCursor === null || (page.items.length === 50 && page.nextCursor === page.items.at(-1)?.id);
}
async function request(path: string, controller: AbortController, method = 'GET') {
  let timer: ReturnType<typeof setTimeout> | undefined;
  let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { method, signal: controller.signal }).then(async response => ({ status: response.status, body: response.ok ? await response.json() as unknown : undefined })),
      new Promise<never>((_, reject) => {
        abort = () => reject(new Error('Invitation request interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true });
        timer = setTimeout(() => controller.abort(), 15_000);
      }),
    ]);
  } finally {
    clearTimeout(timer);
    if (abort) controller.signal.removeEventListener('abort', abort);
  }
}

export function InvitationsPage() {
  const [page, setPage] = useState<Page>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [accepted, setAccepted] = useState<Invitation>();
  const [uncertain, setUncertain] = useState<Invitation>();
  const current = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true);
  const navigate = useNavigate();
  async function load(after?: string) {
    if (current.current) return;
    const controller = new AbortController(); current.current = controller;
    setBusy(true); setError(undefined);
    try {
      const response = await request(after ? `/me/invitations?after=${encodeURIComponent(after)}` : '/me/invitations', controller);
      if (!mounted.current || current.current !== controller) return;
      if (response.status === 401) { setPage(undefined); setUncertain(undefined); setAccepted(undefined); navigate('/login', { replace: true }); return; }
      if (response.status === 403) { setPage(undefined); setUncertain(undefined); setAccepted(undefined); setError('Verify your email before viewing invitations.'); return; }
      if (response.status !== 200 || !validPage(response.body) || response.body.nextCursor === after) throw new Error('Invalid invitation page');
      setPage(response.body);
    } catch {
      if (mounted.current && current.current === controller) setError('Unable to load invitations. Please refresh and try again.');
    } finally {
      if (current.current === controller) { current.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  useEffect(() => {
    mounted.current = true;
    void load();
    return () => { mounted.current = false; current.current?.abort(); current.current = undefined; };
    // This owns the initial read; explicit refresh/paging owns subsequent reads.
  }, []);
  async function accept(invitation: Invitation) {
    if (current.current || (uncertain && uncertain.id !== invitation.id)) return;
    const controller = new AbortController(); current.current = controller;
    setBusy(true); setError(undefined); setAccepted(undefined);
    try {
      const response = await request(`/me/invitations/${invitation.id}/accept`, controller, 'POST');
      if (!mounted.current || current.current !== controller) return;
      if (response.status === 401) { setPage(undefined); setUncertain(undefined); navigate('/login', { replace: true }); return; }
      if (response.status === 400 || response.status === 403 || response.status === 404 || response.status === 409) {
        setUncertain(undefined);
        setPage(previous => previous && { ...previous, items: previous.items.filter(item => item.id !== invitation.id) });
        setError('This invitation is no longer available to your account. Refresh to check current invitations.'); return;
      }
      const ack = response.body as { invitationId?: string; organizationId?: string; surface?: string; targetRole?: string } | undefined;
      if (response.status !== 200 || ack?.invitationId !== invitation.id || ack.organizationId !== invitation.organizationId
        || ack.surface !== invitation.surface || ack.targetRole !== invitation.targetRole) throw new Error('Invalid invitation acknowledgment');
      setAccepted(invitation);
      setUncertain(undefined);
      setPage(previous => previous && { ...previous, items: previous.items.filter(item => item.id !== invitation.id) });
    } catch {
      if (mounted.current && current.current === controller) {
        setUncertain(invitation);
        setError('Unable to confirm acceptance. You can retry this invitation safely.');
      }
    } finally {
      if (current.current === controller) { current.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to="/app">Organizations</Button>
    <Typography variant="h4" component="h1">Your invitations</Typography>
    <Typography>Invitations matching your verified email appear here.</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {accepted && <Alert severity="success">Invitation to {accepted.organizationName} accepted. <Link to={accepted.surface === 'PORTAL' ? `/portal/${accepted.organizationId}` : `/app/${accepted.organizationId}`}>Open {accepted.surface === 'PORTAL' ? 'Owner Portal' : 'organization'}</Link></Alert>}
    {busy && <CircularProgress aria-label="Loading invitation request" />}
    {uncertain && <Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={1}>
      <Typography>An invitation acceptance still needs confirmation. Refreshing the list will preserve this attempt.</Typography>
      <Button disabled={busy} variant="contained" onClick={() => void accept(uncertain)}>Retry invitation acceptance</Button>
    </Stack></Paper>}
    {page?.items.length === 0 && !uncertain && <Typography>No pending invitations on this page.</Typography>}
    {page?.items.filter(invitation => invitation.id !== uncertain?.id).map(invitation => <Paper key={invitation.id} variant="outlined" sx={{ p: 2 }}><Stack spacing={1}>
      <Typography variant="h6" component="h2">{invitation.organizationName}</Typography>
      <Typography>{invitation.surface === 'PORTAL' ? 'Owner Portal' : 'Internal organization'} access · {invitation.targetRole.toLowerCase().replaceAll('_', ' ')}</Typography>
      <Button disabled={busy || Boolean(uncertain)} variant="contained" onClick={() => void accept(invitation)} aria-label={`Accept invitation to ${invitation.organizationName}`}>Accept invitation</Button>
    </Stack></Paper>)}
    {page?.nextCursor && <Button disabled={busy} onClick={() => void load(page.nextCursor!)}>More invitations</Button>}
    <Button disabled={busy} onClick={() => void load()}>Refresh invitations</Button>
  </Stack></Container>;
}
