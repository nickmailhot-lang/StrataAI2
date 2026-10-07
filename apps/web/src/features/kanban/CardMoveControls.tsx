import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';
import type { CardMovePreview } from './cardMovePreview';

type Intent = { destination: string; before: string; version: number; key: string };
export type CardDropRequest = { cardId: string; version: number; destination: string; before: string; nonce: string };
type Props = { card: WorkCard; snapshot: BoardSnapshot; disabled: boolean; onAcknowledged: () => void; onRefresh: () => void; onBusyChange?: (value: boolean) => void; onPreview?: (value?: CardMovePreview) => void; dropRequest?: CardDropRequest; onRecoveryChange?: (cardId: string, unresolved: boolean) => void };
const validRank = (rank: unknown): rank is string => typeof rank === 'string' && /^\d{30}$/.test(rank)
  && BigInt(rank) > 0n && BigInt(rank) < 10n ** 30n - 1n;

export function CardMoveControls({ card, snapshot, disabled, onAcknowledged, onRefresh, onBusyChange, onPreview, dropRequest, onRecoveryChange }: Props) {
  const [review, setReview] = useState<{ version: number; destination: string; before: string }>();
  const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false);
  const [blocked, setBlocked] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [acknowledged, setAcknowledged] = useState(false);
  useEffect(() => { onRecoveryChange?.(card.id, !!intent || blocked); return () => onRecoveryChange?.(card.id, false); }, [card.id, intent, blocked, onRecoveryChange]);
  const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(true); const action = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  useEffect(() => {
    if (focusRequested.current && !disabled && !busy && !review) {
      action.current?.focus(); focusRequested.current = false;
    }
  }, [disabled, busy, review]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false;
    if (pending.current) { pending.current.abort(); onBusyChange?.(false); onPreview?.(); }
  }; }, [onBusyChange, onPreview]);
  const lists = snapshot.lists.filter(column => column.list.lifecycleState === 'active');
  const changed = !!review && review.version !== card.version && !intent;
  const destinationActive = !!review && lists.some(column => column.list.id === review.destination);
  const neighbors = lists.find(column => column.list.id === review?.destination)?.cards.filter(value => value.id !== card.id) ?? [];
  const positionActive = !!review && (!review.before || neighbors.some(value => value.id === review.before));
  const consumeDrop = useEffectEvent((request: CardDropRequest) => {
    if (request.cardId !== card.id || intent || blocked || disabled || pending.current) {
      if (!intent && !pending.current) onPreview?.();
      return;
    }
    const selected = { version: request.version, destination: request.destination, before: request.before };
    setReview(selected);
    if (request.version !== card.version) {
      onPreview?.();
      setBlocked(true); setNotice('The card changed during dragging. Check the current Board before reviewing another move.'); onRefresh(); return;
    }
    void move(selected);
  });
  useEffect(() => {
    if (!dropRequest) return;
    // Strict Mode replays mount effects before any user command should start.
    // Retire that provisional effect before it can publish an abortable request.
    let retired = false;
    queueMicrotask(() => { if (!retired) consumeDrop(dropRequest); });
    return () => { retired = true; };
  }, [dropRequest]);
  async function move(selected?: NonNullable<typeof review>) {
    const proposed = selected ?? review;
    const destination = lists.find(column => column.list.id === proposed?.destination);
    const validPosition = !!proposed && !!destination && (!proposed.before || destination.cards.some(value => value.id === proposed.before && value.id !== card.id));
    if (pending.current || disabled || blocked || !proposed || (selected && intent)
      || (!intent && (proposed.version !== card.version || !validPosition))) {
      if (selected && !intent && !pending.current) onPreview?.();
      return;
    }
    const command = intent ?? { ...proposed, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setAcknowledged(false);
    onBusyChange?.(true);
    if (!intent) onPreview?.({ cardId: card.id, destination: command.destination, before: command.before });
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
  function closeReview() { focusRequested.current = true; setReview(undefined); setBlocked(false); setNotice(undefined); }
  return <Stack spacing={1} sx={{ mt: 2 }}>
    {busy && <Typography role="status">{intent ? 'Checking the original move acknowledgment. Current placement may reflect later edits.'
      : 'Saving move. Placement is provisional until confirmed.'}</Typography>}
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
