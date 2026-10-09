import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';
import { activityEvent, activityResult } from './activityTelemetry';
import { ownsRecoveryFocus } from './focusRecovery';

type Review = { id: string; organizationId: string; name: string; version: number };
type Intent = Review & { key: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onBusyChange: (value: boolean) => void;
  onRecoveryChange: (value: boolean) => void; onRefresh: () => void; onReturnFocus: () => void };

// Archiving changes edit permission. Keep the original receipt recovery while
// current administrative admission remains, including on a read-only Board.
export function BoardArchiveControl({ snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [review, setReview] = useState<Review>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [conflict, setConflict] = useState(false); const [notice, setNotice] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined);
  const action = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  const focusOwner = useRef<HTMLDivElement | null>(null);
  const admitted = snapshot.access.canAdminister && snapshot.access.canView && ['active', 'archived'].includes(snapshot.board.lifecycleState);
  const available = admitted && snapshot.board.lifecycleState === 'active' && Number.isSafeInteger(snapshot.board.version) && Number(snapshot.board.version) > 0;
  const changed = !!review && !intent && (!available || snapshot.board.id !== review.id || snapshot.board.organizationId !== review.organizationId
    || snapshot.board.name !== review.name || snapshot.board.version !== review.version);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort();
    if (pending.current) onBusyChange(false); }; }, [onBusyChange]);
  useEffect(() => {
    if (admitted && (!review || snapshot.board.id === review.id && snapshot.board.organizationId === review.organizationId)) return;
    if (!review && !intent && !pending.current) return;
    pending.current?.abort(); pending.current = undefined; setBusy(false); onBusyChange(false);
    setReview(undefined); setIntent(undefined); setNotice('Board administration is unavailable.');
  }, [admitted, snapshot.board.id, snapshot.board.organizationId, review, intent, onBusyChange]);
  function restoreFocus() {
    if (!mounted.current || !ownsRecoveryFocus(document.activeElement, focusOwner.current)) {
      focusRequested.current = false; return;
    }
    focusRequested.current = disabled || busy;
    if (focusRequested.current) return;
    if (action.current && !action.current.disabled) action.current.focus({ preventScroll: true }); else onReturnFocus();
  }
  useEffect(() => { if (!review && !disabled && !busy && focusRequested.current) restoreFocus(); }, [review, disabled, busy]);
  function close() { if (!busy && !intent) { setReview(undefined); setNotice(undefined); setConflict(false); } }
  async function archive() {
    if (pending.current || disabled || !admitted || !review || !intent && (changed || conflict)) return;
    const command = intent ?? { ...review, key: crypto.randomUUID() };
    const started = performance.now(); activityEvent('board_archive', intent ? 'retry' : 'use');
    const c = new AbortController(); pending.current = c; setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await boundedWorkRead(signal => workRequest<unknown>(`/boards/${encodeURIComponent(command.id)}/archive`, {
        method: 'POST', signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
        body: JSON.stringify({ version: command.version }),
      }), c.signal) as { id?: unknown; organizationId?: unknown; name?: unknown; version?: unknown; lifecycleState?: unknown };
      if (!mounted.current || pending.current !== c) return;
      if (value?.id !== command.id || value.organizationId !== command.organizationId || value.name !== command.name
        || value.version !== command.version + 1 || value.lifecycleState !== 'archived') throw new Error('Unconfirmed archive');
      activityResult('board_archive', true, started); setIntent(undefined); setReview(undefined);
      setNotice('Board archive acknowledged. Current Board state is being checked.'); onRefresh();
    } catch (error) { if (mounted.current && pending.current === c) {
      activityResult('board_archive', false, started);
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        setIntent(undefined); setReview(undefined); setNotice('Board administration is unavailable.');
      } else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) {
        activityEvent('board_archive', 'conflict'); setIntent(undefined); setConflict(true);
        setNotice('This archive could not be applied. Check the Board and review its current state.');
      } else {
        activityEvent('board_archive', 'exception'); setIntent(command);
        setNotice('This archive is unconfirmed. Retry the same archive to recover its acknowledgment.');
      }
      onRefresh();
    } } finally { if (pending.current === c) { pending.current = undefined;
      if (mounted.current) { setBusy(false); onBusyChange(false); } } }
  }
  function open() {
    activityEvent('board_archive', 'open'); setReview({ id: snapshot.board.id, organizationId: snapshot.board.organizationId,
      name: snapshot.board.name, version: snapshot.board.version! }); setConflict(false); setNotice(undefined);
  }
  return <>
    {available && <Button ref={action} disabled={disabled || busy || !!intent} onClick={open}>Archive Board</Button>}
    {!review && notice && <Typography role="status">{notice}</Typography>}
    {admitted && snapshot.board.lifecycleState === 'archived' && <Button component={Link}
      to={`/app/${snapshot.board.organizationId}/archived-boards`} disabled={disabled || busy || !!intent}>Manage archived Boards</Button>}
    <Dialog open={!!review} onClose={close} disableRestoreFocus fullWidth maxWidth="sm" slotProps={{
      paper: { ref: (node: HTMLDivElement | null) => { if (node) focusOwner.current = node; } },
      transition: { onExited: restoreFocus },
    }}>
      <DialogTitle>Archive Board</DialogTitle><DialogContent>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Archive {review?.name}?</Typography>
        <Typography>Archiving makes this Board read-only and hides it from active work. Its Lists and Cards remain associated with it. Current administrators can restore it from Archived boards.</Typography>
        {notice && review && <Alert severity="info" role="status">{notice}</Alert>}
        {(changed || conflict) && !intent && <Alert severity="warning">This review is no longer current. Review the current Board before archiving.</Alert>}
        {(changed || conflict) && available && !intent && <Button disabled={busy || disabled} onClick={open}>Review current Board for archive</Button>}
        {intent && <Typography>The original archive is unresolved. Retry that same request, even if the Board is already read-only.</Typography>}
        <Button disabled={busy} onClick={onRefresh}>Check current Board for this archive</Button>
      </DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>Cancel archive</Button>}
        <Button disabled={disabled || busy || !admitted || !review || !intent && (changed || conflict)} onClick={() => void archive()}>
          {intent ? 'Retry this archive' : 'Confirm archive'}</Button>
      </DialogActions>
    </Dialog>
  </>;
}
