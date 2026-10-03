import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseChecklistDeleted, parseChecklistPage, parseChecklistRenamed, type Checklist, type ChecklistPage } from './checklists';
import type { ChecklistCreateProps } from './ChecklistCreateControl';

type Props = ChecklistCreateProps & { canAdminister?: boolean };
type Draft = { actor: string; checklist: Checklist; title: string; cardVersion: number; kind: 'rename' | 'delete'; total: number; confirmed: boolean };
type Intent = Draft & { key: string };
export function ChecklistManageControl(props: Props) {
  return <ManageControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function ManageControl(props: Props) {
  const [selection, setSelection] = useState<{ page: ChecklistPage; actor: string; cursor?: string }>();
  const [draft, setDraft] = useState<Draft>(); const [busy, setBusy] = useState(false);
  const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const requestedFocus = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useEffect(() => {
    if (requestedFocus.current && !busy && !props.disabled && !props.unavailable && action.current && !action.current.disabled) {
      action.current.focus({ preventScroll: true }); requestedFocus.current = !!intent || blocked;
    }
  }, [busy, props.disabled, props.unavailable, intent, blocked, draft]);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const conflict = !!draft && draft.cardVersion !== props.version;
  async function load(cursor?: string) {
    if (pending.current || disabled || intent || draft) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setSelection(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const page = parseChecklistPage(await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal }), props, cursor);
        if (!page.canEdit || page.cardVersion !== version) throw new WorkRequestError(409, null);
        return { page, actor: profile.id, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== version) throw new Error();
      setSelection(result); setBlocked(false);
    } catch { if (mounted.current) { setNotice('Unable to review current checklists. Refresh the Card and try again.'); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || disabled || blocked || !draft || !intent && conflict) return;
    if (draft.kind === 'delete' && (!props.canAdminister || !draft.confirmed)) return;
    const title = draft.title.trim();
    if (!intent && (!title || title.length > 160 || title.includes('\0'))) { setNotice('Enter a checklist title of 1 to 160 characters.'); return; }
    const command = intent ?? { ...draft, title, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/checklists/${encodeURIComponent(command.checklist.id)}`, { method: command.kind === 'delete' ? 'DELETE' : 'PATCH', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          body: JSON.stringify(command.kind === 'delete' ? { confirmed: true, cardVersion: command.cardVersion, version: command.checklist.version }
            : { title: command.title, cardVersion: command.cardVersion, version: command.checklist.version }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const ack = command.kind === 'delete' ? parseChecklistDeleted(value, props, command.checklist, command.total, command.cardVersion)
        : parseChecklistRenamed(value, props, command.checklist, command.title, command.cardVersion);
      setIntent(undefined); setDraft(undefined); setSelection(undefined); setBlocked(false);
      setNotice(command.kind === 'delete' ? 'Checklist deleted.' : ack.changed ? 'Checklist renamed.' : 'Checklist title is unchanged.'); requestedFocus.current = true; props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      requestedFocus.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice(command.kind === 'delete'
          ? 'This checklist deletion is unavailable. Load the current Card and checklist before confirming another deletion.'
          : 'This checklist rename is unavailable. Your title is preserved. Load the current Card and checklist before reviewing another change.');
      } else { setIntent(command); setNotice(command.kind === 'delete'
        ? 'The checklist deletion is unconfirmed. Retry the original change to recover its acknowledgment.'
        : 'The checklist rename is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  function discard() { setDraft(undefined); setSelection(undefined); setBlocked(false); setNotice(undefined); requestedFocus.current = true; props.onRefresh(); }
  return <Stack component="section" aria-label="Manage checklists" spacing={1} sx={{ my: 2 }}>
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography>Checking current Card access…</Typography> : draft ? <>
      <Typography>{draft.kind === 'delete' ? 'Delete' : 'Rename'} checklist: {draft.checklist.title}</Typography>
      {conflict && !intent && <Alert severity="warning">{draft.kind === 'delete' ? 'This Card changed elsewhere. Review the current checklist before confirming deletion.' : 'This Card changed elsewhere. Your checklist title is preserved.'}</Alert>}
      <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}><Stack spacing={1}>
        {draft.kind === 'delete' ? <>
          <Alert severity="warning">This deletes the checklist and its {draft.total} active {draft.total === 1 ? 'item' : 'items'}.</Alert>
          <FormControlLabel label="Confirm checklist deletion" control={<Checkbox autoFocus checked={draft.confirmed}
            disabled={disabled || !!intent || blocked || !props.canAdminister} onChange={(_, confirmed) => setDraft({ ...draft, confirmed })} />} />
        </> : <TextField autoFocus label="Checklist title" value={draft.title} required fullWidth slotProps={{ htmlInput: { maxLength: 160 } }}
          disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, title: event.target.value })} />}
        {intent ? <Button ref={action} disabled={disabled || draft.kind === 'delete' && !props.canAdminister} onFocus={() => { requestedFocus.current = true; }}
          onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }} onClick={() => void save()}>{draft.kind === 'delete' ? 'Retry checklist deletion' : 'Retry checklist rename'}</Button>
          : <Button type="submit" disabled={disabled || blocked || conflict || !draft.title.trim() || draft.kind === 'delete' && (!draft.confirmed || !props.canAdminister)}>{draft.kind === 'delete' ? 'Delete confirmed checklist' : 'Save checklist title'}</Button>}
        {!intent && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled}
          onFocus={() => { if (blocked) requestedFocus.current = true; }} onBlur={event => { if (event.relatedTarget !== null) requestedFocus.current = false; }}
          onClick={discard}>{draft.kind === 'delete' ? 'Discard checklist deletion and load latest' : 'Discard checklist rename and load latest'}</Button>}
      </Stack></Box>
    </> : selection ? <>
      {selection.page.cardVersion !== props.version && <Alert severity="warning">The Card changed. Load current checklists before choosing a title.</Alert>}
      {selection.page.items.length === 0 && <Typography>No checklists on this page.</Typography>}
      {selection.page.items.map(({ checklist, total }) => <Stack key={checklist.id} direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button disabled={disabled || selection.page.cardVersion !== props.version}
          onClick={() => setDraft({ actor: selection.actor, checklist, title: checklist.title, cardVersion: selection.page.cardVersion, kind: 'rename', total, confirmed: false })}>Rename {checklist.title}</Button>
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
