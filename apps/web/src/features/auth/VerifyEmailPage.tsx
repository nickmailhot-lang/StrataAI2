import { useEffect, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { recoveryError, recoveryObject, recoveryProfileConfirmed, useRecoveryRequest } from './useRecoveryRequest';

export function VerifyEmailPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState(() => new URLSearchParams(location.hash.slice(1)).get('token') ?? '');
  const [email, setEmail] = useState('');
  const { busy, request } = useRecoveryRequest();
  const [verified, setVerified] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [error, setError] = useState<string>();
  useEffect(() => {
    if (location.hash || location.search) navigate(location.pathname, { replace: true });
  }, [location.hash, location.pathname, location.search, navigate]);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setError(undefined); setNotice(undefined);
    const result = await request(token ? '/auth/verify-email' : '/auth/verification/resend', token ? { token } : { email });
    if (!result) return;
    if (token && result.status === 200 && recoveryProfileConfirmed(result.value) && recoveryObject(result.value).emailVerified === true) {
      setVerified(true); setToken(''); setNotice('Email verified. Sign in to continue.');
    } else if (!token && result.status === 202 && recoveryObject(result.value).accepted === true) {
      setNotice('Request received. Use a valid verification link to continue.');
    } else {
      setError(result.status === 429 ? 'Too many requests. Please wait and retry.'
        : recoveryError(result.value, 'Verification could not be confirmed. Your details are preserved. Retry or sign in if the earlier request completed.'));
      if (result.status === 400 && recoveryObject(result.value).code === 'invalid_or_expired_token') setToken('');
    }
  }
  return <Container maxWidth="sm" sx={{ py: 6 }}><Paper variant="outlined" sx={{ p: 3 }}>
    <Stack component="form" onSubmit={submit} spacing={3} aria-label="Verify email" aria-busy={busy}>
      <Typography variant="h4" component="h1">Verify your email</Typography>
      {error ? <Alert severity="error">{error}</Alert> : null}
      {notice ? <Alert severity="success" role="status">{notice}</Alert> : null}
      {!verified ? token ? <>
        <Typography>Confirm this email address to activate your account.</Typography>
        <Button type="submit" variant="contained" disabled={busy}>{busy ? 'Verifying…' : 'Verify email'}</Button>
      </> : <>
        <Typography>Request another verification link if your original link is missing or expired.</Typography>
        <TextField label="Email" type="email" autoComplete="email" value={email} onChange={event => setEmail(event.target.value)} required disabled={busy} />
        <Button type="submit" variant="contained" disabled={busy}>{busy ? 'Requesting…' : 'Request verification link'}</Button>
      </> : null}
      <Button component={Link} to="/login">Back to sign in</Button>
    </Stack>
  </Paper></Container>;
}
