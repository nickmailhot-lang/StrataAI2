import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchBoard, type LiveStatus } from '../../api/boardLive';
import { validInvitationKey } from '../organizations/invitationIntent';

type Member = { boardId: string; userId: string; role: 'ADMIN' | 'MEMBER'; active: boolean; version: number;
  displayName: string | null; email: string | null; organizationMemberActive: boolean };
type Change = { member: Member; role?: 'ADMIN' | 'MEMBER' };
function member(value: unknown, boardId: string): value is Member {
  const m = value as Member | undefined;
  return !!m && m.boardId === boardId && validInvitationKey(m.userId) && m.active === true
    && ['ADMIN', 'MEMBER'].includes(m.role) && Number.isSafeInteger(m.version) && m.version > 0
    && (m.organizationMemberActive === true ? typeof m.displayName === 'string' && !!m.displayName.trim()
      && typeof m.email === 'string' && !!m.email.trim() && m.email.length <= 320
      : m.organizationMemberActive === false && m.displayName === null && m.email === null);
}
async function request(path: string, options: RequestInit, controller: AbortController) {
  let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { ...options, signal: controller.signal }).then(async r => ({ status: r.status,
        cursor: r.headers.get('X-StrataAI-Next-Cursor'), body: [204, 401].includes(r.status) ? undefined : await r.json().catch(() => undefined) as unknown })),
      new Promise<never>((_, reject) => { abort = () => reject(new Error('Member request interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => controller.abort(), 15_000); }),
    ]);
  } finally { clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort); }
}
export function BoardMembersPage() {
  const { organizationId = '', boardId = '' } = useParams();
  return <Members key={`${organizationId}:${boardId}`} org={organizationId} id={boardId} />;
}
function Members({ org, id }: { org: string; id: string }) {
  const [name, setName] = useState<string>(); const [rows, setRows] = useState<Member[]>();
  const [cursor, setCursor] = useState<string | null>(null); const [next, setNext] = useState<string | null>(null);
  const [previous, setPrevious] = useState<(string | null)[]>([]); const [selected, setSelected] = useState<Change>();
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const queued = useRef(false); const position = useRef<{ cursor: string | null; previous: (string | null)[] }>({ cursor: null, previous: [] });
  const [subscribed, setSubscribed] = useState(false); const [liveStatus, setLiveStatus] = useState<LiveStatus>('connecting');
  const [retryRead, setRetryRead] = useState(false);
  const mutationWarning = useRef<string | undefined>(undefined);
  const cancel = useRef<HTMLButtonElement>(null); const refresh = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  const restoreFocus = () => {
    focusRequested.current = !refresh.current || refresh.current.disabled;
    if (refresh.current && !refresh.current.disabled) refresh.current.focus();
  };
  useEffect(() => { if (!busy && !selected && focusRequested.current) restoreFocus(); }, [busy, selected]);
  const root = `/boards/${encodeURIComponent(id)}`;
  const clear = () => { setName(undefined); setRows(undefined); setSelected(undefined); setNext(null); };
  const valid = (c: AbortController) => mounted.current && pending.current === c && !c.signal.aborted;
  function begin(preserveError = false) {
    if (pending.current) return;
    const c = new AbortController(); pending.current = c; setBusy(true);
    if (!preserveError) mutationWarning.current = undefined;
    setError(mutationWarning.current); return c;
  }
  function warn(message: string) { mutationWarning.current = message; setError(message); }
  function finish(c: AbortController, drain = true) {
    if (mounted.current && pending.current === c) {
      pending.current = undefined; setBusy(false);
      const refreshQueued = queued.current; queued.current = false;
      if (drain && refreshQueued) queueMicrotask(() => {
        if (mounted.current && !pending.current) void load(position.current.cursor, position.current.previous, true);
      });
    }
  }
  function deny() { clear(); mutationWarning.current = undefined; queued.current = false; setSubscribed(false); setRetryRead(false); setNotice(undefined); setError('Board member administration is unavailable.'); }
  const invalidate = useEffectEvent(() => {
    if (pending.current) { queued.current = true; return; }
    setNotice('Board membership changed. Current permissions and members are being checked.');
    void load(position.current.cursor, position.current.previous, true);
  });
  useEffect(() => {
    if (!subscribed) return;
    return watchBoard({ organizationId: org, boardId: id, invalidate: () => invalidate(), status: setLiveStatus });
  }, [org, id, subscribed]);
  const retryLatest = useEffectEvent(() => { void load(position.current.cursor, position.current.previous, true); });
  useEffect(() => {
    if (!retryRead || busy) return;
    const timer = setTimeout(() => retryLatest(), 10_000);
    return () => clearTimeout(timer);
  }, [retryRead, busy]);
  async function load(after: string | null, history: (string | null)[], preserveError = false) {
    const c = begin(preserveError); if (!c) return; setRetryRead(false); position.current = { cursor: after, previous: history }; clear();
    try {
      if (!validInvitationKey(org) || !validInvitationKey(id)) { deny(); return; }
      const result = await request(root, {}, c); if (!valid(c)) return;
      const scope = result.body as { board?: { id: string; organizationId: string; name: string; lifecycleState: string }; access?: { canAdminister: boolean } } | undefined;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if (result.status !== 200) throw new Error('Board unavailable');
      if (scope?.board?.id !== id || scope.board.organizationId !== org || scope.board.lifecycleState !== 'active'
        || typeof scope.board.name !== 'string' || !scope.board.name.trim() || scope.access?.canAdminister !== true) { deny(); return; }
      const page = await request(`${root}/members${after ? `?after=${encodeURIComponent(after)}` : ''}`, {}, c); if (!valid(c)) return;
      if ([401, 403, 404].includes(page.status)) { deny(); return; }
      if (page.status !== 200 || !Array.isArray(page.body) || page.body.length > 50
        || !page.body.every((m, index, items) => member(m, id) && m.userId.toLowerCase() > (index ? items[index - 1].userId.toLowerCase() : after?.toLowerCase() ?? ''))
        || (page.cursor !== null && (!validInvitationKey(page.cursor) || page.body.length !== 50 || page.cursor !== page.body.at(-1)?.userId)))
        throw new Error('Invalid directory');
      setName(scope.board.name); setRows(page.body); setCursor(after); setPrevious(history); setNext(page.cursor); setSubscribed(true);
    } catch { if (mounted.current && pending.current === c) { clear(); setRetryRead(true); setError('Unable to confirm current Board members. Please check again.'); } }
    finally { finish(c); }
  }
  async function change() {
    if (!selected) return; const target = selected; const c = begin(); if (!c) return; setNotice(undefined); let reload = false;
    try {
      const result = await request(`${root}/members/${target.member.userId}`, { method: target.role ? 'PATCH' : 'DELETE',
        headers: { 'Content-Type': 'application/json', 'If-Match': `"${target.member.version}"`, 'Idempotency-Key': crypto.randomUUID() },
        ...(target.role ? { body: JSON.stringify({ role: target.role }) } : {}) }, c);
      if (!valid(c)) return;
      if ([401, 403].includes(result.status) || (result.body as { code?: string } | undefined)?.code === 'board_not_found') { deny(); return; }
      const ack = result.body as Member | undefined;
      if (target.role ? result.status === 200 && ack?.boardId === id && ack.userId === target.member.userId && ack.active === true
        && ack.role === target.role && Number.isSafeInteger(ack.version) && ack.version > target.member.version : result.status === 204) {
        setNotice('Member change acknowledged. Review the current directory.'); reload = true;
      } else {
        const code = (result.body as { code?: string } | undefined)?.code; clear();
        warn(code === 'sole_board_admin' ? 'The last Board administrator cannot be removed or demoted by this account. Check current members.'
          : code === 'version_conflict' ? 'This membership changed. Check current members and review a new action.'
          : 'The member change could not be confirmed. Check current members before another action.');
      }
    } catch { if (mounted.current && pending.current === c) { clear(); warn('The member change could not be confirmed. Check current members before another action.'); } }
    finally { if (mounted.current) setSelected(undefined); finish(c, false); }
    if (reload) await load(cursor, previous);
  }
  useEffect(() => { mounted.current = true; void load(null, []); return () => { mounted.current = false; pending.current?.abort(); }; }, []);
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={`/app/${org}/boards/${id}`}>Back to Board</Button>
    <Typography component="h1" variant="h4">Board members</Typography>{name && <Typography component="h2" variant="h6">{name}</Typography>}
    {error && <Alert severity="error">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}
    {subscribed && <Typography role="status">{liveStatus === 'live' ? 'Live member updates connected.' : 'Member updates are reconnecting or checking periodically.'}</Typography>}
    {busy && <CircularProgress aria-label="Loading Board members" />}
    <Button ref={refresh} disabled={busy} onClick={() => { setNotice(undefined); void load(position.current.cursor, position.current.previous); }}>Check current members</Button>
    {rows && <><Button component={Link} to={`/app/${org}/boards/${id}/invite`}>Invite to Board</Button>
      <Typography>Board roles and Organization roles grant separate access. Changes require current administrative permission.</Typography>
      {rows.length === 0 && <Typography>No active Board memberships on this page.</Typography>}
      {rows.map(m => <Paper component="article" variant="outlined" key={m.userId} aria-labelledby={`member-name-${m.userId}`} aria-describedby={`member-profile-${m.userId} member-role-${m.userId}`} sx={{ p: 2, overflowWrap: 'anywhere' }}><Stack spacing={1}>
        <Typography id={`member-name-${m.userId}`} component="h3" variant="h6">{m.displayName ?? 'Former Organization member'}</Typography>
        <Typography id={`member-profile-${m.userId}`}>{m.email ?? `Member reference: ${m.userId}`}</Typography><Typography id={`member-role-${m.userId}`}>Board access: {m.role.toLowerCase()}</Typography>
        {!m.organizationMemberActive && <Typography>Organization membership is inactive. Profile details are unavailable and this Board membership grants no edit access.</Typography>}
        <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          {m.organizationMemberActive && <Button aria-describedby={`member-profile-${m.userId} member-role-${m.userId}`} disabled={busy} onClick={() => setSelected({ member: m, role: m.role === 'ADMIN' ? 'MEMBER' : 'ADMIN' })}>{m.role === 'ADMIN' ? 'Make member' : 'Make administrator'}: {m.displayName}</Button>}
          <Button aria-describedby={`member-profile-${m.userId} member-role-${m.userId}`} disabled={busy} onClick={() => setSelected({ member: m })}>Remove from Board: {m.displayName ?? m.userId}</Button>
        </Stack></Stack></Paper>)}
      <Stack direction="row" spacing={1}><Button disabled={busy || previous.length === 0} onClick={() => void load(previous.at(-1) ?? null, previous.slice(0, -1))}>Previous members</Button>
        <Button disabled={busy || !next} onClick={() => void load(next, [...previous, cursor])}>Next members</Button></Stack></>}
    <Dialog open={!!selected} onClose={() => { if (!busy) setSelected(undefined); }} aria-labelledby="member-change-title"
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: restoreFocus } }}>
      <DialogTitle id="member-change-title">{selected?.role ? 'Change Board role?' : 'Remove Board membership?'}</DialogTitle>
      <DialogContent><Typography>{name}</Typography><Typography>{selected?.member.displayName ?? selected?.member.userId}</Typography><Typography>{selected?.member.email}</Typography>
        <Typography>{selected?.role ? `Board access will change from ${selected.member.role.toLowerCase()} to ${selected.role.toLowerCase()}.` : 'This removes Board membership. Organization membership and read access through visibility are managed separately.'}</Typography></DialogContent>
      <DialogActions><Button ref={cancel} disabled={busy} onClick={() => setSelected(undefined)}>Cancel</Button><Button disabled={busy} onClick={() => void change()}>Confirm member change</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
