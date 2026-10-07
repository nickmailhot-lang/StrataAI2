import { useEffect, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, Paper, Stack, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { watchOrganizationMetadata } from './organizationMetadataLive';

type Member = { membershipId: string; userId: string; displayName: string; email: string; role: number;
  accountStatus: string; emailVerified: boolean; isUsableOwner: boolean; joinedAt: string; updatedAt: string; version: number };
type Page = { organizationId: string; items: Member[]; nextCursor: string | null; actorRole: number };
type RemovalIntent = { key: string; target: string; version: number; actor: string };
type Review = { organizationId: string; member: Member | null; actorRole: number };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
function member(value: unknown): value is Member {
  if (!value || typeof value !== 'object') return false;
  const row = value as Member;
  return uuid(row.membershipId) && uuid(row.userId) && typeof row.displayName === 'string' && typeof row.email === 'string'
    && [0, 1, 2].includes(row.role) && ['ACTIVE', 'PENDING_VERIFICATION', 'SUSPENDED', 'DEACTIVATED'].includes(row.accountStatus)
    && typeof row.emailVerified === 'boolean' && typeof row.isUsableOwner === 'boolean'
    && (!row.isUsableOwner || row.role === 0 && row.accountStatus === 'ACTIVE')
    && typeof row.joinedAt === 'string' && Number.isFinite(Date.parse(row.joinedAt))
    && typeof row.updatedAt === 'string' && Number.isFinite(Date.parse(row.updatedAt))
    && Number.isSafeInteger(row.version) && row.version > 0;
}
async function request(path: string, options: RequestInit, signal: AbortSignal) {
  signal.throwIfAborted();
  const response = await apiFetch(path, { ...options, signal });
  const body = [204, 401].includes(response.status) ? undefined : await response.json().catch(() => undefined) as unknown;
  signal.throwIfAborted();
  return { status: response.status, body };
}
export function OrganizationMembersPage() {
  const { organizationId } = useParams();
  return <Members key={organizationId} organizationId={organizationId ?? ''} />;
}
function Members({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate(); const root = `/organizations/${encodeURIComponent(organizationId)}/members`;
  const [page, setPage] = useState<Page>(); const [actorId, setActorId] = useState<string>();
  const [cursor, setCursor] = useState<string | null>(null); const [history, setHistory] = useState<(string | null)[]>([]);
  const [selected, setSelected] = useState<Member>(); const [reviewId, setReviewId] = useState<string>();
  const [busy, setBusy] = useState(false); const [denied, setDenied] = useState(false);
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [retryIntent, setRetryIntent] = useState<RemovalIntent>();
  const [recovered, setRecovered] = useState(false);
  const [liveNotice, setLiveNotice] = useState<string>(); const [reloadVersion, setReloadVersion] = useState(0);
  const refreshQueued = useRef(false); const epoch = useRef(0);
  const actor = useRef<string | undefined>(undefined);
  const currentIntent = useRef(retryIntent); currentIntent.current = retryIntent;
  const retry = useRef<HTMLButtonElement>(null); const acknowledgment = useRef<HTMLDivElement>(null);
  const retryFocus = useRef(false);
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const recovery = useRef<HTMLButtonElement>(null); const reload = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    mounted.current = true; void load(null, []);
    return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; };
    // Route identity is fenced by the keyed component.
  }, []);
  useEffect(() => {
    if (!actorId) return;
    const refresh = () => {
      epoch.current++; refreshQueued.current = true;
      setPage(undefined); setSelected(undefined); setReviewId(undefined);
      setLiveNotice('Checking current membership and access. Any original removal retry is preserved.');
      setReloadVersion(value => value + 1);
    };
    return watchOrganizationMetadata({ organizationId, userId: actorId, invalidate: refresh, reset: refresh, unavailable: refresh });
  }, [organizationId, actorId]);
  useEffect(() => {
    if (!refreshQueued.current || busy) return;
    refreshQueued.current = false; void load(null, [], true);
  }, [reloadVersion, busy]);
  useEffect(() => {
    if (!busy && recovered && retryFocus.current) {
      if (document.activeElement === document.body) acknowledgment.current?.focus();
      retryFocus.current = false;
    }
  }, [busy, recovered]);
  function allowed(controller: AbortController) { return mounted.current && pending.current === controller && !controller.signal.aborted; }
  function deny(status: number) {
    actor.current = undefined; refreshQueued.current = false; setLiveNotice(undefined);
    setPage(undefined); setSelected(undefined); setReviewId(undefined); setActorId(undefined); setNotice(undefined); setRetryIntent(undefined); setRecovered(false);
    setHistory([]); setCursor(null); setDenied(true); setError('Organization members are unavailable to your account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  function begin() {
    if (pending.current) return undefined;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); return controller;
  }
  function finish(controller: AbortController) {
    if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); }
  }
  async function account(controller: AbortController, signal: AbortSignal, expected = actor.current) {
    const me = await request('/me', {}, signal); if (!allowed(controller)) return;
    if ([401, 403, 404].includes(me.status)) { deny(me.status); return; }
    const id = (me.body as { id?: unknown } | undefined)?.id;
    if (me.status !== 200 || !uuid(id)) throw new Error('Invalid account');
    if (expected && id !== expected) { deny(401); return; }
    return id;
  }
  async function load(after: string | null, previous: (string | null)[], live = false) {
    if (retryIntent && !live) return;
    const controller = begin(); if (!controller) return;
    const started = epoch.current;
    setPage(undefined); setSelected(undefined);
    if (!live) { setNotice(undefined); setRecovered(false); }
    try {
      await boundedWorkRead(async signal => {
        const before = await account(controller, signal); if (!before) return;
        const result = await request(`${root}${after ? `?after=${encodeURIComponent(after)}` : ''}`, {}, signal);
        if (!allowed(controller)) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        const data = result.body as Page | undefined;
        if (result.status !== 200 || !data || data.organizationId !== organizationId || ![0, 1].includes(data.actorRole)
          || !Array.isArray(data.items) || data.items.length > 50 || !data.items.every(member)
          || new Set(data.items.map(row => row.userId)).size !== data.items.length
          || data.items.some((row, index) => (index > 0 && row.userId.toLowerCase() <= data.items[index - 1].userId.toLowerCase())
            || after !== null && row.userId.toLowerCase() <= after.toLowerCase())
          || !(data.nextCursor === null || data.items.length === 50 && uuid(data.nextCursor) && data.nextCursor === data.items[49].userId))
          throw new Error('Invalid member page');
        const afterActor = await account(controller, signal, before); if (!afterActor || started !== epoch.current) return;
        actor.current = afterActor; setActorId(afterActor);
        if (live && currentIntent.current) {
          setLiveNotice('Current access checked. Retry the original removal before reviewing later membership.'); return;
        }
        setPage(data); setCursor(after); setHistory(previous); setReviewId(undefined); setDenied(false);
        if (live) setLiveNotice('Current members checked. Review a membership again before confirming removal.');
      }, controller.signal);
    } catch { if (mounted.current && pending.current === controller) setError('Unable to load current members. Please retry.'); }
    finally { finish(controller); }
  }
  async function review(userId: string, uncertain: boolean) {
    if (retryIntent) return;
    setRecovered(false);
    const controller = begin(); if (!controller) return;
    const started = epoch.current;
    setSelected(undefined); setReviewId(userId); setNotice(undefined); setPage(undefined);
    try {
      await boundedWorkRead(async signal => {
        const before = await account(controller, signal); if (!before) return;
        const result = await request(`${root}/${encodeURIComponent(userId)}`, {}, signal); if (!allowed(controller)) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        const data = result.body as Review | undefined;
        if (result.status !== 200 || !data || data.organizationId !== organizationId || ![0, 1].includes(data.actorRole)
          || !(data.member === null || member(data.member) && data.member.userId === userId)) throw new Error('Invalid member review');
        if (!await account(controller, signal, before) || started !== epoch.current) return;
        if (!data.member) {
          setReviewId(undefined); setNotice(uncertain
            ? 'This person is currently no longer an internal member. The earlier removal acknowledgment was unavailable.'
            : 'This person is currently no longer an internal member.');
        } else if (data.actorRole === 1 && data.member.role === 0) {
          setReviewId(undefined); setError('Only an Owner can remove an Owner. Load current members to continue.');
        } else { setSelected(data.member); if (uncertain) setError('Review this current membership and confirm again before making another removal request.'); }
      }, controller.signal);
    } catch { if (mounted.current && pending.current === controller) setError('Unable to review this membership. No further removal will be sent until the current membership is reviewed.'); }
    finally { finish(controller); }
  }
  async function remove(recover = false) {
    if (recover ? !retryIntent : !selected || !actorId || !!retryIntent) return;
    const intent = recover ? retryIntent! : { key: crypto.randomUUID(), target: selected!.userId, version: selected!.version, actor: actorId! };
    const controller = begin(); if (!controller) return;
    setNotice(undefined);
    try {
      await boundedWorkRead(async signal => {
        if (!await account(controller, signal, intent.actor)) return;
        const path = `${root}/${encodeURIComponent(intent.target)}?expectedVersion=${intent.version}&expectedActorId=${encodeURIComponent(intent.actor)}`;
        const result = await request(path, { method: 'DELETE', headers: { 'Idempotency-Key': intent.key } }, signal);
        if (!allowed(controller)) return;
        const code = (result.body as { code?: unknown } | undefined)?.code;
        if ([401, 403].includes(result.status) || result.status === 404 && code !== 'member_not_found') { deny(result.status); return; }
        if (!await account(controller, signal, intent.actor)) return;
        setSelected(undefined); setPage(undefined);
        if (result.status === 204) {
          setRetryIntent(undefined); setRecovered(recover); setReviewId(recover ? intent.target : undefined);
          setNotice(recover ? 'Original removal acknowledged. Review current membership to check later access.' : 'Member removed.');
          if (!recover && intent.target === intent.actor) navigate('/app', { replace: true });
          return;
        }
        setReviewId(intent.target);
        const definitive = [400, 404, 409, 429].includes(result.status);
        setRetryIntent(definitive ? undefined : intent);
        setError(result.status === 409 && code === 'sole_owner'
          ? 'The Organization needs another usable Owner before this person can be removed.'
          : result.status === 409 && code === 'member_version_conflict'
            ? 'The membership changed elsewhere. Review it before confirming removal again.'
            : definitive ? 'The removal was refused. Review current membership before considering another removal.'
              : 'The removal could not be confirmed. Retry the original removal to recover its acknowledgment.');
      }, controller.signal);
    } catch {
      if (mounted.current && pending.current === controller) {
        setSelected(undefined); setPage(undefined); setReviewId(intent.target); setRetryIntent(intent);
        setError('The removal could not be confirmed. Retry the original removal to recover its acknowledgment.');
      }
    } finally { finish(controller); }
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={`/app/${organizationId}`}>Organization boards</Button>
    <Typography component="h1" variant="h4">Organization members</Typography>
    <Typography>Internal membership controls access to Organization boards. Portal relationships are managed separately.</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {notice && <Alert ref={acknowledgment} tabIndex={-1} severity="info" role="status">{notice}</Alert>}
    {liveNotice && <Alert severity="info">{liveNotice}</Alert>}
    {busy && <CircularProgress aria-label="Loading Organization members" />}
    {!denied && <Button ref={reload} disabled={busy || !!retryIntent} onClick={() => void load(null, [])}>Load current members</Button>}
    {reviewId && !selected && !denied && <Button ref={recovery} disabled={busy || !!retryIntent} onClick={() => void review(reviewId, !recovered)}>Review current membership</Button>}
    {retryIntent && !denied && <Button ref={retry} disabled={busy} onClick={() => {
      retryFocus.current = document.activeElement === retry.current; void remove(true);
    }}>Retry original removal</Button>}
    {page && <>
      <Button component={Link} to={`/app/${organizationId}/invite`}>Create Organization invitation</Button>
      <Button component={Link} to={`/app/${organizationId}/invitations`}>Review issued invitations</Button>
      <Typography>Page {history.length + 1}. Membership may change while you browse.</Typography>
      {!page.items.length && <Typography>No internal members on this page.</Typography>}
      {page.items.map(row => <Paper variant="outlined" sx={{ p: 2, overflowWrap: 'anywhere' }} key={row.membershipId}><Stack spacing={1}>
        <Typography component="h2" variant="h6">{row.displayName}{row.userId === actorId ? ' (you)' : ''}</Typography>
        <Typography>{row.email}</Typography>
        <Typography>Role: {['Owner', 'Admin', 'Member'][row.role]}</Typography>
        <Typography>Account: {{ ACTIVE: 'Active', PENDING_VERIFICATION: 'Pending verification', SUSPENDED: 'Suspended', DEACTIVATED: 'Deactivated' }[row.accountStatus]}</Typography>
        {row.role === 0 && <Typography>{row.isUsableOwner ? 'Usable Owner' : 'This account cannot currently provide owner continuity.'}</Typography>}
        {page.actorRole === 1 && row.role === 0 ? <Typography>Only an Owner can remove an Owner.</Typography>
          : <Button disabled={busy} onClick={() => void review(row.userId, false)} aria-label={`Review removal of ${row.displayName}`}>Review removal</Button>}
      </Stack></Paper>)}
      <Stack direction="row" spacing={2}>
        <Button disabled={busy || !history.length} onClick={() => void load(history[history.length - 1], history.slice(0, -1))}>Previous members</Button>
        <Button disabled={busy || !page.nextCursor} onClick={() => void load(page.nextCursor, [...history, cursor])}>Next members</Button>
      </Stack>
    </>}
    <Dialog open={!!selected} onClose={() => { if (!busy) { setSelected(undefined); setReviewId(undefined); } }} aria-labelledby="member-removal-title"
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: () => (retry.current ?? recovery.current ?? reload.current)?.focus() } }}>
      <DialogTitle id="member-removal-title">Remove internal member?</DialogTitle>
      <DialogContent>{selected && <Stack spacing={1}>
        <Typography>{selected.displayName}</Typography><Typography>{selected.email}</Typography>
        <Typography>Current role: {['Owner', 'Admin', 'Member'][selected.role]}</Typography>
        <Typography>This removes internal access to this Organization. Confirm only after reviewing this current membership.</Typography>
        {selected.userId === actorId && <Typography>You will lose your internal access to this Organization.</Typography>}
      </Stack>}</DialogContent>
      <DialogActions><Button ref={cancel} autoFocus disabled={busy} onClick={() => { setSelected(undefined); setReviewId(undefined); }}>Cancel removal</Button>
        <Button color="error" disabled={busy || !selected} onClick={() => void remove()}>Confirm member removal</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
