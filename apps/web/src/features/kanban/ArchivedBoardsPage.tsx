import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationInstant, notificationUuid } from '../notifications/notificationInbox';
import { activityEvent, activityResult } from './activityTelemetry';
import { watchOrganizationBoards } from './organizationBoardLive';
import { ownsRecoveryFocus } from './focusRecovery';

type Board = { id: string; organizationId: string; name: string; version: number; archivedAt: string | null };
type Page = { organizationId: string; items: Board[]; nextCursor: string | null };
type Intent = { board: Board; deleting: boolean; key: string; actor: string };
class ChangedArchiveIdentity extends Error {}
function page(value: unknown, org: string, cursor: string | null): Page {
  const p = value as Page | null;
  if (!p || Object.keys(p).sort().join(',') !== 'items,nextCursor,organizationId' || p.organizationId !== org || !Array.isArray(p.items) || p.items.length > 50 || p.items.some((b, i) =>
    !b || Object.keys(b).sort().join(',') !== 'archivedAt,id,name,organizationId,version' || !notificationUuid(b.id) ||
    b.organizationId !== org || typeof b.name !== 'string' || !b.name.trim() || b.name.length > 160 ||
    !Number.isSafeInteger(b.version) || b.version < 1 || b.id.toLowerCase() <= (i ? p.items[i - 1].id.toLowerCase() : cursor?.toLowerCase() ?? '') ||
    b.archivedAt !== null && (typeof b.archivedAt !== 'string' || !notificationInstant(b.archivedAt))) ||
    p.nextCursor !== null && (p.items.length !== 50 || p.nextCursor !== p.items.at(-1)?.id)) throw new Error('Invalid Board archive');
  return p;
}
export function ArchivedBoardsPage() {
  const { organizationId = '' } = useParams();
  return <Archive key={organizationId} org={organizationId} />;
}
function Archive({ org }: { org: string }) {
  const [current, setCurrent] = useState<Page>(); const [reading, setReading] = useState(false); const [ready, setReady] = useState(false);
  const [review, setReview] = useState<Board>(); const [deleting, setDeleting] = useState(false); const [confirmed, setConfirmed] = useState(false);
  const [intent, setIntent] = useState<Intent>(); const [writing, setWriting] = useState(false); const [conflict, setConflict] = useState(false);
  const [notice, setNotice] = useState<string>(); const [history, setHistory] = useState<(string | null)[]>([]);
  const [liveActor, setLiveActor] = useState<string>();
  const [liveNotice, setLiveNotice] = useState<string>();
  const mounted = useRef(false); const actor = useRef<string | undefined>(undefined); const read = useRef<AbortController | undefined>(undefined);
  const write = useRef<AbortController | undefined>(undefined); const position = useRef<{ cursor: string | null; trail: (string | null)[] }>({ cursor: null, trail: [] });
  const reviewEpoch = useRef(0);
  const refresh = useRef<HTMLButtonElement>(null); const queued = useRef(false);
  const queuedKind = useRef<'use' | 'retry' | 'reconnect'>('use');
  const focusRequested = useRef(false); const focusFrame = useRef<number | undefined>(undefined);
  function restoreFocus() {
    focusRequested.current = true; if (!refresh.current || refresh.current.disabled) return;
    if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current);
    focusFrame.current = requestAnimationFrame(() => { focusFrame.current = requestAnimationFrame(() => {
      focusFrame.current = undefined;
      if (mounted.current && focusRequested.current && refresh.current && !refresh.current.disabled) {
        // A closing dialog can finish after the user has already focused the
        // unresolved command's retry. Respect that explicit keyboard choice.
        if (!ownsRecoveryFocus(document.activeElement, refresh.current)) { focusRequested.current = false; return; }
        refresh.current.focus({ preventScroll: true }); focusRequested.current = false;
      }
    }); });
  }
  useEffect(() => { if (!reading && !writing && !review && focusRequested.current) restoreFocus(); }, [reading, writing, review]);
  function retire() { reviewEpoch.current++; write.current?.abort(); write.current = undefined; setWriting(false); setReview(undefined); setIntent(undefined); setConfirmed(false); }
  async function load(cursor = position.current.cursor, trail = position.current.trail, kind: 'use' | 'retry' | 'reconnect' = 'use') {
    if (read.current) { queued.current = true;
      if (kind === 'reconnect' || kind === 'retry' && queuedKind.current !== 'reconnect') queuedKind.current = kind;
      return; }
    if (document.activeElement === refresh.current) focusRequested.current = true;
    const started = performance.now(); activityEvent('archive_board_read', kind);
    const c = new AbortController(); read.current = c; position.current = { cursor, trail }; setHistory(trail); setReading(true); setReady(false);
    try {
      const result = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(before)) throw new ChangedArchiveIdentity();
        if (actor.current && actor.current !== before.id) throw new ChangedArchiveIdentity();
        const directory = page(await workRequest<unknown>(`/organizations/${encodeURIComponent(org)}/archived-boards${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal, headers: { 'X-StrataAI-Expected-Actor': before.id } }), org, cursor);
        const after = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(after)) throw new ChangedArchiveIdentity();
        if (after.id !== before.id) throw new ChangedArchiveIdentity();
        return { directory, actor: after.id };
      }, c.signal);
      if (!mounted.current || read.current !== c) return;
      if (actor.current && actor.current !== result.actor) retire();
      actor.current = result.actor; setLiveActor(result.actor); setCurrent(result.directory); setReady(true);
      setLiveNotice(value => value ? 'Current archived boards checked.' : undefined);
      activityResult('archive_board_read', true, started);
      setNotice(value => value === 'Unable to confirm current Board archive access. Check again before continuing.' ? undefined : value);
    } catch (error) { if (mounted.current && read.current === c) {
      if (!(error instanceof WorkRequestError)) activityEvent('archive_board_read', 'exception');
      activityResult('archive_board_read', false, started);
      setCurrent(undefined); setReady(false);
      setLiveNotice(undefined);
      if (error instanceof ChangedArchiveIdentity || error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) { retire(); actor.current = undefined; setLiveActor(undefined); }
      setNotice('Unable to confirm current Board archive access. Check again before continuing.');
    } } finally { if (mounted.current && read.current === c) {
      read.current = undefined; setReading(false); if (queued.current) {
        queued.current = false; const nextKind = queuedKind.current; queuedKind.current = 'use'; void load(undefined, undefined, nextKind);
      }
    } }
  }
  useEffect(() => {
    mounted.current = true; activityEvent('archive_board_disclosure', 'open'); void load();
    const poll = () => { if (document.visibilityState !== 'hidden') void load(); };
    const recover = () => { if (document.visibilityState !== 'hidden') void load(undefined, undefined, 'reconnect'); };
    const timer = setInterval(poll, 10_000); window.addEventListener('online', recover); document.addEventListener('visibilitychange', recover);
    return () => { mounted.current = false; read.current?.abort(); write.current?.abort(); clearInterval(timer); window.removeEventListener('online', recover); document.removeEventListener('visibilitychange', recover);
      if (focusFrame.current !== undefined) cancelAnimationFrame(focusFrame.current); };
  }, [org]);
  useEffect(() => {
    if (!liveActor) return;
    const recover = (message: string) => {
      if (!mounted.current) return;
      // Fence an older read before a new canonical invalidation. Withdraw the
      // cached directory/review immediately, but retain any original command
      // key in memory for private acknowledgment recovery after re-admission.
      reviewEpoch.current++; read.current?.abort(); read.current = undefined; queued.current = false;
      setReading(false); setReady(false); setCurrent(undefined); setReview(undefined); setConfirmed(false);
      setLiveNotice(message);
      void load(undefined, undefined, 'reconnect');
    };
    return watchOrganizationBoards({ organizationId: org, userId: liveActor,
      invalidate: () => recover('Archived boards changed. Checking current access.'),
      reset: () => recover('Checking current archive access.'),
      unavailable: () => recover('Live updates interrupted. Checking current access.') });
  }, [org, liveActor]);
  const latest = current?.items.find(b => b.id === review?.id);
  const changed = !!review && !intent && (!latest || latest.version !== review.version || latest.name !== review.name || latest.archivedAt !== review.archivedAt);
  async function change() {
    if (write.current || reading || !ready || !review && !intent || !actor.current || !intent && (changed || conflict || deleting && !confirmed)) return;
    const command = intent ?? { board: review!, deleting, key: crypto.randomUUID(), actor: actor.current };
    const recovering = !!intent;
    if (command.actor !== actor.current) { retire(); return; }
    const action = command.deleting ? 'archive_board_delete' : 'archive_board_restore';
    const started = performance.now(); activityEvent(action, intent ? 'retry' : 'use');
    const c = new AbortController(); write.current = c; setWriting(true); setNotice(undefined);
    const epoch = reviewEpoch.current;
    let submitted = false; let mutationReturned = false; let refreshAfter = true;
    try {
      const value = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before) || before.id !== command.actor) throw new ChangedArchiveIdentity();
        if (!mounted.current || write.current !== c) throw new ChangedArchiveIdentity();
        // Live directory recovery retires new consent, but an already submitted
        // original retains its fixed actor/key/body. The server still re-admits
        // that original, and both account checks fence acknowledgment disclosure.
        if (reviewEpoch.current !== epoch && !recovering) throw new Error('Archive review withdrawn');
        submitted = true;
        const receipt = await workRequest<unknown>(command.deleting
        ? `/boards/${command.board.id}?version=${command.board.version}&confirmed=true` : `/boards/${command.board.id}/restore`, {
        method: command.deleting ? 'DELETE' : 'POST', signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key, 'X-StrataAI-Expected-Actor': command.actor },
        ...(command.deleting ? {} : { body: JSON.stringify({ version: command.board.version }) }),
        });
        mutationReturned = true;
        const after = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(after) || after.id !== command.actor) throw new ChangedArchiveIdentity();
        return receipt;
      }, c.signal) as { id?: string; organizationId?: string; name?: string; version?: number; lifecycleState?: string; deletedBy?: string };
      if (!mounted.current || write.current !== c) return;
      if (value?.id !== command.board.id || value.organizationId !== org || value.name !== command.board.name || value.version !== command.board.version + 1 ||
        value.lifecycleState !== (command.deleting ? 'deleted' : 'active') || command.deleting && value.deletedBy !== command.actor) throw new Error('Invalid acknowledgment');
      // A live invalidation can already have closed the review. Successful
      // recovery must return focus even when there is no dialog exit left.
      focusRequested.current = true;
      setIntent(undefined); setReview(undefined); setNotice(command.deleting ? 'Board deletion acknowledged.' : 'Board restore acknowledged.');
      activityResult(action, true, started);
    } catch (error) { if (mounted.current && write.current === c) {
      activityResult(action, false, started);
      if (error instanceof ChangedArchiveIdentity || error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        refreshAfter = false; retire(); actor.current = undefined; setLiveActor(undefined); setCurrent(undefined); setReady(false); setLiveNotice(undefined); setNotice('Board administration is unavailable.');
      }
      else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) { activityEvent(action, 'conflict'); setIntent(undefined); setConflict(true); setNotice('This change could not be applied. Cancel and review the current archive.'); }
      else if (submitted && !mutationReturned) { activityEvent(action, 'exception'); setIntent(command); setNotice('This change is unconfirmed. Retry the same request to recover its acknowledgment.'); }
      else {
        activityEvent(action, 'exception'); refreshAfter = false; setCurrent(undefined); setReady(false); setReview(undefined); setConfirmed(false); setLiveNotice(undefined);
        if (submitted || intent) { setIntent(command); setNotice('This change is unconfirmed. Check current archived boards, then retry the same request to recover its acknowledgment.'); }
        else { setNotice('Unable to confirm the current account. No Board change was sent. Check current archived boards before reviewing again.'); }
      }
    } } finally { if (mounted.current && write.current === c) { write.current = undefined; setWriting(false); if (refreshAfter) void load(); } }
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography component="h2" variant="h4">Archived boards</Typography>
    <Button component={Link} to={`/app/${org}`} disabled={writing || !!intent}>Back to Organization</Button>
    <Typography>Only Boards you currently administer appear here. Restore a Board to use it again.</Typography>
    <Typography role="status" aria-live="polite" aria-atomic="true">{liveNotice}</Typography>
    {notice && !review && <Alert severity="info" role="status">{notice}</Alert>}
    {intent && !review && <Button disabled={reading || writing || !ready} onClick={() => void change()}>Retry this change</Button>}
    <Button ref={refresh} disabled={reading || writing} onClick={() => void load(undefined, undefined, 'retry')}>Check current archived boards</Button>
    {reading && <CircularProgress aria-label="Checking archived Boards" />}
    {current?.items.map(b => <Paper component="article" aria-label={b.name} key={b.id} sx={{ p: 2, overflowWrap: 'anywhere' }}>
      <Typography component="h3" variant="h6">{b.name}</Typography>
      <Button disabled={!ready || reading || writing || !!intent} onClick={() => { activityEvent('archive_board_restore', 'open'); setReview(b); setDeleting(false); setConfirmed(false); setConflict(false); setNotice(undefined); }} aria-label={`Restore ${b.name} board`}>Restore Board</Button>
      <Button color="error" disabled={!ready || reading || writing || !!intent} onClick={() => { activityEvent('archive_board_delete', 'open'); setReview(b); setDeleting(true); setConfirmed(false); setConflict(false); setNotice(undefined); }} aria-label={`Permanently delete ${b.name} board`}>Permanently delete Board</Button>
    </Paper>)}
    {ready && current?.items.length === 0 && <Typography>No administrable archived Boards on this page.</Typography>}
    <Stack direction="row"><Button disabled={!ready || reading || writing || !!intent || !history.length} onClick={() => void load(history.at(-1)!, history.slice(0, -1))}>Previous archived boards</Button>
      <Button disabled={!ready || reading || writing || !!intent || !current?.nextCursor} onClick={() => void load(current!.nextCursor, [...history, position.current.cursor])}>Next archived boards</Button></Stack>
  </Stack><Dialog open={!!review} onClose={() => { if (!writing && !intent) setReview(undefined); }} fullWidth maxWidth="sm" disableRestoreFocus
    slotProps={{ transition: { onExited: restoreFocus } }}>
    <DialogTitle>{deleting ? 'Permanently delete Board' : 'Restore Board'}</DialogTitle><DialogContent>
      <Typography sx={{ overflowWrap: 'anywhere' }}>{deleting ? `Permanently delete ${review?.name}?` : `Restore ${review?.name}?`}</Typography>
      {deleting ? <><Alert severity="warning">This cannot be undone. This Board cannot be restored, and its Lists and Cards become unavailable through it.</Alert>
        <FormControlLabel label="I understand this cannot be undone." control={<Checkbox checked={confirmed} disabled={writing || !!intent} onChange={e => setConfirmed(e.target.checked)} />} /></>
        : <Typography>Restoration makes the Board active again. Its Lists and Cards retain their own lifecycle states.</Typography>}
      {notice && review && <Alert severity="info" role="status">{notice}</Alert>}{(changed || conflict) && !intent && <Alert severity="warning">This review changed. Cancel and review current archive information.</Alert>}
      {intent && <Typography>The original request is unresolved. Retry that same request.</Typography>}
      <Button disabled={reading || writing} onClick={() => void load(undefined, undefined, 'retry')}>Check current archive for this change</Button>
    </DialogContent><DialogActions>{!intent && <Button disabled={writing} onClick={() => setReview(undefined)}>Cancel change</Button>}
      <Button color={deleting ? 'error' : 'primary'} disabled={reading || writing || !ready || !review && !intent || !intent && (changed || conflict || deleting && !confirmed)} onClick={() => void change()}>
        {intent ? 'Retry this change' : deleting ? 'Confirm permanent deletion' : 'Confirm restore'}</Button>
    </DialogActions></Dialog></Container>;
}
