import { useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';

export function PasswordRecoveryPage() {
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [accepted, setAccepted] = useState(false);
  const [error, setError] = useState<string>();
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const response = await apiFetch('/auth/password/forgot', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => ({})) as { title?: string };
        setError(problem.title ?? 'Unable to request a reset. Please retry.');
        return;
      }
      setAccepted(true);
    } catch {
      setError('Unable to contact StrataAI2. Your email is preserved; please retry.');
    } finally {
      setBusy(false);
    }
  }
  return <Container maxWidth="sm" sx={{ py: 6 }}><Paper variant="outlined" sx={{ p: 3 }}>
    <Stack component="form" onSubmit={submit} spacing={3} aria-label="Request password reset" aria-busy={busy}>
      <Typography variant="h4" component="h1">Reset your password</Typography>
      {error ? <Alert severity="error">{error}</Alert> : null}
      {accepted ? <Alert severity="success" role="status">Request received. Use a valid password reset link to continue.</Alert> : <>
        <TextField label="Email" type="email" autoComplete="email" required disabled={busy} value={email} onChange={event => setEmail(event.target.value)} />
        <Button type="submit" variant="contained" disabled={busy}>{busy ? 'Requesting…' : 'Request reset'}</Button>
      </>}
      <Button component={Link} to="/login">Back to sign in</Button>
    </Stack>
  </Paper></Container>;
}
