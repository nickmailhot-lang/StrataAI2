import { useEffect, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Paper, Stack, Typography } from '@mui/material';
import { Link, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchInvitationRecipient, type InvitationRecipientInvalidation } from './invitationRecipientLive';

type Invitation = { id: string; organizationId: string; organizationName: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string; expiresAt: string; boardTarget?: { boardId: string; role: 'ADMIN' | 'MEMBER' } | null; boardName?: string | null };
type Page = { items: Invitation[]; nextCursor: string | null };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validPage(value: unknown): value is Page {
  if (!value || typeof value !== 'object') return false;
  const page = value as Partial<Page>;
  if (!Array.isArray(page.items) || page.items.length > 50 || (page.nextCursor !== null && (typeof page.nextCursor !== 'string' || !uuid.test(page.nextCursor)))) return false;
  const seen = new Set<string>();
  for (const item of page.items) {
    const board = item?.boardTarget;
    const validBoard = board == null ? item?.boardName == null
      : item?.surface === 'INTERNAL' && item.targetRole === 'MEMBER' && typeof board.boardId === 'string'
        && uuid.test(board.boardId) && board.boardId !== '00000000-0000-0000-0000-000000000000'
        && ['ADMIN', 'MEMBER'].includes(board.role) && typeof item.boardName === 'string' && Boolean(item.boardName.trim());
    if (!validBoard || !item || typeof item.id !== 'string' || typeof item.organizationId !== 'string' || !uuid.test(item.id) || !uuid.test(item.organizationId) || typeof item.organizationName !== 'string' || !item.organizationName.trim()
      || !(item.surface === 'INTERNAL' ? ['OWNER', 'ADMIN', 'MEMBER'].includes(item.targetRole)
        : item.surface === 'PORTAL' && ['OWNER', 'CO_OWNER', 'TENANT', 'OCCUPANT', 'AUTHORIZED_REPRESENTATIVE', 'OTHER'].includes(item.targetRole))
      || typeof item.expiresAt !== 'string' || !Number.isFinite(Date.parse(item.expiresAt)) || seen.has(item.id)) return false;
    seen.add(item.id);
  }
  // Authorization filtering can leave a short or empty page with a cursor for the last scanned candidate.
  return page.nextCursor === null || page.items.every(item => item.id.toLowerCase() <= page.nextCursor!.toLowerCase());
}
async function request(path: string, controller: AbortController, method = 'GET') {
  controller.signal.throwIfAborted();
  let abort: (() => void) | undefined;
  try {
    const interrupted = new Promise<never>((_, reject) => {
      abort = () => reject(new Error('Invitation request interrupted'));
      controller.signal.addEventListener('abort', abort, { once: true });
    });
    return await Promise.race([
      apiFetch(path, { method, signal: controller.signal }).then(async response => ({ status: response.status, body: response.ok ? await response.json() as unknown : undefined })),
      interrupted,
    ]);
  } finally {
    if (abort) controller.signal.removeEventListener('abort', abort);
  }
}

class AccountUnavailable extends Error { constructor(readonly status: number) { super('Invitation account unavailable'); } }

