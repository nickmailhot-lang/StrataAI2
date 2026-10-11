import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Container, Stack, Typography } from '@mui/material';
import { Link, useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { lifecycleLabel, lifecycleUuid, parseLifecycleReview } from './operationalLifecycle';
import type { LifecycleReview } from './operationalLifecycle';

export function parseLifecycleDirectory(value: unknown, actor: string, after?: string): { items: LifecycleReview[]; nextCursor: string | null } | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return;
  const page = value as Record<string, unknown>;
  if (Object.keys(page).length !== 3 || page.actorId !== actor || !Array.isArray(page.items) || page.items.length > 50) return;
  const items: LifecycleReview[] = []; let previous = after;
  for (const candidate of page.items) {
    const id = candidate && typeof candidate === 'object' ? (candidate as Record<string, unknown>).organizationId : undefined;
    if (!lifecycleUuid(id) || previous && id <= previous) return;
    const item = parseLifecycleReview(candidate, id, actor); if (!item) return;
    items.push(item); previous = id;
  }
  const cursor = page.nextCursor;
  if (cursor !== null && (!lifecycleUuid(cursor) || after && cursor <= after || items.some(item => item.organizationId > cursor))) return;
  return { items, nextCursor: cursor as string | null };
}
export function OrganizationLifecycleDirectoryPage() {
  const navigate = useNavigate(); const [after, setAfter] = useState<string>(); const [reload, setReload] = useState(0);
  const [data, setData] = useState<{ items: LifecycleReview[]; nextCursor: string | null }>();
  const [error, setError] = useState<string>(); const [busy, setBusy] = useState(false); const actor = useRef<string | undefined>(undefined);
  const pending = useRef<AbortController | undefined>(undefined); const heading = useRef<HTMLHeadingElement>(null); const moved = useRef(false);
  function refresh() { pending.current?.abort(); setData(undefined); setError(undefined); setReload(value => value + 1); }
  useEffect(() => {
    const visibility = () => { if (document.visibilityState === 'visible') refresh(); };
    window.addEventListener('focus', refresh); document.addEventListener('visibilitychange', visibility);
    return () => { window.removeEventListener('focus', refresh); document.removeEventListener('visibilitychange', visibility); };
  }, []);
  useEffect(() => {
    const controller = new AbortController(); pending.current = controller; setData(undefined); setError(undefined); setBusy(true);
    void boundedWorkRead(async signal => {
      const beforeResponse = await apiFetch('/me', { signal });
      const before: unknown = beforeResponse.status === 200 ? await beforeResponse.json() : undefined;
      signal.throwIfAborted();
      if (beforeResponse.status === 401 || beforeResponse.status === 403 || beforeResponse.status === 404) { navigate('/login', { replace: true }); return; }
      if (!isNotificationProfile(before)) throw new Error('Account unconfirmed');
      if (actor.current && actor.current !== before.id) { navigate('/login', { replace: true }); return; }
      const response = await apiFetch(`/organizations/operational-lifecycle-directory${after ? `?after=${encodeURIComponent(after)}` : ''}`,
        { signal, headers: { 'X-StrataAI-Expected-Actor': before.id } });
      signal.throwIfAborted();
      if ([401,403,404].includes(response.status)) { navigate('/login', { replace: true }); return; }
      const page = response.status === 200 ? parseLifecycleDirectory(await response.json(), before.id, after) : undefined;
      signal.throwIfAborted();
      if (!page) throw new Error('Directory unconfirmed');
      const afterResponse = await apiFetch('/me', { signal });
      const account: unknown = afterResponse.status === 200 ? await afterResponse.json() : undefined;
      signal.throwIfAborted();
      if ([401,403,404].includes(afterResponse.status)) { navigate('/login', { replace: true }); return; }
      if (!isNotificationProfile(account)) throw new Error('Account unconfirmed');
      if (account.id !== before.id) { navigate('/login', { replace: true }); return; }
      if (!signal.aborted) { actor.current = account.id; setData(page); }
    }, controller.signal).catch(() => { if (!controller.signal.aborted) setError('Unable to confirm lifecycle access. Reload to check current permission.'); })
      .finally(() => { if (!controller.signal.aborted) { setBusy(false); if (moved.current) { heading.current?.focus(); moved.current = false; } } });
    return () => controller.abort();
  }, [after, reload, navigate]);
  function page(cursor?: string) { pending.current?.abort(); setData(undefined); setError(undefined); moved.current = true; setAfter(cursor); }
  return <Container component="main" maxWidth="md" sx={{ py: 3 }}><Stack spacing={2} sx={{ overflowWrap: 'anywhere' }}>
    <Typography component="h1" variant="h4" ref={heading} tabIndex={-1}>Organization lifecycle management</Typography>
    <Button component={Link} to="/app">Back to organizations</Button>
    <Typography>Review lifecycle for organizations you currently administer, including suspended and deactivated organizations. Each action checks current permission again.</Typography>
    {busy && <Typography role="status">Checking current lifecycle access…</Typography>}
    {error && <Alert severity="warning">{error}</Alert>}
    {data && (data.items.length ? <Stack component="ul" spacing={2} sx={{ listStyle: 'none', pl: 0 }}>{data.items.map(item => <Stack component="li" key={item.organizationId}>
      <Button component={Link} to={`/app/${item.organizationId}/operational-lifecycle`} sx={{ justifyContent: 'flex-start', textAlign: 'left' }}
        aria-label={`${item.name}: ${lifecycleLabel(item.status)}; revision ${item.version}`}>{item.name}</Button>
      <Typography>Lifecycle: {lifecycleLabel(item.status)}. Revision {item.version}.</Typography>
    </Stack>)}</Stack> : <Typography>No organizations you can administer on this page.{data.nextCursor ? ' Continue to check later organizations.' : ''}</Typography>)}
    <Stack direction="row" spacing={2} useFlexGap sx={{ flexWrap: 'wrap' }}>
      <Button disabled={busy} onClick={refresh}>Reload lifecycle directory</Button>
      {after && <Button disabled={busy} onClick={() => page()}>First lifecycle page</Button>}
      {data?.nextCursor && <Button disabled={busy} onClick={() => page(data.nextCursor!)}>Next lifecycle page</Button>}
    </Stack>
  </Stack></Container>;
}
