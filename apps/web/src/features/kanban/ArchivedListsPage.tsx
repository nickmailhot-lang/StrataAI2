import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, Checkbox, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchBoard, type LiveStatus } from '../../api/boardLive';
import { validInvitationKey as uuid } from '../organizations/invitationIntent';

type ArchivedList = { id: string; organizationId: string; boardId: string; name: string; rank: string; version: number; lifecycleState: string };
type Entry = { list: ArchivedList; containedCardCount: number };
type ArchivePage = { organizationId: string; boardId: string; items: Entry[]; nextCursor: string | null };
type LifecycleIntent = { entry: Entry; key: string; deleting: boolean };
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
  const [intent, setIntent] = useState<LifecycleIntent>(); const [writing, setWriting] = useState(false); const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false); const [confirmed, setConfirmed] = useState(false);
  const [subscribed, setSubscribed] = useState(false); const [live, setLive] = useState<LiveStatus>('connecting');
  const [retryRead, setRetryRead] = useState(false);
  const position = useRef<{ cursor: string | null; history: (string | null)[] }>({ cursor: null, history: [] });
  const read = useRef<AbortController | undefined>(undefined); const write = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const refresh = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  const focusFrame = useRef<number | undefined>(undefined);
  function restoreFocus() {
    focusRequested.current = true;
    if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current);
    if (!refresh.current || refresh.current.disabled) return;
    // Restore after the exiting MUI focus trap, then preserve that return target
    // if Worker delivery immediately causes another background archive read.
    focusFrame.current = requestAnimationFrame(() => {
      focusFrame.current = requestAnimationFrame(() => {
        focusFrame.current = undefined;
        if (!mounted.current || !focusRequested.current || !refresh.current || refresh.current.disabled) return;
        refresh.current.focus({ preventScroll: true }); focusRequested.current = false;
      });
    });
  }
  useEffect(() => { if (!reading && !writing && !selected && focusRequested.current) restoreFocus(); }, [reading, writing, selected]);
  const current = page?.items.find(e => e.list.id === selected?.list.id);
  const changed = !!selected && !intent && (!current || current.list.version !== selected.list.version
    || current.list.name !== selected.list.name || current.list.rank !== selected.list.rank || current.containedCardCount !== selected.containedCardCount);
  function deny() {
    setPage(undefined); setSelected(undefined); setIntent(undefined); setReady(false); setSubscribed(false);
    setRetryRead(false); setNotice(undefined); setError('Archived List administration is unavailable.');
  }
  async function load(cursor: string | null, trail: (string | null)[]) {
    if (document.activeElement === refresh.current) focusRequested.current = true;
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
    return () => { mounted.current = false; read.current?.abort(); write.current?.abort();
      if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current); };
  }, [org, board]);
  useEffect(() => subscribed ? watchBoard({ organizationId: org, boardId: board, invalidate: () => invalidate(), status: setLive }) : undefined,
    [org, board, subscribed]);
  useEffect(() => { if (!retryRead || reading) return; const timer = setTimeout(() => invalidate(), 10_000); return () => clearTimeout(timer); }, [retryRead, reading]);
  async function change() {
    if (write.current || !selected || !ready || reading || (!intent && (changed || conflict || deleting && !confirmed))) return;
    const command = intent ?? { entry: selected, key: crypto.randomUUID(), deleting }; const l = command.entry.list;
    const c = new AbortController(); write.current = c; setWriting(true); setNotice(undefined);
    try {
      const path = command.deleting ? `/lists/${encodeURIComponent(l.id)}?version=${l.version}&confirmed=true&containedCardCount=${command.entry.containedCardCount}`
        : `/lists/${encodeURIComponent(l.id)}/restore`;
      const result = await request(path, { method: command.deleting ? 'DELETE' : 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
        ...(command.deleting ? {} : { body: JSON.stringify({ version: l.version }) }) }, c);
      if (!mounted.current || write.current !== c) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if ([400, 409].includes(result.status)) {
        setIntent(undefined); setConflict(true); setNotice(command.deleting
          ? 'This deletion could not be applied. Check the archive and review the current List and card impact.'
          : 'This restore could not be applied. Check the archive and review the current List.');
      } else {
        const ack = result.body as ArchivedList | undefined;
        if (result.status !== 200 || ack?.id !== l.id || ack.organizationId !== org || ack.boardId !== board
          || ack.name !== l.name || ack.rank !== l.rank || ack.lifecycleState !== (command.deleting ? 'deleted' : 'active') || ack.version !== l.version + 1)
          throw new Error('Unconfirmed lifecycle change');
        setIntent(undefined); setSelected(undefined); setNotice(command.deleting
          ? 'List deletion acknowledged. Current archived Lists are being checked.'
          : 'List restore acknowledged. Current archived Lists are being checked.');
      }
    } catch { if (mounted.current && write.current === c) {
      setIntent(command); setNotice(command.deleting
        ? 'The deletion could not be confirmed. Retry the same deletion to recover its acknowledgment.'
        : 'The restore could not be confirmed. Retry the same restore to recover its acknowledgment.');
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
        setSelected(e); setDeleting(false); setConfirmed(false); setConflict(false); setNotice(undefined);
      }}>Restore List</Button>
      <Button color="error" disabled={!ready || reading || writing || !!intent} aria-label={`Permanently delete ${e.list.name} list`} onClick={() => {
        setSelected(e); setDeleting(true); setConfirmed(false); setConflict(false); setNotice(undefined);
      }}>Permanently delete List</Button>
    </Paper>)}
    {ready && page?.items.length === 0 && <Typography>No archived lists on this page.</Typography>}
    <Stack direction="row" spacing={1}>
      <Button disabled={!ready || reading || writing || !!intent || history.length === 0} onClick={() => void load(history.at(-1)!, history.slice(0, -1))}>Previous archived lists</Button>
      <Button disabled={!ready || reading || writing || !!intent || !page?.nextCursor} onClick={() => void load(page!.nextCursor, [...history, position.current.cursor])}>Next archived lists</Button>
    </Stack>
  </Stack><Dialog open={!!selected} onClose={() => { if (!writing && !intent) setSelected(undefined); }} fullWidth maxWidth="sm"
    disableRestoreFocus slotProps={{ transition: { onExited: restoreFocus } }}>
    <DialogTitle>{deleting ? 'Permanently delete List' : 'Restore List'}</DialogTitle><DialogContent>
      {deleting ? <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Permanently delete {selected?.list.name} and make its {selected?.containedCardCount} contained cards unavailable?</Typography>
        <Alert severity="warning">This cannot be undone. This List cannot be restored, and its contained cards can no longer be used through it.</Alert>
        <FormControlLabel control={<Checkbox checked={confirmed} disabled={writing || !!intent} onChange={event => setConfirmed(event.target.checked)} />}
          label="I understand this cannot be undone." />
      </> : <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Restore {selected?.list.name} with its {selected?.containedCardCount} contained cards?</Typography>
        <Typography>This makes the List active again. Its position and contained card lifecycle states are preserved.</Typography>
      </>}
      {error && <Alert severity="warning">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}
      {(changed || conflict) && !intent && <Alert severity="warning">{deleting
        ? 'This List or card impact changed. Cancel this review and check the current archive before deletion.'
        : 'This List changed. Cancel this review and check the current archive before another restore.'}</Alert>}
      {intent && <Alert severity="info">{deleting ? 'The original deletion is unresolved. Retry that same request; it cannot apply twice.' : 'The original restore is unresolved. Retry that same request; it cannot apply twice.'}</Alert>}
      <Button disabled={writing || reading} onClick={() => void load(position.current.cursor, position.current.history)}>{deleting ? 'Check current archive for this deletion' : 'Check current archive for this restore'}</Button>
    </DialogContent><DialogActions>
      {!intent && <Button disabled={writing} onClick={() => setSelected(undefined)}>{deleting ? 'Cancel deletion' : 'Cancel restore'}</Button>}
      <Button color={deleting ? 'error' : 'primary'} disabled={writing || reading || !ready || (!intent && (changed || conflict || deleting && !confirmed))} onClick={() => void change()}>
        {deleting ? intent ? 'Retry this deletion' : 'Confirm permanent deletion' : intent ? 'Retry this restore' : 'Confirm restore'}
      </Button>
    </DialogActions>
  </Dialog></Container>;
}
