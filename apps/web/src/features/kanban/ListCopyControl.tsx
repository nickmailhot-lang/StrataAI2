import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import type { BoardSnapshot } from '../../api/workManagement';

type Board = { id: string; name: string; version: number };
type Draft = { id: string; sourceName: string; sourceRank: string; version: number; name: string; destination: string };
type Review = Draft & { destinationName: string };
type Intent = Review & { key: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; unavailableListIds: Set<string>;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (unresolved: boolean) => void;
  onRefresh: () => void; onReturnFocus: () => void };
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';

// Board-owned recovery survives canonical source-column removal after a copy.
export function ListCopyControl({ snapshot, disabled, unavailableListIds, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [open, setOpen] = useState(false); const [boards, setBoards] = useState<Board[]>();
  const [draft, setDraft] = useState<Draft>(); const [review, setReview] = useState<Review>();
  const [intent, setIntent] = useState<Intent>(); const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false); const [denied, setDenied] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false); const epoch = useRef(0);
  const authorized = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active' && !denied;
  const lists = snapshot.lists.map(column => column.list).filter(list => list.lifecycleState === 'active'
    && Number.isSafeInteger(list.version) && Number(list.version) > 0);
  const source = lists.find(list => list.id === draft?.id);
  const changed = !!draft && !intent && (!source || source.name !== draft.sourceName || source.rank !== draft.sourceRank
    || source.version !== draft.version || unavailableListIds.has(draft.id));
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; epoch.current++;
    if (pending.current) { pending.current.abort(); onBusyChange(false); } }; }, [onBusyChange]);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => {
    if (authorized || (!open && !intent && !pending.current)) return;
    epoch.current++; pending.current?.abort(); pending.current = undefined; setBusy(false); onBusyChange(false);
    setOpen(false); setBoards(undefined); setDraft(undefined); setReview(undefined); setIntent(undefined);
    setNotice('This List copy action is unavailable.');
  }, [authorized, open, intent, onBusyChange]);

  async function request(path: string, options: RequestInit = {}) {
    if (pending.current) return;
    const c = new AbortController(); pending.current = c; setBusy(true); onBusyChange(true);
    let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
    try {
      const result = await Promise.race([
        apiFetch(path, { ...options, signal: c.signal }).then(async response => ({ status: response.status,
          value: await response.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Copy request interrupted'));
          c.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => c.abort(), 15_000); }),
      ]);
      if (!mounted.current || pending.current !== c) return;
      return result;
    } finally {
      clearTimeout(timer); if (abort) c.signal.removeEventListener('abort', abort);
      if (pending.current === c) { pending.current = undefined;
        if (mounted.current) { setBusy(false); onBusyChange(false); } }
    }
  }
  function deny() {
    setDenied(true); setOpen(false); setBoards(undefined); setDraft(undefined); setReview(undefined); setIntent(undefined);
    setNotice('This List copy action is unavailable.'); onRefresh();
  }
  async function discover() {
    if (pending.current || intent || !authorized || disabled) return;
    const generation = epoch.current;
    setOpen(true); setBoards(undefined); setReview(undefined); setNotice(undefined);
    try {
      const result = await request(`/organizations/${encodeURIComponent(snapshot.board.organizationId)}/boards`);
      if (!result) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if (result.status !== 200 || !Array.isArray(result.value)) throw new Error('Invalid directory');
      const values = result.value as Board[];
      if (values.some(board => !board || !guid(board.id) || typeof board.name !== 'string' || !board.name.trim()
        || board.name.length > 160 || !Number.isSafeInteger(board.version) || board.version <= 0)
        || new Set(values.map(board => board.id)).size !== values.length) throw new Error('Invalid directory');
      setBoards(values);
    } catch { if (mounted.current && generation === epoch.current) setNotice('Copy destinations could not be loaded. Try loading them again.'); }
  }
  async function prepare() {
    if (pending.current || disabled || !authorized || !draft || changed || intent) return;
    const generation = epoch.current;
    const name = draft.name.trim();
    if (!name || name.length > 160) { setNotice('Use a copy name with 1 to 160 characters.'); return; }
    if (!boards?.some(board => board.id === draft.destination)) { setNotice('Choose a destination Board.'); return; }
    setReview(undefined); setNotice(undefined);
    try {
      const result = await request(`/boards/${encodeURIComponent(draft.destination)}`);
      if (!result) return;
      if (result.status === 401) { deny(); return; }
      const target = result.value as BoardSnapshot | undefined;
      if (result.status !== 200 || target?.board?.id !== draft.destination || target.board.organizationId !== snapshot.board.organizationId
        || target.board.lifecycleState !== 'active' || target.access?.canEdit !== true || typeof target.board.name !== 'string'
        || !target.board.name.trim() || target.board.name.length > 160) {
        setNotice('The destination Board is unavailable for copying. Choose another Board.'); return;
      }
      setReview({ ...draft, name, destinationName: target.board.name });
    } catch { if (mounted.current && generation === epoch.current) setNotice('The destination could not be checked. Review the copy again.'); }
  }
  async function copy() {
    if (pending.current || disabled || !authorized || !review || (!intent && (changed || blocked))) return;
    const generation = epoch.current;
    const command = intent ?? { ...review, key: crypto.randomUUID() }; setNotice(undefined);
    try {
      const result = await request(`/lists/${encodeURIComponent(command.id)}/copy`, { method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
        body: JSON.stringify({ destinationBoardId: command.destination, name: command.name, version: command.version }) });
      if (!result) return;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if ([400, 409].includes(result.status)) {
        setIntent(undefined); setBlocked(true); setReview(undefined);
        setNotice('This copy could not be applied. Check the Board and review a current List.'); onRefresh(); return;
      }
      const ack = result.value as { id?: unknown; organizationId?: unknown; boardId?: unknown; name?: unknown;
        rank?: unknown; lifecycleState?: unknown; version?: unknown } | undefined;
      if (result.status !== 201 || !guid(ack?.id) || ack.id === command.id || ack.organizationId !== snapshot.board.organizationId
        || ack.boardId !== command.destination || ack.name !== command.name || ack.version !== 1 || ack.lifecycleState !== 'active'
        || typeof ack.rank !== 'string' || !/^[0-9]{30}$/.test(ack.rank)) throw new Error('Unconfirmed copy');
      setIntent(undefined); setOpen(false); setDraft(undefined); setReview(undefined); setBoards(undefined);
      setNotice('List copy acknowledged. Check its destination Board for the current copy.'); onRefresh();
    } catch { if (mounted.current && generation === epoch.current) {
      setIntent(command); setNotice('The copy could not be confirmed. Retry this same copy to recover its acknowledgment.'); onRefresh();
    } }
  }
  function close() { if (busy || intent) return; setOpen(false); setDraft(undefined); setReview(undefined); setNotice(undefined); setBlocked(false); }
  return <>
    {authorized && lists.length > 0 && <Button disabled={disabled || busy || !!intent} onClick={() => void discover()}>Copy list</Button>}
    {!open && notice && <Typography role="status">{notice}</Typography>}
    <Dialog open={open} onClose={close} disableRestoreFocus fullWidth maxWidth="sm"
      slotProps={{ transition: { onExited: onReturnFocus } }}>
      {open && <><DialogTitle>Copy list</DialogTitle><DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        {notice && <Typography role="status">{notice}</Typography>}
        <Typography>The copy gets new IDs and includes active and archived Cards. Deleted Cards are excluded. Card order and archive state are preserved.</Typography>
        {!intent && <>
          {boards?.length === 0 && <Typography>No destination Boards are available. Check access or create a Board before copying.</Typography>}
          <TextField autoFocus select label="List to copy" value={draft?.id ?? ''} disabled={busy} onChange={event => {
            const list = lists.find(item => item.id === event.target.value); setReview(undefined); setBlocked(false); setNotice(undefined);
            setDraft(list ? { id: list.id, sourceName: list.name, sourceRank: list.rank, version: list.version!,
              name: `${list.name} copy`.slice(0, 160), destination: snapshot.board.id } : undefined);
          }}><MenuItem value="">Choose a List</MenuItem>{lists.map(list => <MenuItem key={list.id} value={list.id}
            disabled={unavailableListIds.has(list.id)}>{list.name}</MenuItem>)}</TextField>
          <TextField label="Copy name" value={draft?.name ?? ''} disabled={busy || !draft} slotProps={{ htmlInput: { maxLength: 160 } }}
            onChange={event => { setDraft(current => current && { ...current, name: event.target.value }); setReview(undefined); }} />
          <TextField select label="Destination Board" value={boards?.some(board => board.id === draft?.destination) ? draft?.destination : ''}
            disabled={busy || !draft || !boards} onChange={event => {
              setDraft(current => current && { ...current, destination: event.target.value }); setReview(undefined);
            }}><MenuItem value="">Choose a Board</MenuItem>{boards?.map(board => <MenuItem key={board.id} value={board.id}>{board.name}</MenuItem>)}</TextField>
          <Button disabled={busy || disabled} onClick={() => void discover()}>Reload copy destinations</Button>
          {(changed || blocked) && <Alert severity="warning">This copy review changed. Choose a current List again before copying.</Alert>}
          {(changed || blocked) && source && <Button disabled={busy || disabled || unavailableListIds.has(source.id)} onClick={() => {
            setDraft(current => current && { ...current, sourceName: source.name, sourceRank: source.rank, version: source.version! });
            setReview(undefined); setBlocked(false); setNotice(undefined); onRefresh();
          }}>Use current List for copy review</Button>}
          <Button disabled={busy || disabled || !draft || !boards || changed || blocked} onClick={() => void prepare()}>Review List copy</Button>
        </>}
        {review && <Typography sx={{ overflowWrap: 'anywhere' }}>Copy {review.sourceName} as {review.name} to {review.destinationName}?</Typography>}
        {intent && <Alert severity="info">The original copy is unresolved. Keep its name and destination unchanged until its acknowledgment is recovered.</Alert>}
        <Button disabled={busy} onClick={onRefresh}>Check current Board for this copy</Button>
      </Stack></DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>Cancel copy</Button>}
        <Button disabled={busy || disabled || !authorized || !review || (!intent && (changed || blocked))} onClick={() => void copy()}>
          {intent ? 'Retry this List copy' : 'Confirm List copy'}
        </Button>
      </DialogActions></>}
    </Dialog>
  </>;
}
