import { publicCorrelationReference } from '../../api/correlationReference';
import { useState } from 'react';
import { Alert, Button, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link } from 'react-router-dom';
import { recoveryError, recoveryObject, useRecoveryRequest } from './useRecoveryRequest';

export function PasswordRecoveryPage() {
  const [email, setEmail] = useState('');
  const { busy, request } = useRecoveryRequest();
  const [accepted, setAccepted] = useState(false);
  const [error, setFailure] = useState<{ message: string; reference: string | null }>();
  function setError(message: string | undefined, reference: string | null = null) {
    setFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setError(undefined);
    const result = await request('/auth/password/forgot', { email });
    if (!result) return;
    if (result.status === 202 && recoveryObject(result.value).accepted === true) setAccepted(true);
    else setError(result.status === 429 ? 'Too many requests. Please wait and retry.'
      : recoveryError(result.value, 'The request could not be confirmed. Your email is preserved; please retry.'), result.reference);
  }
  return <Container maxWidth="sm" sx={{ py: 6 }}><Paper variant="outlined" sx={{ p: 3 }}>
    <Stack component="form" onSubmit={submit} spacing={3} aria-label="Request password reset" aria-busy={busy}>
      <Typography variant="h4" component="h1">Reset your password</Typography>
      {error ? <Alert severity="error"><span>{error.message}</span>{error.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {error.reference}</Typography>}</Alert> : null}
      {accepted ? <Alert severity="success" role="status">Request received. Use a valid password reset link to continue.</Alert> : <>
        <TextField label="Email" type="email" autoComplete="email" required disabled={busy} value={email} onChange={event => setEmail(event.target.value)} />
        <Button type="submit" variant="contained" disabled={busy}>{busy ? 'Requesting…' : 'Request reset'}</Button>
      </>}
      <Button component={Link} to="/login">Back to sign in</Button>
    </Stack>
  </Paper></Container>;
}
