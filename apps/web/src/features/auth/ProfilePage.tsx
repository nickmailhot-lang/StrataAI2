import { apiFetch } from '../../api/apiFetch';
import { useEffect, useEffectEvent, useRef, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
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
};

function isProfile(value: unknown): value is UserProfile {
  if (!value || typeof value !== 'object') return false;
  const user = value as Partial<UserProfile>;
  return typeof user.id === 'string' && user.id.length > 0 && typeof user.email === 'string'
    && typeof user.displayName === 'string' && (user.avatarUrl === null || typeof user.avatarUrl === 'string')
    && typeof user.locale === 'string' && typeof user.timezone === 'string' && typeof user.status === 'string'
    && typeof user.emailVerified === 'boolean' && Number.isSafeInteger(user.version) && (user.version ?? 0) > 0;
}

export function ProfilePage() {
  const [profile, setProfile] = useState<UserProfile>();
  const [error, setError] = useState<string>();
  const [draft, setDraft] = useState<UserProfile>();
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [reload, setReload] = useState(0);
  const [conflict, setConflict] = useState(false);
  const [refreshError, setRefreshError] = useState<string>();
  const mutationEpoch = useRef(0);
  const navigate = useNavigate();
  const canRead = useEffectEvent(() => !busy);
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
  const readFailed = useEffectEvent(() => {
    if (profile) setRefreshError('Unable to refresh your profile. Your changes are preserved; refresh will retry.');
    else setError('Unable to load your profile.');
  });

  useEffect(() => {
    let active = true;
    let inFlight = false;
    let controller: AbortController | undefined;
    let deadline: ReturnType<typeof setTimeout> | undefined;
    async function refresh() {
      if (!active || inFlight || !canRead() || document.visibilityState === 'hidden') return;
      inFlight = true;
      const epoch = mutationEpoch.current;
      const requestController = new AbortController();
      controller = requestController;
      try {
        // Bound the complete read, even if a transport ignores abort or its body stalls.
        const response = await Promise.race([
          apiFetch('/me', { signal: requestController.signal }).then(async result => ({ status: result.status, ok: result.ok, user: result.ok ? await result.json() : undefined })),
          new Promise<never>((_, reject) => {
            deadline = setTimeout(() => { requestController.abort(); reject(new Error('Profile read timed out')); }, 15_000);
          }),
        ]);
        if (!active || epoch !== mutationEpoch.current) return;
        if (response.status === 401) { deny(); return; }
        if (!response.ok || !isProfile(response.user)) throw new Error('Invalid profile response');
        accept(response.user);
      } catch {
        if (active && epoch === mutationEpoch.current) readFailed();
      } finally {
        clearTimeout(deadline);
        controller = undefined;
        inFlight = false;
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
      controller?.abort();
      clearTimeout(deadline); clearInterval(interval);
      window.removeEventListener('focus', recover);
      window.removeEventListener('online', recover);
      document.removeEventListener('visibilitychange', recover);
    };
  }, [navigate, reload]);

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || busy || conflict) return;
    mutationEpoch.current++;
    setBusy(true);
    setError(undefined);
    setSaved(false);
    try {
      const response = await apiFetch('/me', {
        method: 'PATCH', credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ displayName: draft.displayName, avatarUrl: draft.avatarUrl ?? '', locale: draft.locale, timezone: draft.timezone, version: draft.version }),
      });
      if (response.status === 401) {
        navigate('/login', { replace: true });
        return;
      }
      if (!response.ok) {
        setConflict(response.status === 409);
        const problem = await response.json().catch(() => ({})) as { title?: string };
        setError(problem.title ?? 'Unable to save your profile. Please retry.');
        return;
      }
      const user = await response.json() as UserProfile;
      setProfile(user);
      setDraft(user);
      setSaved(true);
    } catch {
      setError('Unable to save your profile. Your changes are preserved; please retry.');
    } finally {
      setBusy(false);
    }
  }

  async function logout() {
    mutationEpoch.current++;
    setBusy(true);
    setError(undefined);
    try {
      const response = await apiFetch('/auth/logout', { method: 'POST', credentials: 'include' });
      if (!response.ok && response.status !== 401) throw new Error('Sign out failed');
      navigate('/login', { replace: true });
    } catch {
      setError('Unable to sign out. Please retry.');
    } finally {
      setBusy(false);
    }
  }

  if (error && !profile) {
    return <Stack spacing={2}><Alert severity="error">{error}</Alert><Button onClick={() => { setError(undefined); setReload(value => value + 1); }}>Retry</Button></Stack>;
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
        <Typography color="text.secondary">
          {profile.locale} · {profile.timezone}
        </Typography>
        <Typography color="text.secondary">
          Account: {profile.status}
          {profile.emailVerified ? ' · email verified' : ''}
        </Typography>
        {error ? <Alert severity="error">{error}</Alert> : null}
        {refreshError ? <Alert severity="warning" role="status">{refreshError}</Alert> : null}
        {conflict ? <Button type="button" disabled={busy} onClick={() => { setError(undefined); setSaved(false); setProfile(undefined); setDraft(undefined); setReload(value => value + 1); }}>Discard edits and load latest profile</Button> : null}
        {saved ? <Alert severity="success" role="status">Profile saved.</Alert> : null}
        <TextField label="Display name" required value={draft.displayName} disabled={busy} onChange={event => { setDraft({ ...draft, displayName: event.target.value }); setSaved(false); }} slotProps={{ htmlInput: { maxLength: 120 } }} autoComplete="nickname" />
        <TextField label="Avatar URL" type="url" value={draft.avatarUrl ?? ''} disabled={busy} onChange={event => { setDraft({ ...draft, avatarUrl: event.target.value }); setSaved(false); }} helperText="Use an HTTPS image URL, or leave blank to remove it." />
        <TextField label="Locale" required value={draft.locale} disabled={busy} onChange={event => { setDraft({ ...draft, locale: event.target.value }); setSaved(false); }} helperText="For example, en-CA." />
        <TextField label="Timezone" required value={draft.timezone} disabled={busy} onChange={event => { setDraft({ ...draft, timezone: event.target.value }); setSaved(false); }} helperText="For example, America/Vancouver." />
        <Button type="submit" variant="contained" disabled={busy || conflict}>{busy ? 'Please wait…' : 'Save profile'}</Button>
        <Button type="button" disabled={busy || conflict} onClick={() => { setDraft(profile); setError(undefined); setSaved(false); }}>Discard changes</Button>
        <Button type="button" disabled={busy} onClick={logout} variant="outlined" sx={{ alignSelf: 'flex-start' }}>
          Sign out
        </Button>
      </Stack>
    </Paper>
  );
}
