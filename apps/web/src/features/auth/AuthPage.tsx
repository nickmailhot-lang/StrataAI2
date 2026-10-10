import { publicCorrelationReference } from '../../api/correlationReference';
import { apiFetch } from '../../api/apiFetch';
import { useEffect, useRef, useState } from 'react';
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
import { Link, useLocation, useNavigate } from 'react-router-dom';

type AuthMode = 'login' | 'register';

type ApiProblem = {
  title?: string;
  code?: string;
};

export function AuthPage({ onAuthenticated, invitationToken }: { onAuthenticated?: () => void; invitationToken?: string } = {}) {
  const location = useLocation();
  const [mode, setMode] = useState<AuthMode>('login');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setFailure] = useState<{ message: string; reference: string | null }>();
  function setError(message: string | undefined, reference: string | null = null) {
    setFailure(message ? { message, reference: publicCorrelationReference(reference) } : undefined);
  }
  const [notice, setNotice] = useState<string | undefined>(() => location.state?.accountDeactivated === true
    ? 'Your account is deactivated. Historical activity is preserved.' : undefined);
  const [verificationNeeded, setVerificationNeeded] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [expiredAttempt, setExpiredAttempt] = useState(false);
  const attempt = useRef<{ mode: AuthMode; body: string; key: string } | undefined>(undefined);
  const pending = useRef<AbortController | undefined>(undefined);
  useEffect(() => () => { pending.current?.abort(); pending.current = undefined; attempt.current = undefined; }, []);
  const navigate = useNavigate();

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller;
    const body = JSON.stringify(mode === 'login' ? { email, password } : {
      email, password, displayName, locale: 'en-CA', timezone: 'America/Vancouver',
      ...(invitationToken ? { invitationToken } : {}),
    });
    if (attempt.current?.body !== body || attempt.current.mode !== mode) attempt.current = { mode, body, key: crypto.randomUUID() };
    const current = () => pending.current === controller && !controller.signal.aborted;
    let deadline: ReturnType<typeof setTimeout> | undefined;
    let reference: string | null = null;
    setSubmitting(true);
    setError(undefined);
    setNotice(undefined);

    try {
      const { response, result } = await Promise.race([
        (async () => {
          const response = await apiFetch(
        mode === 'login' ? '/auth/login' : '/auth/register',
        {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': attempt.current!.key },
          body,
          signal: controller.signal,
        },
      );
          reference = publicCorrelationReference(response.headers?.get('X-Correlation-ID') ?? null);
          const result: unknown = await response.json().catch(() => ({}));
          return { response, result };
        })(),
        new Promise<never>((_, reject) => {
          deadline = setTimeout(() => { controller.abort(); reject(new Error('Authentication timed out')); }, 15_000);
        }),
      ]);
      if (!current()) return;

      if (!response.ok) {
        const problem = result as ApiProblem;
        if (response.status === 429) {
          setError('Too many attempts. Please wait before retrying with the same details.', reference);
          return;
        }
        const messages: Record<string, string> = {
          invalid_credentials: 'The email or password is incorrect.',
          email_verification_required: 'Verify your email before signing in.',
          account_unavailable: 'This account is unavailable.',
          idempotency_key_expired: mode === 'login' ? 'This sign-in attempt has expired. Start a new sign-in attempt.' : 'This registration attempt has expired. Request a verification link if your account was already created.',
          identity_retry_key_unavailable: 'This retry could not be confirmed. Contact support before starting another attempt.',
          email_unavailable: 'This email cannot be registered.',
          self_registration_disabled: 'Ask your administrator for an invitation.',
          identity_delivery_unavailable: 'Verification email is temporarily unavailable. Please retry later.',
          invalid_or_expired_invitation: 'This invitation is unavailable or does not match your email. Reopen the original invitation or sign in with an existing account.',
        };
        setError(messages[problem.code ?? ''] ?? 'Authentication could not be confirmed. Please try again.', reference);
        setExpiredAttempt(problem.code === 'idempotency_key_expired' && mode === 'login');
        setVerificationNeeded(problem.code === 'email_verification_required' || (mode === 'register' && problem.code === 'idempotency_key_expired'));
        return;
      }

      if (mode === 'register') {
        const registration = result as { user?: { id?: unknown; email?: unknown; emailVerified?: unknown } };
        if (response.status !== 201 || typeof registration.user?.id !== 'string'
          || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(registration.user.id)
          || typeof registration.user.email !== 'string' || registration.user.email.trim().toLowerCase() !== email.trim().toLowerCase()
          || typeof registration.user.emailVerified !== 'boolean') throw new Error('Invalid registration acknowledgment');
        attempt.current = undefined;
        setVerificationNeeded(registration.user?.emailVerified === false);
        setNotice(registration.user?.emailVerified === false
          ? invitationToken ? 'Account created. Verify your email, then reopen your invitation to sign in and accept it.' : 'Account created. Use your verification email to activate it before signing in.'
          : 'Account created. Sign in to continue.');
        setMode('login');
        setPassword('');
        return;
      }

      const login = result as { user?: { id?: unknown; email?: unknown }; sessionExpiresAt?: unknown };
      if (typeof login.user?.id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(login.user.id)
        || typeof login.user.email !== 'string' || login.user.email.trim().toLowerCase() !== email.trim().toLowerCase()
        || typeof login.sessionExpiresAt !== 'string' || !(Date.parse(login.sessionExpiresAt) > Date.now())) throw new Error('Invalid sign-in acknowledgment');
      attempt.current = undefined;
      if (onAuthenticated) onAuthenticated();
      else navigate('/app');
    } catch {
      if (pending.current === controller) setError(`${mode === 'login' ? 'Sign-in' : 'Registration'} could not be confirmed. Retry with the same details to confirm this attempt.`, reference);
    } finally {
      clearTimeout(deadline);
      if (pending.current === controller) { pending.current = undefined; setSubmitting(false); }
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
            <Tab value="login" label="Sign in" disabled={submitting} />
            <Tab value="register" label="Register" disabled={submitting} />
          </Tabs>

          {error ? <Alert severity="error"><span>{error.message}</span>{error.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {error.reference}</Typography>}</Alert> : null}
          {expiredAttempt ? <Button disabled={submitting} onClick={() => { attempt.current = undefined; setExpiredAttempt(false); setError(undefined); }}>Start a new sign-in attempt</Button> : null}
          {notice ? <Alert severity="success" role="status">{notice}</Alert> : null}
          {verificationNeeded ? <Button component={Link} to="/verify-email">Request a verification link</Button> : null}

          <Box component="form" onSubmit={submit}>
            <Stack spacing={2.5}>
              {mode === 'register' ? (
                <TextField
                  label="Display name"
                  disabled={submitting}
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                  autoComplete="name"
                  required
                />
              ) : null}

              <TextField
                label="Email"
                disabled={submitting}
                type="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
                autoComplete="email"
                required
              />

              <TextField
                label="Password"
                disabled={submitting}
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
