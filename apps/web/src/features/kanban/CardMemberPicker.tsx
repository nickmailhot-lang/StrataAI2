import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot, type WorkCard } from '../../api/workManagement';

type Option = { userId: string; displayName: string; assigned: boolean };
type Page = { items: Option[]; nextCursor: string | null; cardVersion: number };
type Intent = { userId: string; displayName: string; assigned: boolean; version: number; key: string };
type Props = { cardId: string; card?: WorkCard; snapshot: BoardSnapshot; disabled: boolean;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (pending: boolean) => void; onRefresh: () => void };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';

export function CardMemberPicker({ cardId, card, snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh }: Props) {
  const [open, setOpen] = useState(false); const [page, setPage] = useState<Page>(); const [intent, setIntent] = useState<Intent>();
  const [notice, setNotice] = useState<string>(); const [busy, setBusy] = useState(false); const [denied, setDenied] = useState(false);
  const controller = useRef<AbortController | undefined>(undefined); const epoch = useRef(0); const trigger = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false); const latestVersion = useRef(card?.version); latestVersion.current = card?.version;
  const admitted = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active';
  const activeCard = !!card && snapshot.lists.some(column => column.list.lifecycleState === 'active' && column.cards.some(item => item.id === cardId));
  const current = page && page.cardVersion === card?.version;
  useEffect(() => {
    if (restoreFocus.current && !busy && !disabled && activeCard) { restoreFocus.current = false; trigger.current?.focus({ preventScroll: true }); }
  }, [busy, disabled, activeCard]);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => {
    epoch.current++;
    setOpen(false); setPage(undefined); setIntent(undefined); setBusy(false); setDenied(false); setNotice(undefined);
    return () => { epoch.current++; controller.current?.abort(); controller.current = undefined; onBusyChange(false); };
  }, [admitted, cardId, snapshot.board.id, snapshot.board.organizationId, onBusyChange]);
  async function request(path: string, init: RequestInit = {}) {
    const pending = new AbortController(); controller.current = pending;
    return boundedWorkRead(signal => workRequest<unknown>(path, { ...init, signal }), pending.signal);
  }
  function failure(error: unknown, command?: Intent) {
    setPage(undefined);
    if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
      setDenied(true); setIntent(undefined); setNotice('Assignee editing is unavailable. Refresh the Board to check access.'); onRefresh();
    } else if (command && !(error instanceof WorkRequestError && [400, 409].includes(error.status))) {
      setIntent(command); setNotice('The change may have completed. Retry the same assignee change to confirm it.');
    } else { setIntent(undefined); setNotice('Assignees changed or could not be loaded. Refresh the Board, then reload member options.'); }
  }
  async function load(after?: string) {
    if (!admitted || !activeCard || disabled || controller.current || intent || denied) return;
    const ticket = epoch.current; const version = card!.version;
    setOpen(true); setBusy(true); onBusyChange(true); setNotice(undefined); setPage(undefined);
    try {
      const data = await request(`/cards/${encodeURIComponent(cardId)}/member-options${after ? `?after=${encodeURIComponent(after)}` : ''}`) as Record<string, unknown> | null;
      if (ticket !== epoch.current) return;
      if (latestVersion.current !== version) throw new Error('Retired member options');
      if (!data || data.organizationId !== snapshot.board.organizationId || data.boardId !== snapshot.board.id || data.cardId !== cardId
        || data.cardVersion !== version || !Array.isArray(data.items) || data.items.length > 50) throw new Error('Invalid member options');
      const ids = new Set<string>(); let previous = after?.toLowerCase();
      const items = data.items.map((item: unknown): Option => {
        const option = item as Record<string, unknown> | null;
        if (!option || !uuid(option.userId) || ids.has(option.userId.toLowerCase()) || (previous && option.userId.toLowerCase() <= previous)
          || typeof option.displayName !== 'string' || option.displayName.length > 160 || typeof option.assigned !== 'boolean') throw new Error('Invalid member option');
        previous = option.userId.toLowerCase(); ids.add(previous);
        return { userId: option.userId, displayName: option.displayName, assigned: option.assigned };
      });
      if (data.nextCursor !== null && (!uuid(data.nextCursor) || items.length !== 50 || data.nextCursor !== items.at(-1)?.userId)) throw new Error('Invalid options cursor');
      setPage({ items, nextCursor: data.nextCursor as string | null, cardVersion: version });
    } catch (error) { if (ticket === epoch.current) failure(error); }
    finally { if (ticket === epoch.current) { controller.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  async function change(option?: Option) {
    if (!admitted || disabled || controller.current || denied || (!intent && (!option || !current || !activeCard))) return;
    const command = intent ?? { userId: option!.userId, displayName: option!.displayName.trim() || 'Unnamed member', assigned: !option!.assigned, version: page!.cardVersion, key: crypto.randomUUID() };
    const ticket = epoch.current; setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await request(`/cards/${encodeURIComponent(cardId)}/members/${encodeURIComponent(command.userId)}?version=${command.version}`, {
        method: command.assigned ? 'PUT' : 'DELETE', headers: { 'Idempotency-Key': command.key },
      }) as { card?: Record<string, unknown>; userId?: unknown; assigned?: unknown; changed?: unknown } | null;
      if (ticket !== epoch.current) return;
      if (!value || value.userId !== command.userId || value.assigned !== command.assigned || typeof value.changed !== 'boolean'
        || value.card?.id !== cardId || value.card.organizationId !== snapshot.board.organizationId || value.card.boardId !== snapshot.board.id
        || value.card.version !== command.version + (value.changed ? 1 : 0)) throw new Error('Unconfirmed assignment');
      restoreFocus.current = true; setIntent(undefined); setPage(undefined); setOpen(false); onRefresh();
    } catch (error) { if (ticket === epoch.current) failure(error, command); }
    finally { if (ticket === epoch.current) { controller.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  if (!admitted) return null;
  return <Box sx={{ mt: 2 }}>
    <Button ref={trigger} disabled={busy || disabled || !!intent || denied || !activeCard} aria-expanded={open} onClick={() => void load()}>Edit Card assignees</Button>
    {open && <Stack component="section" aria-label="Edit Card assignees" spacing={1}>
      {busy && <Typography role="status">Updating member options…</Typography>}
      {notice && <Alert severity="warning">{notice}</Alert>}
      {intent ? <><Typography>{intent.assigned ? 'Assign' : 'Unassign'} {intent.displayName}</Typography><Button disabled={busy || disabled} onClick={() => void change()}>Retry assignee change</Button></> : <>
        {current && !notice && !disabled && page.items.map(option => <Stack key={option.userId} direction="row" useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
          <Typography sx={{ overflowWrap: 'anywhere' }}>{option.displayName.trim() || 'Unnamed member'}</Typography>
          <Button disabled={busy || disabled} onClick={() => void change(option)} aria-label={`${option.assigned ? 'Unassign' : 'Assign'} ${option.displayName.trim() || 'Unnamed member'}`}>{option.assigned ? 'Unassign' : 'Assign'}</Button>
        </Stack>)}
        {current && page.items.length === 0 && <Typography>No eligible Board members on this page.</Typography>}
        {!current && !busy && !notice && <Typography>Reload member options for the current Card.</Typography>}
        {current && page.nextCursor && <Button disabled={busy || disabled} onClick={() => void load(page.nextCursor!)}>Next members</Button>}
        <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          <Button disabled={busy || disabled || denied} onClick={() => void load()}>Reload member options</Button>
          <Button disabled={busy} onClick={() => { setOpen(false); trigger.current?.focus(); }}>Done editing assignees</Button>
        </Stack>
        {notice && <Button disabled={busy} onClick={onRefresh}>Refresh Board</Button>}
      </>}
    </Stack>}
  </Box>;
}
