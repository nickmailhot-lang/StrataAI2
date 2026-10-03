import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseAttachmentPage, type AttachmentMetadata, type AttachmentPage, type AttachmentScope, type ArchivedAttachment } from './attachments';
import { parseAttachmentArchivePage, parseAttachmentLifecycleChanged, type AttachmentArchivePage, type AttachmentLifecycleAction } from './attachmentLifecycle';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import type { UrlAttachmentCreateProps } from './UrlAttachmentCreateControl';

type Props = UrlAttachmentCreateProps & { canAdminister: boolean };
type Source = AttachmentMetadata | ArchivedAttachment;
type Review = { actor: string; archive: boolean; page: AttachmentPage | AttachmentArchivePage; cursor?: string };
type Draft = { actor: string; file: Source; action: AttachmentLifecycleAction; cardVersion: number; confirmed: boolean };
type Intent = Draft & { key: string };
export function AttachmentManageControl(props: Props) {
  return <Management key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function Management(props: Props) {
  const [review, setReview] = useState<Review>(); const [draft, setDraft] = useState<Draft>();
  const [intent, setIntent] = useState<Intent>(); const [busy, setBusy] = useState(false); const [blocked, setBlocked] = useState(false);
  const [notice, setNotice] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const callbacks = useRef(props); callbacks.current = props; const mounted = useRef(false);
  const primary = useRef<HTMLButtonElement>(null); const saveButton = useRef<HTMLButtonElement>(null); const retryButton = useRef<HTMLButtonElement>(null);
  const cancel = useRef<HTMLButtonElement>(null); const consent = useRef<HTMLInputElement>(null);
  const focusOwner = useRef<HTMLElement | null>(null); const focusDialog = useRef<HTMLElement | null>(null); const requestedFocus = useRef(false);
  const disabled = busy || props.disabled || props.unavailable;
  const conflict = !!draft && draft.cardVersion !== props.version;
  function requestFocus(owner: HTMLElement) {
    focusOwner.current = owner; focusDialog.current = owner.closest('[role="dialog"][data-mui-focusable]');
    requestedFocus.current = true; parkRecoveryFocus(owner);
  }
  function releaseFocus(event: React.FocusEvent<HTMLElement>) {
    if (!ownsRecoveryFocus(event.relatedTarget, event.currentTarget)) requestedFocus.current = false;
  }
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useEffect(() => {
    if (!requestedFocus.current || disabled || !(ownsRecoveryFocus(document.activeElement, focusOwner.current) || document.activeElement === focusDialog.current)) return;
    const target = intent ? retryButton.current : blocked ? cancel.current : draft?.action === 'delete' && !draft.confirmed ? consent.current
      : draft ? saveButton.current : primary.current;
    if (target && !target.disabled) { target.focus({ preventScroll: true }); requestedFocus.current = !!intent || blocked; }
  }, [disabled, draft, intent, blocked, review]);
  async function load(archive: boolean, owner: HTMLElement, cursor?: string) {
    if (pending.current || disabled || intent || draft) return;
    requestFocus(owner); const controller = new AbortController(); pending.current = controller; setBusy(true); setReview(undefined); setNotice(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const value = await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/attachments${archive ? '/archive' : ''}${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal });
        const page = archive ? parseAttachmentArchivePage(value, props, cursor) : parseAttachmentPage(value, props, cursor);
        if (page.cardVersion !== version) throw new WorkRequestError(409, null);
        return { actor: profile.id, archive, page, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || callbacks.current.version !== version) throw new Error();
      setReview(result); setBlocked(false);
    } catch { if (mounted.current) { setNotice('Unable to review current attachments. Refresh the Card and try again.'); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  function stage(file: Source, action: AttachmentLifecycleAction, owner: HTMLElement) {
    if (!review || disabled || intent || blocked || review.page.cardVersion !== props.version || !props.editable) return;
    const allowed = action === 'archive' ? !review.archive && 'canEdit' in review.page && review.page.canEdit
      : review.archive && 'canRestore' in review.page && (action === 'restore' ? review.page.canRestore : review.page.canDelete && props.canAdminister);
    if (!allowed) return;
    requestFocus(owner); setDraft({ actor: review.actor, file, action, cardVersion: review.page.cardVersion, confirmed: false });
  }
  async function save(owner: HTMLElement) {
    if (pending.current || disabled || blocked || !draft || !intent && (conflict || !props.editable
      || draft.action === 'delete' && (!props.canAdminister || !draft.confirmed))) return;
    const command = intent ?? { ...draft, key: crypto.randomUUID() };
    requestFocus(owner); const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id !== command.actor) throw new WorkRequestError(401, null);
        const scope: AttachmentScope = props;
        const path = command.action === 'delete'
          ? `/attachments/${encodeURIComponent(command.file.id)}?cardId=${encodeURIComponent(scope.cardId)}&cardVersion=${command.cardVersion}&version=${command.file.version}&confirmed=true`
          : `/cards/${encodeURIComponent(scope.cardId)}/attachments/${encodeURIComponent(command.file.id)}/${command.action}`;
        return workRequest<unknown>(path, { method: command.action === 'delete' ? 'DELETE' : 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key },
          ...(command.action === 'delete' ? {} : { body: JSON.stringify({ cardVersion: command.cardVersion, version: command.file.version }) }) });
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      parseAttachmentLifecycleChanged(value, props, command.file, command.cardVersion, command.actor, command.action);
      setIntent(undefined); setDraft(undefined); setReview(undefined); setBlocked(false);
      setNotice(command.action === 'archive' ? 'Attachment archived.' : command.action === 'restore' ? 'Attachment restored.' : 'Attachment permanently deleted.'); props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setBlocked(true); setNotice('This attachment change is unavailable. Load the latest Card before reviewing another change.');
      } else { setIntent(command); setNotice('The attachment change is unconfirmed. Retry the original request to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  const reviewCurrent = review?.page.cardVersion === props.version;
  return <Stack component="section" aria-label="Manage Card attachments" spacing={1} sx={{ my: 2 }}>
    <Button ref={primary} disabled={disabled || !!intent || !!draft || blocked} onBlur={releaseFocus}
      onClick={event => void load(false, event.currentTarget)}>Manage attachments</Button>
    {busy && <Typography role="status">{draft ? 'Saving attachment change…' : 'Checking current attachments…'}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography role="status">Checking current Card access…</Typography> : <>
      {review && !draft && <>
        {!reviewCurrent && <Alert severity="warning">This Card changed. Review the latest attachments before making a change.</Alert>}
        <Typography>{review.archive ? 'Archived attachments' : 'Active attachments'}</Typography>
        {review.page.items.length === 0 && <Typography>No attachments on this page.</Typography>}
        {review.page.items.map(file => <Stack key={file.id} spacing={0.5} role="group" aria-label={`Attachment ${file.displayName}`}>
          <Typography sx={{ overflowWrap: 'anywhere' }}>{file.displayName}</Typography>
          {review.archive ? <>
            <Button disabled={disabled || !reviewCurrent || !props.editable || !('canRestore' in review.page && review.page.canRestore)} onBlur={releaseFocus}
              onClick={event => stage(file, 'restore', event.currentTarget)}>Restore attachment {file.displayName}</Button>
            <Button disabled={disabled || !reviewCurrent || !props.editable || !props.canAdminister || !('canDelete' in review.page && review.page.canDelete)} onBlur={releaseFocus}
              onClick={event => stage(file, 'delete', event.currentTarget)}>Delete attachment {file.displayName}</Button>
          </> : <Button disabled={disabled || !reviewCurrent || !props.editable || !('canEdit' in review.page && review.page.canEdit)} onBlur={releaseFocus}
            onClick={event => stage(file, 'archive', event.currentTarget)}>Archive attachment {file.displayName}</Button>}
        </Stack>)}
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
          <Button disabled={disabled} onBlur={releaseFocus} onClick={event => void load(!review.archive, event.currentTarget)}>{review.archive ? 'Review active attachments' : 'Review attachment archive'}</Button>
          <Button disabled={disabled || !review.cursor} onBlur={releaseFocus} onClick={event => void load(review.archive, event.currentTarget)}>First managed attachment page</Button>
          <Button disabled={disabled || !reviewCurrent || !review.page.nextCursor} onBlur={releaseFocus} onClick={event => void load(review.archive, event.currentTarget, review.page.nextCursor!)}>Next managed attachment page</Button>
        </Stack>
      </>}
      {draft && <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>{draft.file.displayName}</Typography>
        {conflict && !intent && <Alert severity="warning">The Card changed. Review the latest attachment before confirming.</Alert>}
        {draft.action === 'delete' ? <>
          <Alert severity="warning">Permanent deletion cannot be undone. This attachment will lose access and cannot be restored.</Alert>
          <FormControlLabel label="I understand this attachment deletion cannot be undone" control={<Checkbox slotProps={{ input: { ref: consent } }} checked={draft.confirmed}
            disabled={disabled || !!intent || blocked || conflict || !props.canAdminister || !props.editable}
            onChange={event => setDraft({ ...draft, confirmed: event.target.checked })} />} />
        </> : <Typography>{draft.action === 'archive' ? 'Archiving hides this attachment from normal access. You can restore it later.' : 'Restoring returns this attachment to the Card. Safety scan requirements still apply.'}</Typography>}
        {intent ? <Button ref={retryButton} disabled={disabled} onBlur={releaseFocus} onClick={event => void save(event.currentTarget)}>Retry original attachment change</Button>
          : <Button ref={saveButton} disabled={disabled || blocked || conflict || !props.editable || draft.action === 'delete' && (!props.canAdminister || !draft.confirmed)}
            onBlur={releaseFocus} onClick={event => void save(event.currentTarget)}>{draft.action === 'archive' ? 'Confirm attachment archive' : draft.action === 'restore' ? 'Confirm attachment restore' : 'Permanently delete attachment'}</Button>}
        {!intent && <Button ref={cancel} disabled={disabled} onBlur={releaseFocus} onClick={event => {
          requestFocus(event.currentTarget); setDraft(undefined); setReview(undefined); setBlocked(false); setNotice(undefined); props.onRefresh();
        }}>Discard attachment review and load latest</Button>}
      </>}
    </>}
  </Stack>;
}