export function InvitationsPage() {
  const [page, setPage] = useState<Page>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [accepted, setAccepted] = useState<Invitation>();
  const [uncertain, setUncertain] = useState<Invitation>();
  const current = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true);
  const reviewedActor = useRef<string | undefined>(undefined);
  const firstAdmission = useRef<{ actor: string; until: number } | undefined>(undefined);
  const [accountReady, setAccountReady] = useState(false);
  const epoch = useRef(0); const refreshQueued = useRef(false);
  const [reloadVersion, setReloadVersion] = useState(0);
  const [connecting, setConnecting] = useState(true);
  const [admissionFailed, setAdmissionFailed] = useState(false);
  const [admissionAttempt, setAdmissionAttempt] = useState(0);
  const [announcement, setAnnouncement] = useState('Connecting invitation updates.');
  const refreshButton = useRef<HTMLButtonElement>(null);
  const navigate = useNavigate();
  function valid(controller: AbortController) { return mounted.current && current.current === controller && !controller.signal.aborted; }
  async function verifyAccount(controller: AbortController, expected?: string) {
    try {
      const response = await request('/me', controller);
      if (!valid(controller)) throw new AccountUnavailable(503);
      const id = (response.body as { id?: unknown } | undefined)?.id;
      if (response.status === 401) throw new AccountUnavailable(401);
      if (response.status !== 200 || typeof id !== 'string' || !uuid.test(id) || id === '00000000-0000-0000-0000-000000000000')
        throw new AccountUnavailable(503);
      if (expected && id !== expected) throw new AccountUnavailable(401);
      return id;
    } catch (reason) {
      throw reason instanceof AccountUnavailable ? reason : new AccountUnavailable(503);
    }
  }
  function withdrawAccount(status: number) {
    setPage(undefined); setAccepted(undefined); setAccountReady(false);
    if (status === 401) { setUncertain(undefined); navigate('/login', { replace: true }); }
    else setError('Unable to confirm the reviewed account. Refresh invitations before continuing.');
  }
  async function load(after?: string) {
    if (current.current) return;
    const controller = new AbortController(); current.current = controller;
    const captured = firstAdmission.current; firstAdmission.current = undefined;
    const remaining = captured ? captured.until - performance.now() : 15_000;
    const deadline = setTimeout(() => controller.abort(), Math.max(0, remaining));
    if (remaining <= 0) controller.abort();
    const started = epoch.current;
    setBusy(true); setError(undefined); setPage(undefined); setAccepted(undefined); setAccountReady(false);
    try {
      controller.signal.throwIfAborted();
      const actor = captured?.actor ?? await verifyAccount(controller, reviewedActor.current);
      if (!valid(controller)) return;
      const query = new URLSearchParams({ expectedActorId: actor }); if (after) query.set('after', after);
      const response = await request(`/me/invitations?${query}`, controller);
      await verifyAccount(controller, actor); if (!valid(controller) || started !== epoch.current) return;
      reviewedActor.current = actor; setAccountReady(true);
      if (!mounted.current || current.current !== controller) return;
      if (response.status === 401) { setPage(undefined); setUncertain(undefined); setAccepted(undefined); navigate('/login', { replace: true }); return; }
      if (response.status === 403) { setPage(undefined); setUncertain(undefined); setAccepted(undefined); setError('Verify your email before viewing invitations.'); return; }
      if (response.status !== 200 || !validPage(response.body) || (after && response.body.nextCursor !== null && response.body.nextCursor.toLowerCase() <= after.toLowerCase())) throw new Error('Invalid invitation page');
      setPage(response.body);
    } catch (reason) {
      if (mounted.current && current.current === controller && started === epoch.current) {
        if (reason instanceof AccountUnavailable) withdrawAccount(reason.status);
        else setError('Unable to load invitations. Please refresh and try again.');
      }
    } finally {
      clearTimeout(deadline);
      if (current.current === controller) { current.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  useEffect(() => {
    mounted.current = true;
    setConnecting(true); setError(undefined); setAnnouncement('Connecting invitation updates.');
    let disposed = false;
    const admission = new AbortController(); current.current = admission;
    const until = performance.now() + 15_000;
    let stop: (() => void) | undefined;
    let transport: InvitationRecipientInvalidation | undefined;
    function invalidate(reason: InvitationRecipientInvalidation) {
      if (!mounted.current || disposed) return;
      if (reason === 'unavailable') firstAdmission.current = undefined;
      clearTimeout(bootstrap); setConnecting(false);
      // Repeated connection failures must not continually interrupt the same
      // protected HTTP recovery. Actual resets/transitions always fence it.
      if (reason === 'unavailable' && transport === reason) return;
      transport = reason;
      setAnnouncement(reason === 'change' ? 'Invitations changed. Checking current invitations.'
        : reason === 'reset' ? 'Checking current invitations.'
          : 'Live invitation updates interrupted. Checking current invitations.');
      expire(); current.current?.abort();
    }
    const bootstrap = setTimeout(() => {
      if (current.current === admission) { admission.abort(); setConnecting(false); }
      else invalidate('unavailable');
    }, 15_000);
    async function begin() {
      try {
        const actor = await verifyAccount(admission, reviewedActor.current);
        if (!valid(admission)) return;
        reviewedActor.current = actor; firstAdmission.current = { actor, until };
        current.current = undefined;
        stop = watchInvitationRecipient({ subject: actor, invalidate });
      } catch (reason) {
        if (mounted.current && current.current === admission) {
          clearTimeout(bootstrap); setConnecting(false);
          setAdmissionFailed(true);
          setAnnouncement('Invitation updates unavailable. Refresh invitations before continuing.');
          withdrawAccount(reason instanceof AccountUnavailable ? reason.status : 503);
        }
      } finally {
        if (current.current === admission) current.current = undefined;
      }
    }
    void begin();
    return () => {
      disposed = true; mounted.current = false; clearTimeout(bootstrap); stop?.(); current.current?.abort(); current.current = undefined;
    };
    // A captured stream head precedes protected discovery. Connection failure
    // keeps explicit, bounded HTTP recovery available.
  }, [admissionAttempt]);
  function expire() {
    if (document.activeElement?.closest('[data-invitation-disclosure]')) refreshButton.current?.focus();
    epoch.current++; refreshQueued.current = true;
    setPage(undefined); setAccepted(undefined); setAccountReady(false);
    setReloadVersion(value => value + 1);
  }
  useEffect(() => {
    if (!refreshQueued.current || busy) return;
    refreshQueued.current = false; void load();
  }, [reloadVersion, busy]);
  useEffect(() => {
    const expiries = page?.items.filter(item => item.id !== uncertain?.id)
      .map(item => Date.parse(item.expiresAt)).filter(value => value > Date.now());
    if (!expiries?.length) return;
    const expires = Math.min(...expiries);
    let timer: ReturnType<typeof setTimeout>;
    function check() {
      const remaining = expires - Date.now();
      if (remaining > 0) { timer = setTimeout(check, Math.min(remaining, 2_147_483_647)); return; }
      expire();
    }
    check(); return () => clearTimeout(timer);
  }, [page, uncertain?.id]);
  async function accept(invitation: Invitation) {
    if (current.current || !accountReady || !reviewedActor.current || (uncertain && uncertain.id !== invitation.id)) return;
    const actor = reviewedActor.current; let submitted = false;
    const started = epoch.current;
    const controller = new AbortController(); current.current = controller;
    const deadline = setTimeout(() => controller.abort(), 15_000);
    setBusy(true); setError(undefined); setAccepted(undefined);
    try {
      await verifyAccount(controller, actor); if (!valid(controller)) return;
      // Keep a submitted attempt's exact-ID recovery; the server still owns
      // expiry and acknowledgment admission. Unsent acceptance loses consent.
      if (uncertain?.id !== invitation.id && Date.parse(invitation.expiresAt) <= Date.now()) { expire(); return; }
      submitted = true;
      const response = await request(`/me/invitations/${invitation.id}/accept?expectedActorId=${encodeURIComponent(actor)}`, controller, 'POST');
      await verifyAccount(controller, actor); if (!valid(controller)) return;
      if (!mounted.current || current.current !== controller) return;
      if (response.status === 401) { setPage(undefined); setUncertain(undefined); navigate('/login', { replace: true }); return; }
      if (response.status === 400 || response.status === 403 || response.status === 404 || response.status === 409) {
        setUncertain(undefined);
        setPage(previous => previous && { ...previous, items: previous.items.filter(item => item.id !== invitation.id) });
        setError('This invitation is no longer available to your account. Refresh to check current invitations.'); return;
      }
      const ack = response.body as { invitationId?: string; organizationId?: string; surface?: string; targetRole?: string; boardTarget?: Invitation['boardTarget'] } | undefined;
      if (response.status !== 200 || ack?.invitationId !== invitation.id || ack.organizationId !== invitation.organizationId
        || ack.surface !== invitation.surface || ack.targetRole !== invitation.targetRole
        || (invitation.boardTarget == null ? ack.boardTarget != null
          : ack.boardTarget?.boardId !== invitation.boardTarget.boardId || ack.boardTarget?.role !== invitation.boardTarget.role)) throw new Error('Invalid invitation acknowledgment');
      setAccepted(invitation);
      setUncertain(undefined);
      setPage(previous => previous && { ...previous, items: previous.items.filter(item => item.id !== invitation.id) });
    } catch (reason) {
      if (mounted.current && current.current === controller) {
        if (submitted) { setUncertain(invitation); setPage(undefined); }
        if (started === epoch.current) {
          if (reason instanceof AccountUnavailable) withdrawAccount(reason.status);
          else setError('Unable to confirm acceptance. You can retry this invitation safely.');
        }
      }
    } finally {
      clearTimeout(deadline);
      if (current.current === controller) { current.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  const available = page?.items.filter(invitation => invitation.id !== uncertain?.id && Date.parse(invitation.expiresAt) > Date.now());
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to="/app">Organizations</Button>
    <Typography variant="h4" component="h1">Your invitations</Typography>
    <Typography>Invitations matching your verified email appear here.</Typography>
    <Typography role="status" aria-live="polite" aria-atomic="true">{announcement}</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {accepted && <Alert severity="success" data-invitation-disclosure>Invitation to {accepted.organizationName} accepted. <Link to={accepted.surface === 'PORTAL' ? `/portal/${accepted.organizationId}` : `/app/${accepted.organizationId}${accepted.boardTarget ? `/boards/${accepted.boardTarget.boardId}` : ''}`}>Open {accepted.surface === 'PORTAL' ? 'Owner Portal' : accepted.boardTarget ? 'Board' : 'organization'}</Link></Alert>}
    {(busy || connecting) && <CircularProgress aria-label="Loading invitation request" />}
    {uncertain && <Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={1}>
      <Typography>An invitation acceptance still needs confirmation. Refreshing the list will preserve this attempt.</Typography>
      <Button disabled={busy || !accountReady} variant="contained" onClick={() => void accept(uncertain)}>Retry invitation acceptance</Button>
    </Stack></Paper>}
    {available?.length === 0 && !uncertain && <Typography>No pending invitations on this page.</Typography>}
    {available?.map(invitation => <Paper key={invitation.id} variant="outlined" data-invitation-disclosure sx={{ p: 2 }}><Stack spacing={1}>
      <Typography variant="h6" component="h2">{invitation.organizationName}</Typography>
      {invitation.boardTarget && <Typography variant="h6" component="h3">{invitation.boardName}</Typography>}
      <Typography>{invitation.boardTarget ? `Board access \u00b7 ${invitation.boardTarget.role.toLowerCase()}` : <>{invitation.surface === 'PORTAL' ? 'Owner Portal' : 'Internal organization'} access · {invitation.targetRole.toLowerCase().replaceAll('_', ' ')}</>}</Typography>
      <Button disabled={busy || Boolean(uncertain)} variant="contained" onClick={() => void accept(invitation)} aria-label={`Accept invitation to ${invitation.organizationName}${invitation.boardTarget ? `, Board ${invitation.boardName}, ${invitation.boardTarget.role.toLowerCase()}` : ''}`}>Accept invitation</Button>
    </Stack></Paper>)}
    {page?.nextCursor && <Button disabled={busy} onClick={() => void load(page.nextCursor!)}>More invitations</Button>}
    <Button ref={refreshButton} aria-disabled={busy || connecting} onClick={() => {
      if (busy || connecting || current.current) return;
      if (admissionFailed) { setAdmissionFailed(false); setAdmissionAttempt(value => value + 1); }
      else void load();
    }}>Refresh invitations</Button>
  </Stack></Container>;
}
