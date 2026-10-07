import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { formatUserDateTime } from '../auth/userDateTime';
import { invitationIntentKey, invitationRoles, readInvitationIntent, saveInvitationIntent, validInvitationKey } from './invitationIntent';
import type { InvitationInput, InvitationIntent } from './invitationIntent';
import { watchOrganizationMetadata } from './organizationMetadataLive';
import { watchBoard } from '../../api/boardLive';

type Ack = { id: string; organizationId: string; email: string; surface: string; targetRole: string; expiresAt: string; invitationToken: null; boardTarget?: { boardId: string; role: string } | null };
const empty: InvitationInput = { email: '', surface: 'INTERNAL', targetRole: 'MEMBER' };
const roleLabel = (role: string) => ({ MEMBER: 'Member', ADMIN: 'Admin', OWNER: 'Owner', CO_OWNER: 'Co-owner', TENANT: 'Tenant', OCCUPANT: 'Occupant', AUTHORIZED_REPRESENTATIVE: 'Authorized representative', OTHER: 'Other' }[role] ?? role);
async function request(path: string, options: RequestInit, signal: AbortSignal) {
  signal.throwIfAborted();
  const response = await apiFetch(path, { ...options, signal });
  const body = response.status === 401 ? undefined : await response.json().catch(() => undefined) as unknown;
  signal.throwIfAborted();
  return { status: response.status, body };
}
export function OrganizationInvitationPage() {
  const { organizationId } = useParams(); return <Invitation key={organizationId} organizationId={organizationId ?? ''} />;
}
export function BoardInvitationPage() {
  const { organizationId, boardId } = useParams();
  return <Invitation key={`${organizationId}:${boardId}`} organizationId={organizationId ?? ''} boardId={boardId ?? ''} />;
}
function Invitation({ organizationId, boardId }: { organizationId: string; boardId?: string }) {
  const [boardName, setBoardName] = useState<string>();
  const navigate = useNavigate(); const [actorRole, setActorRole] = useState<number>(); const [storageKey, setStorageKey] = useState<string>();
  const [input, setInput] = useState<InvitationInput>(empty); const [intent, setIntent] = useState<InvitationIntent>();
  const [ack, setAck] = useState<Ack>(); const [busy, setBusy] = useState(false); const [denied, setDenied] = useState(false);
  const [blocked, setBlocked] = useState(false); const [error, setError] = useState<string>();
  const [preferences, setPreferences] = useState<{ locale: string; timezone: string }>();
  const pending = useRef<AbortController | undefined>(undefined); const currentIntent = useRef<InvitationIntent | undefined>(undefined);
  const mounted = useRef(false);
  const reviewedActor = useRef<string | undefined>(undefined);
  const confirmed = useRef<{ actor: string; command: InvitationIntent; acknowledgment: Ack } | undefined>(undefined);
  const [liveActor, setLiveActor] = useState<string>(); const [liveNotice, setLiveNotice] = useState<string>();
  const epoch = useRef(0); const refreshQueued = useRef(false); const [reload, setReload] = useState(0);
  const quietCheck = useRef(false);
  useEffect(() => {
    mounted.current = true; void load();
    return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; };
    // Organization changes remount the keyed route and fence late results.
  }, []);
  useEffect(() => {
    if (!liveActor) return;
    const recover = () => {
      epoch.current++; refreshQueued.current = true; quietCheck.current = false; withdrawAccount(true);
      setLiveNotice('Checking current invitation permissions. The original request is preserved.');
      setReload(value => value + 1);
    };
    if (boardId !== undefined) return watchBoard({ organizationId, boardId, invalidate: recover,
      // Even unchanged content heartbeats must recheck administrative authority.
      // Read access to a Board alone does not authorize invitation issuance.
      status: status => {
        if (status === 'live') {
          if (!refreshQueued.current) { refreshQueued.current = true; quietCheck.current = true; setReload(value => value + 1); }
        } else if (status !== 'connecting') recover();
      } });
    return watchOrganizationMetadata({ organizationId, userId: liveActor, invalidate: recover, reset: recover, unavailable: recover });
  }, [organizationId, boardId, liveActor]);
  useEffect(() => {
    if (!refreshQueued.current || busy) return;
    refreshQueued.current = false; const preserveDisplay = quietCheck.current; quietCheck.current = false; void load(preserveDisplay);
  }, [reload, busy]);
  function valid(controller: AbortController) { return mounted.current && pending.current === controller && !controller.signal.aborted; }
  function withdrawAccount(preserveConfirmed = false) {
    if (!preserveConfirmed) confirmed.current = undefined;
    reviewedActor.current = undefined;
    setStorageKey(undefined); setBoardName(undefined); setActorRole(undefined); setInput(empty); setIntent(undefined);
    currentIntent.current = undefined; setAck(undefined); setPreferences(undefined); setDenied(false);
  }
  function deny(status: number) {
    setLiveActor(undefined); refreshQueued.current = false; setLiveNotice(undefined);
    withdrawAccount(); setDenied(true);
    setError(`${boardId !== undefined ? 'Board' : 'Organization'} invitations are unavailable to your account.`);
    if (status === 401) navigate('/login', { replace: true });
  }
  function begin() { if (pending.current) return; const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller; }
  function finish(controller: AbortController) { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  async function verifyAccount(controller: AbortController, expected: string, signal: AbortSignal) {
    try {
      const me = await request('/me', {}, signal); if (!valid(controller)) return false;
      if ([401, 403, 404].includes(me.status)) { deny(me.status); return false; }
      const id = (me.body as { id?: unknown } | undefined)?.id;
      if (me.status !== 200 || !validInvitationKey(id)) throw new Error('Invalid actor');
      if (id !== expected) { deny(401); return false; }
      return true;
    } catch (reason) {
      // Temporary uncertainty withdraws display authority, while the saved
      // original request remains reserved for a fresh permission check.
      if (mounted.current && pending.current === controller) { withdrawAccount(); setLiveActor(undefined); }
      throw reason;
    }
  }
  async function load(preserveDisplay = false) {
    const controller = begin(); if (!controller) return;
    const started = epoch.current;
    const hadIntent = !!currentIntent.current;
    if (!preserveDisplay) {
      setBoardName(undefined); setActorRole(undefined); setAck(undefined); setInput(empty); setIntent(undefined); currentIntent.current = undefined;
    }
    try {
      await boundedWorkRead(async signal => {
        const me = await request('/me', {}, signal); if (!valid(controller)) return;
        if ([401, 403, 404].includes(me.status)) { deny(me.status); return; }
        const actor = (me.body as { id?: unknown } | undefined)?.id;
        const profile = me.body as { locale?: unknown; timezone?: unknown } | undefined;
        if (me.status !== 200 || !validInvitationKey(actor) || typeof profile?.locale !== 'string' || typeof profile.timezone !== 'string') throw new Error('Invalid actor');
        if (liveActor && actor !== liveActor) { deny(401); return; }
        const display = { locale: profile.locale, timezone: profile.timezone };
        if (!formatUserDateTime('2026-01-01T00:00:00Z', display)) throw new Error('Invalid preferences');
        let admittedRole: number; let admittedBoardName: string | undefined;
        if (boardId !== undefined) {
          if (!validInvitationKey(boardId)) throw new Error('Invalid Board route');
          const result = await request(`/boards/${encodeURIComponent(boardId)}`, {}, signal); if (!valid(controller)) return;
          if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
          const data = result.body as { board?: { id: string; organizationId: string; name: string; lifecycleState: string }; access?: { canAdminister: boolean } } | undefined;
          if (result.status !== 200 || data?.board?.id !== boardId || data.board.organizationId !== organizationId
            || data.board.lifecycleState !== 'active' || typeof data.board.name !== 'string' || !data.board.name.trim()
            || data.access?.canAdminister !== true) { deny(404); return; }
          admittedBoardName = data.board.name; admittedRole = 1;
        } else {
          const result = await request(`/organizations/${encodeURIComponent(organizationId)}/members/${actor}`, {}, signal); if (!valid(controller)) return;
          if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
          const data = result.body as { organizationId: string; actorRole: number; member: { userId: string; role: number } } | undefined;
          if (result.status !== 200 || !data || data.organizationId !== organizationId || ![0, 1].includes(data.actorRole)
            || !data.member || data.member.userId !== actor || data.member.role !== data.actorRole) throw new Error('Invalid actor admission');
          admittedRole = data.actorRole;
        }
        if (!await verifyAccount(controller, actor, signal)) return;
        if (started !== epoch.current) return;
        const key = invitationIntentKey(actor, organizationId) + (boardId !== undefined ? `:board:${boardId}` : '');
        reviewedActor.current = actor;
        setLiveActor(actor); setLiveNotice(value => value ? 'Current invitation permissions checked. Review the request before submitting.' : undefined);
        setBoardName(admittedBoardName);
        setStorageKey(key); setActorRole(admittedRole); setDenied(false); setBlocked(false);
        setPreferences(display);
        try {
          const saved = readInvitationIntent(key);
          if (saved && boardId !== undefined && (saved.input.surface !== 'INTERNAL' || !['ADMIN', 'MEMBER'].includes(saved.input.targetRole))) throw new Error('Invalid Board intent');
          if (saved) {
            setIntent(saved); currentIntent.current = saved; setInput(saved.input);
            const known = confirmed.current;
            if (known?.actor === actor && known.command.key === saved.key
              && known.command.input.email === saved.input.email && known.command.input.surface === saved.input.surface
              && known.command.input.targetRole === saved.input.targetRole) {
              setAck(known.acknowledgment); setError(undefined);
            } else {
              confirmed.current = undefined;
              setAck(undefined);
              setError('A prior invitation request is awaiting acknowledgment. Retry that same request before starting another.');
            }
          } else {
            confirmed.current = undefined; setAck(undefined); setIntent(undefined); currentIntent.current = undefined;
            if (hadIntent) setInput(empty);
          }
        } catch { setBlocked(true); setError('The saved invitation request cannot be read. Review existing invitations before creating another request.'); }
      }, controller.signal);
    } catch { if (mounted.current && pending.current === controller && started === epoch.current) {
      withdrawAccount(); setLiveActor(undefined);
      setError('Unable to verify current invitation permissions. Please retry.');
    } }
    finally { finish(controller); }
  }
  async function create(event: React.FormEvent) {
    event.preventDefault(); if (pending.current || actorRole === undefined || blocked || ack || !storageKey || !reviewedActor.current) return;
    if (!currentIntent.current && (!input.email.trim() || input.email.trim().length > 320)) { setError('Enter a valid invitation email of at most 320 characters.'); return; }
    let command = currentIntent.current;
    if (!command) {
      command = { key: crypto.randomUUID(), input: { ...input, email: input.email.trim() } };
      try { saveInvitationIntent(storageKey, command); }
      catch { setError('This browser could not retain the invitation request. Allow temporary site data before trying again.'); return; }
      currentIntent.current = command; setIntent(command); setInput(command.input);
    }
    const original = command;
    const expected = reviewedActor.current;
    const controller = begin(); if (!controller) return;
    const started = epoch.current;
    let operationSignal: AbortSignal | undefined;
    try {
      await boundedWorkRead(async signal => {
        operationSignal = signal;
        if (!await verifyAccount(controller, expected, signal)) return;
        if (started !== epoch.current) return;
        const root = boardId !== undefined ? `/boards/${encodeURIComponent(boardId)}/invitations` : `/organizations/${encodeURIComponent(organizationId)}/invitations`;
        const result = await request(`${root}?expectedActorId=${encodeURIComponent(expected)}`, {
          method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': original.key }, body: JSON.stringify(boardId !== undefined ? { email: original.input.email, role: original.input.targetRole } : original.input),
        }, signal); if (!valid(controller)) return;
        if (started !== epoch.current) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        if (!await verifyAccount(controller, expected, signal)) return;
        if (started !== epoch.current) return;
        const data = result.body as Ack | undefined;
        if (result.status === 201 && data && validInvitationKey(data.id) && data.organizationId === organizationId
          && typeof data.email === 'string' && data.email.trim().toUpperCase() === original.input.email.toUpperCase()
          && data.surface === original.input.surface && data.invitationToken === null
          && (boardId !== undefined ? data.targetRole === 'MEMBER' && data.boardTarget?.boardId === boardId && data.boardTarget.role === original.input.targetRole
            : data.targetRole === original.input.targetRole && data.boardTarget == null)
          && typeof data.expiresAt === 'string' && preferences && !!formatUserDateTime(data.expiresAt, preferences)) {
          setAck(data); setError(undefined);
          confirmed.current = { actor: expected, command: original, acknowledgment: data };
          // Keep the confirmed intent reserved until the person explicitly starts
          // another invitation. Reload can safely recover this acknowledgment.
          return;
        }
        const code = (result.body as { code?: unknown } | undefined)?.code;
        if (result.status === 400 && ['invalid_email', 'invalid_invitation_role', 'invalid_invitation_surface'].includes(code as string)) {
          try { sessionStorage.removeItem(storageKey); } catch { setBlocked(true); setError('Unable to clear the rejected request. Please retry with the same details.'); return; }
          currentIntent.current = undefined; setIntent(undefined); setError('Check the invitation email and access role, then try again.'); return;
        }
        if (result.status === 409 && ['idempotency_key_reused', 'idempotency_key_expired'].includes(code as string)) {
          setBlocked(true); setError('This request cannot be retried. Review existing invitations before creating another.'); return;
        }
        setError(result.status === 429 ? 'Too many requests. Please wait before retrying this same invitation.' : 'The invitation could not be confirmed. Retry the same request to recover its acknowledgment.');
      }, controller.signal);
    } catch { if (mounted.current && pending.current === controller && started === epoch.current) {
      if (operationSignal?.aborted) { withdrawAccount(); setLiveActor(undefined); }
      setError('The invitation could not be confirmed. Retry the same request to recover its acknowledgment.');
    } }
    finally { finish(controller); }
  }
  function next() {
    if (busy || !ack || !storageKey) return;
    try { sessionStorage.removeItem(storageKey); }
    catch { setError('Unable to clear the confirmed request. Please reload before creating another.'); return; }
    confirmed.current = undefined;
    setAck(undefined); setIntent(undefined); currentIntent.current = undefined; setInput(empty); setError(undefined);
  }
  const locked = busy || !!intent || blocked || !!ack;
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    {liveNotice && <Typography role="status" aria-live="polite">{liveNotice}</Typography>}
    <Button component={Link} to={boardId !== undefined ? `/app/${organizationId}/boards/${boardId}` : `/app/${organizationId}/members`}>{boardId !== undefined ? 'Back to Board' : 'Organization members'}</Button>
    <Button component={Link} to={boardId !== undefined ? `/app/${organizationId}/boards/${boardId}/invitations` : `/app/${organizationId}/invitations`}>Review issued invitations</Button>
    <Typography component="h1" variant="h4">Create {boardId !== undefined ? 'Board' : 'Organization'} invitation</Typography>
    {boardName && <Typography component="h2" variant="h6">{boardName}</Typography>}
    {error && <Alert severity="error">{error}</Alert>}
    {busy && <CircularProgress aria-label={`Loading ${boardId !== undefined ? 'Board' : 'Organization'} invitation`} />}
    {actorRole === undefined && !denied && <Button disabled={busy} onClick={() => void load()}>Retry permission check</Button>}
    {actorRole !== undefined && !denied && <>
      <Typography>{boardId !== undefined ? 'Invite an existing Organization member to this Board. Organization administrators can also invite people to join the Organization. Recipients must verify their email and explicitly accept.' : 'Internal membership and Portal relationships grant separate access. Recipients must verify their email and explicitly accept the invitation.'}</Typography>
      <Box component="form" onSubmit={event => void create(event)}><Stack spacing={2}>
        <TextField label="Invitation email" type="email" required value={input.email} disabled={locked} slotProps={{ htmlInput: { maxLength: 320 } }} onChange={event => setInput({ ...input, email: event.target.value })} />
        {boardId === undefined && <TextField label="Access surface" select value={input.surface} disabled={locked} onChange={event => {
          const surface = event.target.value as InvitationInput['surface']; setInput({ ...input, surface, targetRole: surface === 'INTERNAL' ? 'MEMBER' : 'OWNER' });
        }}><MenuItem value="INTERNAL">Internal Organization</MenuItem><MenuItem value="PORTAL">Owner Portal</MenuItem></TextField>}
        <TextField label="Invitation role" select value={input.targetRole} disabled={locked} onChange={event => setInput({ ...input, targetRole: event.target.value })}>
          {(boardId !== undefined ? ['MEMBER', 'ADMIN'] : invitationRoles(input.surface)).map(role => <MenuItem key={role} value={role} disabled={input.surface === 'INTERNAL' && role === 'OWNER' && actorRole !== 0}>{roleLabel(role)}</MenuItem>)}
        </TextField>
        <Button type="submit" disabled={busy || blocked || !!ack}>{intent ? 'Retry same invitation' : 'Create invitation'}</Button>
      </Stack></Box>
      {ack && <Paper variant="outlined" sx={{ p: 2, overflowWrap: 'anywhere' }}><Stack spacing={1}>
        <Alert severity="success" role="status">Invitation creation acknowledged.</Alert>
        <Typography>{ack.email}</Typography><Typography>{boardId !== undefined ? 'Board' : ack.surface === 'INTERNAL' ? 'Internal Organization' : 'Owner Portal'}: {roleLabel(ack.boardTarget?.role ?? ack.targetRole)}</Typography>
        <Typography>Expires: {preferences && formatUserDateTime(ack.expiresAt, preferences)}</Typography>
        <Typography>This acknowledges creation of the invitation. Email delivery is not confirmed here. Pending invitations are available to verified recipients in their invitations.</Typography>
        <Button disabled={busy} onClick={next}>Create another invitation</Button>
      </Stack></Paper>}
    </>}
  </Stack></Container>;
}
