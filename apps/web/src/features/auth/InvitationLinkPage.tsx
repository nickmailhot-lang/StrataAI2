import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Paper, Stack, Typography } from '@mui/material';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { AuthPage } from './AuthPage';

type Preview = { id: string; organizationId: string; organizationName: string; surface: 'INTERNAL' | 'PORTAL'; targetRole: string; expiresAt: string; boardTarget?: { boardId: string; role: 'ADMIN' | 'MEMBER' } | null; boardName?: string | null };
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function preview(value: unknown): value is Preview {
  const row = value as Partial<Preview> | null;
  const board = row?.boardTarget;
  const validBoard = board == null ? row?.boardName == null
    : row?.surface === 'INTERNAL' && row.targetRole === 'MEMBER' && typeof board.boardId === 'string'
      && uuid.test(board.boardId) && board.boardId !== '00000000-0000-0000-0000-000000000000'
      && ['ADMIN', 'MEMBER'].includes(board.role) && typeof row.boardName === 'string' && Boolean(row.boardName.trim());
  return Boolean(validBoard && row && typeof row.id === 'string' && uuid.test(row.id) && typeof row.organizationId === 'string' && uuid.test(row.organizationId)
    && typeof row.organizationName === 'string' && row.organizationName.trim()
    && (row.surface === 'INTERNAL' ? ['OWNER', 'ADMIN', 'MEMBER'].includes(row.targetRole ?? '')
      : row.surface === 'PORTAL' && ['OWNER', 'CO_OWNER', 'TENANT', 'OCCUPANT', 'AUTHORIZED_REPRESENTATIVE', 'OTHER'].includes(row.targetRole ?? ''))
    && typeof row.expiresAt === 'string' && /(?:Z|[+-]\d{2}:\d{2})$/.test(row.expiresAt) && Date.parse(row.expiresAt) > Date.now());
}

class AccountUnavailable extends Error { constructor(readonly status: number) { super('Reviewed invitation account unavailable'); } }

