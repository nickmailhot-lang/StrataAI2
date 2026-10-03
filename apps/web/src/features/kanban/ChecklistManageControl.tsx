import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { checklistPosition, parseChecklistDeleted, parseChecklistItemCreated, parseChecklistPage, parseChecklistPositioned, parseChecklistRenamed, type Checklist, type ChecklistPage, type ChecklistPosition } from './checklists';
import type { ChecklistCreateProps } from './ChecklistCreateControl';
import { ChecklistItemManageControl } from './ChecklistItemManageControl';

type Props = ChecklistCreateProps & { canAdminister?: boolean };
type Draft = { actor: string; checklist: Checklist; title: string; cardVersion: number; kind: 'rename' | 'delete' | 'move' | 'addItem'; total: number; confirmed: boolean; position?: ChecklistPosition; destination?: string; text?: string };
type Intent = Draft & { key: string };
export function ChecklistManageControl(props: Props) {
  return <ManageControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function ManageControl(props: Props) {
  const [selection, setSelection] = useState<{ page: ChecklistPage; actor: string; cursor?: string }>();
  const [draft, setDraft] = useState<Draft>(); const [busy, setBusy] = useState(false);
  const [itemReview, setItemReview] = useState<{ checklist: Checklist; actor: string }>();
  const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const requestedFocus = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { if (!itemReview) props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, itemReview, props.onRecoveryChange]);
  useEffect(() => {
    if (requestedFocus.current && !busy && !props.disabled && !props.unavailable && action.current && !action.current.disabled) {
      action.current.focus({ preventScroll: true }); requestedFocus.current = !!intent || blocked;
    }
  }, [busy, props.disabled, props.unavailable, intent, blocked, draft, itemReview]);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const conflict = !!draft && draft.cardVersion !== props.version;
  async function load(cursor?: string) {
    if (pending.current || disabled || intent || draft && (draft.kind !== 'move' || blocked || conflict)) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setSelection(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const page = parseChecklistPage(await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal }), props, cursor);
        if (!page.canEdit || page.cardVersion !== version) throw new WorkRequestError(409, null);
        if (draft && profile.id !== draft.actor) throw new WorkRequestError(401, null);
        return { page, actor: profile.id, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== version) throw new Error();
      setSelection(result); setBlocked(false);
    } catch { if (mounted.current) { if (draft) setBlocked(true); setNotice('Unable to review current checklists. Refresh the Card and try again.'); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || disabled || blocked || !draft || !intent && conflict) return;
    if (draft.kind === 'delete' && (!props.canAdminister || !draft.confirmed)) return;
    if (draft.kind === 'move' && !draft.position) return;
    const title = draft.title.trim();
    const text = draft.text?.trim() ?? '';
    if (!intent && draft.kind === 'addItem' && (!text || text.length > 2000 || text.includes('\0'))) { setNotice('Enter item text of 1 to 2000 characters.'); return; }
    if (!intent && (!title || title.length > 160 || title.includes('\0'))) { setNotice('Enter a checklist title of 1 to 160 characters.'); return; }
    const command = intent ?? { ...draft, title, text, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists/${encodeURIComponent(command.checklist.id)}${command.kind === 'move' ? '/position' : command.kind === 'addItem' ? '/items' : ''}`, { method: command.kind === 'delete' ? 'DELETE' : command.kind === 'addItem' ? 'POST' : 'PATCH', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify(command.kind === 'delete' ? { confirmed: true, cardVersion: command.cardVersion, version: command.checklist.version }
            : command.kind === 'move' ? { beforeId: command.position!.beforeId, cardVersion: command.cardVersion, version: command.checklist.version }
              : command.kind === 'addItem' ? { text: command.text, cardVersion: command.cardVersion, checklistVersion: command.checklist.version }
                : { title: command.title, cardVersion: command.cardVersion, version: command.checklist.version }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const ack = command.kind === 'delete' ? parseChecklistDeleted(value, props, command.checklist, command.total, command.cardVersion)
        : command.kind === 'move' ? parseChecklistPositioned(value, props, command.checklist, command.position!, command.cardVersion)
          : command.kind === 'addItem' ? parseChecklistItemCreated(value, props, command.checklist, command.text!, command.cardVersion)
            : parseChecklistRenamed(value, props, command.checklist, command.title, command.cardVersion);
      setIntent(undefined); setDraft(undefined); setSelection(undefined); setBlocked(false);
      setNotice(command.kind === 'addItem' ? 'Checklist item added.' : command.kind === 'delete' ? 'Checklist deleted.' : command.kind === 'move' ? ack.changed ? 'Checklist moved.' : 'Checklist position is unchanged.'
        : ack.changed ? 'Checklist renamed.' : 'Checklist title is unchanged.'); requestedFocus.current = true; props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      requestedFocus.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice(command.kind === 'addItem'
          ? 'This item creation is unavailable. Your text is preserved. Load the current Card and checklist before reviewing another item.' : command.kind === 'move'
          ? 'This checklist move is unavailable. Load the current Card and checklists before reviewing another position.' : command.kind === 'delete'
          ? 'This checklist deletion is unavailable. Load the current Card and checklist before confirming another deletion.'
          : 'This checklist rename is unavailable. Your title is preserved. Load the current Card and checklist before reviewing another change.');
      } else { setIntent(command); setNotice(command.kind === 'addItem'
        ? 'The item creation is unconfirmed. Retry the original change to recover its acknowledgment.' : command.kind === 'move'
        ? 'The checklist move is unconfirmed. Retry the original change to recover its acknowledgment.' : command.kind === 'delete'
        ? 'The checklist deletion is unconfirmed. Retry the original change to recover its acknowledgment.'
        : 'The checklist rename is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  function discard() { setDraft(undefined); setSelection(undefined); setBlocked(false); setNotice(undefined); requestedFocus.current = true; props.onRefresh(); }
  if (itemReview) return <ChecklistItemManageControl {...props} {...itemReview} onClose={message => { setItemReview(undefined); setSelection(undefined); setBlocked(false); setNotice(message); requestedFocus.current = true; }} />;
  return <Stack component="section" aria-label="Manage checklists" spacing={1} sx={{ my: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography>Checking current Card access…</Typography> : draft ? <>
      <Typography>{draft.kind === 'addItem' ? 'Add item to' : draft.kind === 'delete' ? 'Delete' : draft.kind === 'move' ? 'Move' : 'Rename'} checklist: {draft.checklist.title}</Typography>
      {conflict && !intent && <Alert severity="warning">{draft.kind === 'addItem' ? 'This Card changed elsewhere. Your item text is preserved.' : draft.kind === 'move' ? 'This Card changed elsewhere. Review the current checklists before choosing a position.' : draft.kind === 'delete' ? 'This Card changed elsewhere. Review the current checklist before confirming deletion.' : 'This Card changed elsewhere. Your checklist title is preserved.'}</Alert>}
      <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}><Stack spacing={1}>
        {draft.kind === 'delete' ? <>
          <Alert severity="warning">This deletes the checklist and its {draft.total} active {draft.total === 1 ? 'item' : 'items'}.</Alert>
          <FormControlLabel label="Confirm checklist deletion" control={<Checkbox autoFocus checked={draft.confirmed}
            disabled={disabled || !!intent || blocked || !props.canAdminister} onChange={(_, confirmed) => setDraft({ ...draft, confirmed })} />} />
        </> : draft.kind === 'move' ? <>
          <Typography>{draft.destination ? `Chosen position: ${draft.destination}` : 'Choose the checklist to place this before, or choose the end on the final page.'}</Typography>
          {selection && !intent && !blocked && !conflict && <>
            {selection.page.items.filter(value => value.checklist.id !== draft.checklist.id).map(({ checklist }, index) => <Button key={checklist.id} autoFocus={index === 0}
              disabled={disabled} onClick={() => setDraft({ ...draft, position: checklistPosition(selection.page, draft.checklist, checklist.id, selection.cursor), destination: `before ${checklist.title}` })}>Place before {checklist.title}</Button>)}
            {!selection.page.nextCursor && <Button autoFocus={!selection.page.items.some(value => value.checklist.id !== draft.checklist.id)} disabled={disabled} onClick={() => setDraft({ ...draft, position: checklistPosition(selection.page, draft.checklist, null, selection.cursor), destination: 'at end' })}>Place at end</Button>}
            {selection.page.nextCursor && <Button disabled={disabled} onClick={() => void load(selection.page.nextCursor!)}>Next position choices</Button>}
            {selection.cursor && <Button disabled={disabled} onClick={() => void load()}>First position choices</Button>}
          </>}
        </> : draft.kind === 'addItem' ? <TextField autoFocus label="Checklist item text" value={draft.text ?? ''} required fullWidth multiline slotProps={{ htmlInput: { maxLength: 2000 } }}
          disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, text: event.target.value })} /> : <TextField autoFocus label="Checklist title" value={draft.title} required fullWidth slotProps={{ htmlInput: { maxLength: 160 } }}
          disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, title: event.target.value })} />}
        {intent ? <Button ref={action} disabled={disabled || draft.kind === 'delete' && !props.canAdminister} onFocus={() => { requestedFocus.current = true; }}
          onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }} onClick={() => void save()}>{draft.kind === 'addItem' ? 'Retry checklist item creation' : draft.kind === 'move' ? 'Retry checklist move' : draft.kind === 'delete' ? 'Retry checklist deletion' : 'Retry checklist rename'}</Button>
          : <Button type="submit" disabled={disabled || blocked || conflict || !draft.title.trim() || draft.kind === 'addItem' && !draft.text?.trim() || draft.kind === 'move' && !draft.position || draft.kind === 'delete' && (!draft.confirmed || !props.canAdminister)}>{draft.kind === 'addItem' ? 'Create checklist item' : draft.kind === 'move' ? 'Save checklist position' : draft.kind === 'delete' ? 'Delete confirmed checklist' : 'Save checklist title'}</Button>}
        {!intent && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled}
          onFocus={() => { if (blocked) requestedFocus.current = true; }} onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }}
          onClick={discard}>{draft.kind === 'addItem' ? 'Discard checklist item and load latest' : draft.kind === 'move' ? 'Discard checklist move and load latest' : draft.kind === 'delete' ? 'Discard checklist deletion and load latest' : 'Discard checklist rename and load latest'}</Button>}
      </Stack></Box>
    </> : selection ? <>
      {selection.page.cardVersion !== props.version && <Alert severity="warning">The Card changed. Load current checklists before choosing a change.</Alert>}
      {selection.page.items.length === 0 && <Typography>No checklists on this page.</Typography>}
      {selection.page.items.map(({ checklist, total }) => <Stack key={checklist.id} direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setDraft({ actor: selection.actor, checklist, title: checklist.title, cardVersion: selection.page.cardVersion, kind: 'rename', total, confirmed: false })}>Rename {checklist.title}</Button>
        <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setDraft({ actor: selection.actor, checklist, title: checklist.title, cardVersion: selection.page.cardVersion, kind: 'move', total, confirmed: false })}>Move {checklist.title}</Button>
        <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setDraft({ actor: selection.actor, checklist, title: checklist.title, cardVersion: selection.page.cardVersion, kind: 'addItem', total, confirmed: false, text: '' })}>Add item to {checklist.title}</Button>
        <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setItemReview({ actor: selection.actor, checklist })}>Manage items in {checklist.title}</Button>
        {props.canAdminister && <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setDraft({ actor: selection.actor, checklist, title: checklist.title, cardVersion: selection.page.cardVersion, kind: 'delete', total, confirmed: false })}>Delete {checklist.title}</Button>}
      </Stack>)}
      <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        {selection.page.nextCursor && <Button disabled={disabled} onClick={() => void load(selection.page.nextCursor!)}>Next checklists to manage</Button>}
        {selection.cursor && <Button disabled={disabled} onClick={() => void load()}>First checklists to manage</Button>}
        <Button disabled={disabled} onClick={() => void load()}>Load current checklists</Button>
        <Button disabled={busy || props.disabled} onClick={discard}>Close checklist review</Button>
      </Stack>
    </> : <Button ref={action} disabled={disabled} onClick={() => void load()}>Manage checklists</Button>}
  </Stack>;
}
