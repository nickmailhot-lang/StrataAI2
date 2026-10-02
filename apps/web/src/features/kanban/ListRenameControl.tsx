import { useEffect, useRef, useState } from 'react';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, TextField, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';
import { apiFetch } from '../../api/apiFetch';

type List = BoardSnapshot['lists'][number]['list'];
type Review = { name: string; rank: string; version: number };
type Intent = Review & { key: string };
type Props = { list: List; snapshot: BoardSnapshot; disabled: boolean; onRefresh: () => void;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (id: string, unresolved: boolean) => void };

// PRD-07-FR-003 / TC-06/07/08: preserve drafts and the original keyed intent.
export function ListRenameControl({ list, snapshot, disabled, onRefresh, onBusyChange, onRecoveryChange }: Props) {
  const [review, setReview] = useState<Review>();
  const [name, setName] = useState('');
  const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [denied, setDenied] = useState(false);
  const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true);
  const action = useRef<HTMLButtonElement>(null);
  const admitted = snapshot.access.canMove && snapshot.board.lifecycleState === 'active'
    && list.lifecycleState === 'active' && Number.isSafeInteger(list.version) && Number(list.version) > 0;
  const changed = !!review && !intent && (list.version !== review.version || list.name !== review.name || list.rank !== review.rank);
  useEffect(() => { onRecoveryChange(list.id, !!intent); return () => onRecoveryChange(list.id, false); }, [list.id, intent, onRecoveryChange]);
  useEffect(() => { mounted.current = true; return () => {
    mounted.current = false; pending.current?.abort(); if (pending.current) onBusyChange(false);
  }; }, [onBusyChange]);

  const currentReview = () => ({ name: list.name, rank: list.rank, version: list.version! });
  function open() { setReview(currentReview()); setName(list.name); setConflict(false); setNotice(undefined); }
  function close() { if (busy || intent) return; setReview(undefined); setNotice(undefined); setConflict(false); }
  async function save() {
    if (pending.current || disabled || !admitted || denied || !review || (!intent && (changed || conflict))) return;
    const normalized = name.trim();
    if (!intent && (!normalized || normalized.length > 160)) { setNotice('Use a list name with 1 to 160 characters.'); return; }
    const command = intent ?? { ...review, name: normalized, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller;
    setBusy(true); onBusyChange(true); setNotice(undefined);
    let deadline: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
    try {
      const result = await Promise.race([
        apiFetch(`/lists/${encodeURIComponent(list.id)}`, { method: 'PATCH', signal: controller.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify({ name: command.name, version: command.version }) })
          .then(async response => ({ status: response.status, value: await response.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Rename interrupted'));
          controller.signal.addEventListener('abort', abort, { once: true });
          deadline = setTimeout(() => controller.abort(), 15_000); }),
      ]);
      if (!mounted.current || pending.current !== controller) return;
      if ([401, 403, 404].includes(result.status)) {
        setIntent(undefined); setReview(undefined); setName(''); setDenied(true);
        setNotice('This list or action is unavailable.'); onRefresh(); return;
      }
      if (result.status === 409) {
        setIntent(undefined); setConflict(true);
        setNotice('This rename could not be applied. Check the current list before reviewing another save.'); onRefresh(); return;
      }
      if (result.status === 400) { setIntent(undefined); setNotice('Use a list name with 1 to 160 characters.'); return; }
      const value = result.value as { id?: unknown; organizationId?: unknown; boardId?: unknown; name?: unknown;
        rank?: unknown; version?: unknown; lifecycleState?: unknown } | undefined;
      if (result.status !== 200 || value?.id !== list.id || value.organizationId !== snapshot.board.organizationId
        || value.boardId !== snapshot.board.id || value.name !== command.name || value.rank !== command.rank
        || value.lifecycleState !== 'active' || value.version !== command.version + 1 || !Number.isSafeInteger(value.version))
        throw new Error('Unconfirmed rename');
      setIntent(undefined); setReview(undefined); setName('');
      setNotice('List rename acknowledged. Checking current list.'); onRefresh();
    } catch {
      if (mounted.current && pending.current === controller) {
        setIntent(command); setNotice('The rename could not be confirmed. Retry this same rename to recover its acknowledgment.'); onRefresh();
      }
    } finally {
      clearTimeout(deadline); if (abort) controller.signal.removeEventListener('abort', abort);
      if (pending.current === controller) { pending.current = undefined;
        if (mounted.current) { setBusy(false); onBusyChange(false); } }
    }
  }
  return <>
    {admitted && !denied && <IconButton ref={action} size="small" disabled={disabled || busy || !!intent}
      aria-label={`Rename ${list.name} list`} onClick={open}><EditOutlinedIcon /></IconButton>}
    {!review && notice && <Typography role="status" variant="body2">{notice}</Typography>}
    <Dialog open={!!review} onClose={close} disableRestoreFocus
      slotProps={{ transition: { onExited: () => action.current?.focus({ preventScroll: true }) } }} fullWidth maxWidth="sm">
      <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}>
        <DialogTitle>Rename list</DialogTitle>
        <DialogContent>
          {notice && <Alert severity="info">{notice}</Alert>}
          {changed && <Alert severity="warning">This list changed elsewhere. Your draft is preserved.</Alert>}
          <TextField autoFocus required fullWidth margin="normal" label="New list name" value={name}
            disabled={busy || !!intent || denied} onChange={event => setName(event.target.value)}
            slotProps={{ htmlInput: { maxLength: 160 } }} />
          {(changed || conflict) && !intent && <Button disabled={busy || disabled || !admitted} onClick={() => {
            setReview(currentReview()); setName(list.name); setConflict(false); setNotice(undefined); onRefresh();
          }}>Discard draft and use current list</Button>}
          {intent && <Typography>Keep this name unchanged until the original acknowledgment is recovered.</Typography>}
        </DialogContent>
        <DialogActions>
          {!intent && <Button disabled={busy} onClick={close}>Cancel rename</Button>}
          <Button type="submit" disabled={disabled || busy || !admitted || denied || (!intent && (changed || conflict))}>
            {intent ? 'Retry this rename' : 'Save list name'}
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  </>;
}