export function InvitationLinkPage() {
  const location = useLocation(); const navigate = useNavigate();
  const [token, setToken] = useState(''); const [review, setReview] = useState<Preview>();
  const [busy, setBusy] = useState(false); const [signIn, setSignIn] = useState(false);
  const [uncertain, setUncertain] = useState(false); const [accepted, setAccepted] = useState<Preview>();
  const [error, setError] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined);
  const unconfirmed = useRef<Preview | undefined>(undefined);
  const reviewedActor = useRef<string | undefined>(undefined);
  useLayoutEffect(() => {
    if (!location.hash && !location.search) return;
    pending.current?.abort(); pending.current = undefined;
    unconfirmed.current = undefined; reviewedActor.current = undefined;
    const values = new URLSearchParams(location.hash.slice(1)).getAll('token');
    const proof = values.length === 1 && /^[A-Za-z0-9_-]{32,512}$/.test(values[0]) ? values[0] : '';
    setToken(proof); setReview(undefined); setAccepted(undefined); setUncertain(false); setSignIn(false); setBusy(false); setError(undefined);
    navigate(location.pathname, { replace: true });
  }, [location.hash, location.search, location.pathname, navigate]);
  useEffect(() => () => { pending.current?.abort(); pending.current = undefined; }, []);
  function valid(controller: AbortController) { return pending.current === controller && !controller.signal.aborted; }
  async function verifyAccount(controller: AbortController, expected?: string) {
    try {
      const response = await apiFetch('/me', { signal: controller.signal });
      const me = await response.json().catch(() => null) as { id?: unknown } | null;
      if (!valid(controller)) throw new AccountUnavailable(503);
      if (response.status === 401) throw new AccountUnavailable(401);
      if (response.status !== 200 || typeof me?.id !== 'string' || !uuid.test(me.id) || me.id === '00000000-0000-0000-0000-000000000000')
        throw new AccountUnavailable(503);
      if (expected && me.id !== expected) throw new AccountUnavailable(401);
      return me.id;
    } catch (reason) { throw reason instanceof AccountUnavailable ? reason : new AccountUnavailable(503); }
  }
  async function submit(accept: boolean) {
    if (pending.current || (accept ? !(review ?? unconfirmed.current) : !token)) return;
    const chosen = review ?? unconfirmed.current;
    let submitted = false;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined);
    let timer: ReturnType<typeof setTimeout> | undefined; let aborted: (() => void) | undefined;
    try {
      const result = await Promise.race([
        (async () => {
          const actor = await verifyAccount(controller, reviewedActor.current);
          if (!valid(controller) || accept && !reviewedActor.current) throw new AccountUnavailable(503);
          if (accept) { submitted = true; unconfirmed.current = chosen; }
          const response = await apiFetch(accept ? `/me/invitations/${chosen!.id}/accept?expectedActorId=${encodeURIComponent(actor)}`
            : `/invitations/review?expectedActorId=${encodeURIComponent(actor)}`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, signal: controller.signal,
            body: JSON.stringify(accept ? {} : { token }),
          });
          const value = await response.json().catch(() => null) as unknown;
          if (!valid(controller)) throw new AccountUnavailable(503);
          await verifyAccount(controller, actor);
          if (!valid(controller)) throw new AccountUnavailable(503);
          return { status: response.status, value, actor };
        })(),
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
        reviewedActor.current = result.actor; setReview(result.value); setUncertain(false); return;
      }
      const ack = result.value as { invitationId?: unknown; organizationId?: unknown; surface?: unknown; targetRole?: unknown; boardTarget?: Preview['boardTarget'] } | null;
      if (result.status !== 200 || ack?.invitationId !== chosen!.id || ack.organizationId !== chosen!.organizationId
        || ack.surface !== chosen!.surface || ack.targetRole !== chosen!.targetRole
        || (chosen!.boardTarget == null ? ack.boardTarget != null
          : ack.boardTarget?.boardId !== chosen!.boardTarget.boardId || ack.boardTarget?.role !== chosen!.boardTarget.role)) throw new Error('Unconfirmed acceptance');
      setAccepted(chosen); setReview(undefined); setToken(''); setUncertain(false);
      unconfirmed.current = undefined;
    } catch (reason) {
      if (pending.current === controller) {
        setReview(undefined); setAccepted(undefined);
        setUncertain(accept && (submitted || !!unconfirmed.current));
        if (reason instanceof AccountUnavailable) {
          if (reason.status === 401) setSignIn(true);
          setError(reason.status === 401 ? 'Sign in with the invited account, then review the invitation again.'
            : 'The reviewed account could not be confirmed. Retry after account access is available.');
        } else setError(accept ? 'Acceptance could not be confirmed. Retry this invitation to recover its acknowledgment.'
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
      {accepted ? <Alert severity="success">Invitation acceptance acknowledged. <Link to={accepted.surface === 'PORTAL' ? `/portal/${accepted.organizationId}` : `/app/${accepted.organizationId}${accepted.boardTarget ? `/boards/${accepted.boardTarget.boardId}` : ''}`}>Open {accepted.surface === 'PORTAL' ? 'Owner Portal' : accepted.boardTarget ? 'Board' : 'organization'}</Link></Alert>
        : review ? <>
          <Typography variant="h6" component="h2">{review.organizationName}</Typography>
          {review.boardTarget && <Typography variant="h6" component="h3">{review.boardName}</Typography>}
          <Typography>{review.boardTarget ? `Board access · ${review.boardTarget.role.toLowerCase()}` : <>{review.surface === 'PORTAL' ? 'Owner Portal' : 'Internal organization'} access · {review.targetRole.toLowerCase().replaceAll('_', ' ')}</>}</Typography>
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
    {signIn && <AuthPage invitationToken={token || undefined} onAuthenticated={() => { setSignIn(false); setError(undefined); }} />}
  </>;
}
