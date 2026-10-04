import { useEffect, useRef, useState } from 'react';
import { ownsRecoveryFocus } from './focusRecovery';
import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { attachmentFileDigest } from './attachmentFileDigest';
import { parseAttachmentUploadOptions, parseFileAttachmentCreated, type AttachmentUploadOptions } from './attachments';
import type { UrlAttachmentCreateProps } from './UrlAttachmentCreateControl';

type Draft = { actor: string; options: AttachmentUploadOptions; file?: File };
type Intent = { actor: string; file: File; name: string; sizeBytes: number; digest: string; cardVersion: number; key: string };
const typeNames: Record<string, string> = { 'application/pdf': 'PDF', 'image/png': 'PNG', 'image/jpeg': 'JPEG', 'image/webp': 'WebP' };

// A slow provider/read cannot keep the UI busy forever. Aborting this wait is
// not proof that the server did not commit; the original intent stays retained.
async function boundedUpload<T>(operation: (signal: AbortSignal) => Promise<T>, signal: AbortSignal): Promise<T> {
  signal.throwIfAborted(); const controller = new AbortController(); let stop: () => void = () => {};
  let timeout: ReturnType<typeof setTimeout> | undefined;
  const stopped = new Promise<never>((_resolve, reject) => {
    stop = () => { reject(new DOMException('File upload stopped.', 'AbortError')); controller.abort(); };
    signal.addEventListener('abort', stop, { once: true });
    timeout = setTimeout(() => { reject(new WorkRequestError(503, null)); controller.abort(); }, 300000);
  });
  try { return await Promise.race([Promise.resolve().then(() => operation(controller.signal)), stopped]); }
  finally { clearTimeout(timeout); signal.removeEventListener('abort', stop); }
}

