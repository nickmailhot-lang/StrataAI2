import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseChecklistCreated, type ChecklistScope } from './checklists';

export type ChecklistCreateProps = ChecklistScope & { version: number; editable: boolean; disabled: boolean; unavailable: boolean;
  onBusyChange: (value: boolean) => void; onRecoveryChange: (value: boolean) => void; onRefresh: () => void };
type Intent = { actor: string; title: string; cardVersion: number; key: string };
export function ChecklistCreateControl(props: ChecklistCreateProps) {
  return <CreateControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function CreateControl(props: ChecklistCreateProps) {
  const [draft, setDraft] = useState<{ actor: string; title: string; version: number }>();
  const [busy, setBusy] = useState(false); const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false);
  const [notice, setNotice] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useEffect(() => {
    if (focusRequested.current && !busy && !props.disabled && !props.unavailable && action.current && !action.current.disabled) {
      action.current.focus({ preventScroll: true }); focusRequested.current = !!intent || blocked;
    }
  }, [busy, props.disabled, props.unavailable, intent, blocked, draft]);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const conflict = !!draft && draft.version !== props.version;
  async function review() {
    if (pending.current || disabled || intent) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined);
    const version = props.version;
    try {
      const profile = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(profile) || callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== version) throw new Error();
      setDraft({ actor: profile.id, title: '', version }); setBlocked(false);
    } catch { if (mounted.current) setNotice('Unable to start a checklist. Refresh the Card and try again.'); }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || disabled || blocked || !draft || !intent && conflict) return;
    const title = draft.title.trim();
    if (!intent && (!title || title.length > 160 || title.includes('\0'))) { setNotice('Enter a checklist title of 1 to 160 characters.'); return; }
    const command = intent ?? { actor: draft.actor, title, cardVersion: draft.version, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists`, { method: 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ title: command.title, cardVersion: command.cardVersion }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      parseChecklistCreated(value, props, command.title, command.cardVersion);
      setIntent(undefined); setDraft(undefined); setBlocked(false); setNotice('Checklist created.'); focusRequested.current = true; props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      focusRequested.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice('This checklist change is unavailable. Your title is preserved. Load the current Card before reviewing another change.');
      } else { setIntent(command); setNotice('The checklist creation is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  return <Stack component="section" aria-label="Create checklist" spacing={1} sx={{ my: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {!draft ? <Button ref={action} disabled={disabled} onClick={() => void review()}>Add checklist</Button>
      : props.unavailable ? <Typography>Checking current Card access…</Typography> : <>
        {conflict && !intent && <Alert severity="warning">This Card changed elsewhere. Your checklist title is preserved.</Alert>}
        <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}>
          <Stack spacing={1}>
            <TextField autoFocus label="New checklist title" value={draft.title} required fullWidth slotProps={{ htmlInput: { maxLength: 160 } }}
              disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, title: event.target.value })} />
            {intent ? <Button ref={action} disabled={disabled} onFocus={() => { focusRequested.current = true; }}
              onBlur={event => { if (event.relatedTarget !== null) focusRequested.current = false; }} onClick={() => void save()}>Retry checklist creation</Button>
              : <Button type="submit" disabled={disabled || blocked || conflict || !draft.title.trim()}>Create checklist</Button>}
            {!intent && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled || props.unavailable}
              onFocus={() => { if (blocked) focusRequested.current = true; }} onBlur={event => { if (event.relatedTarget !== null) focusRequested.current = false; }}
              onClick={() => { setDraft(undefined); setBlocked(false); setNotice(undefined); focusRequested.current = true; props.onRefresh(); }}>Discard checklist title and load latest</Button>}
          </Stack>
        </Box>
      </>}
  </Stack>;
}
