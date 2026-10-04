import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';
import { isNotificationProfile, notificationInstant, notificationUuid } from '../notifications/notificationInbox';
import { boardColors } from './boardBackground';
import { activityEvent, activityResult } from './activityTelemetry';
type Review = { id: string; organizationId: string; name: string; description: string | null;
  version: number; backgroundType: 'COLOR'; backgroundValue: string | null; userId: string };
type Intent = { review: Review; name: string; key: string };
type Copy = { id: string; userId: string; organizationId: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onBusyChange: (busy: boolean) => void;
  onRecoveryChange: (busy: boolean) => void; onRefresh: () => void; onReturnFocus: () => void };
class ChangedCopyAccount extends Error {}
function readReview(input: unknown, scope: { id: string; organizationId: string }, userId: string): Review {
  const value = input as BoardSnapshot | null; const b = value?.board;
  if (!b || !notificationUuid(scope.id) || !notificationUuid(scope.organizationId) || b.id !== scope.id || b.organizationId !== scope.organizationId || value?.access?.canView !== true
    || value.access.canEdit !== true || b.lifecycleState !== 'active' || !Number.isSafeInteger(b.version) || Number(b.version) < 1
    || typeof b.name !== 'string' || !b.name.trim() || b.name.length > 160 || !(b.description === null || typeof b.description === 'string')
    || b.backgroundType !== 'COLOR' || b.backgroundValue !== null && !boardColors.includes(b.backgroundValue as typeof boardColors[number]))
    throw new Error('Unavailable copy review');
  return { id: b.id, organizationId: b.organizationId, name: b.name, description: b.description,
    version: b.version!, backgroundType: 'COLOR', backgroundValue: b.backgroundValue!, userId };
}
function acknowledgment(input: unknown, command: Intent): Copy {
  const b = input as Record<string, unknown> | null;
  if (!b || !notificationUuid(b.id) || b.id === command.review.id || b.organizationId !== command.review.organizationId
    || b.version !== 1 || b.lifecycleState !== 'active' || b.visibility !== 'PRIVATE' || b.name !== command.name
    || b.description !== command.review.description || b.backgroundType !== command.review.backgroundType
    || b.backgroundValue !== command.review.backgroundValue
    || notificationInstant(b.createdAt).ticks !== notificationInstant(b.updatedAt).ticks) throw new Error('Unconfirmed copy');
  return { id: b.id, organizationId: command.review.organizationId, userId: command.review.userId };
}
export function BoardCopyControl(props: Props) {
  return <CopyDialog key={props.snapshot.board.organizationId + '/' + props.snapshot.board.id} {...props} />;
}
function CopyDialog(props: Props) {
  const callbacks = useRef(props); callbacks.current = props;
  const board = props.snapshot.board; const admitted = props.snapshot.access.canView && props.snapshot.access.canEdit && board.lifecycleState === 'active';
  const available = admitted && notificationUuid(board.id) && notificationUuid(board.organizationId) && board.backgroundType === 'COLOR'
    && (board.backgroundValue === null || boardColors.includes(board.backgroundValue as typeof boardColors[number]));
  const [open, setOpen] = useState(false); const [review, setReview] = useState<Review>(); const [name, setName] = useState('');
  const [intent, setIntent] = useState<Intent>(); const [copy, setCopy] = useState<Copy>(); const [verified, setVerified] = useState<string>();
  const [busy, setBusy] = useState(false); const [conflict, setConflict] = useState(false); const [notice, setNotice] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined); const entry = useRef<HTMLButtonElement>(null);
  const retry = useRef<HTMLButtonElement>(null); const copiedLink = useRef<HTMLAnchorElement>(null); const verify = useRef<HTMLButtonElement>(null);
  const restore = useRef(false);
  const changed = !!review && !intent && !copy && (board.version !== review.version || board.name !== review.name
    || board.description !== review.description || board.backgroundType !== review.backgroundType || board.backgroundValue !== review.backgroundValue);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort();
    callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { callbacks.current.onRecoveryChange(!!intent); }, [intent]);
  function retire(message: string) {
    pending.current?.abort(); pending.current = undefined; setBusy(false); callbacks.current.onBusyChange(false);
    setIntent(undefined); setReview(undefined); setCopy(undefined); setVerified(undefined); setName(''); setOpen(false); setNotice(message);
  }
  useEffect(() => { if (!admitted && (open || intent || copy || pending.current)) retire('Board copying is unavailable.'); }, [admitted]);
  function returnFocus() {
    if (entry.current && !entry.current.disabled) entry.current.focus({ preventScroll: true });
    else { restore.current = true; callbacks.current.onReturnFocus(); }
  }
  useEffect(() => {
    if (busy || props.disabled || !restore.current) return;
    const target = !open ? entry.current : verified ? copiedLink.current : intent ? retry.current : copy ? verify.current : undefined;
    if (target) { target.focus({ preventScroll: true }); restore.current = false; }
  }, [busy, props.disabled, open, verified, intent, copy]);
  function begin() {
    const c = new AbortController(); pending.current = c; setBusy(true); callbacks.current.onBusyChange(true); return c;
  }
  function current(c: AbortController) { return mounted.current && pending.current === c && !c.signal.aborted; }
  function finish(c: AbortController) {
    if (pending.current === c) { pending.current = undefined; if (mounted.current) { setBusy(false); callbacks.current.onBusyChange(false); } }
  }
  async function actor(signal: AbortSignal, expected?: string) {
    const p = await workRequest<unknown>('/me', { signal });
    if (!isNotificationProfile(p) || expected && p.id !== expected) throw new ChangedCopyAccount(); return p.id;
  }
  function denial(error: unknown) {
    if (error instanceof ChangedCopyAccount) { retire('Your account changed. Reopen Board copying.'); return true; }
    if (error instanceof WorkRequestError && [401,403,404].includes(error.status)) { retire('Board copying is unavailable. Check access or sign in.'); callbacks.current.onRefresh(); return true; }
    return false;
  }
  async function loadReview() {
    if (pending.current || props.disabled || !available || intent || copy) return;
    const initial = !open; const previous = review; setOpen(true); setNotice(undefined); const c = begin();
    const started = performance.now(); activityEvent('board_copy_read','use');
    try {
      const result = await boundedWorkRead(async signal => {
        const id = await actor(signal, previous?.userId);
        const result = readReview(await workRequest<unknown>('/boards/' + encodeURIComponent(board.id), { signal }), board, id);
        await actor(signal, id); return result;
      }, c.signal);
      if (!current(c)) return;
      activityResult('board_copy_read',true,started);
      setReview(result); setConflict(false);
      if (initial) setName([...result.name].slice(0,75).join('') + ' copy');
      callbacks.current.onRefresh();
    } catch (error) { if (current(c)) {
      activityResult('board_copy_read',false,started);
      if (!denial(error)) setNotice('Unable to review this Board for copying. Try again.');
    } }
    finally { finish(c); }
  }
  async function verifyCopy(value: Copy) {
    if (pending.current) return;
    const c = begin(); setVerified(undefined); restore.current = true;
    const started = performance.now(); activityEvent('board_copy_read','use');
    try {
      const result = await boundedWorkRead(async signal => {
        await actor(signal, value.userId);
        const current = await workRequest<BoardSnapshot>('/boards/' + encodeURIComponent(value.id), { signal });
        if (current?.board?.id !== value.id || current.board.organizationId !== value.organizationId || current.access?.canView !== true
          || current.board.lifecycleState !== 'active' || typeof current.board.name !== 'string' || !current.board.name.trim() || current.board.name.length > 160)
          throw new Error('Unavailable copied Board');
        await actor(signal, value.userId); return current.board.name;
      }, c.signal);
      if (current(c)) { activityResult('board_copy_read',true,started); setVerified(result); setNotice('Your copy is available.'); }
    } catch (error) { if (current(c)) {
      activityResult('board_copy_read',false,started);
      if (!denial(error)) setNotice('Copy acknowledged. Check copied Board access before opening it.');
    } }
    finally { finish(c); }
  }
  async function submit() {
    if (pending.current || props.disabled || !admitted || !review || copy || !intent && (changed || conflict || !name.trim() || name.trim().length > 160)) return;
    const command = intent ?? { review, name: name.trim(), key: crypto.randomUUID() }; setIntent(command); setNotice(undefined); restore.current = true;
    const c = begin(); const started = performance.now(); activityEvent('board_copy_change', intent ? 'retry' : 'use'); let confirmed: Copy | undefined;
    try {
      confirmed = await boundedWorkRead(async signal => {
        await actor(signal, command.review.userId);
        const result = acknowledgment(await workRequest<unknown>('/boards/' + encodeURIComponent(command.review.id) + '/copy', {
          method: 'POST', signal, headers: { 'Idempotency-Key': command.key }, body: JSON.stringify({ name: command.name, version: command.review.version }),
        }), command);
        await actor(signal, command.review.userId); return result;
      }, c.signal);
      if (!current(c)) return;
      activityResult('board_copy_change', true, started); setIntent(undefined); setCopy(confirmed);
    } catch (error) {
      if (!current(c)) return;
      activityResult('board_copy_change', false, started);
      if (!denial(error)) {
        if (error instanceof WorkRequestError && [400,409].includes(error.status)) {
          activityEvent('board_copy_change','conflict'); setIntent(undefined); setConflict(true); setNotice('This copy could not be applied. Review the current Board before trying again.');
        } else { activityEvent('board_copy_change','exception'); setNotice('This copy is unconfirmed. Keep the name unchanged and retry the same copy.'); }
        callbacks.current.onRefresh();
      }
    } finally { finish(c); }
    if (confirmed && mounted.current && admitted) void verifyCopy(confirmed);
  }
  function close() { if (!busy && !intent) { setOpen(false); setReview(undefined); setCopy(undefined); setVerified(undefined); setNotice(undefined); setName(''); } }
  return <>
    {available && <Button ref={entry} disabled={props.disabled || busy || !!intent} onClick={() => { activityEvent('board_copy_disclosure','open'); void loadReview(); }}>Copy Board</Button>}
    {!open && notice && <Typography role="status">{notice}</Typography>}
    <Dialog open={open} onClose={close} disableRestoreFocus fullWidth maxWidth="sm" slotProps={{ transition: { onExited: returnFocus } }}>
      <DialogTitle>Copy Board</DialogTitle><DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        <Typography>The new Board is private and uses the source contents at creation. Lists, Cards, labels and checklist work receive new identities, including archived items. Completion resets. Membership, history, personal preferences and attachments are excluded.</Typography>
        {!copy && <TextField autoFocus label="Copied Board name" value={name} disabled={busy || !!intent} onChange={e => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 160 } }} />}
        {busy && <Typography role="status" aria-live="polite">Checking Board copying…</Typography>}
        {notice && <Alert severity="info" role="status">{notice}</Alert>}
        {!intent && !copy && (changed || conflict) && <Alert severity="warning">The source changed. Keep your name draft and review the current Board before copying.</Alert>}
        {!intent && !copy && <Button disabled={props.disabled || busy || !available} onClick={() => void loadReview()}>Review current Board for copy</Button>}
        {copy && !verified && <Button ref={verify} disabled={props.disabled || busy} onClick={() => void verifyCopy(copy)}>Check copied Board access</Button>}
        {copy && verified && <><Typography sx={{ overflowWrap: 'anywhere' }}>Copied Board: {verified}</Typography>
          <Button ref={copiedLink} component={Link} to={`/app/${copy.organizationId}/boards/${copy.id}`}>Open copied Board</Button></>}
      </Stack></DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>{copy ? 'Done copying Board' : 'Cancel Board copy'}</Button>}
        {!copy && <Button ref={retry} disabled={props.disabled || busy || !admitted || !review || !intent && (changed || conflict || !name.trim() || name.trim().length > 160)} onClick={() => void submit()}>{intent ? 'Retry same Board copy' : 'Create Board copy'}</Button>}
      </DialogActions>
    </Dialog>
  </>;
}
