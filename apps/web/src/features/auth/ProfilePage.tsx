import { publicCorrelationReference } from '../../api/correlationReference';
import { WorkRequestError } from '../../api/workManagement';
import { apiFetch } from '../../api/apiFetch';
import { forgetInvitationIntents } from '../organizations/invitationIntent';
import { forgetConfigurationChanges } from '../organizations/organizationConfigurationRecovery';
import { formatUserDateTime } from './userDateTime';
import { validateIdentitySync } from './identitySync';
import { watchIdentity } from './identityLive';
import { MentionHandleDialog } from './MentionHandleDialog';
import { useEffect, useEffectEvent, useLayoutEffect, useRef, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';

type UserProfile = {
  id: string;
  email: string;
  displayName: string;
  avatarUrl: string | null;
  locale: string;
  timezone: string;
  status: string;
  emailVerified: boolean;
  version: number;
  createdAt: string;
  updatedAt: string;
};

function isProfile(value: unknown): value is UserProfile {
  if (!value || typeof value !== 'object') return false;
  const user = value as Partial<UserProfile>;
  return typeof user.id === 'string' && user.id.length > 0 && typeof user.email === 'string'
    && typeof user.displayName === 'string' && (user.avatarUrl === null || typeof user.avatarUrl === 'string')
    && typeof user.locale === 'string' && typeof user.timezone === 'string' && typeof user.status === 'string'
    && typeof user.emailVerified === 'boolean' && Number.isSafeInteger(user.version) && (user.version ?? 0) > 0
    && typeof user.createdAt === 'string' && Number.isFinite(Date.parse(user.createdAt))
    && typeof user.updatedAt === 'string' && Number.isFinite(Date.parse(user.updatedAt));
}

type ProfileFailure = { message: string; reference: string | null };
function ProfileError({ failure, severity = 'error', role }: { failure: ProfileFailure; severity?: 'error' | 'warning'; role?: 'status' }) {
  return <Alert severity={severity} role={role}><span>{failure.message}</span>{failure.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {failure.reference}</Typography>}</Alert>;
}

function DeactivationRecovery({ failure, busy, retry, signIn }: { failure?: ProfileFailure; busy: boolean; retry(): void; signIn(): void }) {
  const [ready, setReady] = useState(false); const retryButton = useRef<HTMLButtonElement>(null);
  // The confirmation Modal owns aria-hidden on its surrounding root. Its
  // passive unmount cleanup must finish before announcing the replacement
  // recovery screen and its accessible action together.
  useEffect(() => setReady(true), []);
  useLayoutEffect(() => { if (ready && !busy) retryButton.current?.focus(); }, [ready, busy]);
  return <Paper variant="outlined" sx={{ p: 3, maxWidth: 720 }}><Stack spacing={2} aria-busy={busy}>
    {!ready ? <CircularProgress aria-label="Preparing deactivation recovery" /> : <>
      <Typography variant="h4" component="h2">Account deactivation</Typography>
      {failure && <ProfileError failure={failure} />}
      <Typography>This account's deactivation still needs confirmation.</Typography>
      {busy && <CircularProgress aria-label="Confirming account deactivation" />}
      <Button type="button" variant="contained" ref={retryButton} disabled={busy} onClick={retry}>Retry deactivation</Button>
      <Button type="button" disabled={busy} onClick={signIn}>Go to sign in</Button>
    </>}
  </Stack></Paper>;
}

async function profileCommand(path: string, options: RequestInit, controller: AbortController, readBody: boolean) {
  let deadline: ReturnType<typeof setTimeout> | undefined;
  let abort: (() => void) | undefined;
  let reference: string | null = null; let status = 0;
  try {
    return await Promise.race([
      apiFetch(path, { ...options, signal: controller.signal }).then(async response => {
        status = response.status;
        reference = publicCorrelationReference(response.headers?.get('X-Correlation-ID') ?? null);
        return { status, ok: response.ok, reference,
          body: readBody && response.status !== 401 ? await response.json().catch(() => undefined) as unknown : undefined };
      }),
      new Promise<never>((_, reject) => {
        abort = () => reject(new Error('Profile command interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true });
        deadline = setTimeout(() => controller.abort(), 15_000);
      }),
    ]);
  } catch {
    throw new WorkRequestError(status, reference);
  } finally {
    clearTimeout(deadline);
    if (abort) controller.signal.removeEventListener('abort', abort);
  }
}

export function ProfilePage() {
  const [profile, setProfile] = useState<UserProfile>();
  const [error, setFailure] = useState<ProfileFailure>();
  function setError(message: string | undefined, reference: string | null = null) {
    setFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  const [draft, setDraft] = useState<UserProfile>();
  const [busy, setBusy] = useState(false);
  const [handleDialog, setHandleDialog] = useState(false);
  const [saved, setSaved] = useState(false);
  const [reload, setReload] = useState(0);
  const [conflict, setConflict] = useState(false);
  const [refreshError, setRefreshFailure] = useState<ProfileFailure>();
  function setRefreshError(message: string | undefined, reference: string | null = null) {
    setRefreshFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  const [deactivateDialog, setDeactivateDialog] = useState(false);
  const [deactivateUncertain, setDeactivateUncertain] = useState(false);
  const [deactivateError, setDeactivateFailure] = useState<ProfileFailure>();
  function setDeactivateError(message: string | undefined, reference: string | null = null) {
    setDeactivateFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  const deactivateRetry = useRef<{ key: string; userId: string } | undefined>(undefined);
  const deactivateCancel = useRef<HTMLButtonElement | null>(null);
  const mutationEpoch = useRef(0);
  const mutation = useRef<AbortController | undefined>(undefined);
  const profileRetry = useRef<{ body: string; key: string } | undefined>(undefined);
  const logoutRetry = useRef<string | undefined>(undefined);
  const mounted = useRef(true);
  const navigate = useNavigate();
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      mutationEpoch.current++;
      mutation.current?.abort();
      mutation.current = undefined;
      deactivateRetry.current = undefined;
    };
  }, []);
  const canRead = useEffectEvent(() => !busy && !handleDialog && !mutation.current && !deactivateUncertain);
  const deny = useEffectEvent(() => {
    setProfile(undefined); setDraft(undefined);
    navigate('/login', { replace: true });
  });
  const accept = useEffectEvent((user: UserProfile) => {
    setRefreshError(undefined);
    if (profile && user.id !== profile.id) { deny(); return; }
    if (profile && user.version <= profile.version) return;
    if (!profile) setError(undefined);
    const edited = profile && draft && (draft.displayName !== profile.displayName || draft.avatarUrl !== profile.avatarUrl || draft.locale !== profile.locale || draft.timezone !== profile.timezone);
    setProfile(user);
    setSaved(false);
    if (profile && (edited || conflict)) {
      setConflict(true);
      setError('Your profile changed elsewhere. Your edits are preserved; load the latest profile before saving.');
    } else {
      setDraft(user);
      setConflict(false);
    }
  });
  const readFailed = useEffectEvent((reference: string | null = null) => {
    if (profile) setRefreshError('Unable to refresh your profile. Your changes are preserved; refresh will retry.', reference);
    else setError('Unable to load your profile.', reference);
  });

  useEffect(() => {
    let active = true;
    let inFlight = false;
    let controller: AbortController | undefined;
    let deadline: ReturnType<typeof setTimeout> | undefined;
    let cursor: number | undefined;
    let subject: string | undefined;
    const seenEvents = new Set<string>();
    let stopLive: (() => void) | undefined;
    async function refresh() {
      if (!active || inFlight || !canRead() || document.visibilityState === 'hidden') return;
      inFlight = true;
      const epoch = mutationEpoch.current;
      let more = false;
      let reference: string | null = null;
      const requestController = new AbortController();
      controller = requestController;
      try {
        // Bound the complete read, even if a transport ignores abort or its body stalls.
        const response = await Promise.race([
          apiFetch(cursor === undefined ? '/me/sync' : `/me/sync?after=${cursor}`, { signal: requestController.signal }).then(async result => {
            reference = publicCorrelationReference(result.headers?.get('X-Correlation-ID') ?? null);
            return { status: result.status, ok: result.ok, user: result.ok ? await result.json() : undefined };
          }),
          new Promise<never>((_, reject) => {
            deadline = setTimeout(() => { requestController.abort(); reject(new Error('Profile read timed out')); }, 15_000);
          }),
        ]);
        if (!active || epoch !== mutationEpoch.current) return;
        if (response.status === 401) { deny(); return; }
        if (!response.ok) throw new Error('Invalid profile response');
        const snapshot = validateIdentitySync(response.user, cursor, isProfile, seenEvents);
        if (!snapshot) throw new Error('Invalid account event response');
        if (subject && snapshot.profile.id !== subject) { deny(); return; }
        subject = snapshot.profile.id;
        stopLive ??= watchIdentity({ subject, isProfile, invalidate: () => void refresh() });
        accept(snapshot.profile);
        cursor = snapshot.cursor;
        more = snapshot.hasMore;
        for (const id of snapshot.eventIds) seenEvents.add(id);
        while (seenEvents.size > 1000) seenEvents.delete(seenEvents.values().next().value!);
      } catch {
        if (active && epoch === mutationEpoch.current) readFailed(reference);
      } finally {
        clearTimeout(deadline);
        controller = undefined;
        inFlight = false;
        if (more && active && epoch === mutationEpoch.current) queueMicrotask(() => void refresh());
      }
    }
    void refresh();
    const interval = setInterval(() => void refresh(), 10_000);
    const recover = () => void refresh();
    window.addEventListener('focus', recover);
    window.addEventListener('online', recover);
    document.addEventListener('visibilitychange', recover);
    return () => {
      active = false;
      stopLive?.();
      controller?.abort();
      clearTimeout(deadline); clearInterval(interval);
      window.removeEventListener('focus', recover);
      window.removeEventListener('online', recover);
      document.removeEventListener('visibilitychange', recover);
    };
  }, [navigate, reload]);

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || busy || handleDialog || conflict || deactivateUncertain || mutation.current) return;
    const submitted = draft;
    const epoch = ++mutationEpoch.current;
    const controller = new AbortController();
    mutation.current = controller;
    const current = () => mounted.current && epoch === mutationEpoch.current;
    setBusy(true);
    setError(undefined);
    setSaved(false);
    try {
      const body = JSON.stringify({ displayName: submitted.displayName, avatarUrl: submitted.avatarUrl ?? '', locale: submitted.locale, timezone: submitted.timezone, version: submitted.version });
      if (profileRetry.current?.body !== body) profileRetry.current = { body, key: crypto.randomUUID() };
      const response = await profileCommand('/me', {
        method: 'PATCH', credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': profileRetry.current.key, 'X-StrataAI-Expected-User': submitted.id },
        body,
      }, controller, true);
      if (!current()) return;
      if (response.status === 401) {
        setProfile(undefined); setDraft(undefined);
        navigate('/login', { replace: true });
        return;
      }
      if (!response.ok) {
        setConflict(response.status === 409);
        const problem = response.body as { code?: unknown } | undefined;
        const messages: Record<string, string> = {
          invalid_display_name: 'A valid display name is required.',
          invalid_version: 'Load the latest profile before saving again.',
          invalid_avatar_url: 'Avatar must be an HTTPS image URL without credentials.',
          invalid_locale: 'A valid regional locale is required.',
          invalid_timezone: 'A valid timezone is required.',
        };
        setError(response.status === 409 ? 'Your profile changed elsewhere.'
          : typeof problem?.code === 'string' && messages[problem.code] ? messages[problem.code] : 'Unable to save your profile. Please retry.', response.reference);
        return;
      }
      const user = response.body;
      if (!isProfile(user) || user.id !== submitted.id || user.version <= submitted.version) throw new WorkRequestError(response.status, response.reference);
      setProfile(user);
      setDraft(user);
      profileRetry.current = undefined;
      setSaved(true);
    } catch (reason) {
      if (current()) setError('Unable to confirm your profile save. Your changes are preserved; refresh the latest profile or retry.', reason instanceof WorkRequestError ? reason.correlationId : null);
    } finally {
      if (mutation.current === controller) mutation.current = undefined;
      if (current()) setBusy(false);
    }
  }

  async function logout() {
    if (!profile || busy || handleDialog || deactivateUncertain || mutation.current) return;
    const epoch = ++mutationEpoch.current;
    const controller = new AbortController();
    mutation.current = controller;
    const current = () => mounted.current && epoch === mutationEpoch.current;
    setBusy(true);
    setError(undefined);
    try {
      logoutRetry.current ??= crypto.randomUUID();
      const response = await profileCommand('/auth/logout', {
        method: 'POST', credentials: 'include', headers: { 'Idempotency-Key': logoutRetry.current, 'X-StrataAI-Expected-User': profile.id },
      }, controller, false);
      if (!current()) return;
      if (response.status !== 204 && response.status !== 401) throw new WorkRequestError(response.status, response.reference);
      forgetInvitationIntents();
      forgetConfigurationChanges();
      setProfile(undefined); setDraft(undefined);
      navigate('/login', { replace: true });
    } catch (reason) {
      if (current()) setError('Unable to sign out. Please retry.', reason instanceof WorkRequestError ? reason.correlationId : null);
    } finally {
      if (mutation.current === controller) mutation.current = undefined;
      if (current()) setBusy(false);
    }
  }

  async function deactivate() {
    if (busy || handleDialog || mutation.current || (!deactivateDialog && !deactivateUncertain) || (!deactivateRetry.current && !profile)) return;
    const epoch = ++mutationEpoch.current;
    const controller = new AbortController();
    mutation.current = controller;
    const current = () => mounted.current && epoch === mutationEpoch.current;
    setBusy(true); setDeactivateError(undefined); setSaved(false);
    try {
      deactivateRetry.current ??= { key: crypto.randomUUID(), userId: profile!.id };
      const response = await profileCommand('/me/deactivate', {
        method: 'POST', credentials: 'include', headers: { 'Idempotency-Key': deactivateRetry.current.key, 'X-StrataAI-Expected-User': deactivateRetry.current.userId },
      }, controller, true);
      if (!current()) return;
      if (response.status === 204) {
        forgetInvitationIntents();
        forgetConfigurationChanges();
        deactivateRetry.current = undefined;
        setProfile(undefined); setDraft(undefined); setDeactivateUncertain(false); setDeactivateDialog(false);
        navigate('/login', { replace: true, state: { accountDeactivated: true } }); return;
      }
      if (response.status === 401) {
        deactivateRetry.current = undefined;
        setProfile(undefined); setDraft(undefined); setDeactivateUncertain(false); setDeactivateDialog(false);
        navigate('/login', { replace: true }); return;
      }
      const code = (response.body as { code?: unknown } | undefined)?.code;
      if (response.status === 409 && (code === 'organization_owner_required' || code === 'ownership_changed')) {
        setDeactivateUncertain(false); setDeactivateDialog(false);
        setDeactivateError(code === 'organization_owner_required'
          ? 'Another active owner must take responsibility for every organization you own before you deactivate your account.'
          : 'Your organization ownership changed. Review current access before retrying deactivation.', response.reference);
        return;
      }
      throw new WorkRequestError(response.status, response.reference);
    } catch (reason) {
      if (current()) {
        setDeactivateUncertain(true); setDeactivateDialog(false);
        setDeactivateError('Unable to confirm account deactivation. Retry the original attempt to check whether it completed.', reason instanceof WorkRequestError ? reason.correlationId : null);
      }
    } finally {
      if (mutation.current === controller) mutation.current = undefined;
      if (current()) setBusy(false);
    }
  }

  if (deactivateUncertain) {
    return <DeactivationRecovery failure={deactivateError} busy={busy} retry={() => void deactivate()} signIn={() => navigate('/login', { replace: true })} />;
  }

  if (error && !profile) {
    return <Stack spacing={2}><ProfileError failure={error} /><Button onClick={() => { setError(undefined); setReload(value => value + 1); }}>Retry</Button></Stack>;
  }

  if (!profile || !draft) {
    return (
      <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 240 }}>
        <CircularProgress aria-label="Loading profile" />
      </Box>
    );
  }

  return (
    <Paper variant="outlined" sx={{ p: 3, maxWidth: 720 }}>
      <Stack component="form" onSubmit={save} spacing={2} aria-label="Edit profile" aria-busy={busy}>
        <Typography variant="h4" component="h2">
          {profile.displayName}
        </Typography>
        <Typography>{profile.email}</Typography>
        <Typography>Account created: <time dateTime={profile.createdAt}>{formatUserDateTime(profile.createdAt, profile) ?? 'Date display unavailable for this timezone.'}</time></Typography>
        <Typography>Last updated: <time dateTime={profile.updatedAt}>{formatUserDateTime(profile.updatedAt, profile) ?? 'Date display unavailable for this timezone.'}</time></Typography>
        <Typography color="text.secondary">
          {profile.locale} · {profile.timezone}
        </Typography>
        <Typography color="text.secondary">
          Account: {profile.status}
          {profile.emailVerified ? ' · email verified' : ''}
        </Typography>
        {error ? <ProfileError failure={error} /> : null}
        {deactivateError ? <ProfileError failure={deactivateError} /> : null}
        {refreshError ? <ProfileError failure={refreshError} severity="warning" role="status" /> : null}
        {conflict ? <Button type="button" disabled={busy} onClick={() => { setError(undefined); setSaved(false); setProfile(undefined); setDraft(undefined); setReload(value => value + 1); }}>Discard edits and load latest profile</Button> : null}
        {saved ? <Alert severity="success" role="status">Profile saved.</Alert> : null}
        <TextField label="Display name" required value={draft.displayName} disabled={busy} onChange={event => { setDraft({ ...draft, displayName: event.target.value }); setSaved(false); }} slotProps={{ htmlInput: { maxLength: 120 } }} autoComplete="nickname" />
        <TextField label="Avatar URL" type="url" value={draft.avatarUrl ?? ''} disabled={busy} onChange={event => { setDraft({ ...draft, avatarUrl: event.target.value }); setSaved(false); }} helperText="Use an HTTPS image URL, or leave blank to remove it." />
        <TextField label="Locale" required value={draft.locale} disabled={busy} onChange={event => { setDraft({ ...draft, locale: event.target.value }); setSaved(false); }} helperText="For example, en-CA." />
        <TextField label="Timezone" required value={draft.timezone} disabled={busy} onChange={event => { setDraft({ ...draft, timezone: event.target.value }); setSaved(false); }} helperText="For example, America/Vancouver." />
        <Button type="submit" variant="contained" disabled={busy || handleDialog || conflict}>{busy ? 'Please wait…' : 'Save profile'}</Button>
        <Button type="button" disabled={busy || handleDialog || conflict} onClick={() => { setDraft(profile); setError(undefined); setSaved(false); }}>Discard changes</Button>
        <Button type="button" disabled={busy || handleDialog} onClick={logout} variant="outlined" sx={{ alignSelf: 'flex-start' }}>
          Sign out
        </Button>
        <Button type="button" disabled={busy || handleDialog} color="error" variant="outlined" sx={{ alignSelf: 'flex-start' }} onClick={() => setDeactivateDialog(true)}>
          Deactivate account
        </Button>
        <Button type="button" disabled={busy || conflict || !!profileRetry.current || !!logoutRetry.current} onClick={() => setHandleDialog(true)}>Change mention handle</Button>
      </Stack>
      <MentionHandleDialog key={profile.id} open={handleDialog} subject={profile.id}
        onClose={() => { setHandleDialog(false); setReload(value => value + 1); }}
        onDenied={() => { setHandleDialog(false); setProfile(undefined); setDraft(undefined); navigate('/login', { replace: true }); }} />
      <Dialog open={deactivateDialog} onClose={() => { if (!busy) setDeactivateDialog(false); }} aria-labelledby="confirm-account-deactivation"
        slotProps={{ transition: { onEntered: () => deactivateCancel.current?.focus() } }}>
        <DialogTitle id="confirm-account-deactivation">Deactivate your account?</DialogTitle>
        <DialogContent><Typography>You will be signed out and future sign-in will be blocked. Your historical activity will be preserved. Every organization you own must have another active owner.</Typography></DialogContent>
        <DialogActions>
          <Button type="button" ref={deactivateCancel} autoFocus disabled={busy} onClick={() => setDeactivateDialog(false)}>Keep account active</Button>
          <Button type="button" color="error" variant="contained" disabled={busy} onClick={() => void deactivate()}>Confirm deactivation</Button>
        </DialogActions>
      </Dialog>
    </Paper>
  );
}
