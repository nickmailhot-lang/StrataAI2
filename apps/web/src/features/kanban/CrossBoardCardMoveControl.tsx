import { useEffect, useRef, useState } from 'react';
import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead, type BoardSnapshot, type WorkCard } from '../../api/workManagement';

type Board = { id: string; name: string; version: number };
type Draft = { id: string; title: string; version: number; sourceBoardId: string; destinationBoardId: string; destinationListId: string };
type Intent = Draft & { key: string };
type Props = { card?: WorkCard; selectedCardId: string; snapshot: BoardSnapshot; disabled: boolean; unavailable: boolean;
  onBusyChange: (value: boolean) => void; onRecoveryChange: (value: boolean) => void; onAcknowledged?: (cardId: string, boardId: string) => void; onRefresh: () => void };
const guid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';

// Board-owned recovery survives the moved Card disappearing from its source canvas.
export function CrossBoardCardMoveControl({ card, selectedCardId, snapshot, disabled, unavailable,
  onBusyChange, onRecoveryChange, onAcknowledged, onRefresh }: Props) {
  const [draft, setDraft] = useState<Draft>(); const [intent, setIntent] = useState<Intent>();
  const [boards, setBoards] = useState<Board[]>(); const [target, setTarget] = useState<BoardSnapshot>();
  const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false);
  const [denied, setDenied] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const alive = useRef(false);
  const viewer = useRef<string | undefined>(undefined); const retired = useRef(false);
  const admitted = snapshot.access.canMove && snapshot.board.lifecycleState === 'active' && !denied;
  const sourceActive = !!card && snapshot.lists.some(c => c.list.lifecycleState === 'active' && c.cards.some(v => v.id === card.id));
  const changed = !!draft && !intent && (!sourceActive || !card || card.id !== draft.id || card.version !== draft.version);
  useEffect(() => { alive.current = true; return () => { alive.current = false; pending.current?.abort(); onBusyChange(false); }; }, [onBusyChange]);
  useEffect(() => { onRecoveryChange(!!intent || busy); return () => onRecoveryChange(false); }, [intent, busy, onRecoveryChange]);
  useEffect(() => {
    if (admitted) return;
    retired.current = true; pending.current?.abort(); pending.current = undefined; viewer.current = undefined;
    setBusy(false); onBusyChange(false); setDraft(undefined); setIntent(undefined); setTarget(undefined); setBoards(undefined);
  }, [admitted, onBusyChange]);

  function refuse() {
    retired.current = true; viewer.current = undefined; setDenied(true); setDraft(undefined); setIntent(undefined); setBoards(undefined); setTarget(undefined);
    setNotice('This move is unavailable.'); onRefresh();
  }
  async function request(path: string, options: RequestInit = {}) {
    if (pending.current) return;
    const c = new AbortController(); pending.current = c; setBusy(true); onBusyChange(true);
    try {
      const result = await boundedWorkRead(async signal => {
        async function identity() {
          signal.throwIfAborted();
          const response = await apiFetch('/me', { signal });
          signal.throwIfAborted();
          const value = await response.json().catch(() => undefined) as { id?: unknown } | undefined;
          if (response.status !== 200 || !guid(value?.id)) { refuse(); throw new Error('Identity unavailable'); }
          return value.id;
        }
        const before = await identity();
        if (viewer.current && viewer.current !== before) { refuse(); return; }
        viewer.current = before;
        const response = await apiFetch(path, { ...options, signal });
        const value: unknown = await response.json().catch(() => undefined);
        if (await identity() !== before) { refuse(); return; }
        return { status: response.status, value };
      }, c.signal);
      if (!alive.current || pending.current !== c) return;
      return result;
    } finally {
      if (pending.current === c) { pending.current = undefined; if (alive.current) { setBusy(false); onBusyChange(false); } }
    }
  }
  async function discover() {
    if (pending.current || disabled || unavailable || !admitted || !card || !sourceActive || intent) return;
    setDraft({ id: card.id, title: card.title, version: card.version, sourceBoardId: snapshot.board.id, destinationBoardId: '', destinationListId: '' });
    setBoards(undefined); setTarget(undefined); setNotice(undefined); setBlocked(false);
    try {
      const result = await request(`/organizations/${encodeURIComponent(snapshot.board.organizationId)}/boards`);
      if (!result) return;
      if ([401, 403, 404].includes(result.status)) { refuse(); return; }
      if (result.status !== 200 || !Array.isArray(result.value)) throw new Error('Invalid directory');
      const values = result.value as Board[];
      if (values.some(b => !b || !guid(b.id) || typeof b.name !== 'string' || !b.name.trim() || b.name.length > 160
        || !Number.isSafeInteger(b.version) || b.version <= 0) || new Set(values.map(b => b.id)).size !== values.length)
        throw new Error('Invalid directory');
      setBoards(values.filter(b => b.id !== snapshot.board.id));
    } catch { if (alive.current && !retired.current) setNotice('Destinations could not be loaded. Try again.'); }
  }
  async function selectBoard(id: string) {
    if (pending.current || intent || disabled || unavailable || !draft || !boards?.some(b => b.id === id)) return;
    setDraft({ ...draft, destinationBoardId: id, destinationListId: '' }); setTarget(undefined); setNotice(undefined);
    try {
      const result = await request(`/boards/${encodeURIComponent(id)}`);
      if (!result) return;
      if (result.status === 401) { refuse(); return; }
      const value = result.value as BoardSnapshot | undefined;
      if (result.status !== 200 || value?.board?.id !== id || value.board.organizationId !== snapshot.board.organizationId
        || value.board.lifecycleState !== 'active' || value.access?.canMove !== true || !Array.isArray(value.lists)
        || value.lists.some(c => !c?.list || !guid(c.list.id) || typeof c.list.name !== 'string'
          || !['active', 'archived'].includes(c.list.lifecycleState))) {
        setNotice('This destination is unavailable for moving. Choose another Board.'); return;
      }
      setTarget(value);
    } catch { if (alive.current && !retired.current) setNotice('The destination could not be checked. Choose it again.'); }
  }
  async function move() {
    if (pending.current || disabled || unavailable || !admitted || !draft || blocked || (!intent && (changed
      || !target?.lists.some(c => c.list.id === draft.destinationListId && c.list.lifecycleState === 'active')))) return;
    const command = intent ?? { ...draft, key: crypto.randomUUID() }; setNotice(undefined);
    try {
      const result = await request(`/cards/${encodeURIComponent(command.id)}/move`, { method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
        body: JSON.stringify({ sourceBoardId: command.sourceBoardId, destinationListId: command.destinationListId, expectedVersion: command.version }) });
      if (!result) return;
      if ([401, 403, 404].includes(result.status)) { refuse(); return; }
      if ([400, 409, 429].includes(result.status)) {
        setIntent(undefined); setBlocked(true); setTarget(undefined);
        setNotice('This move could not be applied. Check the current Board and review again.'); onRefresh(); return;
      }
      const ack = result.value as { id?: unknown; organizationId?: unknown; boardId?: unknown; listId?: unknown; version?: unknown; rank?: unknown } | undefined;
      if (result.status !== 200 || ack?.id !== command.id || ack.organizationId !== snapshot.board.organizationId
        || ack.boardId !== command.destinationBoardId || ack.listId !== command.destinationListId
        || !Number.isSafeInteger(ack.version) || Number(ack.version) <= command.version
        || typeof ack.rank !== 'string' || !/^\d{30}$/.test(ack.rank) || BigInt(ack.rank) <= 0n || BigInt(ack.rank) >= 10n ** 30n - 1n)
        throw new Error('Unconfirmed move');
      setIntent(undefined); setDraft(undefined); setBoards(undefined); setTarget(undefined);
      setNotice('Move acknowledged. Check the destination Board for current placement.'); onAcknowledged?.(command.id, command.destinationBoardId); onRefresh();
    } catch { if (alive.current && !retired.current) { setIntent(command); setNotice('The move could not be confirmed. Retry this same move to recover its acknowledgment.'); onRefresh(); } }
  }
  const locked = disabled || unavailable || busy || !!intent || changed || blocked;
  if (unavailable) return draft ? <Typography role="status">Checking current source Board access.</Typography> : null;
  return <Stack spacing={1} sx={{ mt: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {admitted && sourceActive && !draft && card?.id === selectedCardId && <Button disabled={disabled || unavailable || busy} onClick={() => void discover()}>Move to another Board</Button>}
    {draft && admitted && <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>Move {draft.title} to the end of an active destination List.</Typography>
      <Alert severity="info">Labels are copied into the destination. Assignments and personal reminders require destination access. The Card keeps its ID and history.</Alert>
      {unavailable && <Typography role="status">Checking current source Board access.</Typography>}
      {changed && <Alert severity="warning">The Card changed. Check the current Board and review again.</Alert>}
      {boards?.length === 0 && <Typography>No other destination Boards are available.</Typography>}
      <TextField select label="Destination Board" value={draft.destinationBoardId} disabled={locked || !boards} onChange={e => void selectBoard(e.target.value)}>
        <MenuItem value="">Choose a Board</MenuItem>{boards?.map(b => <MenuItem key={b.id} value={b.id}>{b.name}</MenuItem>)}
      </TextField>
      <TextField select label="Destination List on another Board" value={draft.destinationListId} disabled={locked || !target}
        onChange={e => setDraft({ ...draft, destinationListId: e.target.value })}>
        <MenuItem value="">Choose a List</MenuItem>{target?.lists.filter(c => c.list.lifecycleState === 'active').map(c => <MenuItem key={c.list.id} value={c.list.id}>{c.list.name}</MenuItem>)}
      </TextField>
      <Button disabled={disabled || unavailable || busy || blocked || (!intent && (changed || !draft.destinationListId || !target))} onClick={() => void move()}>
        {intent ? 'Retry this cross-Board move' : 'Confirm move to another Board'}
      </Button>
      {!intent && <Button disabled={busy} onClick={() => { setDraft(undefined); setTarget(undefined); setBoards(undefined); setNotice(undefined); }}>Cancel cross-Board move</Button>}
      {intent && <Typography>Keep the original source and destination until acknowledgment is recovered. Current placement may reflect later edits.</Typography>}
    </>}
  </Stack>;
}
