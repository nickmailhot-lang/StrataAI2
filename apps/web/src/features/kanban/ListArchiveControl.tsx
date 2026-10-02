import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, TextField, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';
import { apiFetch } from '../../api/apiFetch';

type Review = { id: string; name: string; rank: string; version: number };
type Intent = Review & { key: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; unavailableListIds: Set<string>;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (unresolved: boolean) => void;
  onRefresh: () => void; onReturnFocus: () => void };

// Keep this owner outside the canvas: a committed archive removes its column
// even when the response was lost. The original request must remain recoverable.
export function ListArchiveControl({ snapshot, disabled, unavailableListIds, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [open, setOpen] = useState(false); const [review, setReview] = useState<Review>();
  const [intent, setIntent] = useState<Intent>(); const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const action = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  const admitted = snapshot.access.canAdminister && snapshot.board.lifecycleState === 'active';
  const lists = snapshot.lists.map(c => c.list).filter(l => l.lifecycleState === 'active'
    && Number.isSafeInteger(l.version) && Number(l.version) > 0);
  const current = lists.find(l => l.id === review?.id);
  const changed = !!review && !intent && (!current || current.version !== review.version || current.name !== review.name || current.rank !== review.rank);
  const unavailable = !!review && !intent && unavailableListIds.has(review.id);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false;
    pending.current?.abort(); if (pending.current) onBusyChange(false); }; }, [onBusyChange]);
  function restoreFocus() {
    focusRequested.current = disabled || busy;
    if (focusRequested.current) return;
    if (action.current && !action.current.disabled) action.current.focus({ preventScroll: true }); else onReturnFocus();
  }
  useEffect(() => { if (!open && !disabled && !busy && focusRequested.current) restoreFocus(); }, [open, disabled, busy]);
  function close() { if (!busy && !intent) { setOpen(false); setReview(undefined); setNotice(undefined); setConflict(false); } }
  async function archive() {
    if (pending.current || disabled || !admitted || !review || (!intent && (changed || conflict || unavailable))) return;
    const command = intent ?? { ...review, key: crypto.randomUUID() }; const c = new AbortController(); pending.current = c;
    setBusy(true); onBusyChange(true); setNotice(undefined);
    let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
    try {
      const result = await Promise.race([
        apiFetch(`/lists/${encodeURIComponent(command.id)}/archive`, { method: 'POST', signal: c.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ version: command.version }) })
          .then(async r => ({ status: r.status, body: await r.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Archive interrupted'));
          c.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => c.abort(), 15_000); }),
      ]);
      if (!mounted.current || pending.current !== c) return;
      if ([401, 403, 404].includes(result.status)) {
        setIntent(undefined); setReview(undefined); setOpen(false); setNotice('This List or administration action is unavailable.');
      } else if ([400, 409].includes(result.status)) {
        setIntent(undefined); setConflict(true); setNotice('This archive could not be applied. Check the Board and review the current List.');
      } else {
        const value = result.body as { id?: unknown; organizationId?: unknown; boardId?: unknown; name?: unknown;
          rank?: unknown; version?: unknown; lifecycleState?: unknown } | undefined;
        if (result.status !== 200 || value?.id !== command.id || value.organizationId !== snapshot.board.organizationId
          || value.boardId !== snapshot.board.id || value.name !== command.name || value.rank !== command.rank
          || value.lifecycleState !== 'archived' || value.version !== command.version + 1)
          throw new Error('Unconfirmed archive');
        setIntent(undefined); setReview(undefined); setOpen(false);
        setNotice('List archive acknowledged. Current Board state is being checked.');
      }
      onRefresh();
    } catch { if (mounted.current && pending.current === c) {
      setIntent(command); setNotice('The archive could not be confirmed. Retry that same archive to recover its acknowledgment.'); onRefresh();
    } } finally {
      clearTimeout(timer); if (abort) c.signal.removeEventListener('abort', abort);
      if (pending.current === c) { pending.current = undefined; if (mounted.current) { setBusy(false); onBusyChange(false); } }
    }
  }
  return <>
    {admitted && <Button ref={action} disabled={disabled || busy || !!intent || lists.length === 0} onClick={() => {
      setOpen(true); setReview(undefined); setConflict(false); setNotice(undefined);
    }}>Archive a list</Button>}
    {!open && notice && <Typography role="status">{notice}</Typography>}
    <Dialog open={open} onClose={close} disableRestoreFocus fullWidth maxWidth="sm"
      slotProps={{ transition: { onExited: restoreFocus } }}>
      <DialogTitle>Archive List</DialogTitle><DialogContent>
        {open && notice && <Alert severity="info">{notice}</Alert>}
        <Typography>Archiving hides this List and its cards from the active Board. All contained cards remain associated with it, and the List can be restored from Archived lists.</Typography>
        {!intent && <TextField select fullWidth margin="normal" label="List to archive" value={review?.id ?? ''} disabled={busy || disabled} onChange={event => {
          const selected = lists.find(l => l.id === event.target.value); setConflict(false); setNotice(undefined);
          setReview(selected ? { id: selected.id, name: selected.name, rank: selected.rank, version: selected.version! } : undefined);
        }}>
          <MenuItem value="">Choose a List</MenuItem>
          {lists.map(l => <MenuItem key={l.id} value={l.id} disabled={unavailableListIds.has(l.id)}>{l.name}</MenuItem>)}
        </TextField>}
        {intent && <Typography sx={{ overflowWrap: 'anywhere' }}>Recover the original archive of {intent.name}, even if that List is no longer on the active canvas.</Typography>}
        {(changed || conflict || unavailable) && !intent && <Alert severity="warning">This review is no longer current. Choose a current available List again before archiving.</Alert>}
        {(changed || conflict) && current && !intent && <Button disabled={busy || disabled || unavailable} onClick={() => {
          setReview({ id: current.id, name: current.name, rank: current.rank, version: current.version! }); setConflict(false); setNotice(undefined);
        }}>Review current list for archive</Button>}
        <Button disabled={busy} onClick={onRefresh}>Check current Board for this archive</Button>
      </DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>Cancel archive</Button>}
        <Button disabled={disabled || busy || !admitted || !review || (!intent && (changed || conflict || unavailable))} onClick={() => void archive()}>
          {intent ? 'Retry this archive' : 'Confirm archive'}
        </Button>
      </DialogActions>
    </Dialog>
  </>;
}
