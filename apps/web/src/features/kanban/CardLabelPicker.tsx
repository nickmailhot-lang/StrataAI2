import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot, type WorkCard } from '../../api/workManagement';

type Option = { label: { id: string; name: string; color: string }; assigned: boolean };
type Page = { items: Option[]; nextCursor: string | null; cardVersion: number };
type Intent = { labelId: string; name: string; assigned: boolean; version: number; key: string };
type Props = { cardId: string; card?: WorkCard; snapshot: BoardSnapshot; disabled: boolean;
  onBusyChange: (busy: boolean) => void; onRecoveryChange: (pending: boolean) => void; onRefresh: () => void };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
const palette = ['green', 'yellow', 'orange', 'red', 'purple', 'blue', 'sky', 'lime', 'pink', 'black'];
export function CardLabelPicker({ cardId, card, snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh }: Props) {
  const [open, setOpen] = useState(false); const [page, setPage] = useState<Page>(); const [intent, setIntent] = useState<Intent>();
  const [notice, setNotice] = useState<string>(); const [busy, setBusy] = useState(false); const [denied, setDenied] = useState(false);
  const controller = useRef<AbortController | undefined>(undefined); const epoch = useRef(0); const trigger = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  const retryTrigger = useRef<HTMLButtonElement>(null); const recoverFocus = useRef(false);
  const admitted = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active';
  const activeCard = card && snapshot.lists.some(column => column.list.lifecycleState === 'active' && column.cards.some(item => item.id === cardId));
  const current = page && page.cardVersion === card?.version;
  useEffect(() => {
    if (restoreFocus.current && !busy && !disabled && activeCard) { restoreFocus.current = false; trigger.current?.focus({ preventScroll: true }); }
  }, [busy, disabled, activeCard]);
  useEffect(() => {
    if (intent && recoverFocus.current && !busy && !disabled && activeCard
      && (document.activeElement === document.body || document.activeElement === retryTrigger.current))
      retryTrigger.current?.focus({ preventScroll: true });
  }, [intent, busy, disabled, activeCard]);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => {
    epoch.current++;
    if (!admitted) { setOpen(false); setPage(undefined); setIntent(undefined); setBusy(false); }
    return () => { epoch.current++; controller.current?.abort(); controller.current = undefined; onBusyChange(false); };
  }, [admitted, cardId, snapshot.board.id, snapshot.board.organizationId, onBusyChange]);
  async function request(path: string, init: RequestInit = {}) {
    const pending = new AbortController(); controller.current = pending;
    return boundedWorkRead(signal => workRequest<unknown>(path, { ...init, signal }), pending.signal);
  }
  function failure(error: unknown, command?: Intent) {
    setPage(undefined);
    if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
      setDenied(true); setIntent(undefined); setNotice('Label editing is unavailable. Refresh the Board to check access.'); onRefresh();
    } else if (command && !(error instanceof WorkRequestError && [400, 409].includes(error.status))) {
      recoverFocus.current = true; setIntent(command); setNotice('The change may have completed. Retry the same label change to confirm it.');
    } else { setIntent(undefined); setNotice('Labels changed or could not be loaded. Refresh the Board, then reload label options.'); }
  }
  async function load(after?: string) {
    if (!admitted || !activeCard || disabled || controller.current || intent || denied) return;
    const ticket = epoch.current; setOpen(true); setBusy(true); onBusyChange(true); setNotice(undefined); setPage(undefined);
    try {
      const data = await request(`/cards/${encodeURIComponent(cardId)}/label-options${after ? `?after=${encodeURIComponent(after)}` : ''}`) as Record<string, unknown> | null;
      if (ticket !== epoch.current) return;
      if (!data || data.organizationId !== snapshot.board.organizationId || data.boardId !== snapshot.board.id || data.cardId !== cardId
        || data.cardVersion !== card!.version || !Array.isArray(data.items) || data.items.length > 50) throw new Error('Invalid label options');
      const ids = new Set<string>();
      const items = data.items.map((item: unknown): Option => {
        const option = item as { label?: Record<string, unknown>; assigned?: unknown } | null; const label = option?.label;
        if (!label || !uuid(label.id) || ids.has(label.id) || label.organizationId !== snapshot.board.organizationId || label.boardId !== snapshot.board.id
          || label.deleted !== false || typeof label.name !== 'string' || label.name.length > 160 || typeof label.color !== 'string'
          || !palette.includes(label.color) || typeof option?.assigned !== 'boolean') throw new Error('Invalid label option');
        ids.add(label.id); return { label: { id: label.id, name: label.name, color: label.color }, assigned: option.assigned };
      });
      if (data.nextCursor !== null && (!uuid(data.nextCursor) || items.length !== 50 || data.nextCursor !== items.at(-1)?.label.id || data.nextCursor === after)) throw new Error('Invalid options cursor');
      setPage({ items, nextCursor: data.nextCursor as string | null, cardVersion: data.cardVersion as number });
    } catch (error) { if (ticket === epoch.current) failure(error); }
    finally { if (ticket === epoch.current) { controller.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  async function change(option?: Option) {
    if (!admitted || disabled || controller.current || denied || (!intent && (!option || !current || !activeCard))) return;
    const command = intent ?? { labelId: option!.label.id, name: option!.label.name || `${option!.label.color} label`, assigned: !option!.assigned, version: page!.cardVersion, key: crypto.randomUUID() };
    const ticket = epoch.current; setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await request(`/cards/${encodeURIComponent(cardId)}/labels/${encodeURIComponent(command.labelId)}?version=${command.version}`, {
        method: command.assigned ? 'PUT' : 'DELETE', headers: { 'Idempotency-Key': command.key },
      }) as { card?: Record<string, unknown>; labelId?: unknown; assigned?: unknown; changed?: unknown } | null;
      if (ticket !== epoch.current) return;
      if (!value || value.labelId !== command.labelId || value.assigned !== command.assigned || typeof value.changed !== 'boolean'
        || value.card?.id !== cardId || value.card.organizationId !== snapshot.board.organizationId || value.card.boardId !== snapshot.board.id
        || value.card.version !== command.version + (value.changed ? 1 : 0)) throw new Error('Unconfirmed assignment');
      restoreFocus.current = true; setIntent(undefined); setPage(undefined); setOpen(false); onRefresh();
    } catch (error) { if (ticket === epoch.current) failure(error, command); }
    finally { if (ticket === epoch.current) { controller.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  if (!admitted) return null;
  return <Box sx={{ mt: 2 }}>
    <Button ref={trigger} disabled={busy || disabled || !!intent || denied || !activeCard} aria-expanded={open} onClick={() => void load()}>Edit Card labels</Button>
    {open && <Stack component="section" aria-label="Edit Card labels" spacing={1}>
      {busy && <Typography role="status">Updating label options…</Typography>}
      {notice && <Alert severity="warning">{notice}</Alert>}
      {intent ? <><Typography>{intent.assigned ? 'Add' : 'Remove'} {intent.name}</Typography><Button ref={retryTrigger} disabled={busy || disabled} onFocus={() => { recoverFocus.current = true; }}
        onBlur={event => { if (event.relatedTarget !== null) recoverFocus.current = false; }} onClick={() => void change()}>Retry label change</Button></> : <>
        {current && !notice && page.items.map(option => <Stack key={option.label.id} direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
          <Typography>{option.label.name || 'Unnamed label'} ({option.label.color})</Typography>
          <Button disabled={busy || disabled} onClick={() => void change(option)} aria-label={`${option.assigned ? 'Remove' : 'Add'} label ${option.label.name || option.label.color}`}>{option.assigned ? 'Remove' : 'Add'}</Button>
        </Stack>)}
        {current && page.items.length === 0 && <Typography>No Board labels available.</Typography>}
        {!current && !busy && !notice && <Typography>Reload label options for the current Card.</Typography>}
        {current && page.nextCursor && <Button disabled={busy || disabled} onClick={() => void load(page.nextCursor!)}>Next labels</Button>}
        <Stack direction="row"><Button disabled={busy || disabled || denied} onClick={() => void load()}>Reload label options</Button><Button disabled={busy} onClick={() => { setOpen(false); trigger.current?.focus(); }}>Done editing labels</Button></Stack>
        {notice && <Button disabled={busy} onClick={onRefresh}>Refresh Board</Button>}
      </>}
    </Stack>}
  </Box>;
}
