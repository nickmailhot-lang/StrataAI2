import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { formatUserDateTime } from '../auth/userDateTime';
import { invitationIntentKey, invitationRoles, readInvitationIntent, saveInvitationIntent, validInvitationKey } from './invitationIntent';
import type { InvitationInput, InvitationIntent } from './invitationIntent';

type Ack = { id: string; organizationId: string; email: string; surface: string; targetRole: string; expiresAt: string; invitationToken: null; boardTarget?: { boardId: string; role: string } | null };
const empty: InvitationInput = { email: '', surface: 'INTERNAL', targetRole: 'MEMBER' };
const roleLabel = (role: string) => ({ MEMBER: 'Member', ADMIN: 'Admin', OWNER: 'Owner', CO_OWNER: 'Co-owner', TENANT: 'Tenant', OCCUPANT: 'Occupant', AUTHORIZED_REPRESENTATIVE: 'Authorized representative', OTHER: 'Other' }[role] ?? role);
async function request(path: string, options: RequestInit, controller: AbortController) {
  let timeout: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { ...options, signal: controller.signal }).then(async response => ({ status: response.status,
        body: response.status === 401 ? undefined : await response.json().catch(() => undefined) as unknown })),
      new Promise<never>((_, reject) => {
        abort = () => reject(new Error('Invitation request interrupted')); controller.signal.addEventListener('abort', abort, { once: true });
        timeout = setTimeout(() => controller.abort(), 15_000);
      }),
    ]);
  } finally { clearTimeout(timeout); if (abort) controller.signal.removeEventListener('abort', abort); }
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
  useEffect(() => {
    mounted.current = true; void load();
    return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; };
    // Organization changes remount the keyed route and fence late results.
  }, []);
  function valid(controller: AbortController) { return mounted.current && pending.current === controller && !controller.signal.aborted; }
  function deny(status: number) {
    setBoardName(undefined); setActorRole(undefined); setInput(empty); setIntent(undefined); currentIntent.current = undefined; setAck(undefined); setPreferences(undefined); setDenied(true);
    setError(`${boardId !== undefined ? 'Board' : 'Organization'} invitations are unavailable to your account.`);
    if (status === 401) navigate('/login', { replace: true });
  }
  function begin() { if (pending.current) return; const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller; }
  function finish(controller: AbortController) { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  async function load() {
    const controller = begin(); if (!controller) return;
    setBoardName(undefined); setActorRole(undefined); setAck(undefined); setInput(empty); setIntent(undefined); currentIntent.current = undefined;
    try {
      const me = await request('/me', {}, controller); if (!valid(controller)) return;
      if ([401, 403, 404].includes(me.status)) { deny(me.status); return; }
      const actor = (me.body as { id?: unknown } | undefined)?.id;
      const profile = me.body as { locale?: unknown; timezone?: unknown } | undefined;
      if (me.status !== 200 || !validInvitationKey(actor) || typeof profile?.locale !== 'string' || typeof profile.timezone !== 'string') throw new Error('Invalid actor');
      const display = { locale: profile.locale, timezone: profile.timezone };
      if (!formatUserDateTime('2026-01-01T00:00:00Z', display)) throw new Error('Invalid preferences');
      let admittedRole: number;
      if (boardId !== undefined) {
        if (!validInvitationKey(boardId)) throw new Error('Invalid Board route');
        const result = await request(`/boards/${encodeURIComponent(boardId)}`, {}, controller); if (!valid(controller)) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        const data = result.body as { board?: { id: string; organizationId: string; name: string; lifecycleState: string }; access?: { canAdminister: boolean } } | undefined;
        if (result.status !== 200 || data?.board?.id !== boardId || data.board.organizationId !== organizationId
          || data.board.lifecycleState !== 'active' || typeof data.board.name !== 'string' || !data.board.name.trim()
          || data.access?.canAdminister !== true) { deny(404); return; }
        setBoardName(data.board.name); admittedRole = 1;
      } else {
        const result = await request(`/organizations/${encodeURIComponent(organizationId)}/members/${actor}`, {}, controller); if (!valid(controller)) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        const data = result.body as { organizationId: string; actorRole: number; member: { userId: string; role: number } } | undefined;
        if (result.status !== 200 || !data || data.organizationId !== organizationId || ![0, 1].includes(data.actorRole)
          || !data.member || data.member.userId !== actor || data.member.role !== data.actorRole) throw new Error('Invalid actor admission');
        admittedRole = data.actorRole;
      }
      const key = invitationIntentKey(actor, organizationId) + (boardId !== undefined ? `:board:${boardId}` : '');
      setStorageKey(key); setActorRole(admittedRole); setDenied(false); setBlocked(false);
      setPreferences(display);
      try {
        const saved = readInvitationIntent(key);
        if (saved && boardId !== undefined && (saved.input.surface !== 'INTERNAL' || !['ADMIN', 'MEMBER'].includes(saved.input.targetRole))) throw new Error('Invalid Board intent');
        if (saved) { setIntent(saved); currentIntent.current = saved; setInput(saved.input); setError('A prior invitation request is awaiting acknowledgment. Retry that same request before starting another.'); }
      } catch { setBlocked(true); setError('The saved invitation request cannot be read. Review existing invitations before creating another request.'); }
    } catch { if (mounted.current && pending.current === controller) setError('Unable to verify current invitation permissions. Please retry.'); }
    finally { finish(controller); }
  }
  async function create(event: React.FormEvent) {
    event.preventDefault(); if (pending.current || actorRole === undefined || blocked || ack || !storageKey) return;
    if (!currentIntent.current && (!input.email.trim() || input.email.trim().length > 320)) { setError('Enter a valid invitation email of at most 320 characters.'); return; }
    let command = currentIntent.current;
    if (!command) {
      command = { key: crypto.randomUUID(), input: { ...input, email: input.email.trim() } };
      try { saveInvitationIntent(storageKey, command); }
      catch { setError('This browser could not retain the invitation request. Allow temporary site data before trying again.'); return; }
      currentIntent.current = command; setIntent(command); setInput(command.input);
    }
    const controller = begin(); if (!controller) return;
    try {
      const result = await request(boardId !== undefined ? `/boards/${encodeURIComponent(boardId)}/invitations` : `/organizations/${encodeURIComponent(organizationId)}/invitations`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify(boardId !== undefined ? { email: command.input.email, role: command.input.targetRole } : command.input),
      }, controller); if (!valid(controller)) return;
      if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
      const data = result.body as Ack | undefined;
      if (result.status === 201 && data && validInvitationKey(data.id) && data.organizationId === organizationId
        && typeof data.email === 'string' && data.email.trim().toUpperCase() === command.input.email.toUpperCase()
        && data.surface === command.input.surface && data.invitationToken === null
        && (boardId !== undefined ? data.targetRole === 'MEMBER' && data.boardTarget?.boardId === boardId && data.boardTarget.role === command.input.targetRole
          : data.targetRole === command.input.targetRole && data.boardTarget == null)
        && typeof data.expiresAt === 'string' && preferences && !!formatUserDateTime(data.expiresAt, preferences)) {
        setAck(data); setError(undefined);
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
    } catch { if (mounted.current && pending.current === controller) setError('The invitation could not be confirmed. Retry the same request to recover its acknowledgment.'); }
    finally { finish(controller); }
  }
  function next() {
    if (busy || !ack || !storageKey) return;
    try { sessionStorage.removeItem(storageKey); }
    catch { setError('Unable to clear the confirmed request. Please reload before creating another.'); return; }
    setAck(undefined); setIntent(undefined); currentIntent.current = undefined; setInput(empty); setError(undefined);
  }
  const locked = busy || !!intent || blocked || !!ack;
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={boardId !== undefined ? `/app/${organizationId}/boards/${boardId}` : `/app/${organizationId}/members`}>{boardId !== undefined ? 'Back to Board' : 'Organization members'}</Button>
    {boardId === undefined && <Button component={Link} to={`/app/${organizationId}/invitations`}>Review issued invitations</Button>}
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
