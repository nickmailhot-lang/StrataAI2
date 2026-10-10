import { publicCorrelationReference } from '../../api/correlationReference';
import { useEffect, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { recoveryError, recoveryObject, recoveryProfileConfirmed, useRecoveryRequest } from './useRecoveryRequest';

export function ResetPasswordPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState(() => new URLSearchParams(location.hash.slice(1)).get('token') ?? '');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const { busy, request } = useRecoveryRequest();
  const [completed, setCompleted] = useState(false);
  const [error, setFailure] = useState<{ message: string; reference: string | null }>();
  function setError(message: string | undefined, reference: string | null = null) {
    setFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  useEffect(() => {
    // Fragments never reach Nginx/API logs; remove even the fragment from history.
    if (location.hash || location.search) navigate(location.pathname, { replace: true });
  }, [location.hash, location.pathname, location.search, navigate]);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || !token) return;
    setError(undefined);
    if (password !== confirmation) { setError('Passwords must match.'); return; }
    const result = await request('/auth/password/reset', { token, newPassword: password });
    if (!result) return;
    if (result.status === 200 && recoveryProfileConfirmed(result.value)) {
      setCompleted(true);
      setToken('');
      setPassword('');
      setConfirmation('');
    } else {
      setError(recoveryError(result.value, 'Password reset could not be confirmed. Your details are preserved. Retry or sign in with the new password if the earlier request completed.'), result.reference);
      if (result.status === 400 && recoveryObject(result.value).code === 'invalid_or_expired_token') setToken('');
    }
  }
  return <Container maxWidth="sm" sx={{ py: 6 }}><Paper variant="outlined" sx={{ p: 3 }}>
    <Stack component="form" onSubmit={submit} spacing={3} aria-label="Reset password" aria-busy={busy}>
      <Typography variant="h4" component="h1">Choose a new password</Typography>
      {error ? <Alert severity="error"><span>{error.message}</span>{error.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {error.reference}</Typography>}</Alert> : null}
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
