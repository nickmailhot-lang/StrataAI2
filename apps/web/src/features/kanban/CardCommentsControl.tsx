import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { normalizeComment, parseCardCommentChange, parseCardCommentPage, type CardComment, type CardCommentChange, type CardCommentPage, type CommentIntent } from './cardComments';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import type { UrlAttachmentCreateProps } from './UrlAttachmentCreateControl';

type Review = { actor: string; page: CardCommentPage; cursor?: string };
type Draft = { actor: string; version: number; original: CardComment | null; text: string; deleting: boolean; confirmed: boolean };
type Intent = { actor: string; key: string; path: string; method: string; body: string; check: CommentIntent };
export type CardCommentsProps = UrlAttachmentCreateProps & { reconnectSequence?: number };
export function CardCommentsControl(props: CardCommentsProps) {
  return <CommentsControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function CommentsControl(props: CardCommentsProps) {
  const [review, setReview] = useState<Review>(); const [draft, setDraft] = useState<Draft>();
  const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const [acknowledged, setAcknowledged] = useState<CardCommentChange>();
  const observedReconnect = useRef(props.reconnectSequence);
  const mounted = useRef(false); const callbacks = useRef(props); callbacks.current = props;
  const primary = useRef<HTMLButtonElement>(null); const editor = useRef<HTMLInputElement>(null); const consent = useRef<HTMLInputElement>(null);
  const retry = useRef<HTMLButtonElement>(null); const discard = useRef<HTMLButtonElement>(null);
  const focusOwner = useRef<HTMLElement | null>(null); const focusDialog = useRef<HTMLElement | null>(null); const restoreFocus = useRef(false);
  const disabled = busy || props.disabled || props.unavailable; const conflict = !!draft && draft.version !== props.version;
  const path = `/cards/${encodeURIComponent(props.cardId)}/comments`;
  function focus(owner: HTMLElement) {
    focusOwner.current = owner; focusDialog.current = owner.closest('[role="dialog"][data-mui-focusable]'); restoreFocus.current = true; parkRecoveryFocus(owner);
  }
  function blur(event: React.FocusEvent<HTMLElement>) { if (!ownsRecoveryFocus(event.relatedTarget, event.currentTarget)) restoreFocus.current = false; }
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!draft || !!intent || blocked); }, [draft, intent, blocked, props.onRecoveryChange]);
  useEffect(() => {
    if (disabled || !restoreFocus.current || !(ownsRecoveryFocus(document.activeElement, focusOwner.current) || document.activeElement === focusDialog.current)) return;
    const target = intent ? retry.current : blocked ? discard.current : draft ? draft.deleting ? consent.current : editor.current : primary.current;
    if (target && !target.disabled) { target.focus({ preventScroll: true }); restoreFocus.current = !!intent || blocked; }
  }, [disabled, draft, intent, blocked, review]);
  useEffect(() => {
    // A clean opened view follows aggregate invalidations and reconnects.
    // Retire the old cursor and reread the bounded first page. Dirty drafts and
    // uncertain original receipts retain their captured concurrency boundary.
    const version = review?.page.cardVersion ?? acknowledged?.cardVersion;
    if (version === undefined || props.version < version || pending.current || disabled || draft || intent || blocked) return;
    if (props.version !== version || observedReconnect.current !== props.reconnectSequence) void load();
  }, [props.version, props.reconnectSequence, disabled, draft, intent, blocked, review, acknowledged]);
  async function load(owner?: HTMLElement, cursor?: string) {
    if (pending.current || disabled || draft || intent || blocked) return;
    if (owner) focus(owner);
    observedReconnect.current = props.reconnectSequence;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setReview(undefined); setAcknowledged(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const page = parseCardCommentPage(await workRequest<unknown>(path + (cursor ? '?after=' + encodeURIComponent(cursor) : ''), { signal }), props, version, cursor);
        const current = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(current) || current.id.toLowerCase() !== profile.id.toLowerCase()) throw new WorkRequestError(401, null);
        return { actor: profile.id, page, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || callbacks.current.version !== version) throw new Error();
      setReview(result);
    } catch { if (mounted.current && pending.current === controller) { setNotice('Unable to read current comments. Refresh the Card and try again.'); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  function stage(original: CardComment | null, deleting: boolean, owner: HTMLElement) {
    if (!review || disabled || draft || intent || blocked || !props.editable || !review.page.canComment || review.page.cardVersion !== props.version
      || original && (original.authorId.toLowerCase() !== review.actor.toLowerCase() || original.deletedAt !== null)) return;
    focus(owner); setDraft({ actor: review.actor, version: review.page.cardVersion, original: original ? { ...original } : null,
      text: original?.content ?? '', deleting, confirmed: false });
  }
  async function save(owner: HTMLElement) {
    if (pending.current || disabled || blocked || !draft || !intent && (conflict || !props.editable || draft.deleting && !draft.confirmed)) return;
    let command = intent;
    if (!command) {
      let text: string | undefined;
      try { text = draft.deleting ? undefined : normalizeComment(draft.text); }
      catch { setNotice('Enter valid comment text, up to 10000 characters.'); editor.current?.focus(); return; }
      const check: CommentIntent = { actor: draft.actor, cardVersion: draft.version, original: draft.original,
        content: text, deleting: draft.deleting, confirmed: draft.confirmed };
      command = { actor: draft.actor, key: crypto.randomUUID(), path: path + (draft.original ? '/' + encodeURIComponent(draft.original.id) : ''),
        method: draft.deleting ? 'DELETE' : draft.original ? 'PATCH' : 'POST', check,
        body: JSON.stringify(draft.deleting ? { cardVersion: draft.version, version: draft.original!.version, confirmed: true }
          : { content: text, cardVersion: draft.version, ...(draft.original ? { version: draft.original.version } : {}) }) };
    }
    const originalRetry = !!intent; const captured = command;
    focus(owner); const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const acknowledged = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id.toLowerCase() !== captured.actor.toLowerCase()) throw new WorkRequestError(401, null);
        if (callbacks.current.unavailable || !originalRetry && (callbacks.current.version !== captured.check.cardVersion || !callbacks.current.editable)) throw new WorkRequestError(409, null);
        const result = await workRequest<unknown>(captured.path, { method: captured.method, signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': captured.key }, body: captured.body });
        const admitted = parseCardCommentChange(result, props, captured.check);
        const current = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(current) || current.id.toLowerCase() !== captured.actor.toLowerCase()) throw new WorkRequestError(401, null);
        return admitted;
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      setIntent(undefined); setDraft(undefined); setReview(undefined); setBlocked(false);
      setAcknowledged(acknowledged);
      setNotice(captured.check.deleting ? 'Comment body removed.' : captured.check.original ? 'Comment saved.' : 'Comment added.'); props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        setIntent(undefined); setDraft(undefined); setReview(undefined); setAcknowledged(undefined); setBlocked(true);
        setNotice('This comment change is unavailable. Load the latest Card before starting another change.');
      } else { setIntent(captured); setNotice('The comment change is unconfirmed. Retry the original request to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  function reset(owner: HTMLElement) { focus(owner); setDraft(undefined); setIntent(undefined); setReview(undefined); setAcknowledged(undefined); setBlocked(false); setNotice(undefined); props.onRefresh(); }
  const current = review?.page.cardVersion === props.version;
  return <Stack component="section" aria-label="Card comments" spacing={1} sx={{ my: 2 }}>
    <Button ref={primary} disabled={disabled || !!draft || !!intent || blocked} onBlur={blur} onClick={event => void load(event.currentTarget)}>Review Card comments</Button>
    {busy && <Typography role="status">{draft ? 'Saving comment change…' : 'Checking current comments…'}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography role="status">Checking current Card access…</Typography> : <>
      {acknowledged && acknowledged.cardVersion === props.version && !review && !draft && <Stack aria-label="Acknowledged comment">
        <Typography variant="caption">You · {acknowledged.comment.createdAt}{acknowledged.comment.editedAt ? ' · Edited' : ''}</Typography>
        <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{acknowledged.comment.deletedAt ? 'Comment body removed.' : acknowledged.comment.content}</Typography>
      </Stack>}
      {review && !current && <Alert severity="warning">This Card changed. Review the latest comments.</Alert>}
      {review && current && !draft && <>
        {review.page.items.length === 0 && <Typography>No comments on this page.{review.page.canComment ? ' Add the first comment.' : ''}</Typography>}
        {review.page.items.map(item => <Stack key={item.id} spacing={0.5}>
          <Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>{item.authorId.toLowerCase() === review.actor.toLowerCase() ? 'You' : `Account ${item.authorId}`} · {item.createdAt}{item.editedAt ? ' · Edited' : ''}</Typography>
          <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{item.deletedAt ? 'Comment body removed.' : item.content}</Typography>
          {item.deletedAt === null && item.authorId.toLowerCase() === review.actor.toLowerCase() && <Stack direction={{ xs: 'column', sm: 'row' }}>
            <Button disabled={disabled || !props.editable || !review.page.canComment} onBlur={blur} onClick={event => stage(item, false, event.currentTarget)}>Edit comment</Button>
            <Button disabled={disabled || !props.editable || !review.page.canComment} onBlur={blur} onClick={event => stage(item, true, event.currentTarget)}>Remove comment body</Button>
          </Stack>}
        </Stack>)}
        <Button disabled={disabled || !props.editable || !review.page.canComment} onBlur={blur} onClick={event => stage(null, false, event.currentTarget)}>Add comment</Button>
        <Button disabled={disabled || !review.cursor} onBlur={blur} onClick={event => void load(event.currentTarget)}>First comment page</Button>
        <Button disabled={disabled || !review.page.nextCursor} onBlur={blur} onClick={event => void load(event.currentTarget, review.page.nextCursor!)}>Next comment page</Button>
      </>}
      {draft && <>
        {conflict && !intent && <Alert severity="warning">The Card changed. Discard this review and load the latest comments.</Alert>}
        {draft.deleting ? <FormControlLabel label="I confirm removal of my comment body" control={<Checkbox slotProps={{ input: { ref: consent } }} checked={draft.confirmed}
          disabled={disabled || !!intent || conflict} onChange={event => setDraft({ ...draft, confirmed: event.target.checked })} />} /> :
          <TextField label={draft.original ? 'Edit your comment' : 'New comment'} multiline minRows={3} inputRef={editor} value={draft.text}
            disabled={disabled || !!intent || blocked || conflict || !props.editable} slotProps={{ htmlInput: { maxLength: 10000 } }}
            onChange={event => setDraft({ ...draft, text: event.target.value })} />}
        {intent ? <Button ref={retry} disabled={disabled} onBlur={blur} onClick={event => void save(event.currentTarget)}>Retry original comment change</Button> :
          <Button disabled={disabled || blocked || conflict || !props.editable || draft.deleting && !draft.confirmed} onBlur={blur}
            onClick={event => void save(event.currentTarget)}>{draft.deleting ? 'Confirm comment removal' : 'Save comment'}</Button>}
      </>}
      {!intent && (draft || blocked) && <Button ref={discard} disabled={disabled} onBlur={blur} onClick={event => reset(event.currentTarget)}>Discard comment review and load latest</Button>}
    </>}
  </Stack>;
}
