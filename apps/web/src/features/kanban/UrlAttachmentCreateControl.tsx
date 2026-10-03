import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { attachmentUrl, parseUrlAttachmentCreated, type AttachmentScope } from './attachments';

export type UrlAttachmentCreateProps = AttachmentScope & { version: number; editable: boolean; disabled: boolean; unavailable: boolean;
  onBusyChange: (value: boolean) => void; onRecoveryChange: (value: boolean) => void; onRefresh: () => void };
type Intent = { actor: string; title: string; url: string; cardVersion: number; key: string };
export function UrlAttachmentCreateControl(props: UrlAttachmentCreateProps) {
  return <CreateControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function CreateControl(props: UrlAttachmentCreateProps) {
  const [draft, setDraft] = useState<{ actor: string; title: string; url: string; version: number }>();
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
      setDraft({ actor: profile.id, title: '', url: '', version }); setBlocked(false);
    } catch { if (mounted.current) setNotice('Unable to start a link attachment. Refresh the Card and try again.'); }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save() {
    if (pending.current || disabled || blocked || !draft || !intent && conflict) return;
    const title = draft.title.trim(); let url: string;
    try { url = attachmentUrl(draft.url.trim()); }
    catch { setNotice('Enter an HTTP(S) link without embedded credentials.'); return; }
    if (!intent && (!title || title.length > 255 || /[\p{Cc}\p{Cf}]/u.test(title))) { setNotice('Enter a link title of 1 to 255 characters without control characters.'); return; }
    const command = intent ?? { actor: draft.actor, title, url, cardVersion: draft.version, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        return workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/attachments/url`, { method: 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ title: command.title, url: command.url, cardVersion: command.cardVersion }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      parseUrlAttachmentCreated(value, props, command.actor, command.title, command.url, command.cardVersion);
      setIntent(undefined); setDraft(undefined); setBlocked(false); setNotice('Link attachment created.'); focusRequested.current = true; props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      focusRequested.current = true;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice('This link attachment change is unavailable. Your title and URL are preserved. Load the current Card before reviewing another change.');
      } else { setIntent(command); setNotice('The link attachment creation is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  return <Stack component="section" aria-label="Create link attachment" spacing={1} sx={{ my: 2 }}>
    {busy && <Typography role="status">{draft ? 'Creating link attachment…' : 'Checking link attachment access…'}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {!draft ? <Button ref={action} disabled={disabled} onClick={() => void review()}>Add link attachment</Button>
      : props.unavailable ? <Typography>Checking current Card access…</Typography> : <>
        {conflict && !intent && <Alert severity="warning">This Card changed elsewhere. Your title and URL are preserved.</Alert>}
        <Box component="form" onSubmit={event => { event.preventDefault(); void save(); }}>
          <Stack spacing={1}>
            <TextField autoFocus label="New link attachment title" value={draft.title} required fullWidth slotProps={{ htmlInput: { maxLength: 255 } }}
              disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, title: event.target.value })} />
            <TextField label="Attachment URL" value={draft.url} required fullWidth slotProps={{ htmlInput: { maxLength: 2048 } }}
              disabled={disabled || !!intent || blocked} onChange={event => setDraft({ ...draft, url: event.target.value })} />
            {intent ? <Button ref={action} disabled={disabled} onFocus={() => { focusRequested.current = true; }}
              onBlur={event => { if (event.relatedTarget !== null) focusRequested.current = false; }} onClick={() => void save()}>Retry link attachment creation</Button>
              : <Button type="submit" disabled={disabled || blocked || conflict || !draft.title.trim() || !draft.url.trim()}>Create link attachment</Button>}
            {!intent && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled || props.unavailable}
              onFocus={() => { if (blocked) focusRequested.current = true; }} onBlur={event => { if (event.relatedTarget !== null) focusRequested.current = false; }}
              onClick={() => { setDraft(undefined); setBlocked(false); setNotice(undefined); focusRequested.current = true; props.onRefresh(); }}>Discard title and URL and load latest</Button>}
          </Stack>
        </Box>
      </>}
  </Stack>;
}
