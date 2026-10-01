import { useEffect, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
import type { CardMovePreview } from './cardMovePreview';

type Intent = { destination: string; before: string; version: number; key: string };
type Props = { card: WorkCard; snapshot: BoardSnapshot; disabled: boolean; onAcknowledged: () => void; onRefresh: () => void; onBusyChange?: (value: boolean) => void; onPreview?: (value?: CardMovePreview) => void };
const validRank = (rank: unknown): rank is string => typeof rank === 'string' && /^\d{30}$/.test(rank)
  && BigInt(rank) > 0n && BigInt(rank) < 10n ** 30n - 1n;

export function CardMoveControls({ card, snapshot, disabled, onAcknowledged, onRefresh, onBusyChange, onPreview }: Props) {
  const [review, setReview] = useState<{ version: number; destination: string; before: string }>();
  const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [acknowledged, setAcknowledged] = useState(false);
  const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true); const action = useRef<HTMLButtonElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false;
    if (pending.current) { pending.current.abort(); onBusyChange?.(false); onPreview?.(); }
  }; }, [onBusyChange, onPreview]);
  const lists = snapshot.lists.filter(column => column.list.lifecycleState === 'active');
  const changed = !!review && review.version !== card.version && !intent;
  const destinationActive = !!review && lists.some(column => column.list.id === review.destination);
  const neighbors = lists.find(column => column.list.id === review?.destination)?.cards.filter(value => value.id !== card.id) ?? [];
  const positionActive = !!review && (!review.before || neighbors.some(value => value.id === review.before));
  async function move() {
    if (pending.current || disabled || blocked || changed || !review || (!intent && (!destinationActive || !positionActive))) return;
    const command = intent ?? { destination: review.destination, before: review.before, version: review.version, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setAcknowledged(false);
    onBusyChange?.(true);
    onPreview?.({ cardId: card.id, destination: command.destination, before: command.before });
    let abort: (() => void) | undefined;
    const timer = setTimeout(() => controller.abort(), 15_000);
    try {
      const result = await Promise.race([
        apiFetch(`/cards/${encodeURIComponent(card.id)}/move`, { method: 'POST', signal: controller.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify({ destinationListId: command.destination, expectedVersion: command.version,
            ...(command.before ? { beforeCardId: command.before } : {}) }) })
          .then(async response => ({ status: response.status, body: await response.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Move interrupted'));
          controller.signal.addEventListener('abort', abort, { once: true }); }),
      ]);
      if (!mounted.current || pending.current !== controller) return;
      if ([400, 401, 403, 404, 409, 429].includes(result.status)) {
        setIntent(undefined); setBlocked(true);
        setNotice(result.status === 409 ? 'The card changed. Check the current Board before reviewing another move.'
          : 'This move is unavailable. Check the current Board before reviewing another move.');
        onRefresh(); return;
      }
      const value = result.body as { id?: unknown; organizationId?: unknown; boardId?: unknown; listId?: unknown; version?: unknown; rank?: unknown } | undefined;
      if (result.status !== 200 || value?.id !== card.id || value.organizationId !== snapshot.board.organizationId
        || value.boardId !== snapshot.board.id || value.listId !== command.destination || !validRank(value.rank)
        || !Number.isSafeInteger(value.version) || Number(value.version) <= command.version) throw new Error('Unconfirmed move');
      setIntent(undefined); setReview(undefined); setAcknowledged(true);
      setNotice('Move acknowledged. Current placement is being checked.'); onAcknowledged();
    } catch {
      if (mounted.current && pending.current === controller) {
        setIntent(command); setNotice('The move could not be confirmed. Retry this same move to recover its acknowledgment.');
        onRefresh();
      }
    } finally {
      clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort);
      if (pending.current === controller) { pending.current = undefined; if (mounted.current) { setBusy(false); onBusyChange?.(false); onPreview?.(); } }
    }
  }
  function closeReview() { setReview(undefined); setBlocked(false); setNotice(undefined); queueMicrotask(() => action.current?.focus()); }
  return <Stack spacing={1} sx={{ mt: 2 }}>
    {busy && <Typography role="status">Saving move. Placement is provisional until confirmed.</Typography>}
    {notice && <Alert severity={acknowledged ? 'success' : 'info'}>{notice}</Alert>}
    {changed && <Alert severity="info">The card changed while reviewing this move. Check the current Board and review again.</Alert>}
    {review && !intent && destinationActive && !positionActive && <Alert severity="info">The selected card is no longer in this list. Choose a current position.</Alert>}
    {!review ? <Button ref={action} disabled={disabled} onClick={() => { setAcknowledged(false); setNotice(undefined); setReview({ version: card.version, destination: '', before: '' }); }}>Move card</Button>
      : <><Typography>Choose an active list and a position for this card on this Board.</Typography>
        <TextField select autoFocus label="Destination list" value={review.destination} disabled={disabled || busy || !!intent || blocked || changed}
          onChange={event => setReview({ ...review, destination: event.target.value, before: '' })}>
          {lists.map(column => <MenuItem key={column.list.id} value={column.list.id}>{column.list.name}</MenuItem>)}
        </TextField>
        {destinationActive && <TextField select label="Card position" value={review.before} disabled={disabled || busy || !!intent || blocked || changed}
          onChange={event => setReview({ ...review, before: event.target.value })}>
          <MenuItem value="">End of list</MenuItem>
          {neighbors.map(value => <MenuItem key={value.id} value={value.id}>Before {value.title}</MenuItem>)}
          {!positionActive && <MenuItem value={review.before} disabled>Previously selected card</MenuItem>}
        </TextField>}
        <Button disabled={disabled || busy || blocked || changed || (!intent && (!destinationActive || !positionActive))} onClick={() => void move()}>{intent ? 'Retry this move' : 'Confirm card move'}</Button>
        {!intent && <Button disabled={busy} onClick={closeReview}>Cancel move</Button>}
        {(blocked || changed) && <Button disabled={busy} onClick={() => { closeReview(); onRefresh(); }}>Check current Board</Button>}
        {intent && <Typography>Keep this destination and position unchanged until the move is confirmed. Acknowledgment does not guarantee current placement after later edits.</Typography>}
      </>}
  </Stack>;
}
