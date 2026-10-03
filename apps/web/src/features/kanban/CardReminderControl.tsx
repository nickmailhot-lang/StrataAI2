import { useEffect, useEffectEvent, useRef, useState, type FocusEvent } from 'react';
import { Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type WorkCard } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import { cardDates, dateInstantTicks } from './cardDates';
import { parseReminderState, type ReminderInterval, type ReminderState } from './cardReminder';

type Props = { organizationId: string; boardId: string; card: WorkCard; disabled: boolean; unavailable: boolean;
  onBusyChange: (value: boolean) => void; onRecoveryChange: (value: boolean) => void; onRefresh: () => void };
type Intent = { key: string; actor: string; version: number; cardVersion: number; generation: number; id: string | null;
  interval: ReminderInterval | null; dueAt: string | null; method: 'POST' | 'DELETE'; body?: string; path: string };

export function CardReminderControl(props: Props) {
  return <ReminderControl key={`${props.organizationId}/${props.boardId}/${props.card.id}`} {...props} />;
}
function ReminderControl(props: Props) {
  const [open, setOpen] = useState(false), [current, setCurrent] = useState<ReminderState>();
  const [openingPending, setOpeningPending] = useState(false);
  const [interval, setChoice] = useState<ReminderInterval | ''>(''); const [intent, setIntent] = useState<Intent>();
  const [notice, setNotice] = useState<string>(); const [blocked, setBlocked] = useState(false); const [busy, setBusy] = useState(false);
  const [now, setNow] = useState(Date.now()); const mounted = useRef(false);
  const pending = useRef<{ controller: AbortController; write: boolean } | undefined>(undefined);
  const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  const refreshNeeded = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.controller.abort();
    callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  const refreshRead = useEffectEvent(() => void read());
  const admitOpening = useEffectEvent(() => {
    if (openingPending && !props.unavailable && !props.disabled && !pending.current) void read();
  });
  useEffect(() => { admitOpening(); }, [openingPending, props.unavailable, props.disabled, busy]);
  useEffect(() => {
    if (!open) { refreshNeeded.current = false; return; }
    if (intent || blocked || pending.current?.write) return;
    if (props.unavailable || current && current.cardVersion !== props.card.version) refreshNeeded.current = true;
    if (props.unavailable && pending.current && !pending.current.write) {
      pending.current.controller.abort(); pending.current = undefined; setBusy(false); setCurrent(undefined);
    }
    if (refreshNeeded.current && !props.unavailable && !props.disabled && !pending.current) {
      refreshNeeded.current = false; refreshRead();
    }
  }, [props.unavailable, props.disabled, props.card.version, current, open, intent, blocked, busy]);
  useEffect(() => { if (!open) return; const timer = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, [open]);
  useEffect(() => { if (focusRequested.current && !busy && !props.disabled && !props.unavailable
    && ownsRecoveryFocus(document.activeElement, action.current)) {
    action.current?.focus({ preventScroll: true });
  } }, [busy, props.disabled, props.unavailable, open, intent, blocked]);
  const actionFocus = {
    onFocus: () => { focusRequested.current = true; },
    onBlur: (event: FocusEvent<HTMLButtonElement>) => { if (!ownsRecoveryFocus(event.relatedTarget, action.current)) focusRequested.current = false; },
  };
  const path = `/cards/${encodeURIComponent(props.card.id)}/reminders`;
  const outdated = !!current && current.cardVersion !== props.card.version;
  const disabled = busy || props.disabled || props.unavailable;
  const options = current?.options.filter(option => dateInstantTicks(option.triggerAt) > BigInt(now) * 10000n) ?? [];
  async function read() {
    if (pending.current || props.disabled || props.unavailable || intent) return;
    setOpeningPending(false);
    refreshNeeded.current = false;
    const operation = { controller: new AbortController(), write: false }; pending.current = operation;
    setOpen(true); setBusy(true); setCurrent(undefined); setNotice(undefined); setBlocked(false);
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        return parseReminderState(await workRequest<unknown>(path, { signal }), {
          organizationId: props.organizationId, boardId: props.boardId, cardId: props.card.id, userId: profile.id, dueAt: cardDates(props.card).dueAt,
        });
      }, operation.controller.signal);
      if (!mounted.current || pending.current !== operation) return;
      if (result.cardVersion !== callbacks.current.card.version) throw new WorkRequestError(409, null);
      setCurrent(result); setChoice(result.reminder?.intervalCode ?? result.options[0]?.code ?? ''); setNow(Date.now());
    } catch (error) { if (mounted.current && pending.current === operation) {
      setBlocked(true);
      setCurrent(undefined); setNotice(error instanceof WorkRequestError && [401, 403, 404].includes(error.status)
        ? 'This Card or personal reminder is unavailable.' : 'Unable to load your reminder. Refresh the Card and try again.');
      props.onRefresh();
    } } finally { if (mounted.current && pending.current === operation) { pending.current = undefined; setBusy(false); } }
  }
  async function save(remove = false, source?: HTMLButtonElement) {
    if (pending.current || props.disabled || props.unavailable || blocked || !current || !current.canChange || !intent && outdated) return;
    if (!intent && !remove && !options.some(option => option.code === interval)) return;
    const command: Intent = intent ?? {
      key: crypto.randomUUID(), actor: current.userId, version: current.reminder?.version ?? 0, cardVersion: current.cardVersion,
      generation: current.reminder?.generation ?? 0, id: current.reminder?.id ?? null,
      interval: remove ? null : interval as ReminderInterval, dueAt: cardDates(props.card).dueAt,
      method: remove ? 'DELETE' : 'POST',
      path: remove ? `${path}?cardVersion=${current.cardVersion}&version=${current.reminder?.version ?? 0}` : path,
      ...(remove ? {} : { body: JSON.stringify({ intervalCode: interval, enabled: true, cardVersion: current.cardVersion, version: current.reminder?.version ?? 0 }) }),
    };
    parkRecoveryFocus(source ?? null);
    const operation = { controller: new AbortController(), write: true }; pending.current = operation;
    setBusy(true); props.onBusyChange(true); setNotice(undefined);
    try {
      const ack = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return parseReminderState(await workRequest<unknown>(command.path, { method: command.method, signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, ...(command.body ? { body: command.body } : {}) }),
        { organizationId: props.organizationId, boardId: props.boardId, cardId: props.card.id, userId: command.actor, dueAt: command.dueAt });
      }, operation.controller.signal);
      if (!mounted.current || pending.current !== operation) return;
      const row = ack.reminder;
      if (!row || ack.cardVersion !== command.cardVersion || row.version !== command.version + (ack.changed ? 1 : 0) ||
        row.generation !== command.generation + (ack.changed ? 1 : 0) || command.id !== null && row.id !== command.id ||
        (command.method === 'POST' ? !row.enabled || row.intervalCode !== command.interval || row.status !== 'SCHEDULED'
          : row.enabled || row.status !== 'CANCELLED')) throw new Error('Unconfirmed Reminder');
      setIntent(undefined); setCurrent(undefined); setOpen(false); setNotice(command.method === 'POST' ? 'Due reminder saved.' : 'Due reminder cancelled.');
      focusRequested.current = true; props.onRefresh();
    } catch (error) { if (mounted.current && pending.current === operation) {
      focusRequested.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice('This reminder change is unavailable. Load the current Card and reminder before reviewing another change.');
        if ([401, 403, 404].includes(error.status)) setCurrent(undefined);
      } else { setIntent(command); setNotice('The reminder change is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } } finally { if (mounted.current && pending.current === operation) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  return <Stack component="section" aria-label="Personal due reminder" spacing={1} sx={{ my: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {!open ? <Button ref={action} {...actionFocus} onClick={() => { focusRequested.current = false; setOpen(true); setOpeningPending(true); }}>Due reminder</Button> : <>
      {intent ? <>
        <Typography>Recover the original reminder change before choosing another interval.</Typography>
        <Button ref={action} {...actionFocus} disabled={disabled} onClick={event => void save(false, event.currentTarget)}>Retry reminder change</Button>
        <Button disabled={busy} onClick={props.onRefresh}>Check current Card</Button>
      </> : props.unavailable || openingPending ? <><Typography role="status">Checking current Card access…</Typography>
        {openingPending && <Button onClick={() => { setOpen(false); setOpeningPending(false); focusRequested.current = true; }}>Close reminder</Button>}</> : <>
        {current && !outdated && !blocked && <>
          <Typography>{!current.reminder?.enabled ? 'You have no active due reminder.' : current.reminder.status === 'SUSPENDED'
            ? 'Your reminder is waiting for an available due time.' : current.reminder.status === 'FIRED' ? 'Your due reminder was delivered.' : 'Your due reminder is scheduled.'}</Typography>
          {options.length > 0 ? <TextField select label="Reminder interval" value={options.some(option => option.code === interval) ? interval : ''}
            slotProps={{ inputLabel: { shrink: true }, select: { displayEmpty: true } }}
            helperText={interval && !options.some(option => option.code === interval) ? 'That interval is no longer available. Choose a current interval.' : undefined}
            disabled={disabled || !current.canChange} onChange={event => setChoice(event.target.value as ReminderInterval)}>
            <MenuItem value="">Choose an interval</MenuItem>{options.map(option => <MenuItem key={option.code} value={option.code}>{option.label}</MenuItem>)}
          </TextField> : <Typography>{current.canChange ? 'No future reminder intervals are available. Set a future due date or reopen the due date to choose one.'
            : 'Reminder changes are unavailable in this Organization.'}</Typography>}
          <Button disabled={disabled || !current.canChange || !options.some(option => option.code === interval)} onClick={event => void save(false, event.currentTarget)}>Save due reminder</Button>
          {current.reminder?.enabled && <Button disabled={disabled || !current.canChange} onClick={event => void save(true, event.currentTarget)}>Cancel due reminder</Button>}
        </>}
        {outdated && <Typography role="status">The Card changed. Load your current reminder before choosing an interval.</Typography>}
        <Button ref={blocked ? action : undefined} {...(blocked ? actionFocus : {})} disabled={disabled} onClick={() => { focusRequested.current = false; void read(); }}>Load current reminder</Button>
        <Button disabled={busy} onClick={() => { setOpen(false); setCurrent(undefined); setBlocked(false); focusRequested.current = true; }}>Close reminder</Button>
      </>}
    </>}
  </Stack>;
}
