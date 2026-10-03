import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, Container, Stack, TextField, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { watchBoard, type LiveStatus } from '../../api/boardLive';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { dateTimezone } from './cardDates';

type Board = { id: string; organizationId: string; name: string; version: number; lifecycleState: 'active'; dateTimezoneOverride: string | null };
type Intent = { actor: string; version: number; timezone: string | null; key: string; body: string };
function boardValue(value: unknown, org: string, id: string): Board {
  const b = value as Board | undefined;
  if (!b || b.id !== id || b.organizationId !== org || b.lifecycleState !== 'active' || typeof b.name !== 'string'
    || !b.name.trim() || !Number.isSafeInteger(b.version) || b.version < 1 ||
    b.dateTimezoneOverride !== null && typeof b.dateTimezoneOverride !== 'string') throw new Error('Invalid Board policy');
  if (b.dateTimezoneOverride !== null) dateTimezone(b.dateTimezoneOverride);
  return b;
}
export function BoardDatePolicyPage() {
  const { organizationId = '', boardId = '' } = useParams();
  return <Policy key={`${organizationId}/${boardId}`} org={organizationId} id={boardId} />;
}
function Policy({ org, id }: { org: string; id: string }) {
  const [board, setBoard] = useState<Board>(); const [actor, setActor] = useState<string>();
  const [draft, setDraft] = useState(''); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const [subscribed, setSubscribed] = useState(false); const [live, setLive] = useState<LiveStatus>('connecting');
  const mounted = useRef(false); const queued = useRef(false); const focusRequested = useRef(false);
  const acknowledged = useRef(false);
  const pending = useRef<{ controller: AbortController; write: boolean } | undefined>(undefined);
  const action = useRef<HTMLButtonElement>(null); const refresh = useRef<HTMLButtonElement>(null);
  const base = `/boards/${encodeURIComponent(id)}`;
  const loadCurrent = useEffectEvent(() => void load());
  const invalidate = useEffectEvent(() => {
    setBoard(undefined); setDraft('');
    if (intent) return;
    setNotice('Board settings changed. Checking the current timezone policy.');
    if (pending.current) { queued.current = true; if (!pending.current.write) pending.current.controller.abort(); }
    else void load();
  });
  useEffect(() => { mounted.current = true; loadCurrent(); return () => { mounted.current = false; pending.current?.controller.abort(); }; }, []);
  useEffect(() => subscribed ? watchBoard({ organizationId: org, boardId: id, invalidate: () => invalidate(), status: setLive }) : undefined, [org, id, subscribed]);
  useEffect(() => { if (!busy && !intent && queued.current) { queued.current = false; loadCurrent(); } }, [busy, intent]);
  useEffect(() => { if (!busy && !queued.current && !pending.current && focusRequested.current) {
    const target = action.current && !action.current.disabled ? action.current : refresh.current;
    if (target && !target.disabled) { target.focus(); focusRequested.current = false; }
  } }, [busy, board, intent]);
  function deny() { setBoard(undefined); setActor(undefined); setDraft(''); setIntent(undefined); setSubscribed(false);
    setNotice('Board timezone administration is unavailable. Check current access before continuing.'); }
  async function load() {
    if (pending.current || intent) return;
    const operation = { controller: new AbortController(), write: false }; pending.current = operation;
    queued.current = false; setBusy(true); setBoard(undefined); setDraft('');
    try {
      const admitted = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (signal.aborted) throw new Error('Retired policy read');
        if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const result = await workRequest<unknown>(base, { signal }) as { board?: unknown; access?: { canAdminister?: boolean } } | undefined;
        if (result?.access?.canAdminister !== true) throw new WorkRequestError(404, null);
        return { board: boardValue(result.board, org, id), actor: profile.id };
      }, operation.controller.signal);
      if (!mounted.current || pending.current !== operation || operation.controller.signal.aborted) return;
      setBoard(admitted.board); setActor(admitted.actor); setDraft(admitted.board.dateTimezoneOverride ?? ''); setSubscribed(true);
      setNotice(acknowledged.current ? 'Timezone policy saved. Current Board settings loaded.' : 'Current Board timezone policy loaded.'); acknowledged.current = false;
    } catch (error) { if (mounted.current && pending.current === operation && !operation.controller.signal.aborted) {
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) deny();
      else { setBoard(undefined); setNotice('Unable to load current Board timezone settings. Check again.'); }
    } } finally { if (mounted.current && pending.current === operation) {
      pending.current = undefined; setBusy(false);
    } }
  }
  async function save() {
    if (pending.current || !intent && (!board || !actor)) return;
    const timezone = draft === '' ? null : draft;
    if (!intent && timezone !== null) { try { dateTimezone(timezone); } catch { setNotice('Enter a valid IANA timezone, such as America/Vancouver, or leave it empty for each viewer.'); return; } }
    const command = intent ?? { actor: actor!, version: board!.version, timezone, key: crypto.randomUUID(),
      body: JSON.stringify({ timezone, version: board!.version }) };
    const operation = { controller: new AbortController(), write: true }; pending.current = operation;
    setBusy(true); setNotice(undefined);
    try {
      const ack = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (signal.aborted) throw new Error('Retired policy command');
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        const admission = await workRequest<unknown>(base, { signal }) as { board?: unknown; access?: { canAdminister?: boolean } } | undefined;
        if (admission?.access?.canAdminister !== true) throw new WorkRequestError(404, null);
        boardValue(admission.board, org, id);
        if (signal.aborted) throw new Error('Retired policy command');
        return await workRequest<unknown>(`${base}/date-policy`, { method: 'PATCH', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: command.body });
      }, operation.controller.signal) as { board?: unknown; changed?: unknown } | undefined;
      if (!mounted.current || pending.current !== operation) return;
      const confirmed = boardValue(ack?.board, org, id);
      if (typeof ack?.changed !== 'boolean' || confirmed.version !== command.version + (ack.changed ? 1 : 0)
        || confirmed.dateTimezoneOverride !== command.timezone) throw new Error('Unconfirmed policy');
      acknowledged.current = true; setIntent(undefined); setBoard(undefined); setDraft(''); queued.current = true; focusRequested.current = true;
      setNotice('Timezone policy saved. Checking current Board settings.');
    } catch (error) { if (mounted.current && pending.current === operation) {
      queued.current = false; setBoard(undefined); setDraft(''); focusRequested.current = true;
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) deny();
      else if (error instanceof WorkRequestError && [400, 409, 429].includes(error.status)) {
        setIntent(undefined); setNotice('The policy change is unavailable or the Board changed. Check current settings before another change.');
      } else { setIntent(command); setNotice('The policy change is unconfirmed. Retry the original change to recover its acknowledgment.'); }
    } } finally { if (mounted.current && pending.current === operation) {
      pending.current = undefined; setBusy(false);
    } }
  }
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Typography variant="h4" component="h2">Board timezone</Typography>
    <Button component={Link} disabled={busy || !!intent} to={`/app/${org}/boards/${id}`}>Back to Board</Button>
    <Typography>Choose a shared timezone for date display and due status. Leave it empty to use each viewer’s timezone. Stored dates and personal reminder times stay the same.</Typography>
    <Typography role="status">Board updates: {live}.</Typography>
    {notice && <Alert severity="info" role="status">{notice}</Alert>}
    {busy && <Typography role="status">Checking Board timezone settings…</Typography>}
    {intent ? <Button ref={action} disabled={busy} onClick={() => void save()}>Retry timezone change</Button> : <>
      {board && <><Typography>{board.name}</Typography>
        <TextField label="Board timezone override" helperText="IANA timezone, for example America/Vancouver. Empty uses each viewer’s timezone."
          value={draft} disabled={busy} onChange={event => setDraft(event.target.value)} />
        <Button ref={action} disabled={busy} onClick={() => void save()}>Save timezone policy</Button></>}
      <Button ref={refresh} disabled={busy} onClick={() => void load()}>Check current timezone policy</Button>
    </>}
  </Stack></Container>;
}
