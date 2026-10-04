import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Stack, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import { activityEvent, activityResult } from './activityTelemetry';
import type { BoardSnapshot, WorkCard } from '../../api/workManagement';

type Review = { id: string; title: string; rank: string; version: number; listId: string; listName: string; listVersion: number };
type Intent = Review & { key: string };
type Props = { cardId: string; card?: WorkCard; snapshot: BoardSnapshot; disabled: boolean;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (unresolved: boolean) => void;
  onRefresh: () => void; onAcknowledged: () => void };

// Owned by the detail route, outside the conditional Card editor/canvas row.
// Canonical removal after a lost committed response cannot erase this receipt intent.
export function CardArchiveControl({ cardId, card, snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh, onAcknowledged }: Props) {
  const [review, setReview] = useState<Review>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false); const [denied, setDenied] = useState(false);
  const [notice, setNotice] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const action = useRef<HTMLButtonElement>(null);
  const parent = snapshot.lists.find(column => column.list.lifecycleState === 'active' && column.cards.some(item => item.id === cardId))?.list;
  const authorized = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active' && !denied;
  const admitted = authorized && !!card && card.id === cardId && !!parent
    && Number.isSafeInteger(card.version) && card.version > 0 && Number.isSafeInteger(parent.version) && Number(parent.version) > 0;
  const changed = !!review && !intent && (!admitted || card?.version !== review.version || card.title !== review.title
    || card.rank !== review.rank || parent?.id !== review.listId || parent.name !== review.listName || parent.version !== review.listVersion);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false;
    if (pending.current) { pending.current.abort(); onBusyChange(false); } }; }, [onBusyChange]);
  useEffect(() => {
    if (authorized || (!review && !intent && !pending.current)) return;
    pending.current?.abort(); pending.current = undefined; setBusy(false); onBusyChange(false);
    setReview(undefined); setIntent(undefined); setNotice('This Card or archive action is unavailable.');
  }, [authorized, review, intent, onBusyChange]);
  async function archive() {
    if (pending.current || disabled || !authorized || !review || (!intent && (changed || blocked))) return;
    const started = performance.now(); activityEvent('card_archive', intent ? 'retry' : 'use');
    const command = intent ?? { ...review, key: crypto.randomUUID() };
    const c = new AbortController(); pending.current = c; setBusy(true); onBusyChange(true); setNotice(undefined);
    let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
    try {
      const result = await Promise.race([
        apiFetch(`/cards/${encodeURIComponent(command.id)}/archive`, { method: 'POST', signal: c.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ version: command.version }) })
          .then(async response => ({ status: response.status, value: await response.json().catch(() => undefined) as unknown })),
        new Promise<never>((_, reject) => { abort = () => reject(new Error('Archive interrupted'));
          c.signal.addEventListener('abort', abort, { once: true }); timer = setTimeout(() => c.abort(), 15_000); }),
      ]);
      if (!mounted.current || pending.current !== c) return;
      if ([401, 403, 404].includes(result.status)) {
        activityResult('card_archive', false, started);
        setIntent(undefined); setReview(undefined); setDenied(true); setNotice('This Card or archive action is unavailable.'); onRefresh();
      } else if ([400, 409].includes(result.status)) {
        activityEvent('card_archive', 'conflict'); activityResult('card_archive', false, started);
        setIntent(undefined); setBlocked(true); setNotice('This archive could not be applied. Check the Board and review the current Card.'); onRefresh();
      } else {
        const ack = result.value as { id?: unknown; organizationId?: unknown; boardId?: unknown; listId?: unknown; title?: unknown;
          rank?: unknown; version?: unknown; lifecycleState?: unknown } | undefined;
        if (result.status !== 200 || ack?.id !== command.id || ack.organizationId !== snapshot.board.organizationId || ack.boardId !== snapshot.board.id
          || ack.listId !== command.listId || ack.title !== command.title || ack.rank !== command.rank || ack.lifecycleState !== 'archived'
          || ack.version !== command.version + 1) throw new Error('Unconfirmed archive');
        activityResult('card_archive', true, started); setIntent(undefined); setReview(undefined); setNotice('Card archive acknowledged. Current Board state is being checked.'); onAcknowledged();
      }
    } catch { if (mounted.current && pending.current === c) {
      activityEvent('card_archive', 'exception'); activityResult('card_archive', false, started);
      setIntent(command); setNotice('The archive could not be confirmed. Retry this same archive to recover its acknowledgment.'); onRefresh();
    } } finally {
      clearTimeout(timer); if (abort) c.signal.removeEventListener('abort', abort);
      if (pending.current === c) { pending.current = undefined;
        if (mounted.current) { setBusy(false); onBusyChange(false); } }
    }
  }
  return <Stack spacing={1} sx={{ mt: 2 }}>
    {admitted && !intent && <Button ref={action} disabled={disabled || busy} onClick={() => {
      activityEvent('card_archive', 'open'); setReview({ id: cardId, title: card.title, rank: card.rank, version: card.version,
        listId: parent.id, listName: parent.name, listVersion: parent.version! }); setBlocked(false); setNotice(undefined);
    }}>Archive Card</Button>}
    {notice && <Typography role="status">{notice}</Typography>}
    {review && <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>Archive {review.title} from {review.listName}?</Typography>
      <Typography>This hides the Card from the Board. It can be restored from Archived cards when its parent List is active.</Typography>
      {(changed || blocked) && !intent && <Alert severity="warning">This review changed. Cancel and review the current Card before archiving.</Alert>}
      {intent && <Alert severity="info">The original archive is unresolved. Retry that same request, even if the Card has left the active Board.</Alert>}
      <Button disabled={busy} onClick={onRefresh}>Check current Board for this Card archive</Button>
      {!intent && <Button disabled={busy} onClick={() => { setReview(undefined); setNotice(undefined); setBlocked(false); action.current?.focus({ preventScroll: true }); }}>Cancel Card archive</Button>}
      <Button disabled={disabled || busy || !authorized || (!intent && (changed || blocked))} onClick={() => void archive()}>
        {intent ? 'Retry this Card archive' : 'Confirm Card archive'}
      </Button>
    </>}
  </Stack>;
}
