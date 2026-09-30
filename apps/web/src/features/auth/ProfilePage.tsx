import { apiFetch } from '../../api/apiFetch';
import { useEffect, useState } from 'react';
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

export function ProfilePage() {
  const [profile, setProfile] = useState<UserProfile>();
  const [error, setError] = useState<string>();
  const [draft, setDraft] = useState<UserProfile>();
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [reload, setReload] = useState(0);
  const [conflict, setConflict] = useState(false);
  const navigate = useNavigate();

  useEffect(() => {
    let active = true;

    void apiFetch('/me', { credentials: 'include' })
      .then(async (response) => {
        if (response.status === 401) {
          navigate('/login', { replace: true });
          return;
        }

        if (!response.ok) {
          throw new Error('Unable to load profile.');
        }

        const user = (await response.json()) as UserProfile;
        if (active) {
          setProfile(user);
          setDraft(user);
          setConflict(false);
        }
      })
      .catch(() => {
        if (active) {
          setError('Unable to load your profile.');
        }
      });

    return () => {
      active = false;
    };
  }, [navigate, reload]);

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || busy || conflict) return;
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
