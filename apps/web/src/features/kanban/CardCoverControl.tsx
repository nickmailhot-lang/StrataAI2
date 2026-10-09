import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseCardCoverCandidates, parseCardCoverChange, parseCardCoverView, type CardCoverCandidate, type CardCoverCandidatePage, type CardCoverIntent, type CardCoverView } from './cardCovers';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import type { UrlAttachmentCreateProps } from './UrlAttachmentCreateControl';

type Review = { actor: string; view: CardCoverView; page: CardCoverCandidatePage; cursor?: string };
type Draft = { actor: string; view: CardCoverView; candidate: CardCoverCandidate | null; confirmed: boolean };
type Intent = Draft & { key: string; input: CardCoverIntent };
export function CardCoverControl(props: UrlAttachmentCreateProps) {
  return <CoverControl key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function CoverControl(props: UrlAttachmentCreateProps) {
  const [review, setReview] = useState<Review>(); const [draft, setDraft] = useState<Draft>();
  const [intent, setIntent] = useState<Intent>(); const [blocked, setBlocked] = useState(false); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false); const callbacks = useRef(props); callbacks.current = props;
  const primary = useRef<HTMLButtonElement>(null); const saveButton = useRef<HTMLButtonElement>(null); const retry = useRef<HTMLButtonElement>(null);
  const discard = useRef<HTMLButtonElement>(null); const consent = useRef<HTMLInputElement>(null);
  const focusOwner = useRef<HTMLElement | null>(null); const focusDialog = useRef<HTMLElement | null>(null); const restoreFocus = useRef(false);
  const disabled = busy || props.disabled || props.unavailable; const conflict = !!draft && draft.view.cardVersion !== props.version;
  const path = `/cards/${encodeURIComponent(props.cardId)}/cover`;
  function focus(owner: HTMLElement) {
    focusOwner.current = owner; focusDialog.current = owner.closest('[role="dialog"][data-mui-focusable]'); restoreFocus.current = true; parkRecoveryFocus(owner);
  }
  function blur(event: React.FocusEvent<HTMLElement>) {
    if (event.relatedTarget !== focusOwner.current && !ownsRecoveryFocus(event.relatedTarget, event.currentTarget)) restoreFocus.current = false;
  }
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { props.onRecoveryChange(!!intent || blocked); }, [intent, blocked, props.onRecoveryChange]);
  useLayoutEffect(() => {
    if (disabled || !restoreFocus.current || !(ownsRecoveryFocus(document.activeElement, focusOwner.current) || document.activeElement === focusDialog.current)) return;
    const target = intent ? retry.current : blocked ? discard.current : draft?.candidate && draft.view.isPublic && !draft.confirmed ? consent.current
      : draft ? saveButton.current : primary.current;
    if (target && !target.disabled) { focusOwner.current = target; target.focus({ preventScroll: true }); restoreFocus.current = true; }
  }, [disabled, draft, intent, blocked, review]);
  async function load(owner: HTMLElement, cursor?: string) {
    if (pending.current || disabled || intent || draft || blocked) return;
    focus(owner); const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); setReview(undefined);
    const version = props.version;
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
        const view = parseCardCoverView(await workRequest<unknown>(path, { signal }), props, version);
        const page = parseCardCoverCandidates(await workRequest<unknown>(path + '/candidates' + (cursor ? '?after=' + encodeURIComponent(cursor) : ''), { signal }), props, version, cursor);
        const current = await workRequest<unknown>('/me', { signal }).catch(() => { throw new WorkRequestError(401, null); });
        if (!isNotificationProfile(current) || current.id.toLowerCase() !== profile.id.toLowerCase()) throw new WorkRequestError(401, null);
        if (view.canEdit !== page.canEdit || view.isPublic !== page.isPublic) throw new Error();
        return { actor: profile.id, view, page, cursor };
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (callbacks.current.unavailable || callbacks.current.version !== version) throw new Error();
      setReview(result);
    } catch { if (mounted.current && pending.current === controller) { setNotice('Unable to review the current cover. Refresh the Card and try again.'); props.onRefresh(); } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  function stage(candidate: CardCoverCandidate | null, owner: HTMLElement) {
    if (!review || disabled || draft || intent || blocked || !props.editable || !review.view.canEdit || review.view.cardVersion !== props.version) return;
    if (candidate === null && review.view.attachmentId === null) return;
    focus(owner); setDraft({ actor: review.actor, view: review.view, candidate, confirmed: false });
  }
  async function save(owner: HTMLElement) {
    if (pending.current || disabled || blocked || !draft || !intent && (conflict || !props.editable || !draft.view.canEdit
      || draft.candidate && draft.view.isPublic && !draft.confirmed)) return;
    const command: Intent = intent ?? { ...draft, key: crypto.randomUUID(), input: {
      attachmentId: draft.candidate?.attachmentId ?? null, attachmentVersion: draft.candidate?.attachmentVersion ?? null,
      cardVersion: draft.view.cardVersion, publicVisibilityConfirmed: draft.candidate !== null && draft.view.isPublic && draft.confirmed } };
    const originalRetry = !!intent;
    focus(owner); const controller = new AbortController(); pending.current = controller; setBusy(true); setNotice(undefined); props.onBusyChange(true);
    try {
      const value = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id.toLowerCase() !== command.actor.toLowerCase()) throw new WorkRequestError(401, null);
        if (callbacks.current.unavailable || !originalRetry && (callbacks.current.version !== command.input.cardVersion || !callbacks.current.editable))
          throw new WorkRequestError(409, null);
        const result = await workRequest<unknown>(path, { method: 'PUT', signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify(command.input) });
        parseCardCoverChange(result, props, command.view, command.input);
        const current = await workRequest<unknown>('/me', { signal }).catch(() => { throw new WorkRequestError(401, null); });
        if (!isNotificationProfile(current) || current.id.toLowerCase() !== command.actor.toLowerCase()) throw new WorkRequestError(401, null);
        return result;
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      parseCardCoverChange(value, props, command.view, command.input);
      setIntent(undefined); setDraft(undefined); setReview(undefined); setBlocked(false);
      setNotice(command.candidate === null ? 'Card cover removed.' : 'Card cover updated.'); props.onRefresh();
    } catch (error) {
      if (!mounted.current || pending.current !== controller) return;
      if (error instanceof WorkRequestError && [400, 401, 403, 404, 409, 429].includes(error.status)) {
        if ([401, 403, 404].includes(error.status)) { setReview(undefined); setDraft(undefined); }
        setIntent(undefined); setBlocked(true); setNotice('This cover change is unavailable. Load the latest Card before reviewing another change.');
      } else { setIntent(command); setNotice('The cover change is unconfirmed. Retry the original request to recover its acknowledgment.'); }
      props.onRefresh();
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); props.onBusyChange(false); } }
  }
  const current = review?.view.cardVersion === props.version;
  return <Stack component="section" aria-label="Card cover" spacing={1} sx={{ my: 2 }}>
    <Button ref={primary} disabled={disabled || !!draft || !!intent || blocked} onBlur={blur} onClick={event => void load(event.currentTarget)}>Review Card cover</Button>
    {busy && <Typography role="status">{draft ? 'Saving cover change…' : 'Checking current cover images…'}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography role="status">Checking current Card access…</Typography> : <>
      {blocked && !draft && <Button ref={discard} disabled={disabled} onBlur={blur} onClick={event => {
        focus(event.currentTarget); setBlocked(false); setNotice(undefined); props.onRefresh();
      }}>Load latest Card before reviewing a cover</Button>}
      {review && !draft && <>
        {!current && <Alert severity="warning">This Card changed. Review its latest cover before making a change.</Alert>}
        <Typography>{review.view.attachmentId === null ? 'This Card has no cover.' : 'This Card has a selected image cover.'}</Typography>
        {review.page.items.length === 0 && <Typography>No eligible image attachments on this page. Attach an image and wait for its safety checks.</Typography>}
        {review.page.items.map(candidate => <Button key={candidate.attachmentId} disabled={disabled || !current || !props.editable || !review.view.canEdit} onBlur={blur}
          onClick={event => stage(candidate, event.currentTarget)}>Use {candidate.displayName} as cover</Button>)}
        <Button disabled={disabled || !current || !props.editable || !review.view.canEdit || review.view.attachmentId === null} onBlur={blur}
          onClick={event => stage(null, event.currentTarget)}>Remove Card cover</Button>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
          <Button disabled={disabled} onBlur={blur} onClick={event => void load(event.currentTarget)}>Review latest cover</Button>
          <Button disabled={disabled || !review.cursor} onBlur={blur} onClick={event => void load(event.currentTarget)}>First cover image page</Button>
          <Button disabled={disabled || !current || !review.page.nextCursor} onBlur={blur} onClick={event => void load(event.currentTarget, review.page.nextCursor!)}>Next cover image page</Button>
        </Stack>
      </>}
      {draft && <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>{draft.candidate?.displayName ?? 'Remove the selected cover'}</Typography>
        {conflict && !intent && <Alert severity="warning">The Card changed. Review the latest cover before confirming.</Alert>}
        {draft.candidate && draft.view.isPublic ? <>
          <Alert severity="warning">This image will be visible to anyone who can view this PUBLIC Board, including visitors who are not signed in.</Alert>
          <FormControlLabel label="I understand this cover image will be publicly visible" control={<Checkbox slotProps={{ input: { ref: consent } }} checked={draft.confirmed}
            disabled={disabled || !!intent || blocked || conflict || !props.editable} onChange={event => setDraft({ ...draft, confirmed: event.target.checked })} />} />
        </> : <Typography>{draft.candidate ? 'This image will appear as the Card cover.' : 'Removing the cover keeps its attachment on this Card.'}</Typography>}
        {intent ? <Button ref={retry} disabled={disabled} onBlur={blur} onClick={event => void save(event.currentTarget)}>Retry original cover change</Button>
          : <Button ref={saveButton} disabled={disabled || blocked || conflict || !props.editable || !!draft.candidate && draft.view.isPublic && !draft.confirmed}
            onBlur={blur} onClick={event => void save(event.currentTarget)}>{draft.candidate ? 'Confirm Card cover' : 'Confirm cover removal'}</Button>}
        {!intent && <Button ref={discard} disabled={disabled} onBlur={blur} onClick={event => {
          focus(event.currentTarget); setDraft(undefined); setReview(undefined); setBlocked(false); setNotice(undefined); props.onRefresh();
        }}>Discard cover review and load latest</Button>}
      </>}
    </>}
  </Stack>;
}
