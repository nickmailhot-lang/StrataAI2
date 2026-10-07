import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseStarHistory, type StarHistoryPage } from './boardStarHistory';
import { activityEvent, activityResult } from './activityTelemetry';
import { formatUserDateTime } from '../auth/userDateTime';
import { watchIdentity } from '../auth/identityLive';
import { ownsRecoveryFocus, parkRecoveryFocus } from './focusRecovery';
type Props = { organizationId: string; boardId: string; userId: string; version: number; unavailable: boolean; onDenied: () => void };
type Position = { after: number; entityId?: string };
export function BoardStarHistory(props: Props) {
  return <History key={props.organizationId + '/' + props.boardId + '/' + props.userId + '/' + props.version} {...props} />;
}
function History(props: Props) {
  const [open, setOpen] = useState(false); const [positions, setPositions] = useState<Position[]>([{ after: 0 }]);
  const [attempt, setAttempt] = useState(0); const [busy, setBusy] = useState(false);
  const [view, setView] = useState<{ page: StarHistoryPage; locale: string; timezone: string }>();
  const [notice, setNotice] = useState<string>();
  const [admission, setAdmission] = useState({ unavailable: props.unavailable, denied: false });
  const pending = useRef<AbortController | undefined>(undefined);
  const refreshQueued = useRef(false);
  const denied = useRef(props.onDenied); denied.current = props.onDenied;
  const entry = useRef<HTMLButtonElement>(null); const next = useRef<HTMLButtonElement>(null);
  const retry = useRef<HTMLButtonElement>(null); const close = useRef<HTMLButtonElement>(null);
  const previous = useRef<HTMLButtonElement>(null);
  const restore = useRef(false);
  const focusOwner = useRef<HTMLElement | null>(null), retained = useRef<HTMLButtonElement | undefined>(undefined);
  const focusDialog = useRef<HTMLElement | null>(null);
  function ownFocus(owner: HTMLElement) {
    focusOwner.current = owner; focusDialog.current = owner.closest('[role="dialog"][data-mui-focusable]');
    retained.current = undefined; restore.current = true; parkRecoveryFocus(owner);
  }
  const invalidate = useCallback(() => {
    if (pending.current) { refreshQueued.current = true; return; }
    setAttempt(value => value + 1);
  }, []);
  if (admission.unavailable !== props.unavailable) {
    setAdmission({ unavailable: props.unavailable, denied: false }); setView(undefined); setNotice(undefined);
  }
  useEffect(() => {
    if (!open || props.unavailable || admission.denied) { setView(undefined); return; }
    const controller = new AbortController(); pending.current = controller; refreshQueued.current = false;
    const focused = document.activeElement;
    if (!restore.current && [entry.current, next.current, previous.current, retry.current].some(button => button === focused)
      && focused instanceof HTMLButtonElement) { ownFocus(focused); retained.current = focused; }
    setBusy(true); setView(undefined); setNotice(undefined);
    const started = performance.now(); activityEvent('board_star_read', 'use');
    const position = positions.at(-1)!;
    void boundedWorkRead(async signal => {
      const before = await workRequest<unknown>('/me', { signal });
      if (!isNotificationProfile(before) || before.id !== props.userId) throw new WorkRequestError(401, null);
      const page = parseStarHistory(await workRequest<unknown>('/boards/' + encodeURIComponent(props.boardId) + '/star/events?after=' + position.after, { signal }),
        props.organizationId, props.boardId, props.userId, position.after, position.entityId);
      const after = await workRequest<unknown>('/me', { signal });
      if (!isNotificationProfile(after) || after.id !== props.userId) throw new WorkRequestError(401, null);
      return { page, locale: after.locale, timezone: after.timezone };
    }, controller.signal).then(result => {
      if (!controller.signal.aborted && pending.current === controller) { activityResult('board_star_read', true, started); setView(result); }
    }).catch((error: unknown) => {
      if (controller.signal.aborted || pending.current !== controller) return;
      activityResult('board_star_read', false, started);
      if (!(error instanceof WorkRequestError)) activityEvent('board_star_read', 'exception');
      if (error instanceof WorkRequestError && [401,403,404].includes(error.status)) {
        pending.current = undefined; refreshQueued.current = false; setBusy(false);
        setAdmission(value => ({ ...value, denied: true }));
        setNotice('Your star history is unavailable. Refresh the Board to check access.'); denied.current();
      }
      else setNotice('Your star history could not be loaded. Retry this page.');
    }).finally(() => {
      if (pending.current !== controller) return;
      pending.current = undefined; setBusy(false);
      if (refreshQueued.current) { refreshQueued.current = false; setAttempt(value => value + 1); }
    });
    return () => { controller.abort(); if (pending.current === controller) pending.current = undefined; };
  }, [open, props.unavailable, admission.denied, props.organizationId, props.boardId, props.userId, positions, attempt]);
  useEffect(() => {
    if (!open || props.unavailable || admission.denied) return;
    const check = () => { if (document.visibilityState !== 'hidden') invalidate(); };
    const stop = watchIdentity({ subject: props.userId, isProfile: isNotificationProfile, invalidate: check });
    const timer = setInterval(check, 10_000);
    window.addEventListener('focus', check); window.addEventListener('online', check); document.addEventListener('visibilitychange', check);
    return () => {
      stop(); clearInterval(timer); window.removeEventListener('focus', check); window.removeEventListener('online', check);
      document.removeEventListener('visibilitychange', check);
    };
  }, [open, props.unavailable, admission.denied, props.userId, invalidate]);
  useLayoutEffect(() => {
    if (!restore.current || busy || props.unavailable || !(ownsRecoveryFocus(document.activeElement, focusOwner.current) || document.activeElement === focusDialog.current)) return;
    if (open && !notice && !view) return;
    const target = admission.denied ? close.current : !open ? entry.current : retained.current?.isConnected && !retained.current.disabled
      ? retained.current : notice ? retry.current : view?.page.nextAfter ? next.current : close.current;
    if (target && !target.disabled) { target.focus({ preventScroll: true }); restore.current = false; }
  }, [open, busy, props.unavailable, admission.denied, notice, view]);
  const visible = props.unavailable || admission.denied ? undefined : view;
  return <Stack spacing={1} onBlur={event => {
    if (!ownsRecoveryFocus(event.relatedTarget, focusOwner.current) && event.relatedTarget !== focusDialog.current) restore.current = false;
  }}>
    <Button ref={entry} disabled={busy || props.unavailable || admission.denied} onClick={event => { activityEvent('board_star_disclosure', 'open'); ownFocus(event.currentTarget); setOpen(true); setPositions([{ after: 0 }]); }}>Review your star history</Button>
    {open && <Box component="section" aria-label="Your star history" aria-busy={busy}>
      <Typography role="status" aria-live="polite">{props.unavailable ? 'Checking history access…' : busy ? 'Loading your star history…' : visible ? `${visible.page.items.length} personal changes on this page.` : ''}</Typography>
      {!props.unavailable && notice && <Alert severity="info">{notice}</Alert>}
      {!props.unavailable && !admission.denied && notice && <Button ref={retry} disabled={busy} onClick={event => { activityEvent('board_star_read', 'retry'); ownFocus(event.currentTarget); setAttempt(value => value + 1); }}>Retry star history</Button>}
      {visible && <>
        {visible.page.items.length === 0 && <Typography>No recorded star changes on this page. Earlier changes may predate recorded history.</Typography>}
        <Box component="ol" sx={{ pl: 3, overflowWrap: 'anywhere' }}>{visible.page.items.map(item => <Box component="li" key={item.eventId}>
          <Typography>You changed your Board star (revision {item.version}).</Typography>
          <Typography component="time" dateTime={item.createdAt} variant="body2">{formatUserDateTime(item.createdAt, visible) ?? 'Date unavailable'}</Typography>
        </Box>)}</Box>
      </>}
      <Button ref={next} disabled={busy || props.unavailable || !visible?.page.nextAfter} onClick={event => {
        if (!visible?.page.nextAfter) return; ownFocus(event.currentTarget);
        setPositions(previous => [...previous, { after: visible.page.nextAfter!, entityId: visible.page.items.at(-1)!.entityId }]);
      }}>Next star history page</Button>
      <Button ref={previous} disabled={busy || props.unavailable || admission.denied || positions.length < 2} onClick={event => { ownFocus(event.currentTarget); setPositions(previous => previous.slice(0,-1)); }}>Previous star history page</Button>
      <Button ref={close} onClick={event => { ownFocus(event.currentTarget); pending.current?.abort(); setOpen(false); setView(undefined); setNotice(undefined); setBusy(false); }}>Close star history</Button>
    </Box>}
  </Stack>;
}