export function FileAttachmentCreateControl(props: UrlAttachmentCreateProps) {
  return <FileControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function FileControl(props: UrlAttachmentCreateProps) {
  const [draft, setDraft] = useState<Draft>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false); const [notice, setNotice] = useState<string>();
  const [phase, setPhase] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const callbacks = useRef(props); callbacks.current = props;
  const action = useRef<HTMLButtonElement>(null); const focusRequested = useRef(false);
  const selection = useRef<HTMLInputElement>(null);
  useEffect(() => { mounted.current = true; return () => {
    mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false);
  }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useEffect(() => { if (props.unavailable || !props.editable) pending.current?.abort(); }, [props.unavailable, props.editable]);
  useEffect(() => {
    if (!focusRequested.current || busy || props.disabled || props.unavailable || !props.editable) return;
    const target = action.current && !action.current.disabled ? action.current : selection.current && !selection.current.disabled ? selection.current : null;
    if (target) { target.focus({ preventScroll: true }); focusRequested.current = !!intent || blocked; }
  }, [busy, props.disabled, props.unavailable, props.editable, draft, intent, blocked]);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const conflict = !!draft && draft.options.cardVersion !== props.version;
  function finish(controller: AbortController) {
    if (mounted.current && pending.current === controller) {
      pending.current = undefined; setBusy(false); setPhase(undefined); callbacks.current.onBusyChange(false);
    }
  }
  async function review() {
    if (disabled || pending.current || intent) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    const version = props.version; setPhase('Checking file upload access…');
    try {
      const reviewed = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const options = parseAttachmentUploadOptions(await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/attachment-upload-options`, { signal }), props, version);
        return { actor: profile.id, options };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== version) throw new Error();
      setDraft(reviewed); setBlocked(false); focusRequested.current = true;
    } catch { if (mounted.current && pending.current === controller) { setNotice('File upload is unavailable. Refresh the Card and try again.'); focusRequested.current = true; } }
    finally { finish(controller); }
  }
  async function save() {
    if (disabled || pending.current || blocked || !draft?.file || !intent && conflict) return;
    const file = draft.file; const name = file.name.trim(); const sizeBytes = file.size;
    if (!intent && (!name || name.length > 255 || /[\p{Cc}\p{Cf}]/u.test(name)
      || new TextDecoder().decode(new TextEncoder().encode(name)) !== name)) {
      setNotice('Choose a file with a name of 1 to 255 characters without control characters.'); return;
    }
    if (!intent && (file.size < 1 || file.size > draft.options.maximumBytes)) { setNotice('Choose a nonempty file within the displayed upload size limit.'); return; }
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    let command = intent; let posted = false;
    try {
      const value = await boundedUpload(async signal => {
        const profile = await boundedWorkRead(current => workRequest<unknown>('/me', { signal: current }), signal);
        if (!isNotificationProfile(profile) || profile.id !== draft.actor) throw new WorkRequestError(401, null);
        if (!command) {
          setPhase('Preparing selected file…');
          const digest = await attachmentFileDigest(file, draft.options.maximumBytes, signal);
          if (!mounted.current || callbacks.current.unavailable || !callbacks.current.editable || callbacks.current.version !== draft.options.cardVersion) throw new WorkRequestError(409, null);
          // Recheck actor after lengthy local preparation, before any mutation.
          const current = await boundedWorkRead(token => workRequest<unknown>('/me', { signal: token }), signal);
          if (!isNotificationProfile(current) || current.id !== draft.actor) throw new WorkRequestError(401, null);
          if (file.size !== sizeBytes) throw new Error();
          command = { actor: draft.actor, file, name, sizeBytes, digest, cardVersion: draft.options.cardVersion, key: crypto.randomUUID() };
        }
        signal.throwIfAborted(); setIntent(command); posted = true; setPhase('Uploading file for a safety scan…');
        const encoded = btoa(Array.from(new TextEncoder().encode(command.name), byte => String.fromCharCode(byte)).join(''));
        const result = await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/attachments`, { method: 'POST', signal, body: command.file,
          headers: { 'Content-Type': 'application/octet-stream', 'Idempotency-Key': command.key, 'X-Attachment-Name': encoded,
            'X-Attachment-Size': String(command.sizeBytes), 'X-Attachment-SHA256': command.digest, 'X-Card-Version': String(command.cardVersion) } });
        const current = await boundedWorkRead(token => workRequest<unknown>('/me', { signal: token }), signal)
          .catch(() => { throw new WorkRequestError(401, null); });
        if (!isNotificationProfile(current) || current.id !== command.actor) throw new WorkRequestError(401, null);
        return result;
      }, controller.signal);
      if (!mounted.current || pending.current !== controller || !command) return;
      parseFileAttachmentCreated(value, props, command.actor, command.name, command.sizeBytes, command.cardVersion);
      setIntent(undefined); setDraft(undefined); setBlocked(false); setNotice('File attached. Safety scan pending.'); focusRequested.current = true; props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      focusRequested.current = true;
      const terminal = error instanceof WorkRequestError && [400, 401, 403, 404, 409, 413].includes(error.status) && error.code !== 'attachment_upload_in_progress';
      if (terminal) {
        const denied = [401, 403, 404].includes(error.status);
        if (denied) { setDraft(undefined); setIntent(undefined); }
        setBlocked(true); setNotice(denied ? 'This file upload is unavailable. Load the current Card before reviewing another change.'
          : 'This file upload is unavailable. Your selected file is preserved. Load the current Card before reviewing another change.');
      }
      else if (posted || intent) { setIntent(command); setNotice('The file upload is unconfirmed. Retry the original file to recover its acknowledgment.'); }
      else setNotice('File preparation stopped. Your selected file is preserved.');
      if (posted || terminal || intent) props.onRefresh();
    } finally { finish(controller); }
  }
  return <Stack component="section" aria-label="Create file attachment" spacing={1} sx={{ my: 2 }}>
    {phase && <Typography role="status">{phase}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {busy && <Button onClick={() => pending.current?.abort()}>Stop file upload</Button>}
    {!draft ? <Button ref={action} disabled={disabled} onClick={() => void review()}>Add file attachment</Button>
      : props.unavailable || !props.editable ? <Typography>Checking current Card upload access…</Typography> : <>
        <Typography>Maximum file size: {draft.options.maximumBytes.toLocaleString()} bytes. Supported files: {draft.options.allowedMimeTypes.map(type => typeNames[type]).join(', ')}.</Typography>
        {draft.file && <Typography sx={{ overflowWrap: 'anywhere' }}>Selected file: {draft.file.name} ({draft.file.size.toLocaleString()} bytes)</Typography>}
        {conflict && !intent && <Alert severity="warning">This Card changed elsewhere. Your selected file is preserved.</Alert>}
        <TextField inputRef={selection} type="file" label="File to attach" disabled={disabled || !!intent || blocked}
          slotProps={{ inputLabel: { shrink: true }, htmlInput: { accept: draft.options.allowedMimeTypes.join(',') } }}
          onChange={event => { setDraft({ ...draft, file: (event.target as HTMLInputElement).files?.[0] }); setNotice(undefined); }} />
        {!blocked && <Button ref={action} disabled={disabled || !draft.file || !intent && conflict} onClick={() => void save()}
          onBlur={event => { if (!ownsRecoveryFocus(event.relatedTarget, action.current)) focusRequested.current = false; }}>
          {intent ? 'Retry original file upload' : 'Upload selected file'}
        </Button>}
        {(!intent || blocked) && <Button ref={blocked ? action : undefined} disabled={busy || props.disabled || props.unavailable}
          onClick={() => { setDraft(undefined); setIntent(undefined); setBlocked(false); setNotice(undefined); focusRequested.current = true; props.onRefresh(); }}>
          Discard selected file and load latest
        </Button>}
      </>}
    {draft && blocked && !props.unavailable && !props.editable && <Button disabled={busy || props.disabled}
      onClick={() => { setDraft(undefined); setIntent(undefined); setBlocked(false); setNotice(undefined); props.onRefresh(); }}>
      Discard selected file and load latest
    </Button>}
  </Stack>;
}
