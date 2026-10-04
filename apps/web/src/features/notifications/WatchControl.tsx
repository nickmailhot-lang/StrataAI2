import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile, notificationInstant, notificationUuid } from './notificationInbox';
import { activityEvent, activityResult } from '../kanban/activityTelemetry';

type Props = { organizationId: string; boardId: string; entityType: 'CARD' | 'LIST' | 'BOARD'; entityId: string; admitted: boolean; disabled: boolean; refreshing?: boolean; onReturnFocus?: () => void };
type State = { organizationId: string; boardId: string; userId: string; entityType: string; entityId: string;
  watching: boolean; version: number; subscriptionId: string | null; createdAt: string | null; updatedAt: string | null; changed: boolean; canChange: boolean };
type Intent = { userId: string; watching: boolean; version: number; subscriptionId: string | null; createdAt: string | null; key: string };
class ChangedWatchIdentity extends Error {}

function state(value: unknown, scope: Props, user: string): State {
  const s = value as State | null;
  if (!s || s.organizationId !== scope.organizationId || s.boardId !== scope.boardId || s.entityType !== scope.entityType ||
    s.entityId !== scope.entityId || !notificationUuid(s.userId) || s.userId !== user || typeof s.watching !== 'boolean' ||
    !Number.isSafeInteger(s.version) || s.version < 0 || typeof s.changed !== 'boolean' || typeof s.canChange !== 'boolean' ||
    scope.entityType === 'BOARD' && scope.entityId !== scope.boardId) throw new Error('Invalid watch scope');
  if (s.version === 0) {
    if (s.watching || s.subscriptionId !== null || s.createdAt !== null || s.updatedAt !== null || s.changed) throw new Error('Invalid empty watch');
  } else if (!notificationUuid(s.subscriptionId) || typeof s.createdAt !== 'string' || typeof s.updatedAt !== 'string' ||
    notificationInstant(s.updatedAt).ticks < notificationInstant(s.createdAt).ticks) throw new Error('Invalid watch revision');
  return s;
}

