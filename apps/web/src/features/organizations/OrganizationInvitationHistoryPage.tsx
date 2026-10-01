import { useEffect, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { formatUserDateTime } from '../auth/userDateTime';
import { invitationRoles, validInvitationKey } from './invitationIntent';

type Row = { id: string; email: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string; createdAt: string;
  expiresAt: string; acceptedAt: string | null; revokedAt: string | null; deliveryState: string | null; boardTarget?: { boardId: string; role: string } | null };
type Page = { items: Row[]; nextCursor: string | null };
type Preferences = { locale: string; timezone: string };
const date = (value: unknown): value is string => typeof value === 'string'
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(value) && Number.isFinite(Date.parse(value));
function page(value: unknown, boardId?: string): value is Page {
  if (!value || typeof value !== 'object') return false;
  const result = value as Page;
  return Array.isArray(result.items) && result.items.length <= 50 && result.items.every(row => row && validInvitationKey(row.id)
    && typeof row.email === 'string' && row.email.length <= 320 && ['INTERNAL', 'PORTAL'].includes(row.surface)
    && invitationRoles(row.surface).includes(row.targetRole)
    && (boardId !== undefined ? row.surface === 'INTERNAL' && row.targetRole === 'MEMBER'
      && row.boardTarget?.boardId === boardId && ['ADMIN', 'MEMBER'].includes(row.boardTarget.role) : row.boardTarget == null)
    && date(row.createdAt) && date(row.expiresAt)
    && (row.acceptedAt === null || date(row.acceptedAt)) && (row.revokedAt === null || date(row.revokedAt))
    && [null, 'PENDING', 'SENT', 'CANCELLED', 'FAILED', 'RETRY_EXHAUSTED'].includes(row.deliveryState))
    && new Set(result.items.map(row => row.id)).size === result.items.length
    && (result.nextCursor === null || validInvitationKey(result.nextCursor) && result.items.length === 50
      && result.nextCursor === result.items.at(-1)?.id);
}
const delivery = (state: string | null) => ({ PENDING: 'Email pending', SENT: 'Email sent',
  CANCELLED: 'Email cancelled', FAILED: 'Email delivery failed', RETRY_EXHAUSTED: 'Email retries exhausted' }[state ?? ''] ?? 'No email delivery recorded');
async function request(path: string, options: RequestInit, controller: AbortController) {
  let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { ...options, signal: controller.signal }).then(async response => ({ status: response.status,
        body: [204, 401].includes(response.status) ? undefined : await response.json().catch(() => undefined) as unknown })),
      new Promise<never>((_, reject) => {
        abort = () => reject(new Error('Invitation history interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => controller.abort(), 15_000);
      }),
    ]);
  } finally { clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort); }
}
export function OrganizationInvitationHistoryPage() {
  const { organizationId } = useParams();
  return <History key={organizationId} organizationId={organizationId ?? ''} />;
}
export function BoardInvitationHistoryPage() {
  const { organizationId, boardId } = useParams();
  return <History key={`${organizationId}:${boardId}`} organizationId={organizationId ?? ''} boardId={boardId ?? ''} />;
}
function History({ organizationId, boardId }: { organizationId: string; boardId?: string }) {
  const root = boardId !== undefined ? `/boards/${encodeURIComponent(boardId)}/invitations` : `/organizations/${encodeURIComponent(organizationId)}/invitations`;
  const [boardName, setBoardName] = useState<string>();
  const [rows, setRows] = useState<Page>(); const [preferences, setPreferences] = useState<Preferences>();
  const [cursor, setCursor] = useState<string | null>(null); const [previous, setPrevious] = useState<(string | null)[]>([]);
  const [selected, setSelected] = useState<Row>(); const [unconfirmed, setUnconfirmed] = useState<string>();
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [busy, setBusy] = useState(false); const [denied, setDenied] = useState(false);
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const recoveryId = useRef<string | undefined>(undefined); const cancel = useRef<HTMLButtonElement>(null);
  const refresh = useRef<HTMLButtonElement>(null);
  useEffect(() => { mounted.current = true; void load(null, []); return () => {
    mounted.current = false; pending.current?.abort(); pending.current = undefined; recoveryId.current = undefined;
  }; }, []); // The keyed component isolates every Organization navigation.
  function begin() { if (pending.current) return; const controller = new AbortController(); pending.current = controller; setBusy(true); return controller; }
  const valid = (controller: AbortController) => mounted.current && pending.current === controller && !controller.signal.aborted;
  function finish(controller: AbortController) { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  function deny(status: number) {
    setBoardName(undefined); setRows(undefined); setSelected(undefined); setPreferences(undefined); setNotice(undefined); setDenied(true);
    recoveryId.current = undefined; setUnconfirmed(undefined);
    setError(status === 401 ? `Sign in again to review ${boardId !== undefined ? 'Board' : 'Organization'} invitations.` : `${boardId !== undefined ? 'Board' : 'Organization'} invitation administration is unavailable.`);
  }
  async function load(next: string | null, history: (string | null)[]) {
    const controller = begin(); if (!controller) return;
    setBoardName(undefined); setError(undefined); setRows(undefined); setSelected(undefined);
    try {
      if (!validInvitationKey(organizationId)) { deny(404); return; }
      const me = await request('/me', {}, controller); if (!valid(controller)) return;
      if ([401, 403, 404].includes(me.status)) { deny(me.status); return; }
      const prefs = me.body as Preferences | undefined;
      if (me.status !== 200 || !prefs || typeof prefs.locale !== 'string' || typeof prefs.timezone !== 'string'
        || !formatUserDateTime('2030-01-01T00:00:00Z', prefs)) throw new Error('Invalid preferences');
      let currentBoardName: string | undefined;
      if (boardId !== undefined) {
        if (!validInvitationKey(boardId)) { deny(404); return; }
        const boardResult = await request(`/boards/${encodeURIComponent(boardId)}`, {}, controller); if (!valid(controller)) return;
        if ([401, 403, 404].includes(boardResult.status)) { deny(boardResult.status); return; }
        const scope = boardResult.body as { board?: { id: string; organizationId: string; lifecycleState: string; name: string }; access?: { canAdminister: boolean } } | undefined;
        if (boardResult.status !== 200 || scope?.board?.id !== boardId || scope.board.organizationId !== organizationId
          || scope.board.lifecycleState !== 'active' || typeof scope.board.name !== 'string' || !scope.board.name.trim()
          || scope.access?.canAdminister !== true) { deny(404); return; }
        currentBoardName = scope.board.name;
      }
      const result = await request(root + (next ? `?after=${encodeURIComponent(next)}` : ''), {}, controller); if (!valid(controller)) return;
      if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
      if (result.status !== 200 || !page(result.body, boardId) || result.body.items.some((row, index, items) =>
        row.id.toLowerCase() <= (index ? items[index - 1].id.toLowerCase() : next?.toLowerCase() ?? '')))
        throw new Error('Invalid history');
      setBoardName(currentBoardName); setRows(result.body); setPreferences(prefs); setCursor(next); setPrevious(history); setDenied(false);
      if (recoveryId.current) {
        const recovered = result.body.items.find(row => row.id === recoveryId.current);
        if (recovered) {
          recoveryId.current = undefined; setUnconfirmed(undefined);
          setNotice(recovered.revokedAt ? 'Invitation revocation confirmed.' : recovered.acceptedAt
            ? 'This invitation was accepted. Existing access is managed separately.' : 'Revocation was not confirmed. Review the current invitation before trying again.');
        } else setError('Revocation is still unconfirmed. Review the remaining invitation pages to locate its current state.');
      }
    } catch { if (mounted.current && pending.current === controller) setError('Unable to confirm invitation history. Please retry.'); }
    finally { finish(controller); }
  }
  async function revoke() {
    if (!selected || unconfirmed) return;
    const target = selected; const controller = begin(); if (!controller) return;
    setError(undefined); setNotice(undefined); let reload = false;
    try {
      const result = await request(`${root}/${target.id}`, { method: 'DELETE' }, controller); if (!valid(controller)) return;
      if ([401, 403].includes(result.status) || boardId !== undefined && (result.body as { code?: string } | undefined)?.code === 'board_not_found') { deny(result.status); return; }
      if (result.status === 204) { setNotice('Invitation revocation confirmed.'); reload = true; }
      else {
        recoveryId.current = target.id; setUnconfirmed(target.id); setBoardName(undefined); setRows(undefined);
        setError('Revocation could not be confirmed. Check the current invitation state before another action.');
      }
    } catch { if (mounted.current && pending.current === controller) {
      recoveryId.current = target.id; setUnconfirmed(target.id); setBoardName(undefined); setRows(undefined);
      setError('Revocation could not be confirmed. Check the current invitation state before another action.');
    } } finally { if (mounted.current) setSelected(undefined); finish(controller); }
    if (reload) await load(cursor, previous);
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={boardId !== undefined ? `/app/${organizationId}/boards/${boardId}` : `/app/${organizationId}/members`}>{boardId !== undefined ? 'Back to Board' : 'Organization members'}</Button>
    <Typography component="h1" variant="h4">{boardId !== undefined ? 'Issued Board invitations' : 'Issued invitations'}</Typography>
    {boardName && <Typography component="h2" variant="h6">{boardName}</Typography>}
    {error && <Alert severity="error">{error}</Alert>}{notice && <Alert severity={notice === 'Invitation revocation confirmed.' ? 'success' : 'info'}>{notice}</Alert>}
    {busy && <CircularProgress aria-label="Loading issued invitations" />}
    {denied ? <Button component={Link} to="/login">Sign in</Button> : <>
      <Stack direction="row" spacing={1}><Button ref={refresh} disabled={busy} onClick={() => void load(cursor, previous)}>{unconfirmed ? 'Check revocation' : 'Refresh invitations'}</Button>
        {rows && !unconfirmed && <Button component={Link} to={boardId !== undefined ? `/app/${organizationId}/boards/${boardId}/invite` : `/app/${organizationId}/invite`}>Create invitation</Button>}</Stack>
      {rows && preferences && <><Typography>Email acknowledgment does not prove inbox delivery or grant access. Revocation prevents acceptance; existing membership is managed separately.</Typography>
        {rows.items.length === 0 && <Typography>No issued invitations on this page.</Typography>}
        {rows.items.map(row => <Paper component="article" variant="outlined" key={row.id} sx={{ p: 2 }}><Stack spacing={1}>
          <Typography component={boardId !== undefined ? "h3" : "h2"} variant="h6">{row.email}</Typography><Typography>{boardId !== undefined ? `Board access: ${row.boardTarget!.role.toLowerCase()}` : <>{row.surface === 'PORTAL' ? 'Owner Portal' : 'Internal Organization'} · {row.targetRole.replaceAll('_', ' ').toLowerCase()}</>}</Typography>
          <Typography>{row.revokedAt ? 'Revoked' : row.acceptedAt ? 'Accepted' : Date.parse(row.expiresAt) <= Date.now() ? 'Expired' : 'Awaiting acceptance'}</Typography>
          <Typography>{delivery(row.deliveryState)}</Typography><Typography>Expires: {formatUserDateTime(row.expiresAt, preferences)}</Typography>
          {!row.revokedAt && !row.acceptedAt && Date.parse(row.expiresAt) > Date.now() && <Button disabled={busy || !!unconfirmed} onClick={() => setSelected(row)}>Revoke invitation for {row.email}</Button>}
        </Stack></Paper>)}
        <Stack direction="row" spacing={1}><Button disabled={busy || previous.length === 0} onClick={() => void load(previous.at(-1) ?? null, previous.slice(0, -1))}>Previous invitations</Button>
          <Button disabled={busy || !rows.nextCursor} onClick={() => void load(rows.nextCursor, [...previous, cursor])}>Next invitations</Button></Stack>
      </>}
    </>}
    <Dialog open={!!selected} onClose={() => { if (!busy) setSelected(undefined); }} aria-labelledby="revoke-invitation-title"
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: () => refresh.current?.focus() } }}>
      <DialogTitle id="revoke-invitation-title">Revoke invitation?</DialogTitle><DialogContent>{boardId !== undefined && <Typography>{boardName}: {selected?.boardTarget?.role.toLowerCase()}</Typography>}<Typography>{selected?.email}</Typography><Typography>This prevents invitation acceptance. It cannot recall email or remove access already granted.</Typography></DialogContent>
      <DialogActions><Button ref={cancel} disabled={busy} onClick={() => setSelected(undefined)}>Cancel</Button><Button disabled={busy} onClick={() => void revoke()}>Confirm revocation</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
