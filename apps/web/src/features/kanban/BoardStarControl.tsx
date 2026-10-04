import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';

type Props = { organizationId: string; boardId: string; admitted: boolean; disabled: boolean };
type Preference = { organizationId: string; boardId: string; userId: string; starred: boolean };
type Intent = { userId: string; starred: boolean; key: string };
class ChangedStarAccount extends Error {}
function preference(value: unknown, scope: Props, actor: string): Preference {
  const p = value as Preference | null;
  if (!p || Object.keys(p).sort().join(',') !== 'boardId,organizationId,starred,userId'
    || p.organizationId !== scope.organizationId || p.boardId !== scope.boardId || p.userId !== actor
    || typeof p.starred !== 'boolean') throw new Error('Invalid personal preference');
  return p;
}
export function BoardStarControl(props: Props) {
  return <StarDialog key={props.organizationId + '/' + props.boardId} {...props} />;
}
function StarDialog(props: Props) {
  const { admitted, organizationId, boardId, disabled } = props;
  const [open, setOpen] = useState(false); const [current, setCurrent] = useState<Preference>();
  const [busy, setBusy] = useState(false); const [recovery, setRecovery] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const intent = useRef<Intent | undefined>(undefined); const actor = useRef<string | undefined>(undefined);
  const retry = useRef<HTMLButtonElement>(null); const check = useRef<HTMLButtonElement>(null);
  const entry = useRef<HTMLButtonElement>(null); const done = useRef<HTMLButtonElement>(null);
  const restore = useRef(false);
  const path = '/boards/' + encodeURIComponent(boardId) + '/star';
  const retire = useCallback((message: string) => {
    intent.current = undefined; actor.current = undefined; setRecovery(false); setCurrent(undefined); setNotice(message);
  }, []);
  const load = useCallback(async () => {
    if (!mounted.current || pending.current || !admitted) return;
    const c = new AbortController(); pending.current = c; setBusy(true); setCurrent(undefined);
    try {
      const result = await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before)) throw new ChangedStarAccount();
        if (actor.current && actor.current !== before.id) throw new ChangedStarAccount();
        const value = preference(await workRequest<unknown>(path, { signal }), { organizationId, boardId, admitted, disabled: false }, before.id);
        const after = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(after) || after.id !== before.id) throw new ChangedStarAccount();
        return value;
      }, c.signal);
      if (!mounted.current || pending.current !== c || c.signal.aborted) return;
      actor.current = result.userId; setCurrent(result);
      setNotice(intent.current ? 'This star change is unconfirmed. Retry the same change.' : undefined);
    } catch (error) {
      if (!mounted.current || pending.current !== c || c.signal.aborted) return;
      if (error instanceof ChangedStarAccount) retire('Your account changed. Close and reopen Board starring.');
      else if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) retire('Board starring is unavailable. Check access or sign in.');
      else setNotice('Unable to check your current star. Try again.');
    } finally { if (pending.current === c) { pending.current = undefined; if (mounted.current) setBusy(false); } }
  }, [admitted, organizationId, boardId, path, retire]);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); intent.current = undefined; }; }, []);
  useEffect(() => {
    if (admitted) return;
    pending.current?.abort(); pending.current = undefined; setBusy(false); retire('Board starring is unavailable.');
  }, [admitted, retire]);
  useEffect(() => {
    if (!open || !admitted) return;
    void load();
    const refresh = () => { if (document.visibilityState !== 'hidden') void load(); };
    const timer = setInterval(refresh, 10_000);
    window.addEventListener('focus', refresh); window.addEventListener('online', refresh); document.addEventListener('visibilitychange', refresh);
    return () => { clearInterval(timer); window.removeEventListener('focus', refresh); window.removeEventListener('online', refresh); document.removeEventListener('visibilitychange', refresh); };
  }, [open, admitted, load]);
  useEffect(() => {
    if (busy || !restore.current) return;
    const target = recovery ? retry.current : check.current;
    if (!target) return;
    restore.current = false; (target.disabled ? done.current : target)?.focus({ preventScroll: true });
  }, [busy, recovery, current]);
  async function change() {
    if (pending.current || !admitted || disabled || !current || current.userId !== actor.current) return;
    const command = intent.current ?? { userId: current.userId, starred: !current.starred, key: crypto.randomUUID() };
    intent.current = command; const c = new AbortController(); pending.current = c;
    setBusy(true); setCurrent(undefined); setNotice(undefined); restore.current = true;
    let refresh = false;
    try {
      await boundedWorkRead(async signal => {
        const before = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(before) || before.id !== command.userId) throw new ChangedStarAccount();
        const acknowledgment = await workRequest<unknown>(path, { method: command.starred ? 'PUT' : 'DELETE', signal,
          headers: { 'Idempotency-Key': command.key } });
        if (acknowledgment !== undefined) throw new Error('Unconfirmed personal change');
        const after = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(after) || after.id !== command.userId) throw new ChangedStarAccount();
      }, c.signal);
      if (!mounted.current || pending.current !== c || c.signal.aborted) return;
      intent.current = undefined; setRecovery(false); refresh = true;
    } catch (error) {
      if (!mounted.current || pending.current !== c || c.signal.aborted) return;
      if (error instanceof ChangedStarAccount) retire('Your account changed. Close and reopen Board starring.');
      else if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) retire('Board starring is unavailable. Check access or sign in.');
      else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) {
        intent.current = undefined; setRecovery(false); setNotice('This star change could not be applied. Check your current star.'); refresh = true;
      } else { setRecovery(true); setNotice('This star change is unconfirmed. Retry the same change.'); refresh = true; }
    } finally {
      if (pending.current === c) { pending.current = undefined; if (mounted.current) { setBusy(false); if (refresh) void load(); } }
    }
  }
  function close() { if (!busy && !intent.current) { setOpen(false); setCurrent(undefined); actor.current = undefined; setNotice(undefined); } }
  return <>
    {admitted && <Button ref={entry} disabled={disabled || busy} onClick={() => setOpen(true)}>Board starring</Button>}
    <Dialog open={open} onClose={close} disableRestoreFocus fullWidth maxWidth="sm"
      slotProps={{ transition: { onExited: () => entry.current?.focus({ preventScroll: true }) } }}>
      <DialogTitle>Board starring</DialogTitle><DialogContent>
        <Typography>Stars are personal to your account.</Typography>
        {current && <Typography>{current.starred ? 'You have starred this Board.' : 'You have not starred this Board.'}</Typography>}
        {notice && <Alert severity="info" role="status">{notice}</Alert>}
        <Button ref={check} disabled={busy || !admitted} onClick={() => { restore.current = true; void load(); }}>Check current star</Button>
      </DialogContent><DialogActions>
        {!recovery && <Button ref={done} disabled={busy} onClick={close}>Done</Button>}
        {recovery ? <Button ref={retry} disabled={busy || disabled || !admitted || !current} onClick={() => void change()}>Retry same star change</Button>
          : current && <Button disabled={busy || disabled || !admitted} onClick={() => void change()}>{current.starred ? 'Unstar Board' : 'Star Board'}</Button>}
      </DialogActions>
    </Dialog>
  </>;
}
