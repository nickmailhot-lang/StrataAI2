import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Checkbox, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { formatUserDateTime } from '../auth/userDateTime';
import { watchIdentity } from '../auth/identityLive';
import { isNotificationProfile, notificationLabels, notificationUuid, parseInbox, validateReadAcknowledgment,
  type InboxPage, type NotificationProfile } from './notificationInbox';

type ReadIntent = { recipientId: string; targets: { id: string; createdTicks: bigint }[]; key: string };
class ChangedNotificationIdentity extends Error {}

export function NotificationCenterPage() {
  const { organizationId } = useParams();
  return organizationId && notificationUuid(organizationId) ? <NotificationCenter key={organizationId.toLowerCase()} organizationId={organizationId.toLowerCase()} /> :
    <Alert severity="info">Open an Organization to view notifications.</Alert>;
}
function NotificationCenter({ organizationId }: { organizationId: string }) {
  const [profile, setProfile] = useState<NotificationProfile>(); const [page, setPage] = useState<InboxPage>();
  const [selected, setSelected] = useState<string[]>([]); const [busy, setBusy] = useState(false);
  const [recovery, setRecovery] = useState(false); const [notice, setNotice] = useState<string>(); const [denied, setDenied] = useState(false);
  const pending = useRef<AbortController | undefined>(undefined); const epoch = useRef(0); const mounted = useRef(false);
  const currentProfile = useRef<NotificationProfile | undefined>(undefined); const intent = useRef<ReadIntent | undefined>(undefined);
  const currentCursor = useRef<string | undefined>(undefined); const refresh = useRef<HTMLButtonElement>(null);
  const retry = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLDivElement>(null); const focusTarget = useRef<string | undefined>(undefined);
  const path = `/organizations/${encodeURIComponent(organizationId)}/notifications`;
  const rememberFocus = () => {
    const active = document.activeElement;
    if (active === refresh.current) focusTarget.current = 'refresh';
    else if (active === retry.current) focusTarget.current = 'retry';
    if (active instanceof HTMLElement && list.current?.contains(active)) focusTarget.current = active.closest<HTMLElement>('[data-notification-focus]')?.dataset.notificationFocus ?? 'refresh';
  };
  const retire = useCallback((message: string) => {
    intent.current = undefined; currentProfile.current = undefined; setProfile(undefined); setPage(undefined); setSelected([]);
    setRecovery(false); setDenied(true); setNotice(message);
  }, []);
  const load = useCallback(async (after?: string) => {
    if (!mounted.current || pending.current) return;
    const ticket = ++epoch.current; const controller = new AbortController(); pending.current = controller;
    const active = document.activeElement;
    if (active === refresh.current) focusTarget.current = 'refresh';
    else if (active === retry.current) focusTarget.current = 'retry';
    if (active instanceof HTMLElement && list.current?.contains(active)) focusTarget.current = active.closest<HTMLElement>('[data-notification-focus]')?.dataset.notificationFocus ?? 'refresh';
    currentCursor.current = after; setBusy(true); setPage(undefined);
    setNotice(intent.current ? 'Unable to confirm read status. Retry the same selection to confirm it.' : undefined);
    try {
      const result = await boundedWorkRead(async signal => {
        const user = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(user)) throw new Error('Invalid current profile');
        const data = await workRequest<unknown>(`${path}${after ? `?after=${encodeURIComponent(after)}` : ''}`, { signal });
        const page = parseInbox(data, organizationId, user.id, after);
        const current = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(current)) throw new Error('Invalid current profile');
        if (current.id.toLowerCase() !== user.id.toLowerCase()) throw new ChangedNotificationIdentity();
        return { user: current, page };
      }, controller.signal);
      if (!mounted.current || ticket !== epoch.current || controller.signal.aborted) return;
      if (currentProfile.current && currentProfile.current.id.toLowerCase() !== result.user.id.toLowerCase()) {
        intent.current = undefined; setRecovery(false); setSelected([]);
      }
      currentProfile.current = result.user; setProfile(result.user); setPage(result.page); setDenied(false);
      setSelected(previous => previous.filter(id => result.page.items.some(n => n.id === id && n.readAt === null)));
    } catch (reason) {
      if (!mounted.current || ticket !== epoch.current) return;
      if (reason instanceof ChangedNotificationIdentity) retire('Your account changed. Check notifications again.');
      else if (reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) retire('Notifications are unavailable. Check access again or sign in.');
      else { setPage(undefined); setNotice('Unable to load current notifications. Try again.'); }
    } finally {
      if (mounted.current && ticket === epoch.current) { pending.current = undefined; setBusy(false); }
    }
  }, [path, organizationId, retire]);
  useEffect(() => {
    mounted.current = true; void load();
    const check = () => { if (document.visibilityState !== 'hidden') void load(currentCursor.current); };
    const interval = setInterval(check, 10_000); window.addEventListener('focus', check); window.addEventListener('online', check); document.addEventListener('visibilitychange', check);
    return () => {
      mounted.current = false; ++epoch.current; pending.current?.abort(); pending.current = undefined; intent.current = undefined;
      clearInterval(interval); window.removeEventListener('focus', check); window.removeEventListener('online', check); document.removeEventListener('visibilitychange', check);
    };
  }, [load]);
  const subject = profile?.id;
  useEffect(() => {
    if (!subject) return;
    return watchIdentity({ subject, isProfile: isNotificationProfile, invalidate: () => { void load(currentCursor.current); } });
  }, [subject, load]);
  useEffect(() => {
    if (busy || !focusTarget.current) return;
    if (document.activeElement !== document.body && document.activeElement !== refresh.current
      && document.activeElement !== retry.current && !list.current?.contains(document.activeElement)) {
      focusTarget.current = undefined; return;
    }
    const target = focusTarget.current;
    if (target === 'retry' && retry.current && !retry.current.disabled) { retry.current.focus({ preventScroll: true }); return; }
    const element = Array.from(list.current?.querySelectorAll<HTMLElement>('[data-notification-focus]') ?? [])
      .find(node => node.dataset.notificationFocus === target && !((node instanceof HTMLButtonElement || node instanceof HTMLInputElement) && node.disabled));
    const control = element?.matches('input,button,a') ? element : element?.querySelector<HTMLElement>('input,button,a');
    const available = control && !((control instanceof HTMLButtonElement || control instanceof HTMLInputElement) && control.disabled);
    (available ? control : refresh.current)?.focus({ preventScroll: true });
  }, [busy, page]);
  async function markRead(ids?: string[]) {
    if (pending.current || denied || !currentProfile.current || (!intent.current && (!page || !ids?.length))) return;
    const command = intent.current ?? { recipientId: currentProfile.current.id,
      targets: page!.items.filter(n => ids!.includes(n.id) && n.readAt === null).map(n => ({ id: n.id, createdTicks: n.createdTicks })).sort((a, b) => a.id.localeCompare(b.id)),
      key: crypto.randomUUID() };
    if (!command.targets.length) return;
    intent.current = command; rememberFocus(); focusTarget.current = 'refresh';
    const ticket = ++epoch.current; const controller = new AbortController(); pending.current = controller;
    setBusy(true); setPage(undefined); setNotice(undefined);
    let reload = false;
    try {
      await boundedWorkRead(async signal => {
        const user = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(user)) throw new Error('Invalid current profile');
        if (user.id.toLowerCase() !== command.recipientId.toLowerCase()) throw new ChangedNotificationIdentity();
        const value = await workRequest<unknown>(`${path}/read`, { method: 'POST', signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ ids: command.targets.map(n => n.id) }) });
        validateReadAcknowledgment(value, organizationId, command.targets);
      }, controller.signal);
      if (!mounted.current || ticket !== epoch.current || controller.signal.aborted) return;
      intent.current = undefined; setRecovery(false); setSelected([]); setNotice('Notifications marked read.'); reload = true;
    } catch (reason) {
      if (!mounted.current || ticket !== epoch.current) return;
      if (reason instanceof ChangedNotificationIdentity) retire('Your account changed. Check notifications again.');
      else if (reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) retire('Notifications are unavailable. Check access again or sign in.');
      else if (reason instanceof WorkRequestError && [400, 409].includes(reason.status)) {
        intent.current = undefined; setRecovery(false); setSelected([]); setNotice('The selection changed. Refresh notifications before trying again.');
      } else { focusTarget.current = 'retry'; setRecovery(true); setNotice('Unable to confirm read status. Retry the same selection to confirm it.'); }
    } finally {
      if (mounted.current && ticket === epoch.current) { pending.current = undefined; setBusy(false); if (reload) void load(currentCursor.current); }
    }
  }
  const unread = page?.items.filter(n => n.readAt === null) ?? [];
  return <Stack spacing={2} sx={{ maxWidth: 900, overflowWrap: 'anywhere' }}>
    <Typography component="h2" variant="h4">Notifications</Typography>
    <Typography>Updates automatically while this page is open. New notifications appear on the first page.</Typography>
    <Stack direction="row" useFlexGap sx={{ gap: 1, flexWrap: 'wrap' }}>
      <Button ref={refresh} disabled={busy} onFocus={() => { focusTarget.current = 'refresh'; }}
        onBlur={event => { if (event.relatedTarget !== null) focusTarget.current = undefined; }}
        onClick={() => { setSelected([]); void load(); }}>{denied ? 'Check notifications again' : 'Refresh notifications'}</Button>
      <Button component={Link} to={`/app/${organizationId}`}>Open boards</Button>
      {denied && <Button component={Link} to="/login">Sign in</Button>}
    </Stack>
    {busy && <Typography role="status">Checking current notifications…</Typography>}
    {notice && <Alert severity={recovery ? 'warning' : 'info'}>{notice}</Alert>}
    {recovery && <Button ref={retry} disabled={busy} onFocus={() => { focusTarget.current = 'retry'; }}
      onBlur={event => { if (event.relatedTarget !== null) focusTarget.current = undefined; }}
      onClick={() => { void markRead(); }}>Retry mark read</Button>}
    {page && profile && <Box ref={list} component="section" aria-label="Notification inbox" aria-busy={busy}>
      <Typography role="status">{unread.length} unread on this page.</Typography>
      {!page.items.length && <Typography>No notifications on this page. Card assignments from other people will appear here.</Typography>}
      {!!unread.length && <Stack direction="row" useFlexGap sx={{ gap: 1, flexWrap: 'wrap', mb: 2 }}>
        <Button disabled={busy || recovery} onClick={() => setSelected(unread.map(n => n.id))}>Select unread on this page</Button>
        <Button disabled={busy || recovery || !selected.length} onClick={() => { void markRead(selected); }}>Mark selected read</Button>
        <Button disabled={busy || recovery || !selected.length} onClick={() => setSelected([])}>Clear selection</Button>
      </Stack>}
      <Stack spacing={1}>
        {page.items.map(n => {
          const created = formatUserDateTime(n.createdAt, profile) ?? 'Date unavailable';
          return <Paper key={n.id} component="article" variant="outlined" aria-label={`${notificationLabels[n.type]}, ${created}, ${n.readAt ? 'Read' : 'Unread'}`} sx={{ p: 2 }}>
            <Stack spacing={1}>
              <Typography sx={{ fontWeight: 600 }}>{notificationLabels[n.type]} · {n.readAt ? 'Read' : 'Unread'}</Typography>
              <Typography component="time" dateTime={n.createdAt}>{created}</Typography>
              {n.readAt && <Typography>Read {formatUserDateTime(n.readAt, profile) ?? 'at an unavailable time'}</Typography>}
              <Stack direction="row" useFlexGap sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
                {!n.readAt && <Checkbox checked={selected.includes(n.id)} disabled={busy || recovery} data-notification-focus={`select/${n.id}`}
                  slotProps={{ input: { 'aria-label': `Select unread ${notificationLabels[n.type].toLowerCase()} from ${created}` } }}
                  onChange={(_, checked) => setSelected(previous => checked ? [...previous.filter(id => id !== n.id), n.id] : previous.filter(id => id !== n.id))} />}
                <Button component={Link} to={n.entityLink} data-notification-focus={`link/${n.id}`}>Open Card</Button>
                {!n.readAt && <Button disabled={busy || recovery} data-notification-focus={`read/${n.id}`} onClick={() => { void markRead([n.id]); }}>Mark read</Button>}
              </Stack>
            </Stack>
          </Paper>;
        })}
      </Stack>
      <Stack direction="row" useFlexGap sx={{ gap: 1, flexWrap: 'wrap', mt: 2 }}>
        {page.nextCursor && <Button disabled={busy || recovery} onClick={() => { setSelected([]); void load(page.nextCursor!); }}>Next notifications</Button>}
        {currentCursor.current && <Button disabled={busy || recovery} onClick={() => { setSelected([]); void load(); }}>First notifications</Button>}
      </Stack>
    </Box>}
  </Stack>;
}