export function WatchControl(props: Props) {
  return <WatchDialog key={`${props.organizationId}/${props.boardId}/${props.entityType}/${props.entityId}`} {...props} />;
}
function WatchDialog(props: Props) {
  const kind = props.entityType === 'CARD' ? 'Card' : props.entityType === 'LIST' ? 'List' : 'Board'; const title = useId();
  const [open, setOpen] = useState(false); const [current, setCurrent] = useState<State>(); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const [recovery, setRecovery] = useState(false); const [denied, setDenied] = useState(false);
  const mounted = useRef(false); const epoch = useRef(0); const pending = useRef<AbortController | undefined>(undefined);
  const intent = useRef<Intent | undefined>(undefined); const user = useRef<string | undefined>(undefined);
  const button = useRef<HTMLButtonElement>(null);
  const content = useRef<HTMLDivElement>(null); const checkButton = useRef<HTMLButtonElement>(null);
  const retryButton = useRef<HTMLButtonElement>(null); const doneButton = useRef<HTMLButtonElement>(null); const returnFocus = useRef(false);
  const { organizationId, boardId, entityType, entityId, admitted, disabled } = props;
  const commandBlocked = disabled || !!props.refreshing;
  const path = `/watch/${entityType}/${encodeURIComponent(entityId)}`;
  const retire = useCallback((message: string) => {
    intent.current = undefined; user.current = undefined; setCurrent(undefined); setRecovery(false); setDenied(true); setNotice(message);
  }, []);
  const load = useCallback(async (kind: 'use' | 'retry' | 'reconnect' = 'use') => {
    if (!mounted.current || pending.current || !admitted) return;
    const ticket = ++epoch.current; const controller = new AbortController(); pending.current = controller;
    const started = performance.now(); activityEvent('watch_read', kind);
    if (document.activeElement instanceof HTMLElement && content.current?.contains(document.activeElement)) returnFocus.current = true;
    setBusy(true); setCurrent(undefined); setNotice(intent.current ? 'The watch change is unconfirmed. Retry the same change.' : undefined);
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile)) throw new Error('Invalid current account');
        const value = state(await workRequest<unknown>(path, { signal }), { organizationId, boardId, entityType, entityId, admitted, disabled: false }, profile.id);
        if (value.changed) throw new Error('Invalid watch read');
        const current = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(current)) throw new Error('Invalid current account');
        if (current.id !== profile.id) throw new ChangedWatchIdentity();
        return value;
      }, controller.signal);
      if (!mounted.current || epoch.current !== ticket || controller.signal.aborted) return;
      activityResult('watch_read', true, started);
      if (user.current && user.current !== result.userId) { intent.current = undefined; setRecovery(false); }
      if (!result.canChange) { intent.current = undefined; setRecovery(false); setNotice('Watching is read-only while this Organization is archived.'); }
      user.current = result.userId; setCurrent(result); setDenied(false);
    } catch (reason) {
      if (!mounted.current || epoch.current !== ticket) return;
      activityResult('watch_read', false, started);
      if (!(reason instanceof WorkRequestError) && !(reason instanceof ChangedWatchIdentity)) activityEvent('watch_read', 'exception');
      if (reason instanceof ChangedWatchIdentity) retire('Your account changed. Check watching again.');
      else if (reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) retire('Watching is unavailable. Check access or sign in.');
      else setNotice('Unable to check current watching. Try again.');
    } finally { if (mounted.current && epoch.current === ticket) { pending.current = undefined; setBusy(false); } }
  }, [admitted, organizationId, boardId, entityType, entityId, path, retire]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; ++epoch.current; pending.current?.abort(); intent.current = undefined; }; }, []);
  useEffect(() => {
    if (busy || pending.current || !open || !returnFocus.current) return;
    // Async result state may commit separately from busy=false. The original
    // intent is authoritative: defer until its recovery button is attached,
    // rather than consuming the focus request on the old Check button.
    const target = intent.current ? retryButton.current : checkButton.current;
    if (!target) return;
    returnFocus.current = false;
    (target && !target.disabled ? target : doneButton.current)?.focus({ preventScroll: true });
  }, [busy, current, open, recovery]);
  useEffect(() => {
    if (admitted) return;
    ++epoch.current; pending.current?.abort(); pending.current = undefined; setBusy(false); retire('Watching is unavailable for this entity.');
  }, [admitted, retire]);
  useEffect(() => {
    if (!open || !admitted) return;
    void load(); const check = () => { if (document.visibilityState !== 'hidden') void load(); };
    const reconnect = () => { if (document.visibilityState !== 'hidden') void load('reconnect'); };
    const timer = setInterval(check, 10_000); window.addEventListener('focus', check); window.addEventListener('online', reconnect); document.addEventListener('visibilitychange', check);
    return () => { clearInterval(timer); window.removeEventListener('focus', check); window.removeEventListener('online', reconnect); document.removeEventListener('visibilitychange', check); };
  }, [open, admitted, load]);
  async function submit() {
    if (pending.current || !admitted || commandBlocked || denied || (!intent.current && (!current || !current.canChange))) return;
    const started = performance.now(); activityEvent('watch_change', intent.current ? 'retry' : 'use');
    const command = intent.current ?? { userId: current!.userId, watching: !current!.watching, version: current!.version,
      subscriptionId: current!.subscriptionId, createdAt: current!.createdAt, key: crypto.randomUUID() };
    intent.current = command; returnFocus.current = true; const ticket = ++epoch.current; const controller = new AbortController(); pending.current = controller;
    setBusy(true); setCurrent(undefined); setNotice(undefined); let reload = false;
    try {
      await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile)) throw new Error('Invalid current account');
        if (profile.id !== command.userId) throw new ChangedWatchIdentity();
        const result = state(await workRequest<unknown>(`${path}?version=${command.version}`, { method: command.watching ? 'PUT' : 'DELETE', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: '{}' }), props, profile.id);
        if (!result.changed || !result.canChange || result.watching !== command.watching || result.version !== command.version + 1 ||
          command.subscriptionId !== null && (result.subscriptionId !== command.subscriptionId || result.createdAt !== command.createdAt)) throw new Error('Unconfirmed watch change');
      }, controller.signal);
      if (!mounted.current || epoch.current !== ticket || controller.signal.aborted) return;
      activityResult('watch_change', true, started);
      intent.current = undefined; setRecovery(false); reload = true;
    } catch (reason) {
      if (!mounted.current || epoch.current !== ticket) return;
      activityResult('watch_change', false, started);
      if (reason instanceof WorkRequestError && [400, 409].includes(reason.status)) activityEvent('watch_change', 'conflict');
      else if (!(reason instanceof WorkRequestError) && !(reason instanceof ChangedWatchIdentity)) activityEvent('watch_change', 'exception');
      if (reason instanceof ChangedWatchIdentity) retire('Your account changed. Check watching again.');
      else if (reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) retire('Watching is unavailable. Check access or sign in.');
      else if (reason instanceof WorkRequestError && [400, 409].includes(reason.status)) {
        intent.current = undefined; setRecovery(false); setNotice('Watching changed. Check the current state before trying again.');
      } else { setRecovery(true); setNotice('The watch change is unconfirmed. Retry the same change.'); }
    } finally { if (mounted.current && epoch.current === ticket) { pending.current = undefined; setBusy(false); if (reload) void load(); } }
  }
  const valid = [organizationId, boardId, entityId].every(notificationUuid);
  const close = () => {
    if (recovery || intent.current) return;
    // A read-only poll must not prevent dismissal or steal the Done key press.
    ++epoch.current; pending.current?.abort(); pending.current = undefined;
    setBusy(false); setOpen(false); setCurrent(undefined); setNotice(undefined);
  };
  return <>
    <Button ref={button} disabled={!valid || !admitted || disabled} onClick={() => { activityEvent('watch_disclosure', 'open'); setOpen(true); }}>{kind} watching</Button>
    <Dialog open={open} onClose={close} fullWidth maxWidth="xs" aria-labelledby={title} disableRestoreFocus
      slotProps={{ transition: { onExited: () => {
        if (button.current && !button.current.disabled) button.current.focus({ preventScroll: true }); else props.onReturnFocus?.();
      } } }}>
      <DialogTitle id={title}>{kind} watching</DialogTitle>
      <DialogContent ref={content}><Stack spacing={1}>
        <Typography>Manage your own watch subscription. Watching is separate from being assigned to a Card.</Typography>
        {busy && <Typography role="status">Checking watching…</Typography>}
        {props.refreshing && <Typography role="status">Checking current Board access…</Typography>}
        {notice && <Alert severity={recovery ? 'warning' : 'info'}>{notice}</Alert>}
        {current && <Typography role="status">{current.watching ? `You are watching this ${kind}.` : `You are not watching this ${kind}.`}</Typography>}
        <Button ref={checkButton} disabled={busy || !admitted} onClick={() => void load('retry')}>Check current watching</Button>
        {recovery ? <Button ref={retryButton} disabled={busy || commandBlocked || !admitted} onClick={() => void submit()}>Retry same watch change</Button> :
          current && <Button disabled={busy || commandBlocked || !admitted || !current.canChange} onClick={() => void submit()}>{current.watching ? `Unwatch ${kind}` : `Watch ${kind}`}</Button>}
        {denied && <Button component={Link} to="/login">Sign in</Button>}
      </Stack></DialogContent>
      <DialogActions><Button ref={doneButton} disabled={recovery || busy && !!intent.current} onClick={close}>Done watching</Button></DialogActions>
    </Dialog>
  </>;
}
