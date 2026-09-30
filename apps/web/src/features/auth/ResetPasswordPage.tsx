import { useEffect, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';

export function ResetPasswordPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState(() => new URLSearchParams(location.hash.slice(1)).get('token') ?? '');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [busy, setBusy] = useState(false);
  const [completed, setCompleted] = useState(false);
  const [error, setError] = useState<string>();
  useEffect(() => {
    // Fragments never reach Nginx/API logs; remove even the fragment from history.
    if (location.hash || location.search) navigate(location.pathname, { replace: true });
  }, [location.hash, location.pathname, location.search, navigate]);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !token) return;
    setError(undefined);
    if (password !== confirmation) { setError('Passwords must match.'); return; }
    setBusy(true);
    try {
      const response = await apiFetch('/auth/password/reset', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ token, newPassword: password }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };
        setError(problem.title ?? 'Unable to reset your password. Please retry.');
        if (problem.code === 'invalid_or_expired_token') setToken('');
        return;
      }
      setCompleted(true);
      setToken('');
      setPassword('');
      setConfirmation('');
    } catch {
      setError('Unable to contact StrataAI2. Please retry.');
    } finally {
      setBusy(false);
    }
  }
  return <Container maxWidth="sm" sx={{ py: 6 }}><Paper variant="outlined" sx={{ p: 3 }}>
    <Stack component="form" onSubmit={submit} spacing={3} aria-label="Reset password" aria-busy={busy}>
      <Typography variant="h4" component="h1">Choose a new password</Typography>
      {error ? <Alert severity="error">{error}</Alert> : null}
      {completed ? <Alert severity="success" role="status">Password reset. Sign in with your new password.</Alert> : token ? <>
        <TextField label="New password" type="password" autoComplete="new-password" required disabled={busy} value={password} onChange={event => setPassword(event.target.value)} helperText="Use at least 12 characters. The server enforces your organization's policy." slotProps={{ htmlInput: { minLength: 12 } }} />
        <TextField label="Confirm new password" type="password" autoComplete="new-password" required disabled={busy} value={confirmation} onChange={event => setConfirmation(event.target.value)} />
        <Button type="submit" variant="contained" disabled={busy}>{busy ? 'Resetting…' : 'Reset password'}</Button>
      </> : <Alert severity="warning">This reset link is missing, invalid, or expired. Request a new link.</Alert>}
      {!completed ? <Button component={Link} to="/forgot-password">Request a new link</Button> : null}
      <Button component={Link} to="/login">Back to sign in</Button>
    </Stack>
  </Paper></Container>;
}
