import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';
import { isNotificationProfile, notificationUuid } from '../notifications/notificationInbox';
import { parseCardCoverCandidates, type CardCoverCandidate, type CardCoverCandidatePage } from './cardCovers';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import { activityEvent, activityResult } from './activityTelemetry';
import type { UrlAttachmentCreateProps } from './UrlAttachmentCreateControl';

type Props = UrlAttachmentCreateProps & { boardVersion: number };
type ReviewedBoard = { version: number; name: string; description: string | null; visibility: string };
type Review = { actor: string; board: ReviewedBoard; cardVersion: number; page: CardCoverCandidatePage; cursor?: string };
type Draft = Review & { candidate: CardCoverCandidate; confirmed: boolean };
type Intent = Draft & { key: string; input: { cardId: string; attachmentId: string; attachmentVersion: number; boardVersion: number; publicVisibilityConfirmed: boolean } };
class ChangedBackgroundAccount extends Error {}
function reviewedBoard(input: unknown, props: Props): ReviewedBoard {
  const s = input as BoardSnapshot | null; const b = s?.board as (BoardSnapshot['board'] & { visibility?: string }) | undefined;
  if (!b || b.id !== props.boardId || b.organizationId !== props.organizationId || s?.access?.canView !== true || s.access.canEdit !== true
    || b.lifecycleState !== 'active' || b.version !== props.boardVersion || !Number.isSafeInteger(b.version) || Number(b.version) < 1
    || typeof b.name !== 'string' || !b.name.trim() || b.name.length > 160 || !(b.description === null || typeof b.description === 'string')
    || !['PRIVATE', 'ORGANIZATION', 'PUBLIC'].includes(b.visibility ?? '')) throw new Error('Unavailable Board');
  return { version: b.version!, name: b.name, description: b.description, visibility: b.visibility! };
}
function acknowledgment(value: unknown, props: Props, intent: Intent) {
  const b = value as Record<string, unknown> | null;
  if (!b || b.id !== props.boardId || b.organizationId !== props.organizationId || b.version !== intent.board.version + 1
    || b.name !== intent.board.name || b.description !== intent.board.description || b.visibility !== intent.board.visibility
    || b.lifecycleState !== 'active' || b.backgroundType !== 'IMAGE' || !notificationUuid(b.backgroundValue)) throw new Error('Unconfirmed background');
}
export function BoardBackgroundImageControl(props: Props) {
  return <Control key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function Control(props: Props) {
  const [review, setReview] = useState<Review>(); const [draft, setDraft] = useState<Draft>(); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const mounted = useRef(false); const pending = useRef<AbortController | undefined>(undefined); const callbacks = useRef(props); callbacks.current = props;
  const primary = useRef<HTMLButtonElement>(null); const confirm = useRef<HTMLButtonElement>(null); const retry = useRef<HTMLButtonElement>(null);
  const consent = useRef<HTMLInputElement>(null); const discard = useRef<HTMLButtonElement>(null);
  const owner = useRef<HTMLElement | null>(null); const dialog = useRef<HTMLElement | null>(null); const restore = useRef(false);
  const disabled = busy || props.disabled || props.unavailable || !props.editable;
  const changed = !!draft && (draft.board.version !== props.boardVersion || draft.cardVersion !== props.version);
  const currentReview = !!review && review.board.version === props.boardVersion && review.cardVersion === props.version;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); callbacks.current.onBusyChange(false); callbacks.current.onRecoveryChange(false); }; }, []);
  useEffect(() => { callbacks.current.onRecoveryChange(!!intent); }, [intent]);
  useLayoutEffect(() => {
    if (disabled || !restore.current || !ownsRecoveryFocus(document.activeElement, owner.current, dialog.current)) return;
    const target = intent ? retry.current : draft && changed ? discard.current
      : draft && draft.board.visibility === 'PUBLIC' && !draft.confirmed ? consent.current : draft ? confirm.current : primary.current;
    // Acknowledgment can precede another access refresh that disables the
    // restored control. Keep owned focus recoverable until an intentional
    // move to another control cancels it through onBlur.
    if (target && !target.disabled) { owner.current = target; target.focus({ preventScroll: true }); restore.current = true; }
  }, [disabled, intent, draft, review, changed]);
  function focus(element: HTMLElement) { owner.current = element; dialog.current = element.closest('[role="dialog"][data-mui-focusable]'); restore.current = true; parkRecoveryFocus(element); }
  function blur(event: React.FocusEvent<HTMLElement>) {
    if (event.relatedTarget !== owner.current && !ownsRecoveryFocus(event.relatedTarget, event.currentTarget, dialog.current)) restore.current = false;
  }
  function current(c: AbortController) { return mounted.current && pending.current === c && !c.signal.aborted; }
  function start(element: HTMLElement) { focus(element); const c = new AbortController(); pending.current = c; setBusy(true); setNotice(undefined); callbacks.current.onBusyChange(true); return c; }
  function finish(c: AbortController) { if (pending.current === c) { pending.current = undefined; if (mounted.current) { setBusy(false); callbacks.current.onBusyChange(false); } } }
  function retire(message: string) { setIntent(undefined); setDraft(undefined); setReview(undefined); setNotice(message); }
  async function actor(signal: AbortSignal, expected?: string) {
    const p = await workRequest<unknown>('/me', { signal });
    if (!isNotificationProfile(p) || expected && p.id !== expected) throw new ChangedBackgroundAccount(); return p.id;
  }
  useEffect(() => {
    if (props.editable) return;
    pending.current?.abort(); pending.current = undefined; setBusy(false); callbacks.current.onBusyChange(false);
    retire('Board background changes are unavailable.');
  }, [props.editable]);
  async function load(element: HTMLElement, cursor?: string) {
    if (pending.current || disabled || intent || draft) return;
    const c = start(element); setReview(undefined); const started = performance.now(); activityEvent('board_read', 'use');
    try {
      const result = await boundedWorkRead(async signal => {
        const id = await actor(signal);
        const board = reviewedBoard(await workRequest<unknown>(`/boards/${encodeURIComponent(props.boardId)}`, { signal }), props);
        const page = parseCardCoverCandidates(await workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/cover/candidates${cursor ? '?after=' + encodeURIComponent(cursor) : ''}`, { signal }), props, props.version, cursor);
        if (!page.canEdit || page.isPublic !== (board.visibility === 'PUBLIC')) throw new Error('Changed image access');
        await actor(signal, id); return { actor: id, board, cardVersion: props.version, page, cursor };
      }, c.signal);
      if (!current(c)) return;
      // A concurrent foreground read conceals the controls but does not revoke
      // this fresh scoped API review. Keep the actor/rights/revision fences;
      // rendering remains concealed until current parent access is available.
      if (!callbacks.current.editable || callbacks.current.version !== result.cardVersion || callbacks.current.boardVersion !== result.board.version) throw new Error('Changed review');
      activityResult('board_read', true, started); setReview(result);
    } catch { if (current(c)) { activityResult('board_read', false, started); retire('Unable to review Board background images. Refresh and try again.'); callbacks.current.onRefresh(); } }
    finally { finish(c); }
  }
  async function save(element: HTMLElement) {
    if (pending.current || disabled || !draft || !intent && (changed || draft.board.visibility === 'PUBLIC' && !draft.confirmed)) return;
    const command = intent ?? { ...draft, key: crypto.randomUUID(), input: { cardId: props.cardId, attachmentId: draft.candidate.attachmentId,
      attachmentVersion: draft.candidate.attachmentVersion, boardVersion: draft.board.version, publicVisibilityConfirmed: draft.board.visibility === 'PUBLIC' && draft.confirmed } };
    const c = start(element); setIntent(command); const started = performance.now(); activityEvent('board_metadata_update', intent ? 'retry' : 'use');
    try {
      await boundedWorkRead(async signal => {
        await actor(signal, command.actor);
        if (!callbacks.current.editable) throw new WorkRequestError(403, null);
        // A background refresh may begin while the account proof awaits IO.
        // Check actual current Board admission rather than treating that
        // temporary loading flag as permanent loss of the original intent.
        const currentBoard = await workRequest<BoardSnapshot>(`/boards/${encodeURIComponent(props.boardId)}`, { signal });
        if (currentBoard.board?.id !== props.boardId || currentBoard.board.organizationId !== props.organizationId
          || currentBoard.board.lifecycleState !== 'active' || currentBoard.access?.canView !== true || currentBoard.access.canEdit !== true)
          throw new WorkRequestError(403, null);
        const value = await workRequest<unknown>(`/boards/${encodeURIComponent(props.boardId)}/background/image`, { method: 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify(command.input) });
        acknowledgment(value, props, command); await actor(signal, command.actor);
      }, c.signal);
      if (current(c)) { activityResult('board_metadata_update', true, started); retire('Board background updated.'); callbacks.current.onRefresh(); }
    } catch (error) { if (current(c)) {
      activityResult('board_metadata_update', false, started);
      if (error instanceof ChangedBackgroundAccount || error instanceof WorkRequestError && [400,401,403,404,409].includes(error.status)) {
        retire('This background change is unavailable. Review the current Board and images before another change.');
      } else { setNotice('The background change is unconfirmed. Retry the original change to recover its acknowledgment.'); }
      callbacks.current.onRefresh();
    } }
    finally { finish(c); }
  }
  return <Stack component="section" aria-label="Board background image" spacing={1} sx={{ my: 2 }}>
    <Button ref={primary} disabled={disabled || !!draft || !!intent} onBlur={blur} onClick={e => void load(e.currentTarget)}>Review Board background images</Button>
    {busy && <Typography role="status">{draft ? 'Saving Board background…' : 'Checking current Board images…'}</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {props.unavailable ? <Typography role="status">Checking current Board and Card access…</Typography> : <>
      {review && !draft && <>
        <Typography>Choose a checked image attachment from this Card. The Board keeps its own image reference.</Typography>
        {!currentReview && <Alert severity="warning">The Board or Card changed. Review the current images before selecting.</Alert>}
        {!review.page.items.length && <Typography>No eligible images on this page. Attach an image and wait for its safety checks.</Typography>}
        {review.page.items.map(candidate => <Button key={candidate.attachmentId} disabled={disabled || !currentReview} onBlur={blur}
          onClick={e => { focus(e.currentTarget); setDraft({ ...review, candidate, confirmed: false }); }}>Use {candidate.displayName} as Board background</Button>)}
        <Button disabled={disabled} onBlur={blur} onClick={e => void load(e.currentTarget)}>Review latest Board background images</Button>
        <Button disabled={disabled || !review.cursor} onBlur={blur} onClick={e => void load(e.currentTarget)}>First Board background image page</Button>
        <Button disabled={disabled || !currentReview || !review.page.nextCursor} onBlur={blur} onClick={e => void load(e.currentTarget, review.page.nextCursor!)}>Next Board background image page</Button>
      </>}
      {draft && <>
        <Typography sx={{ overflowWrap: 'anywhere' }}>{draft.candidate.displayName}</Typography>
        {changed && !intent && <Alert severity="warning">The Board or Card changed. Discard this review and review the current images.</Alert>}
        {draft.board.visibility === 'PUBLIC' && <>
          <Alert severity="warning">This background image will be publicly visible, including to visitors who are not signed in.</Alert>
          <FormControlLabel label="I understand this Board background image will be publicly visible" control={<Checkbox checked={draft.confirmed} slotProps={{ input: { ref: consent } }}
            disabled={disabled || !!intent || changed} onChange={e => setDraft({ ...draft, confirmed: e.target.checked })} />} />
        </>}
        {intent ? <Button ref={retry} disabled={disabled} onBlur={blur} onClick={e => void save(e.currentTarget)}>Retry original Board background change</Button>
          : <Button ref={confirm} disabled={disabled || changed || draft.board.visibility === 'PUBLIC' && !draft.confirmed} onBlur={blur}
            onClick={e => void save(e.currentTarget)}>Confirm Board background image</Button>}
        {!intent && <Button ref={discard} disabled={disabled} onBlur={blur} onClick={e => { focus(e.currentTarget); retire('Review discarded.'); callbacks.current.onRefresh(); }}>Discard Board background review</Button>}
      </>}
    </>}
  </Stack>;
}
