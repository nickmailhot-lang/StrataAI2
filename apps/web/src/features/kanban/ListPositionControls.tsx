import { useEffect, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';
import { apiFetch } from '../../api/apiFetch';
import type { ListMovePreview } from './listMovePreview';

type List = BoardSnapshot['lists'][number]['list'];
type Review = { name: string; version: number; before: string };
type Intent = Review & { key: string };
type Props = { list: List; snapshot: BoardSnapshot; disabled: boolean; onRefresh: () => void; onBusyChange: (value: boolean) => void; onPreview?: (value?: ListMovePreview) => void };

export function ListPositionControls({ list, snapshot, disabled, onRefresh, onBusyChange, onPreview }: Props) {
  const [review, setReview] = useState<Review>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(true); const action = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  useEffect(() => { if (focusRequested.current && !disabled && !busy && !review) { action.current?.focus(); focusRequested.current = false; } }, [disabled, busy, review]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false;
    if (pending.current) { pending.current.abort(); onBusyChange(false); onPreview?.(); }
  }; }, [onBusyChange, onPreview]);
  const neighbors = snapshot.lists.filter(column => column.list.lifecycleState === 'active' && column.list.id !== list.id);
  const admitted = snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && list.lifecycleState === 'active'
    && Number.isSafeInteger(list.version) && Number(list.version) > 0;
  const changed = !!review && !intent && (review.version !== list.version || review.name !== list.name);
  const positioned = !!review && (!review.before || neighbors.some(column => column.list.id === review.before));
  function close() { focusRequested.current = true; setReview(undefined); setBlocked(false); setNotice(undefined); }
  async function move() {
    if (pending.current || disabled || !admitted || blocked || changed || !review || (!intent && !positioned)) return;
    const command = intent ?? { ...review, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); onBusyChange(true); setNotice(undefined);
    if (!intent) onPreview?.({ listId: list.id, before: command.before });
    let abort: (() => void) | undefined; const timer = setTimeout(() => controller.abort(), 15_000);
    try {
      const result = await Promise.race([
        apiFetch(`/lists/${encodeURIComponent(list.id)}`, { method: 'PATCH', signal: controller.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify({ name: command.name, version: command.version,
            ...(command.before ? { beforeListId: command.before } : { moveToEnd: true }) }) })
          .then(async response => ({ status: response.status, body: await response.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Interrupted'));
          controller.signal.addEventListener('abort', abort, { once: true }); }),
      ]);
      if (!mounted.current || pending.current !== controller) return;
      if ([400, 401, 403, 404, 409, 429].includes(result.status)) {
        setIntent(undefined); setBlocked(true); setNotice('This list move is unavailable. Check current ordering before reviewing another move.'); onRefresh(); return;
      }
      const value = result.body as { id?: unknown; organizationId?: unknown; boardId?: unknown; rank?: unknown; version?: unknown } | undefined;
      if (result.status !== 200 || value?.id !== list.id || value.organizationId !== snapshot.board.organizationId || value.boardId !== snapshot.board.id
        || typeof value.rank !== 'string' || !/^\d{30}$/.test(value.rank) || BigInt(value.rank) <= 0n || BigInt(value.rank) >= 10n ** 30n - 1n
        || !Number.isSafeInteger(value.version) || Number(value.version) <= command.version) throw new Error('Unconfirmed');
      setIntent(undefined); setReview(undefined); setNotice('List move acknowledged. Current ordering is being checked.');
      focusRequested.current = true; onRefresh();
    } catch {
      if (mounted.current && pending.current === controller) { setIntent(command); setNotice('The list move could not be confirmed. Retry this same move to recover its acknowledgment.'); onRefresh(); }
    } finally {
      clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort);
      if (pending.current === controller) { pending.current = undefined; if (mounted.current) { setBusy(false); onBusyChange(false); onPreview?.(); } }
    }
  }
  return <Stack spacing={1} sx={{ mt: 1 }}>
    {notice && <Alert severity="info">{notice}</Alert>}
    {!review ? <Button ref={action} disabled={disabled || !admitted} onClick={() => { setNotice(undefined); setReview({ name: list.name, version: list.version!, before: '' }); }}>Move {list.name} list</Button>
      : <><Typography>Choose this list’s position on the Board.</Typography>
        <TextField select label={`Position for ${review.name}`} value={review.before} disabled={disabled || busy || !!intent || blocked || changed}
          onChange={event => setReview({ ...review, before: event.target.value })}>
          <MenuItem value="">End of Board</MenuItem>
          {neighbors.map(column => <MenuItem key={column.list.id} value={column.list.id}>Before {column.list.name}</MenuItem>)}
          {!positioned && <MenuItem value={review.before} disabled>Previously selected list</MenuItem>}
        </TextField>
        {changed && <Alert severity="info">This list changed. Check current ordering and review again.</Alert>}
        {!positioned && !intent && <Alert severity="info">The selected list is unavailable. Choose a current position.</Alert>}
        <Button disabled={disabled || busy || !admitted || blocked || changed || (!intent && !positioned)} onClick={() => void move()}>{intent ? 'Retry this list move' : 'Confirm list move'}</Button>
        {!intent && <Button disabled={busy} onClick={close}>Cancel list move</Button>}
        {(blocked || changed) && <Button disabled={busy} onClick={() => { close(); onRefresh(); }}>Check current ordering</Button>}
        {busy && <Typography role="status">{intent ? 'Checking the original list acknowledgment.' : 'Saving list position. Ordering is provisional until confirmed.'}</Typography>}
        {intent && <Typography>Keep this position unchanged until acknowledgment recovery. Later edits may have changed current ordering.</Typography>}
      </>}
  </Stack>;
}
