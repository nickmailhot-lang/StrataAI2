import { useEffect, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';

export function VerifyEmailPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState(() => new URLSearchParams(location.hash.slice(1)).get('token') ?? '');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [verified, setVerified] = useState(false);
  const [notice, setNotice] = useState<string>();
  const [error, setError] = useState<string>();
  useEffect(() => {
    if (location.hash || location.search) navigate(location.pathname, { replace: true });
  }, [location.hash, location.pathname, location.search, navigate]);
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setBusy(true); setError(undefined); setNotice(undefined);
    try {
      const response = await apiFetch(token ? '/auth/verify-email' : '/auth/verification/resend', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(token ? { token } : { email }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({})) as { title?: string; code?: string };
        setError(problem.title ?? 'Unable to verify your email. Please retry.');
        if (problem.code === 'invalid_or_expired_token') setToken('');
        return;
      }
      if (token) { setVerified(true); setToken(''); setNotice('Email verified. Sign in to continue.'); }
      else setNotice('Request received. Use a valid verification link to continue.');
    } catch { setError('Unable to contact StrataAI2. Please retry.'); }
    finally { setBusy(false); }
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
