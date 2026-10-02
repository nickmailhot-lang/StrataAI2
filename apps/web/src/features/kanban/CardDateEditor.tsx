import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type WorkCard } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { cardDates, sameDateInstant } from './cardDates';
import { dateCommand, dateDraft, dateDraftDirty, localDate, type DateCommand, type DateDraft } from './cardDateDraft';

type Props = { card: WorkCard; organizationId: string; boardId: string; listId: string; editable: boolean; disabled: boolean; unavailable: boolean;
  onRefresh: () => void; onBusyChange: (busy: boolean) => void; onRecoveryChange: (unresolved: boolean) => void };
type Intent = { input: DateCommand; key: string; actor: string; listId: string };
export function CardDateEditor(props: Props) {
  return <DateEditor key={`${props.organizationId}/${props.boardId}/${props.card.id}`} {...props} />;
}
function DateEditor(props: Props) {
  const [draft, setDraft] = useState<DateDraft>(); const [open, setOpen] = useState(false); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false);
  const actor = useRef<string | undefined>(undefined), mounted = useRef(false), pending = useRef<AbortController | undefined>(undefined);
  const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const requestedFocus = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  const dirty = draft ? dateDraftDirty(draft) : false;
  const conflict = !!draft && props.card.version > draft.base.version;
  if (draft && !dirty && !intent && !blocked && conflict) {
    try { setDraft(dateDraft(props.card, draft.fields.zone)); } catch { setDraft(undefined); }
  }
  useEffect(() => {
    if (requestedFocus.current && !busy && !props.disabled && !props.unavailable) { action.current?.focus(); requestedFocus.current = false; }
  }, [busy, props.disabled, props.unavailable, intent, blocked]);
  async function review() {
    if (pending.current || props.disabled || props.unavailable || !props.editable) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setBlocked(false);
    try {
      const profile = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(profile)) throw new Error();
      actor.current = profile.id; setDraft(dateDraft(props.card, profile.timezone)); setOpen(true);
    } catch { if (mounted.current) setNotice('Unable to load dates. Refresh the Card and try again.'); }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || props.disabled || props.unavailable || !props.editable || blocked || !draft || (!intent && conflict)) return;
    let command: Intent;
    try { command = intent ?? { input: dateCommand(draft), key: crypto.randomUUID(), actor: actor.current!, listId: props.listId }; }
    catch { setNotice('Check the dates, timezone, date order and completion fields.'); return; }
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return await workRequest<unknown>(`/cards/${encodeURIComponent(props.card.id)}/dates`, { method: 'PATCH', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify(command.input) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const ack = value as { card?: WorkCard & { organizationId: string; boardId: string; listId: string }; changed?: boolean } | null;
      if (!ack?.card || typeof ack.changed !== 'boolean' || ack.card.id !== props.card.id || ack.card.organizationId !== props.organizationId ||
          ack.card.boardId !== props.boardId || ack.card.listId !== command.listId || !Number.isSafeInteger(ack.card.version) ||
          ack.card.version !== command.input.version + (ack.changed ? 1 : 0)) throw new Error('Unconfirmed dates');
      const normalized = cardDates(ack.card), input = command.input;
      if (normalized.dueTimezone !== input.dueTimezone || normalized.dueHasTime !== input.dueHasTime || normalized.dueComplete !== input.dueComplete ||
          (normalized.startAt === null) !== (input.startAt === null) || (normalized.dueAt === null) !== (input.dueAt === null) ||
          input.startAt && !sameDateInstant(normalized.startAt!, input.startAt) ||
          input.dueAt && (input.dueHasTime ? !sameDateInstant(normalized.dueAt!, input.dueAt) : localDate(normalized.dueAt!, input.dueTimezone!) !== input.dueAt)) throw new Error('Unconfirmed dates');
      setDraft(dateDraft(ack.card)); setIntent(undefined); setNotice(ack.changed ? 'Dates saved.' : 'Dates are unchanged.'); requestedFocus.current = true;
      props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      requestedFocus.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice(error.status === 409 ? 'The Card changed. Your date draft is preserved. Refresh and load the latest dates before saving.'
          : 'Date editing is unavailable. Refresh the Card before reviewing another change.');
      } else { setIntent(command); setNotice('The date save is unconfirmed. Retry the same dates to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  const disabled = props.disabled || props.unavailable || busy || !props.editable;
  return <Stack component="section" aria-label="Edit Card dates" spacing={1} sx={{ my: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {!open ? <Button disabled={disabled} onClick={() => void review()}>Edit dates</Button> : props.unavailable ? <Typography>Checking current Card dates…</Typography> : draft && <>
      {conflict && !intent && <Alert severity="warning">This Card changed elsewhere. Your date draft is preserved.</Alert>}
      <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}>
        <Stack spacing={2}>
          <TextField label="Start date and time (UTC)" type="datetime-local" slotProps={{ inputLabel: { shrink: true }, htmlInput: { step: '0.001' } }}
            value={draft.fields.start} disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, fields: { ...draft.fields, start: event.target.value } })} />
          <TextField label={draft.fields.timed ? 'Due date and time (UTC)' : 'Due date'} type={draft.fields.timed ? 'datetime-local' : 'date'}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { step: draft.fields.timed ? '0.001' : undefined } }} value={draft.fields.due}
            disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, fields: { ...draft.fields, due: event.target.value, ...(event.target.value ? {} : { timed: false, complete: false }) } })} />
          <TextField label="Date timezone" helperText="Use an IANA timezone, such as America/Vancouver. Timed fields use UTC."
            value={draft.fields.zone} disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, fields: { ...draft.fields, zone: event.target.value } })} />
          <FormControlLabel label="Include due time" control={<Checkbox checked={draft.fields.timed} disabled={disabled || !!intent || blocked || !draft.fields.due}
            onChange={(_, timed) => setDraft({ ...draft, fields: { ...draft.fields, timed, due: timed ? `${draft.fields.due}T00:00:00.000` : draft.fields.due.slice(0, 10) } })} />} />
          <FormControlLabel label="Due complete" control={<Checkbox checked={draft.fields.complete} disabled={disabled || !!intent || blocked || !draft.fields.due}
            onChange={(_, complete) => setDraft({ ...draft, fields: { ...draft.fields, complete } })} />} />
          <Button ref={!intent && !blocked ? action : undefined} disabled={disabled || !!intent || blocked} onClick={() => setDraft({ ...draft, fields: { ...draft.fields, start: '', due: '', timed: false, complete: false } })}>Clear dates</Button>
          {intent ? <Button ref={action} disabled={disabled} onClick={() => void save()}>Retry date save</Button>
            : <Button type="submit" disabled={disabled || blocked || conflict || !dirty}>Save dates</Button>}
          {!intent && <Button ref={blocked ? action : undefined} disabled={disabled} onClick={() => { setDraft(undefined); setOpen(false); actor.current = undefined; setBlocked(false); setNotice(undefined); props.onRefresh(); }}>Discard date edits and load latest</Button>}
        </Stack>
      </Box>
    </>}
  </Stack>;
}
