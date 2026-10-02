import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, Checkbox, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchBoard, type LiveStatus } from '../../api/boardLive';
import { validInvitationKey as uuid } from '../organizations/invitationIntent';

type Card = { id: string; organizationId: string; boardId: string; listId: string; title: string;
  description: null; rank: string; version: number; lifecycleState: string };
type List = { id: string; organizationId: string; boardId: string; name: string; rank: string; version: number; lifecycleState: string };
type Entry = { card: Card; list: List };
type ArchivePage = { organizationId: string; boardId: string; items: Entry[]; nextCursor: string | null; canDelete: boolean };
type Intent = { entry: Entry; key: string; deleting: boolean };
function validEntry(value: unknown, org: string, board: string): value is Entry {
  const e = value as Entry | undefined; const c = e?.card; const l = e?.list;
  return !!c && !!l && uuid(c.id) && uuid(l.id) && c.listId === l.id
    && c.organizationId === org && l.organizationId === org && c.boardId === board && l.boardId === board
    && c.lifecycleState === 'archived' && ['active', 'archived'].includes(l.lifecycleState)
    && typeof c.title === 'string' && !!c.title.trim() && c.title.length <= 500 && c.description === null
    && typeof l.name === 'string' && !!l.name.trim() && l.name.length <= 160
    && typeof c.rank === 'string' && !!c.rank && typeof l.rank === 'string' && !!l.rank
    && Number.isSafeInteger(c.version) && c.version > 0 && Number.isSafeInteger(l.version) && l.version > 0;
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
export function ArchivedCardsPage() {
  const { organizationId = '', boardId = '' } = useParams();
  return <Archive key={`${organizationId}:${boardId}`} org={organizationId} board={boardId} />;
}
function Archive({ org, board }: { org: string; board: string }) {
  const [page, setPage] = useState<ArchivePage>(); const [reading, setReading] = useState(false);
  const [ready, setReady] = useState(false); const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [history, setHistory] = useState<(string | null)[]>([]); const [selected, setSelected] = useState<Entry>();
  const [intent, setIntent] = useState<Intent>(); const [writing, setWriting] = useState(false); const [conflict, setConflict] = useState(false);
  const [deleting, setDeleting] = useState(false); const [confirmed, setConfirmed] = useState(false);
  const [subscribed, setSubscribed] = useState(false); const [live, setLive] = useState<LiveStatus>('connecting'); const [retryRead, setRetryRead] = useState(false);
  const position = useRef<{ cursor: string | null; history: (string | null)[] }>({ cursor: null, history: [] });
  const read = useRef<AbortController | undefined>(undefined); const write = useRef<AbortController | undefined>(undefined);
  const queued = useRef(false); const mounted = useRef(false); const refresh = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  const reviewingDeletion = useRef(false);
  const focusFrame = useRef<number | undefined>(undefined);
  function restoreFocus() {
    focusRequested.current = true;
    if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current);
    if (!refresh.current || refresh.current.disabled) return;
    // Let the exiting MUI focus trap finish before focusing the archive.
    focusFrame.current = requestAnimationFrame(() => {
      focusFrame.current = requestAnimationFrame(() => {
        focusFrame.current = undefined;
        if (!mounted.current || !focusRequested.current || !refresh.current || refresh.current.disabled) return;
        refresh.current.focus({ preventScroll: true }); focusRequested.current = false;
      });
    });
  }
  useEffect(() => { if (!reading && !writing && !selected && focusRequested.current) restoreFocus(); }, [reading, writing, selected]);
  const current = page?.items.find(e => e.card.id === selected?.card.id);
  const changed = !!selected && !intent && (!current || current.card.version !== selected.card.version
    || current.card.title !== selected.card.title || current.card.rank !== selected.card.rank || current.card.listId !== selected.card.listId
    || current.list.version !== selected.list.version || current.list.name !== selected.list.name
    || current.list.lifecycleState !== selected.list.lifecycleState || !deleting && current.list.lifecycleState !== 'active');
  function deny() {
    reviewingDeletion.current = false;
    write.current?.abort(); write.current = undefined; setWriting(false);
    setPage(undefined); setSelected(undefined); setIntent(undefined); setReady(false); setSubscribed(false);
    queued.current = false; setRetryRead(false); setNotice(undefined); setError('Archived Card access is unavailable.');
  }
  async function load(cursor: string | null, trail: (string | null)[]) {
    // Disabling a focused button can move browser focus to the document body.
    // Preserve that return target through queued realtime reads as well.
    if (document.activeElement === refresh.current) focusRequested.current = true;
    read.current?.abort(); queued.current = false; const c = new AbortController(); read.current = c;
    position.current = { cursor, history: trail }; setHistory(trail); setReading(true); setReady(false); setRetryRead(false); setError(undefined);
    try {
      const result = await request(`/boards/${encodeURIComponent(board)}/archived-cards${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, {}, c);
      if (!mounted.current || read.current !== c || c.signal.aborted) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      const p = result.body as ArchivePage | undefined;
      if (result.status !== 200 || p?.organizationId !== org || p.boardId !== board || typeof p.canDelete !== 'boolean' || !Array.isArray(p.items) || p.items.length > 50
        || !p.items.every((e, i, rows) => validEntry(e, org, board) && e.card.id.toLowerCase() > (i ? rows[i - 1].card.id.toLowerCase() : cursor?.toLowerCase() ?? ''))
        || (p.nextCursor !== null && (!uuid(p.nextCursor) || p.items.length !== 50 || p.nextCursor !== p.items.at(-1)?.card.id))) throw new Error('Invalid archive page');
      if (!p.canDelete && reviewingDeletion.current) {
        reviewingDeletion.current = false;
        write.current?.abort(); write.current = undefined; setWriting(false); setIntent(undefined); setSelected(undefined);
        setConfirmed(false); setDeleting(false); setNotice('Permanent Card deletion is unavailable.');
      }
      setPage(p); setReady(true); setSubscribed(true);
    } catch { if (mounted.current && read.current === c) {
      setPage(undefined); setReady(false); setRetryRead(true); setError('Unable to confirm current archived Cards. Please check again.');
    } } finally { if (mounted.current && read.current === c) {
      read.current = undefined; setReading(false);
      if (queued.current) void load(position.current.cursor, position.current.history);
    } }
  }
  const invalidate = useEffectEvent(() => {
    if (read.current) queued.current = true; else void load(position.current.cursor, position.current.history);
  });
  useEffect(() => { mounted.current = true; void load(null, []);
    return () => { mounted.current = false; read.current?.abort(); write.current?.abort();
      if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current); }; }, [org, board]);
  useEffect(() => subscribed ? watchBoard({ organizationId: org, boardId: board, invalidate: () => invalidate(), status: setLive }) : undefined, [org, board, subscribed]);
  useEffect(() => { if (!retryRead || reading) return; const timer = setTimeout(() => invalidate(), 10_000); return () => clearTimeout(timer); }, [retryRead, reading]);
  async function change() {
    if (write.current || !selected || !ready || reading || deleting && !page?.canDelete
      || (!intent && (changed || conflict || deleting && !confirmed || !deleting && selected.list.lifecycleState !== 'active'))) return;
    const command = intent ?? { entry: selected, key: crypto.randomUUID(), deleting }; const card = command.entry.card;
    const c = new AbortController(); write.current = c; setWriting(true); setNotice(undefined);
    try {
      const path = command.deleting ? `/cards/${encodeURIComponent(card.id)}?version=${card.version}&confirmed=true` : `/cards/${encodeURIComponent(card.id)}/restore`;
      const result = await request(path, { method: command.deleting ? 'DELETE' : 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
        ...(command.deleting ? {} : { body: JSON.stringify({ version: card.version }) }) }, c);
      if (!mounted.current || write.current !== c) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if ([400, 409].includes(result.status)) {
        setIntent(undefined); setConflict(true); setNotice(command.deleting
          ? 'This deletion could not be applied. Check the archive and review the current Card and parent List.'
          : 'This restore could not be applied. Check the archive and review the current Card and parent List.');
      } else {
        const ack = result.body as Card | undefined;
        if (result.status !== 200 || ack?.id !== card.id || ack.organizationId !== org || ack.boardId !== board
          || ack.listId !== card.listId || ack.title !== card.title || ack.rank !== card.rank || ack.lifecycleState !== (command.deleting ? 'deleted' : 'active')
          || ack.version !== card.version + 1) throw new Error('Unconfirmed restore');
        reviewingDeletion.current = false; setIntent(undefined); setSelected(undefined); setNotice(command.deleting
          ? 'Card deletion acknowledged. Current archived Cards are being checked.' : 'Card restore acknowledged. Current archived Cards are being checked.');
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
    <Typography variant="h4" component="h2">Archived cards</Typography>
    <Button component={Link} to={`/app/${org}/boards/${board}`} disabled={writing || !!intent}>Back to Board</Button>
    <Typography>Restore a Card to its current List. An archived parent List must be restored first.</Typography>
    <Typography role="status">Archive updates: {live}.</Typography>
    {error && <Alert severity="warning">{error}</Alert>}{!selected && notice && <Alert severity="info">{notice}</Alert>}
    <Button ref={refresh} disabled={writing || reading} onClick={() => void load(position.current.cursor, position.current.history)}>Check current archived cards</Button>
    {reading && <CircularProgress aria-label="Checking archived Cards" />}
    {page?.items.map(e => <Paper key={e.card.id} component="article" aria-label={e.card.title} sx={{ p: 2, overflowWrap: 'anywhere' }}>
      <Typography component="h3" variant="h6">{e.card.title}</Typography><Typography>List: {e.list.name}</Typography>
      {e.list.lifecycleState === 'archived' && <Typography>Restore the parent List before restoring this Card.</Typography>}
      <Button disabled={!ready || reading || writing || !!intent || e.list.lifecycleState !== 'active'} aria-label={`Restore ${e.card.title} card`}
        onClick={() => { reviewingDeletion.current = false; setSelected(e); setDeleting(false); setConfirmed(false); setConflict(false); setNotice(undefined); }}>Restore Card</Button>
      {page.canDelete && <Button color="error" disabled={!ready || reading || writing || !!intent} aria-label={`Permanently delete ${e.card.title} card`}
        onClick={() => { reviewingDeletion.current = true; setSelected(e); setDeleting(true); setConfirmed(false); setConflict(false); setNotice(undefined); }}>Permanently delete Card</Button>}
    </Paper>)}
    {ready && page?.items.length === 0 && <Typography>No archived cards on this page.</Typography>}
    <Stack direction="row" spacing={1}>
      <Button disabled={!ready || reading || writing || !!intent || history.length === 0} onClick={() => void load(history.at(-1)!, history.slice(0, -1))}>Previous archived cards</Button>
      <Button disabled={!ready || reading || writing || !!intent || !page?.nextCursor} onClick={() => void load(page!.nextCursor, [...history, position.current.cursor])}>Next archived cards</Button>
    </Stack>
  </Stack><Dialog open={!!selected} onClose={() => { if (!writing && !intent) { reviewingDeletion.current = false; setSelected(undefined); } }} fullWidth maxWidth="sm"
    disableRestoreFocus slotProps={{ transition: { onExited: restoreFocus } }}>
    {selected && <><DialogTitle>{deleting ? 'Permanently delete Card' : 'Restore Card'}</DialogTitle><DialogContent>
      {deleting ? <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Permanently delete {selected?.card.title} from {selected?.list.name}?</Typography>
        <Alert severity="warning">This cannot be undone. This Card can no longer be restored or used.</Alert>
        <FormControlLabel control={<Checkbox checked={confirmed} disabled={writing || !!intent} onChange={event => setConfirmed(event.target.checked)} />}
          label="I understand this cannot be undone." />
      </> : <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Restore {selected?.card.title} to {selected?.list.name}?</Typography>
        <Typography>This makes the Card active again in its existing List and position.</Typography>
      </>}
      {selected && error && <Alert severity="warning">{error}</Alert>}{selected && notice && <Alert severity="info">{notice}</Alert>}
      {(changed || conflict) && !intent && <Alert severity="warning">{deleting
        ? 'This Card or its parent List changed. Cancel this review and check the current archive before deletion.'
        : 'This Card or its parent List changed. Cancel this review and check the current archive before another restore.'}</Alert>}
      {intent && <Alert severity="info">{deleting ? 'The original deletion is unresolved. Retry that same request; it cannot apply twice.' : 'The original restore is unresolved. Retry that same request; it cannot apply twice.'}</Alert>}
      <Button disabled={writing || reading} onClick={() => void load(position.current.cursor, position.current.history)}>{deleting ? 'Check current archive for this deletion' : 'Check current archive for this restore'}</Button>
    </DialogContent><DialogActions>
      {!intent && <Button disabled={writing} onClick={() => { reviewingDeletion.current = false; setSelected(undefined); }}>{deleting ? 'Cancel deletion' : 'Cancel restore'}</Button>}
      <Button color={deleting ? 'error' : 'primary'} disabled={writing || reading || !ready || deleting && !page?.canDelete || (!intent && (changed || conflict || deleting && !confirmed))} onClick={() => void change()}>
        {deleting ? intent ? 'Retry this deletion' : 'Confirm permanent deletion' : intent ? 'Retry this restore' : 'Confirm restore'}
      </Button>
    </DialogActions></>}
  </Dialog></Container>;
}
