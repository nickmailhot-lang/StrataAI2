import { apiFetch } from '../../api/apiFetch';
import { useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Container,
  Paper,
  Stack,
  Tab,
  Tabs,
  TextField,
  Typography,
} from '@mui/material';
import { Link, useNavigate } from 'react-router-dom';

type AuthMode = 'login' | 'register';

type ApiProblem = {
  title?: string;
  code?: string;
};

export function AuthPage() {
  const [mode, setMode] = useState<AuthMode>('login');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string>();
  const [notice, setNotice] = useState<string>();
  const [verificationNeeded, setVerificationNeeded] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const navigate = useNavigate();

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setError(undefined);
    setNotice(undefined);

    try {
      const response = await apiFetch(
        mode === 'login' ? '/auth/login' : '/auth/register',
        {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(
            mode === 'login'
              ? { email, password }
              : {
                  email,
                  password,
                  displayName,
                  locale: 'en-CA',
                  timezone: 'America/Vancouver',
                },
          ),
        },
      );

      if (!response.ok) {
        const problem = (await response.json().catch(() => ({}))) as ApiProblem;
        setError(problem.title ?? 'Authentication failed.');
        setVerificationNeeded(problem.code === 'email_verification_required');
        return;
      }

      if (mode === 'register') {
        const result = await response.json().catch(() => ({})) as { user?: { emailVerified?: boolean } };
        setVerificationNeeded(result.user?.emailVerified === false);
        setNotice(result.user?.emailVerified === false ? 'Account created. Use your verification email to activate it before signing in.' : 'Account created. Sign in to continue.');
        setMode('login');
        setPassword('');
        return;
      }

      navigate('/app');
    } catch {
      setError('Unable to contact StrataAI2.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Container maxWidth="sm" sx={{ py: { xs: 4, sm: 8 } }}>
      <Paper variant="outlined" sx={{ p: { xs: 3, sm: 4 } }}>
        <Stack spacing={3}>
          <Box>
            <Typography variant="h4" component="h1">
              StrataAI2
            </Typography>
            <Typography color="text.secondary">
              Sign in to council and property operations.
            </Typography>
          </Box>
          <Button component={Link} to="/forgot-password">Forgot password?</Button>

          <Tabs
            value={mode}
            onChange={(_, next: AuthMode) => setMode(next)}
            aria-label="Authentication mode"
          >
            <Tab value="login" label="Sign in" />
            <Tab value="register" label="Register" />
          </Tabs>

          {error ? <Alert severity="error">{error}</Alert> : null}
          {notice ? <Alert severity="success" role="status">{notice}</Alert> : null}
          {verificationNeeded ? <Button component={Link} to="/verify-email">Request a verification link</Button> : null}

          <Box component="form" onSubmit={submit}>
            <Stack spacing={2.5}>
              {mode === 'register' ? (
                <TextField
                  label="Display name"
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                  autoComplete="name"
                  required
                />
              ) : null}

              <TextField
                label="Email"
                type="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                autoComplete="email"
                required
              />

              <TextField
                label="Password"
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete={
                  mode === 'login' ? 'current-password' : 'new-password'
                }
                helperText={
                  mode === 'register'
                    ? 'Use at least 12 characters unless your administrator configured a stricter policy.'
                    : undefined
                }
                required
              />

              <Button
                type="submit"
                variant="contained"
                size="large"
                disabled={submitting}
              >
                {submitting
                  ? 'Working…'
                  : mode === 'login'
                    ? 'Sign in'
                    : 'Create account'}
              </Button>
            </Stack>
          </Box>
        </Stack>
      </Paper>
    </Container>
  );
}
