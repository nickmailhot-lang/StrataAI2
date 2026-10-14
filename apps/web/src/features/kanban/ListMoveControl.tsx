import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../notifications/notificationInbox';

type Board = { id: string; name: string; version: number };
type Draft = { id: string; name: string; rank: string; version: number; destination: string };
type Review = Draft & { destinationName: string; userId: string };
type Intent = Review & { key: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; refreshing?: boolean; unavailableListIds: Set<string>;
  onBusyChange: (value: boolean) => void; onRecoveryChange: (value: boolean) => void;
  onRefresh: () => void; onReturnFocus: () => void };
class ChangedMoveAccount extends Error {}

export function ListMoveControl(props: Props) {
  return <MoveDialog key={props.snapshot.board.organizationId + '/' + props.snapshot.board.id} {...props} />;
}
function MoveDialog(props: Props) {
  const callbacks = useRef(props); callbacks.current = props;
  const board = props.snapshot.board;
  const available = props.snapshot.access.canView && props.snapshot.access.canMove && board.lifecycleState === 'active'
    && notificationUuid(board.id) && notificationUuid(board.organizationId);
  const [open, setOpen] = useState(false); const [boards, setBoards] = useState<Board[]>(); const [userId, setUserId] = useState<string>();
  const [draft, setDraft] = useState<Draft>(); const [review, setReview] = useState<Review>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false); const [notice, setNotice] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined);
  const lists = props.snapshot.lists.map(column => column.list).filter(list => list.lifecycleState === 'active'
    && notificationUuid(list.id) && Number.isSafeInteger(list.version) && Number(list.version) > 0 && Number(list.version) < Number.MAX_SAFE_INTEGER);
  const source = lists.find(list => list.id === draft?.id);
  const changed = !!draft && !intent && (!source || source.name !== draft.name || source.rank !== draft.rank
    || source.version !== draft.version || props.unavailableListIds.has(draft.id));
  const commandBlocked = props.disabled || props.refreshing;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort();
    callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { callbacks.current.onRecoveryChange(!!intent); }, [intent]);
  function retire(message: string) {
    pending.current?.abort(); pending.current = undefined; setBusy(false); callbacks.current.onBusyChange(false);
    setOpen(false); setBoards(undefined); setUserId(undefined); setDraft(undefined); setReview(undefined); setIntent(undefined); setNotice(message);
  }
  useEffect(() => { if (!available && (open || intent || pending.current)) retire('List movement is unavailable. Check current Board access.'); }, [available]);
  function begin() { const c = new AbortController(); pending.current = c; setBusy(true); callbacks.current.onBusyChange(true); return c; }
  function live(c: AbortController) { return mounted.current && pending.current === c && !c.signal.aborted; }
  function finish(c: AbortController) { if (pending.current === c) { pending.current = undefined;
    if (mounted.current) { setBusy(false); callbacks.current.onBusyChange(false); } } }
  async function actor(signal: AbortSignal, expected?: string) {
    const profile = await workRequest<unknown>('/me', { signal });
    if (!isNotificationProfile(profile) || expected && profile.id !== expected) throw new ChangedMoveAccount();
    return profile.id;
  }
  function denial(error: unknown) {
    if (error instanceof ChangedMoveAccount) { retire('Your account changed. Reopen List movement.'); return true; }
    if (error instanceof WorkRequestError && [401,403,404].includes(error.status)) {
      retire('List movement is unavailable. Check access or sign in.'); callbacks.current.onRefresh(); return true;
    }
    return false;
  }
  async function discover() {
    if (pending.current || intent || !available || props.disabled) return;
    setOpen(true); setBoards(undefined); setReview(undefined); setNotice(undefined); const c = begin();
    try {
      const result = await boundedWorkRead(async signal => {
        const user = await actor(signal, userId);
        signal.throwIfAborted();
        const values = await workRequest<unknown>(`/organizations/${encodeURIComponent(board.organizationId)}/boards`, { signal });
        if (!Array.isArray(values) || values.some(value => !value || !notificationUuid(value.id) || typeof value.name !== 'string'
          || !value.name.trim() || value.name.length > 160 || !Number.isSafeInteger(value.version) || value.version < 1)
          || new Set(values.map(value => value.id)).size !== values.length) throw new Error('Invalid directory');
        await actor(signal, user); return { user, boards: (values as Board[]).filter(value => value.id !== board.id) };
      }, c.signal);
      if (live(c)) { setUserId(result.user); setBoards(result.boards); }
    } catch (error) { if (live(c) && !denial(error)) setNotice('Move destinations could not be loaded. Try again.'); }
    finally { finish(c); }
  }
  async function prepare() {
    if (pending.current || commandBlocked || !available || !draft || changed || blocked || intent || !userId
      || !boards?.some(value => value.id === draft.destination)) return;
    setReview(undefined); setNotice(undefined); const c = begin();
    try {
      const result = await boundedWorkRead(async signal => {
        await actor(signal, userId);
        signal.throwIfAborted();
        const target = await workRequest<BoardSnapshot>(`/boards/${encodeURIComponent(draft.destination)}`, { signal });
        if (target?.board?.id !== draft.destination || target.board.id === board.id || target.board.organizationId !== board.organizationId
          || target.board.lifecycleState !== 'active' || target.access?.canView !== true || target.access.canMove !== true
          || typeof target.board.name !== 'string' || !target.board.name.trim() || target.board.name.length > 160)
          throw new Error('Unavailable destination');
        await actor(signal, userId); return { ...draft, destinationName: target.board.name, userId };
      }, c.signal);
      if (live(c)) setReview(result);
    } catch (error) { if (live(c)) {
      if (error instanceof WorkRequestError && [403,404].includes(error.status)) setNotice('The destination is unavailable for moving. Choose another Board.');
      else if (!denial(error)) setNotice('The destination could not be checked for moving. Review it again.');
    } }
    finally { finish(c); }
  }
  async function move() {
    if (pending.current || commandBlocked || !available || !review || !intent && (changed || blocked)) return;
    const command = intent ?? { ...review, key: crypto.randomUUID() }; const c = begin(); let sent = false; setNotice(undefined);
    try {
      const result = await boundedWorkRead(async signal => {
        await actor(signal, command.userId); signal.throwIfAborted(); sent = true; setIntent(command);
        const ack = await workRequest<Record<string, unknown>>(`/lists/${encodeURIComponent(command.id)}/move`, { method: 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify({ sourceBoardId: board.id, destinationBoardId: command.destination, expectedVersion: command.version }) });
        await actor(signal, command.userId); return ack;
      }, c.signal);
      if (!live(c)) return;
      if (!result || result.id !== command.id || result.organizationId !== board.organizationId || result.boardId !== command.destination
        || result.name !== command.name || result.version !== command.version + 1 || result.lifecycleState !== 'active'
        || typeof result.rank !== 'string' || !/^[0-9]{30}$/.test(result.rank)) throw new Error('Unconfirmed move');
      setIntent(undefined); setOpen(false); setDraft(undefined); setReview(undefined); setBoards(undefined);
      setNotice('List move acknowledged. Check the destination Board for its current contents.'); callbacks.current.onRefresh();
    } catch (error) { if (live(c) && !denial(error)) {
      if (error instanceof WorkRequestError && [400,409].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setReview(undefined);
        setNotice('This move could not be applied. Check the Board and review a current List.'); callbacks.current.onRefresh();
      } else if (sent || intent) {
        setIntent(command); setNotice('The move could not be confirmed. Retry this same move to recover its acknowledgment.'); callbacks.current.onRefresh();
      } else setNotice('Your current account could not be checked. Try again before moving.');
    } }
    finally { finish(c); }
  }
  function close() { if (busy || intent) return; setOpen(false); setDraft(undefined); setReview(undefined); setNotice(undefined); setBlocked(false); }
  return <>
    {available && lists.length > 0 && <Button disabled={props.disabled || busy || !!intent} onClick={() => void discover()}>Move list</Button>}
    {!open && notice && <Typography role="status">{notice}</Typography>}
    <Dialog open={open} onClose={close} disableRestoreFocus fullWidth maxWidth="sm"
      slotProps={{ transition: { onExited: () => callbacks.current.onReturnFocus() } }}>
      {open && <><DialogTitle>Move list to another Board</DialogTitle><DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        {notice && <Typography role="status">{notice}</Typography>}
        {props.refreshing && <Typography role="status">Checking current Board access before moving.</Typography>}
        <Typography>The whole List moves to the end of the destination Board. Its Cards keep their identities and lifecycle states, including archived Cards. Labels and members are reconciled for the destination, and its access rules apply.</Typography>
        {!intent && <>
          {boards?.length === 0 && <Typography>No other destination Boards are available. Check access or create another Board.</Typography>}
          <TextField autoFocus select label="List to move" value={draft?.id ?? ''} disabled={busy} onChange={event => {
            const list = lists.find(value => value.id === event.target.value); setReview(undefined); setBlocked(false); setNotice(undefined);
            setDraft(list ? { id: list.id, name: list.name, rank: list.rank, version: list.version!, destination: '' } : undefined);
          }}><MenuItem value="">Choose a List</MenuItem>{lists.map(list => <MenuItem key={list.id} value={list.id}
            disabled={props.unavailableListIds.has(list.id)}>{list.name}</MenuItem>)}</TextField>
          <TextField select label="Destination Board" value={boards?.some(value => value.id === draft?.destination) ? draft?.destination : ''}
            disabled={busy || !draft || !boards} onChange={event => { setDraft(value => value && { ...value, destination: event.target.value }); setReview(undefined); }}>
            <MenuItem value="">Choose a Board</MenuItem>{boards?.map(value => <MenuItem key={value.id} value={value.id}>{value.name}</MenuItem>)}</TextField>
          <Button disabled={busy || props.disabled} onClick={() => void discover()}>Reload move destinations</Button>
          {(changed || blocked) && <Alert severity="warning">This move review changed. Choose a current List again before moving.</Alert>}
          {(changed || blocked) && source && <Button disabled={busy || commandBlocked || props.unavailableListIds.has(source.id)} onClick={() => {
            setDraft(value => value && { ...value, name: source.name, rank: source.rank, version: source.version! });
            setReview(undefined); setBlocked(false); setNotice(undefined);
          }}>Use current List for move review</Button>}
          <Button disabled={busy || commandBlocked || !draft?.destination || !boards || changed || blocked} onClick={() => void prepare()}>Review List move</Button>
        </>}
        {review && <Typography sx={{ overflowWrap: 'anywhere' }}>Move {review.name} to the end of {review.destinationName}?</Typography>}
        {intent && <Alert severity="info">The original move is unresolved. Keep its source and destination unchanged until its acknowledgment is recovered.</Alert>}
        <Button disabled={busy} onClick={() => callbacks.current.onRefresh()}>Check current Board for this move</Button>
      </Stack></DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>Cancel move</Button>}
        <Button disabled={busy || commandBlocked || !available || !review || !intent && (changed || blocked)} onClick={() => void move()}>
          {intent ? 'Retry this List move' : 'Confirm List move'}
        </Button>
      </DialogActions></>}
    </Dialog>
  </>;
}
