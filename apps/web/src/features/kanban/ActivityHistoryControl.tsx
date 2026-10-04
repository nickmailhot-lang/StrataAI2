import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Link, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { activityCardLink, activityLabel, parseActivityPage, type ActivityItem, type ActivityPage, type ActivityScope } from './activityHistory';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
import { activityEvent, activityResult } from './activityTelemetry';

type Props = ActivityScope & { unavailable: boolean; refreshSequence: string; onDenied: (error: Error) => void };
type View = { epoch: string; page: ActivityPage; profile: { locale: string; timezone: string } };
type Position = { cursor?: string; before?: ActivityItem };
export function ActivityHistoryControl(props: Props) {
  return <History key={`${props.organizationId}/${props.boardId}/${props.kind}/${props.targetId}`} {...props} />;
}
function History(props: Props) {
  const [opened, setOpened] = useState(false); const [view, setView] = useState<View>();
  const [notice, setNotice] = useState<{ epoch: string; message: string }>(); const [busy, setBusy] = useState(false);
  const [navigation, setNavigation] = useState({ refresh: props.refreshSequence, unavailable: props.unavailable, generation: 0, denied: false, positions: [{}] as Position[] });
  const [attempt, setAttempt] = useState(0);
  const positions = navigation.positions;
  const setPositions = (update: Position[] | ((previous: Position[]) => Position[])) => setNavigation(previous => ({
    ...previous, positions: typeof update === 'function' ? update(previous.positions) : update,
  }));
  const pending = useRef<AbortController | undefined>(undefined); const callbacks = useRef(props); callbacks.current = props;
  const primary = useRef<HTMLButtonElement>(null); const older = useRef<HTMLButtonElement>(null);
  const newer = useRef<HTMLButtonElement>(null); const newest = useRef<HTMLButtonElement>(null); const retry = useRef<HTMLButtonElement>(null);
  const close = useRef<HTMLButtonElement>(null);
  const focusOwner = useRef<HTMLElement | null>(null); const restoreFocus = useRef(false);
  function ownFocus(owner: HTMLElement) { focusOwner.current = owner; restoreFocus.current = true; parkRecoveryFocus(owner); }
  const epoch = `${props.refreshSequence}/${navigation.generation}`;
  // A changed parent read/realtime generation discards both the previous page
  // and its continuation. Rendering hides it immediately, before effects run.
  if (navigation.refresh !== props.refreshSequence || navigation.unavailable !== props.unavailable) {
    setNavigation({ refresh: props.refreshSequence, unavailable: props.unavailable, generation: navigation.generation + 1, denied: false, positions: [{}] });
    setView(undefined); setNotice(undefined); setBusy(false);
  }
  useEffect(() => {
    if (!opened || props.unavailable || navigation.denied) return;
    const controller = new AbortController(); pending.current?.abort(); pending.current = controller;
    setBusy(true); setView(undefined); setNotice(undefined);
    const captured = `${props.refreshSequence}/${navigation.generation}`;
    const action = props.kind === 'BOARD' ? 'board_read' : 'card_read';
    const started = performance.now(); activityEvent(action, 'use');
    const position = positions.at(-1)!;
    const path = `/${props.kind === 'BOARD' ? 'boards' : 'cards'}/${encodeURIComponent(props.targetId)}/activity`;
    void boundedWorkRead(async signal => {
      const profile = await workRequest<unknown>('/me', { signal }); if (!isNotificationProfile(profile)) throw new WorkRequestError(401, null);
      const page = parseActivityPage(await workRequest<unknown>(path + (position.cursor ? '?after=' + encodeURIComponent(position.cursor) : ''), { signal }),
        { organizationId: props.organizationId, boardId: props.boardId, kind: props.kind, targetId: props.targetId }, position.before);
      const current = await workRequest<unknown>('/me', { signal });
      if (!isNotificationProfile(current) || current.id.toLowerCase() !== profile.id.toLowerCase()) throw new WorkRequestError(401, null);
      return { epoch: captured, page, profile: { locale: current.locale, timezone: current.timezone } };
    }, controller.signal).then(result => {
      if (!controller.signal.aborted && pending.current === controller) { activityResult(action, true, started); setView(result); }
    }).catch((error: unknown) => {
      if (controller.signal.aborted || pending.current !== controller) return;
      activityResult(action, false, started);
      if (!(error instanceof WorkRequestError)) activityEvent(action, 'exception');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        // Clear the completed request before publishing the idle denial state.
        // Finally may run after its focus effect; setting busy=false twice would
        // not trigger another render when that effect observed a pending ref.
        pending.current = undefined;
        setView(undefined); setBusy(false);
        // Discard the continuation without scheduling another protected read.
        // Only a fresh parent access generation may resume this viewer.
        setNavigation(previous => ({ ...previous, positions: [{}], denied: true }));
        callbacks.current.onDenied(error);
        setNotice({ epoch: captured, message: 'Activity is unavailable. Refresh the Board to check access.' });
      } else setNotice({ epoch: captured, message: error instanceof WorkRequestError && error.status === 400
        ? 'This history page expired. Return to newest activity.' : 'Activity could not be loaded. Retry this page or return to newest activity.' });
    }).finally(() => { if (pending.current === controller) { pending.current = undefined; setBusy(false); } });
    return () => { controller.abort(); if (pending.current === controller) pending.current = undefined; };
  }, [opened, props.organizationId, props.boardId, props.kind, props.targetId, props.refreshSequence, props.unavailable, navigation.generation, navigation.denied, positions, attempt]);
  const admitted = !props.unavailable && view?.epoch === epoch ? view : undefined;
  const message = !props.unavailable && notice?.epoch === epoch ? notice.message : undefined;
  useEffect(() => {
    if (busy || pending.current || props.unavailable || !restoreFocus.current || !ownsRecoveryFocus(document.activeElement, focusOwner.current)) return;
    const target = navigation.denied ? close.current : !opened ? primary.current : message ? retry.current : admitted?.page.nextCursor ? older.current : positions.length > 1 ? newer.current : newest.current;
    if (target && !target.disabled) { target.focus({ preventScroll: true }); restoreFocus.current = false; }
  }, [busy, props.unavailable, opened, message, admitted, positions.length, navigation.denied]);
  const name = props.kind === 'BOARD' ? 'Board' : 'Card';
  return <Stack spacing={1} sx={{ minWidth: 0 }} onBlur={event => { if (!ownsRecoveryFocus(event.relatedTarget, focusOwner.current)) restoreFocus.current = false; }}>
    <Button ref={primary} disabled={props.unavailable || busy || navigation.denied} onClick={event => { ownFocus(event.currentTarget); if (opened) { setPositions([{}]); setAttempt(value => value + 1); } else { activityEvent(props.kind === 'BOARD' ? 'board_disclosure' : 'card_disclosure', 'open'); setOpened(true); } }}>
      {opened ? `Refresh ${name} activity` : `Review ${name} activity`}
    </Button>
    {opened && <Box component="section" aria-label={`${name} activity`} aria-busy={busy}>
      <Typography role="status" aria-live="polite">{props.unavailable ? 'Checking activity access…' : busy ? 'Loading activity…' : admitted ? `${admitted.page.items.length} activity events on this page.` : ''}</Typography>
      {message && <Alert severity="warning">{message}</Alert>}
      {message && !navigation.denied && <Button ref={retry} disabled={busy} onClick={event => { ownFocus(event.currentTarget); activityEvent(props.kind === 'BOARD' ? 'board_read' : 'card_read', 'retry'); setAttempt(value => value + 1); }}>Retry activity page</Button>}
      {admitted && <>
        {admitted.page.items.length === 0 && <Typography>No activity to review yet. Authorized changes will appear here.</Typography>}
        <Box component="ol" sx={{ pl: 3, m: 0 }}>
          {admitted.page.items.map(item => <Box component="li" key={item.eventId} sx={{ my: 1.5, overflowWrap: 'anywhere' }}>
            <Typography component="span">{item.actorLabel} {activityLabel(item)}.</Typography>{' '}
            <Typography component="time" dateTime={item.createdAt} variant="body2" sx={{ display: 'block' }}>
              {new Intl.DateTimeFormat(admitted.profile.locale, { dateStyle: 'medium', timeStyle: 'short', timeZone: admitted.profile.timezone }).format(new Date(item.createdAt))}
            </Typography>
            {activityCardLink(item) && <Link component={RouterLink} to={activityCardLink(item)!}>Open Card</Link>}
          </Box>)}
        </Box>
      </>}
      <Button ref={older} disabled={busy || props.unavailable || !admitted?.page.nextCursor} onClick={event => {
        if (!admitted?.page.nextCursor) return; ownFocus(event.currentTarget);
        setPositions(previous => [...previous, { cursor: admitted.page.nextCursor!, before: admitted.page.items.at(-1) }]);
      }}>Older activity</Button>
      <Button ref={newer} disabled={busy || props.unavailable || positions.length < 2} onClick={event => { ownFocus(event.currentTarget); setPositions(previous => previous.slice(0, -1)); }}>Newer activity</Button>
      <Button ref={newest} disabled={busy || props.unavailable || navigation.denied} onClick={event => { ownFocus(event.currentTarget); setPositions([{}]); setAttempt(value => value + 1); }}>Newest activity</Button>
      <Button ref={close} onClick={event => { ownFocus(event.currentTarget); pending.current?.abort(); setOpened(false); setView(undefined); setNotice(undefined); setPositions([{}]); setBusy(false); }}>Close activity</Button>
    </Box>}
  </Stack>;
}
