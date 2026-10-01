import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { AuthPage } from './AuthPage';

type Preview = { id: string; organizationId: string; organizationName: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string; expiresAt: string };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function preview(value: unknown): value is Preview {
  const row = value as Partial<Preview> | null;
  return Boolean(row && typeof row.id === 'string' && uuid.test(row.id) && typeof row.organizationId === 'string' && uuid.test(row.organizationId)
    && typeof row.organizationName === 'string' && row.organizationName.trim()
    && (row.surface === 'INTERNAL' ? ['OWNER', 'ADMIN', 'MEMBER'].includes(row.targetRole ?? '')
      : row.surface === 'PORTAL' && ['OWNER', 'CO_OWNER', 'TENANT', 'OCCUPANT', 'AUTHORIZED_REPRESENTATIVE', 'OTHER'].includes(row.targetRole ?? ''))
    && typeof row.expiresAt === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/.test(row.expiresAt) && Date.parse(row.expiresAt) > Date.now());
}

export function InvitationLinkPage() {
  const location = useLocation(); const navigate = useNavigate();
  const [token, setToken] = useState(''); const [review, setReview] = useState<Preview>();
  const [busy, setBusy] = useState(false); const [signIn, setSignIn] = useState(false);
  const [uncertain, setUncertain] = useState(false); const [accepted, setAccepted] = useState<Preview>();
  const [error, setError] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined);
  const unconfirmed = useRef<Preview | undefined>(undefined);
  useLayoutEffect(() => {
    if (!location.hash && !location.search) return;
    pending.current?.abort(); pending.current = undefined;
    unconfirmed.current = undefined;
    const values = new URLSearchParams(location.hash.slice(1)).getAll('token');
    const proof = values.length === 1 && /^[A-Za-z0-9_-]{32,512}$/.test(values[0]) ? values[0] : '';
    setToken(proof); setReview(undefined); setAccepted(undefined); setUncertain(false); setSignIn(false); setBusy(false); setError(undefined);
    navigate(location.pathname, { replace: true });
  }, [location.hash, location.search, location.pathname, navigate]);
  useEffect(() => () => { pending.current?.abort(); pending.current = undefined; }, []);
  async function submit(accept: boolean) {
    if (pending.current || (accept ? !(review ?? unconfirmed.current) : !token)) return;
    const chosen = review ?? unconfirmed.current;
    if (accept) unconfirmed.current = chosen;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined);
    let timer: ReturnType<typeof setTimeout> | undefined; let aborted: (() => void) | undefined;
    try {
      const result = await Promise.race([
        apiFetch(accept ? `/me/invitations/${chosen!.id}/accept` : '/invitations/review', {
          method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: controller.signal,
          body: JSON.stringify(accept ? {} : { token }),
        }).then(async response => ({ status: response.status, value: await response.json().catch(() => null) as unknown })),
        new Promise<never>((_, reject) => {
          aborted = () => reject(new Error('Invitation request interrupted'));
          controller.signal.addEventListener('abort', aborted, { once: true });
          timer = setTimeout(() => controller.abort(), 15_000);
        }),
      ]);
      if (pending.current !== controller || controller.signal.aborted) return;
      if (result.status === 401) {
        setReview(undefined); setUncertain(accept); setAccepted(undefined); setSignIn(true);
        setError('Sign in with the invited account, then review the invitation again.'); return;
      }
      if ([400, 403, 404, 409].includes(result.status)) {
        setReview(undefined); setUncertain(false); setToken('');
        unconfirmed.current = undefined;
        setError('This link is unavailable to your account. Verify your email or check your current invitations.'); return;
      }
      if (!accept) {
        if (result.status !== 200 || !preview(result.value)) throw new Error('Unconfirmed preview');
        setReview(result.value); setUncertain(false); return;
      }
      const ack = result.value as { invitationId?: unknown; organizationId?: unknown; surface?: unknown; targetRole?: unknown } | null;
      if (result.status !== 200 || ack?.invitationId !== chosen!.id || ack.organizationId !== chosen!.organizationId
        || ack.surface !== chosen!.surface || ack.targetRole !== chosen!.targetRole) throw new Error('Unconfirmed acceptance');
      setAccepted(chosen); setReview(undefined); setToken(''); setUncertain(false);
      unconfirmed.current = undefined;
    } catch {
      if (pending.current === controller) {
        if (accept) setUncertain(true);
        setError(accept ? 'Acceptance could not be confirmed. Retry this invitation to recover its acknowledgment.'
          : 'The invitation could not be reviewed. Wait and retry.');
      }
    } finally {
      clearTimeout(timer); if (aborted) controller.signal.removeEventListener('abort', aborted);
      if (pending.current === controller) { pending.current = undefined; setBusy(false); }
    }
  }
  return <>
    <Container maxWidth="sm" sx={{ py: 4 }}><Paper variant="outlined" sx={{ p: 3 }}><Stack spacing={2}>
      <Typography variant="h4" component="h1">Your invitation link</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      {accepted ? <Alert severity="success">Invitation acceptance acknowledged. <Link to={accepted.surface === 'PORTAL' ? `/portal/${accepted.organizationId}` : `/app/${accepted.organizationId}`}>Open {accepted.surface === 'PORTAL' ? 'Owner Portal' : 'organization'}</Link></Alert>
        : review ? <>
          <Typography variant="h6" component="h2">{review.organizationName}</Typography>
          <Typography>{review.surface === 'PORTAL' ? 'Owner Portal' : 'Internal organization'} access · {review.targetRole.toLowerCase().replaceAll('_', ' ')}</Typography>
          <Button disabled={busy || signIn} variant="contained" onClick={() => void submit(true)}>{uncertain ? 'Retry invitation acceptance' : 'Accept reviewed invitation'}</Button>
          <Typography>Acceptance checks current access again. Acknowledgment does not guarantee that access is still available later.</Typography>
        </> : uncertain ? <>
          <Typography>An earlier acceptance needs confirmation. Sign in with the same invited account before recovering its acknowledgment.</Typography>
          <Button disabled={busy || signIn} variant="contained" onClick={() => void submit(true)}>Retry invitation acceptance</Button>
        </> : token ? <>
          <Typography>Sign in with your invited, verified email to review the organization and access before accepting.</Typography>
          <Button disabled={busy || signIn} variant="contained" onClick={() => void submit(false)}>Review invitation</Button>
        </> : <Typography>Open your original invitation email to review its link. You can also check invitations matching your verified email.</Typography>}
      <Button component={Link} to="/app/invitations">View your invitations</Button>
      <Button component={Link} to="/verify-email">Verify your email</Button>
    </Stack></Paper></Container>
    {signIn && <AuthPage onAuthenticated={() => { setSignIn(false); setError(undefined); }} />}
  </>;
}
