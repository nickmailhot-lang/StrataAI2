import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseChecklistItemDeleted, parseChecklistItemEdited, parseChecklistItemPage, type Checklist, type ChecklistItem, type ChecklistItemPage } from './checklists';
import type { ChecklistCreateProps } from './ChecklistCreateControl';

type Props = ChecklistCreateProps & { checklist: Checklist; actor: string; canAdminister?: boolean; onClose: (message?: string) => void };
type Draft = { checklist: Checklist; item: ChecklistItem; cardVersion: number; text: string; completed: boolean; kind: 'edit' | 'delete'; confirmed: boolean };
type Intent = Draft & { actor: string; key: string };
export function ChecklistItemManageControl(props: Props) {
  const [page, setPage] = useState<{ value: ChecklistItemPage; cursor?: string }>();
  const [draft, setDraft] = useState<Draft>(); const [intent, setIntent] = useState<Intent>();
  const [blocked, setBlocked] = useState(false); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const requestedFocus = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useEffect(() => {
    if (requestedFocus.current && !busy && !props.disabled && !props.unavailable && action.current && !action.current.disabled) {
      action.current.focus({ preventScroll: true }); requestedFocus.current = !!intent || blocked;
    }
  }, [busy, props.disabled, props.unavailable, props.editable, props.canAdminister, intent, blocked, draft]);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const conflict = !!draft && draft.cardVersion !== props.version;
  async function load(cursor?: string) {
    if (pending.current || disabled || intent || draft || blocked) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setPage(undefined); setNotice(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== props.actor) throw new WorkRequestError(401, null);
        const value = parseChecklistItemPage(await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists/${encodeURIComponent(props.checklist.id)}/items${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal }), props, props.checklist.id, cursor);
        if (!value.canEdit || value.cardVersion !== version || value.summary.checklist.version !== props.checklist.version) throw new WorkRequestError(409, null);
        return { value, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== version) throw new Error();
      setPage(result);
    } catch { if (mounted.current) { setNotice('Unable to review current checklist items. Load the current Card and checklist before trying again.'); setBlocked(true); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || disabled || blocked || !draft || !intent && conflict) return;
    if (draft.kind === 'delete' && (!props.canAdminister || !draft.confirmed)) return;
    const text = draft.text.trim();
    if (!intent && (!text || text.length > 2000 || text.includes('\0'))) { setNotice('Enter item text of 1 to 2000 characters.'); return; }
    const command = intent ?? { ...draft, text, actor: props.actor, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists/${encodeURIComponent(command.checklist.id)}/items/${encodeURIComponent(command.item.id)}`, {
          method: command.kind === 'delete' ? 'DELETE' : 'PATCH', signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify(command.kind === 'delete' ? { confirmed: true, cardVersion: command.cardVersion, checklistVersion: command.checklist.version, version: command.item.version }
            : { text: command.text, completed: command.completed, cardVersion: command.cardVersion, checklistVersion: command.checklist.version, version: command.item.version }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const ack = command.kind === 'delete' ? parseChecklistItemDeleted(value, props, command.checklist, command.item, command.cardVersion)
        : parseChecklistItemEdited(value, props, command.checklist, command.item, command.text, command.completed, command.actor, command.cardVersion);
      setIntent(undefined); setBlocked(false); props.onRefresh(); props.onClose(command.kind === 'delete' ? 'Checklist item deleted.' : ack.changed ? 'Checklist item saved.' : 'Checklist item is unchanged.');
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      requestedFocus.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice(command.kind === 'delete'
          ? 'This item deletion is unavailable. Load the current Card and checklist before confirming another deletion.'
          : 'This item change is unavailable. Your text and completion choice are preserved. Load the current Card and checklist before reviewing another change.');
      } else { setIntent(command); setNotice(command.kind === 'delete' ? 'The item deletion is unconfirmed. Retry the original change to recover its acknowledgment.'
        : 'The item change is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  function close() { props.onRefresh(); props.onClose(); }
  return <Stack component="section" aria-label="Manage checklist items" spacing={1}>
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography>Checking current Card access…</Typography> : <>
      <Typography>Items in {props.checklist.title}</Typography>
      {draft ? <>
        {conflict && !intent && <Alert severity="warning">{draft.kind === 'delete' ? 'This Card changed elsewhere. Review the current item before confirming deletion.' : 'This Card changed elsewhere. Your item text and completion choice are preserved.'}</Alert>}
        <Stack component="form" spacing={1} onSubmit={event => { event.preventDefault(); void save(); }}>
          {draft.kind === 'delete' ? <>
            <Alert severity="warning">Delete item: {draft.item.text}</Alert>
            <FormControlLabel label="Confirm item deletion" control={<Checkbox autoFocus checked={draft.confirmed} disabled={disabled || !!intent || blocked || !props.canAdminister}
              onChange={(_, confirmed) => setDraft({ ...draft, confirmed })} />} />
          </> : <><TextField autoFocus label="Checklist item text" required multiline fullWidth value={draft.text} slotProps={{ htmlInput: { maxLength: 2000 } }}
            disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, text: event.target.value })} />
          <FormControlLabel label="Item complete" control={<Checkbox checked={draft.completed} disabled={disabled || !!intent || blocked}
            onChange={(_, completed) => setDraft({ ...draft, completed })} />} /></>}
          {intent ? <Button ref={action} disabled={disabled || draft.kind === 'delete' && !props.canAdminister} onFocus={() => { requestedFocus.current = true; }}
            onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }} onClick={() => void save()}>{draft.kind === 'delete' ? 'Retry checklist item deletion' : 'Retry checklist item change'}</Button>
            : <Button type="submit" disabled={disabled || blocked || conflict || !draft.text.trim() || draft.kind === 'delete' && (!draft.confirmed || !props.canAdminister)}>{draft.kind === 'delete' ? 'Delete confirmed item' : 'Save checklist item'}</Button>}
        </Stack>
      </> : page ? <>
        {page.value.cardVersion !== props.version && <Alert severity="warning">The Card changed. Review the current checklist before choosing an item.</Alert>}
        {page.value.items.length === 0 && <Typography>No items on this page.</Typography>}
        {page.value.items.map(item => <Stack key={item.id} direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          <Button disabled={disabled || page.value.cardVersion !== props.version}
            onClick={() => setDraft({ checklist: page.value.summary.checklist, item, cardVersion: page.value.cardVersion, text: item.text, completed: item.completed, kind: 'edit', confirmed: false })}>Edit item: {item.text}</Button>
          {props.canAdminister && <Button disabled={disabled || page.value.cardVersion !== props.version}
            onClick={() => setDraft({ checklist: page.value.summary.checklist, item, cardVersion: page.value.cardVersion, text: item.text, completed: item.completed, kind: 'delete', confirmed: false })}>Delete item: {item.text}</Button>}
        </Stack>)}
        {page.value.nextCursor && <Button disabled={disabled} onClick={() => void load(page.value.nextCursor!)}>Next items to manage</Button>}
        {page.cursor && <Button disabled={disabled} onClick={() => void load()}>First items to manage</Button>}
      </> : <Button ref={action} autoFocus disabled={disabled || blocked} onClick={() => void load()}>Review checklist items</Button>}
      {!intent && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled}
        onFocus={() => { if (blocked) requestedFocus.current = true; }} onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }}
        onClick={close}>{draft || blocked ? 'Discard item review and load latest' : 'Close item review'}</Button>}
    </>}
  </Stack>;
}
