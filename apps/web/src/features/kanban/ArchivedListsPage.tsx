import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchBoard, type LiveStatus } from '../../api/boardLive';
import { validInvitationKey as uuid } from '../organizations/invitationIntent';

type ArchivedList = { id: string; organizationId: string; boardId: string; name: string; rank: string; version: number; lifecycleState: string };
type Entry = { list: ArchivedList; containedCardCount: number };
type ArchivePage = { organizationId: string; boardId: string; items: Entry[]; nextCursor: string | null };
type Restore = { entry: Entry; key: string };
function entry(value: unknown, org: string, board: string): value is Entry {
  const e = value as Entry | undefined; const l = e?.list;
  return !!l && uuid(l.id) && l.organizationId === org && l.boardId === board && l.lifecycleState === 'archived'
    && typeof l.name === 'string' && !!l.name.trim() && l.name.length <= 160 && typeof l.rank === 'string'
    && !!l.rank && Number.isSafeInteger(l.version) && l.version > 0
    && Number.isSafeInteger(e?.containedCardCount) && e!.containedCardCount >= 0;
}
async function request(path: string, init: RequestInit, controller: AbortController) {
  let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { ...init, signal: controller.signal }).then(async r => ({ status: r.status, body: await r.json().catch(() => undefined) as unknown })),
      new Promise<never>((_, reject) => { abort = () => reject(new Error('Archive request interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => controller.abort(), 15_000); }),
    ]);
  } finally { clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort); }
}
export function ArchivedListsPage() {
  const { organizationId = '', boardId = '' } = useParams();
  return <Archive key={`${organizationId}:${boardId}`} org={organizationId} board={boardId} />;
}
function Archive({ org, board }: { org: string; board: string }) {
  const [page, setPage] = useState<ArchivePage>(); const [reading, setReading] = useState(false);
  const [ready, setReady] = useState(false); const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [history, setHistory] = useState<(string | null)[]>([]); const [selected, setSelected] = useState<Entry>();
  const [intent, setIntent] = useState<Restore>(); const [writing, setWriting] = useState(false); const [conflict, setConflict] = useState(false);
  const [subscribed, setSubscribed] = useState(false); const [live, setLive] = useState<LiveStatus>('connecting');
  const [retryRead, setRetryRead] = useState(false);
  const position = useRef<{ cursor: string | null; history: (string | null)[] }>({ cursor: null, history: [] });
  const read = useRef<AbortController | undefined>(undefined); const write = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const refresh = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  function restoreFocus() {
    focusRequested.current = !refresh.current || refresh.current.disabled;
    if (refresh.current && !refresh.current.disabled) refresh.current.focus({ preventScroll: true });
  }
  useEffect(() => { if (!reading && !writing && !selected && focusRequested.current) restoreFocus(); }, [reading, writing, selected]);
  const current = page?.items.find(e => e.list.id === selected?.list.id);
  const changed = !!selected && !intent && (!current || current.list.version !== selected.list.version
    || current.list.name !== selected.list.name || current.list.rank !== selected.list.rank);
  function deny() {
    setPage(undefined); setSelected(undefined); setIntent(undefined); setReady(false); setSubscribed(false);
    setRetryRead(false); setNotice(undefined); setError('Archived List administration is unavailable.');
  }
  async function load(cursor: string | null, trail: (string | null)[]) {
    read.current?.abort(); const c = new AbortController(); read.current = c;
    position.current = { cursor, history: trail }; setHistory(trail); setReading(true); setReady(false); setRetryRead(false); setError(undefined);
    try {
      const result = await request(`/boards/${encodeURIComponent(board)}/archived-lists${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, {}, c);
      if (!mounted.current || read.current !== c || c.signal.aborted) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      const p = result.body as ArchivePage | undefined;
      if (result.status !== 200 || p?.organizationId !== org || p.boardId !== board || !Array.isArray(p.items) || p.items.length > 50
        || !p.items.every((e, i, rows) => entry(e, org, board) && e.list.id.toLowerCase() > (i ? rows[i - 1].list.id.toLowerCase() : cursor?.toLowerCase() ?? ''))
        || (p.nextCursor !== null && (!uuid(p.nextCursor) || p.items.length !== 50 || p.nextCursor !== p.items.at(-1)?.list.id)))
        throw new Error('Invalid archive page');
      setPage(p); setReady(true); setSubscribed(true);
    } catch { if (mounted.current && read.current === c) {
      setPage(undefined); setReady(false); setRetryRead(true); setError('Unable to confirm current archived Lists. Please check again.');
    } } finally { if (mounted.current && read.current === c) { read.current = undefined; setReading(false); } }
  }
  const invalidate = useEffectEvent(() => { void load(position.current.cursor, position.current.history); });
  useEffect(() => {
    mounted.current = true; void load(null, []);
    return () => { mounted.current = false; read.current?.abort(); write.current?.abort(); };
  }, [org, board]);
  useEffect(() => subscribed ? watchBoard({ organizationId: org, boardId: board, invalidate: () => invalidate(), status: setLive }) : undefined,
    [org, board, subscribed]);
  useEffect(() => { if (!retryRead || reading) return; const timer = setTimeout(() => invalidate(), 10_000); return () => clearTimeout(timer); }, [retryRead, reading]);
  async function restore() {
    if (write.current || !selected || !ready || reading || (!intent && (changed || conflict))) return;
    const command = intent ?? { entry: selected, key: crypto.randomUUID() }; const l = command.entry.list;
    const c = new AbortController(); write.current = c; setWriting(true); setNotice(undefined);
    try {
      const result = await request(`/lists/${encodeURIComponent(l.id)}/restore`, { method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ version: l.version }) }, c);
      if (!mounted.current || write.current !== c) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if ([400, 409].includes(result.status)) {
        setIntent(undefined); setConflict(true); setNotice('This restore could not be applied. Check the archive and review the current List.');
      } else {
        const ack = result.body as ArchivedList | undefined;
        if (result.status !== 200 || ack?.id !== l.id || ack.organizationId !== org || ack.boardId !== board
          || ack.name !== l.name || ack.rank !== l.rank || ack.lifecycleState !== 'active' || ack.version !== l.version + 1)
          throw new Error('Unconfirmed restore');
        setIntent(undefined); setSelected(undefined); setNotice('List restore acknowledged. Current archived Lists are being checked.');
      }
    } catch { if (mounted.current && write.current === c) {
      setIntent(command); setNotice('The restore could not be confirmed. Retry the same restore to recover its acknowledgment.');
    } } finally { if (mounted.current && write.current === c) {
      write.current = undefined; setWriting(false); void load(position.current.cursor, position.current.history);
    } }
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography variant="h4" component="h2">Archived lists</Typography>
    <Button component={Link} to={`/app/${org}/boards/${board}`} disabled={writing || !!intent}>Back to Board</Button>
    <Typography>Restoring a List keeps its cards associated with it. Cards already archived remain archived.</Typography>
    <Typography role="status">Archive updates: {live}.</Typography>
    {error && <Alert severity="warning">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}
    <Button ref={refresh} disabled={writing || reading} onClick={() => void load(position.current.cursor, position.current.history)}>Check current archived lists</Button>
    {reading && <CircularProgress aria-label="Checking archived Lists" />}
    {page?.items.map(e => <Paper key={e.list.id} component="article" aria-label={e.list.name} sx={{ p: 2, overflowWrap: 'anywhere' }}>
      <Typography component="h3" variant="h6">{e.list.name}</Typography>
      <Typography>{e.containedCardCount} contained cards</Typography>
      <Button disabled={!ready || reading || writing || !!intent} aria-label={`Restore ${e.list.name} list`} onClick={() => {
        setSelected(e); setConflict(false); setNotice(undefined);
      }}>Restore List</Button>
    </Paper>)}
    {ready && page?.items.length === 0 && <Typography>No archived lists on this page.</Typography>}
    <Stack direction="row" spacing={1}>
      <Button disabled={!ready || reading || writing || !!intent || history.length === 0} onClick={() => void load(history.at(-1)!, history.slice(0, -1))}>Previous archived lists</Button>
      <Button disabled={!ready || reading || writing || !!intent || !page?.nextCursor} onClick={() => void load(page!.nextCursor, [...history, position.current.cursor])}>Next archived lists</Button>
    </Stack>
  </Stack><Dialog open={!!selected} onClose={() => { if (!writing && !intent) setSelected(undefined); }} fullWidth maxWidth="sm"
    disableRestoreFocus slotProps={{ transition: { onExited: restoreFocus } }}>
    <DialogTitle>Restore List</DialogTitle><DialogContent>
      <Typography sx={{ overflowWrap: 'anywhere' }}>Restore {selected?.list.name} with its {selected?.containedCardCount} contained cards?</Typography>
      <Typography>This makes the List active again. Its position and contained card lifecycle states are preserved.</Typography>
      {error && <Alert severity="warning">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}
      {(changed || conflict) && !intent && <Alert severity="warning">This List changed. Cancel this review and check the current archive before another restore.</Alert>}
      {intent && <Alert severity="info">The original restore is unresolved. Retry that same request; it cannot apply twice.</Alert>}
      <Button disabled={writing || reading} onClick={() => void load(position.current.cursor, position.current.history)}>Check current archive for this restore</Button>
    </DialogContent><DialogActions>
      {!intent && <Button disabled={writing} onClick={() => setSelected(undefined)}>Cancel restore</Button>}
      <Button disabled={writing || reading || !ready || (!intent && (changed || conflict))} onClick={() => void restore()}>
        {intent ? 'Retry this restore' : 'Confirm restore'}
      </Button>
    </DialogActions>
  </Dialog></Container>;
}
