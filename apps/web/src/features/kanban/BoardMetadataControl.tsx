import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';
import { boardColors } from './boardBackground';
import { activityEvent, activityResult } from './activityTelemetry';
type Review = { id: string; organizationId: string; version: number; name: string; description: string | null; backgroundType: string; backgroundValue: string | null };
type Intent = { review: Review; key: string; name: string; description: string | null; selection: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onBusyChange: (busy: boolean) => void; onRecoveryChange: (unresolved: boolean) => void;
  onRefresh: () => void; onReturnFocus: () => void };
export function BoardMetadataControl({ snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [review, setReview] = useState<Review>(); const [name, setName] = useState(''); const [description, setDescription] = useState('');
  const [selection, setSelection] = useState('preserve'); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [conflict, setConflict] = useState(false); const [notice, setNotice] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined); const action = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false); const board = snapshot.board;
  const admitted = snapshot.access.canView && snapshot.access.canEdit && board.lifecycleState === 'active';
  const available = admitted && Number.isSafeInteger(board.version) && Number(board.version) > 0 && ['COLOR', 'IMAGE'].includes(board.backgroundType ?? '')
    && typeof board.name === 'string' && !!board.name.trim() && board.name.length <= 160 && (board.description === null || typeof board.description === 'string')
    && (board.backgroundValue === null || typeof board.backgroundValue === 'string');
  const changed = !!review && !intent && (!available || board.id !== review.id || board.organizationId !== review.organizationId || board.version !== review.version
    || board.name !== review.name || board.description !== review.description || board.backgroundType !== review.backgroundType || board.backgroundValue !== review.backgroundValue);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); if (pending.current) onBusyChange(false); }; }, [onBusyChange]);
  useEffect(() => {
    if (admitted && (!review || board.id === review.id && board.organizationId === review.organizationId)) return;
    if (!review && !intent && !pending.current) return;
    pending.current?.abort(); pending.current = undefined; setBusy(false); onBusyChange(false); setReview(undefined); setIntent(undefined);
    setName(''); setDescription(''); setSelection('preserve'); setNotice('Board editing is unavailable.');
  }, [admitted, board.id, board.organizationId, review, intent, onBusyChange]);
  function restoreFocus() {
    focusRequested.current = disabled || busy; if (focusRequested.current) return;
    if (action.current && !action.current.disabled) action.current.focus({ preventScroll: true }); else onReturnFocus();
  }
  useEffect(() => { if (!review && !disabled && !busy && focusRequested.current) restoreFocus(); }, [review, disabled, busy]);
  function currentReview(): Review { return { id: board.id, organizationId: board.organizationId, version: board.version!, name: board.name,
    description: board.description, backgroundType: board.backgroundType!, backgroundValue: board.backgroundValue! }; }
  function open() { activityEvent('board_metadata_update', 'open'); setReview(currentReview()); setName(board.name); setDescription(board.description ?? ''); setSelection('preserve'); setNotice(undefined); setConflict(false); }
  function close() { if (!busy && !intent) { setReview(undefined); setNotice(undefined); } }
  async function save() {
    if (pending.current || disabled || !admitted || !review || !intent && (changed || conflict)) return;
    if (!intent && (!name.trim() || name.trim().length > 160 || !['preserve', 'default', ...boardColors].includes(selection))) {
      setNotice('Use a Board name with 1 to 160 characters and an available background choice.'); return;
    }
    const command = intent ?? { review, name: name.trim(), description: description.trim() || null, selection, key: crypto.randomUUID() };
    const started = performance.now(); activityEvent('board_metadata_update', intent ? 'retry' : 'use');
    const c = new AbortController(); pending.current = c; setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await boundedWorkRead(signal => workRequest<unknown>(`/boards/${encodeURIComponent(command.review.id)}`, { method: 'PATCH', signal,
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ name: command.name, description: command.description,
          version: command.review.version, ...(command.selection === 'preserve' ? {} : { backgroundType: 'COLOR', backgroundValue: command.selection === 'default' ? null : command.selection }) }),
      }), c.signal) as Partial<Review> & { lifecycleState?: string };
      if (!mounted.current || pending.current !== c) return;
      if (value?.id !== command.review.id || value.organizationId !== command.review.organizationId || value.version !== command.review.version + 1
        || value.name !== command.name || value.description !== command.description || value.lifecycleState !== 'active'
        || value.backgroundType !== (command.selection === 'preserve' ? command.review.backgroundType : 'COLOR')
        || value.backgroundValue !== (command.selection === 'preserve' ? command.review.backgroundValue : command.selection === 'default' ? null : command.selection)) throw new Error('Unconfirmed metadata');
      activityResult('board_metadata_update', true, started); setIntent(undefined); setReview(undefined); setNotice('Board changes acknowledged. Current Board state is being checked.'); onRefresh();
    } catch (error) { if (mounted.current && pending.current === c) {
      activityResult('board_metadata_update', false, started);
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        setIntent(undefined); setReview(undefined); setName(''); setDescription(''); setNotice('Board editing is unavailable.');
      } else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) {
        activityEvent('board_metadata_update', 'conflict');
        setIntent(undefined); setConflict(true); setNotice('This save could not be applied. Check the Board and review its current revision.');
      } else { activityEvent('board_metadata_update', 'exception'); setIntent(command); setNotice('This save is unconfirmed. Keep its fields unchanged and retry the same save.'); }
      onRefresh();
    } } finally { if (pending.current === c) { pending.current = undefined; if (mounted.current) { setBusy(false); onBusyChange(false); } } }
  }
  return <>
    {available && <Button ref={action} disabled={disabled || busy || !!intent} onClick={open}>Edit Board details</Button>}
    {!review && notice && <Typography role="status">{notice}</Typography>}
    <Dialog open={!!review} onClose={close} disableRestoreFocus fullWidth maxWidth="sm" slotProps={{ transition: { onExited: restoreFocus } }}>
      <DialogTitle>Edit Board details</DialogTitle><DialogContent><Stack spacing={2} sx={{ pt: 1 }}>
        <TextField label="Board name" value={name} disabled={busy || !!intent} onChange={e => setName(e.target.value)} slotProps={{ htmlInput: { maxLength: 160 } }} />
        <TextField label="Board description" multiline minRows={3} value={description} disabled={busy || !!intent} onChange={e => setDescription(e.target.value)} />
        <TextField select label="Board background" value={selection} disabled={busy || !!intent} onChange={e => setSelection(e.target.value)}>
          <MenuItem value="preserve">Keep current background</MenuItem><MenuItem value="default">Default background</MenuItem>
          {boardColors.map(color => <MenuItem key={color} value={color}>{color[0].toUpperCase() + color.slice(1)}</MenuItem>)}
        </TextField>
        {notice && review && <Alert severity="info" role="status">{notice}</Alert>}
        {(changed || conflict) && !intent && <Alert severity="warning">This review changed. Keep your draft and review the current Board revision before saving.</Alert>}
        {(changed || conflict) && available && !intent && <Stack sx={{ overflowWrap: 'anywhere' }}>
          <Typography>Current Board name: {board.name}</Typography><Typography>Current description: {board.description ?? 'No description'}</Typography>
          <Typography>Current background: {board.backgroundType === 'COLOR' && boardColors.includes(board.backgroundValue as typeof boardColors[number]) ? board.backgroundValue : 'Existing background'}</Typography>
        </Stack>}
        {(changed || conflict) && available && !intent && <Button disabled={busy || disabled} onClick={() => { setReview(currentReview()); setConflict(false); setNotice(undefined); }}>Review current Board revision</Button>}
        <Button disabled={busy} onClick={onRefresh}>Check current Board for this save</Button>
      </Stack></DialogContent><DialogActions>
        {!intent && <Button disabled={busy} onClick={close}>Cancel Board changes</Button>}
        <Button disabled={disabled || busy || !admitted || !review || !intent && (changed || conflict)} onClick={() => void save()}>{intent ? 'Retry this Board save' : 'Save Board details'}</Button>
      </DialogActions>
    </Dialog>
  </>;
}
