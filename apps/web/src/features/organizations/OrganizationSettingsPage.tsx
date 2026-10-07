import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { watchOrganizationMetadata } from './organizationMetadataLive';

type Organization = { id: string; name: string; description: string | null; logoUrl: string | null; status: number; version: number };
type Draft = Pick<Organization, 'name' | 'description' | 'logoUrl'>;
type Intent = { draft: Draft; version: number; key: string; actorId: string };
type Summary = { organization: Organization; role: number };
function valid(value: unknown): value is Summary {
  if (!value || typeof value !== 'object') return false;
  const item = value as Partial<Summary>; const org = item.organization;
  return !!org && typeof org.id === 'string' && typeof org.name === 'string'
    && (org.description === null || typeof org.description === 'string')
    && (org.logoUrl === null || typeof org.logoUrl === 'string')
    && [0, 1, 2].includes(org.status) && Number.isSafeInteger(org.version) && org.version > 0
    && [0, 1, 2].includes(item.role ?? -1);
}
function fields(org: Organization): Draft { return { name: org.name, description: org.description, logoUrl: org.logoUrl }; }
function matches(org: Organization, draft: Draft) {
  const normalize = (value: string | null) => value?.trim() || null;
  return org.name === draft.name.trim() && normalize(org.description) === normalize(draft.description) && normalize(org.logoUrl) === normalize(draft.logoUrl);
}
async function command(path: string, options: RequestInit, signal: AbortSignal) {
  signal.throwIfAborted();
  const response = await apiFetch(path, { ...options, signal });
  const body = response.status === 401 ? undefined : await response.json().catch(() => undefined) as unknown;
  signal.throwIfAborted();
  return { status: response.status, body };
}
export function OrganizationSettingsPage() {
  const { organizationId } = useParams();
  return <Settings key={organizationId} organizationId={organizationId ?? ''} />;
}
function Settings({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate();
  const [record, setRecord] = useState<Summary>(); const [draft, setDraft] = useState<Draft>();
  const [latest, setLatest] = useState<Summary>(); const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [review, setReview] = useState(false); const [unavailable, setUnavailable] = useState(false);
  const [intent, setIntent] = useState<Intent>();
  const [liveActor, setLiveActor] = useState<string>();
  const [liveNotice, setLiveNotice] = useState<string>();
  const [reload, setReload] = useState(0);
  const [backgroundReading, setBackgroundReading] = useState(false);
  const actor = useRef<string | undefined>(undefined);
  const refreshQueued = useRef(false);
  const current = useRef({ draft, intent }); current.current = { draft, intent };
  const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true; void load(false);
    return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; };
    // The keyed route remounts when the Organization changes.
  }, []);
  useEffect(() => {
    if (!liveActor) return;
    const refresh = () => {
      refreshQueued.current = true; setReview(true);
      setLiveNotice('Checking current Organization settings. Your draft and original save are preserved.');
      setReload(value => value + 1);
    };
    return watchOrganizationMetadata({ organizationId, userId: liveActor, invalidate: refresh, reset: refresh, unavailable: refresh });
  }, [organizationId, liveActor]);
  useEffect(() => {
    if (!refreshQueued.current || busy) return;
    refreshQueued.current = false; void load(true, true);
  }, [reload, busy]);
  function deny(status: number) {
    actor.current = undefined; setLiveActor(undefined); refreshQueued.current = false; setLiveNotice(undefined);
    setRecord(undefined); setDraft(undefined); setLatest(undefined); setIntent(undefined); setUnavailable(true);
    setError('Organization settings are unavailable to your account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function load(preserve: boolean, live = false) {
    if (pending.current || intent && !live) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined);
    setBackgroundReading(live);
    try {
      await boundedWorkRead(async signal => {
        const before = await command('/me', {}, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(before.status)) { deny(before.status); return; }
        if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Invalid account');
        if (actor.current && actor.current !== before.body.id) { deny(401); return; }
        const result = await command(`/organizations/${encodeURIComponent(organizationId)}`, {
          headers: { 'X-StrataAI-Expected-Actor': before.body.id },
        }, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        if (result.status !== 200 || !valid(result.body)) throw new Error('Invalid settings response');
        const found = result.body;
        if (found.organization.id !== organizationId || found.role > 1 || found.organization.status !== 0) { deny(404); return; }
        const after = await command('/me', {}, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(after.status)) { deny(after.status); return; }
        if (after.status !== 200 || !isNotificationProfile(after.body)) throw new Error('Invalid account');
        if (after.body.id !== before.body.id) { deny(401); return; }
        actor.current = after.body.id; setLiveActor(after.body.id);
        setUnavailable(false);
        const retained = current.current;
        if (retained.intent) { setLatest(found); setReview(true); }
        else if (!preserve || !retained.draft) {
          setRecord(found); setDraft(fields(found.organization)); setLatest(undefined); setReview(false);
        } else if (matches(found.organization, retained.draft)) {
          setRecord(found); setDraft(fields(found.organization)); setLatest(undefined); setReview(false);
          if (!live) setNotice('Current Organization settings match your draft.');
        } else { setLatest(found); setReview(true); }
        if (live) setLiveNotice('Current settings checked. Review any saved changes before replacing them with your draft.');
      }, controller.signal);
    } catch {
      if (mounted.current && pending.current === controller) {
        setReview(true); setLatest(undefined); setNotice(undefined);
        setError('Unable to load current settings. Your draft is preserved. Please retry.');
      }
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); setBackgroundReading(false); } }
  }
  async function save(event?: React.FormEvent, retry = false) {
    event?.preventDefault();
    if (pending.current || (!retry && (review || intent)) || !record || !draft) return;
    if (!draft.name.trim() || draft.name.trim().length > 160) { setError('Enter an Organization name of at most 160 characters.'); return; }
    const proposed = intent ?? { draft: { ...draft }, version: record.organization.version, key: crypto.randomUUID(), actorId: actor.current ?? '' };
    let submitted = false;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); setNotice(undefined);
    setBackgroundReading(false);
    try {
      await boundedWorkRead(async signal => {
        const before = await command('/me', {}, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(before.status)) { deny(before.status); return; }
        if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Invalid account');
        if (!proposed.actorId || proposed.actorId !== before.body.id) { deny(401); return; }
        submitted = true;
        const result = await command(`/organizations/${encodeURIComponent(organizationId)}`, {
          method: 'PATCH', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': proposed.key, 'X-StrataAI-Expected-Actor': proposed.actorId }, body: JSON.stringify({ ...proposed.draft, version: proposed.version }),
        }, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
        const after = await command('/me', {}, signal);
        if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
        if ([401, 403, 404].includes(after.status)) { deny(after.status); return; }
        if (after.status !== 200 || !isNotificationProfile(after.body)) throw new Error('Invalid account');
        if (after.body.id !== proposed.actorId) { deny(401); return; }
        const updated = { organization: result.body, role: record.role };
        if (result.status === 200 && valid(updated) && updated.organization.id === organizationId
          && updated.organization.status === 0 && updated.organization.version === proposed.version + 1 && matches(updated.organization, proposed.draft)) {
          setIntent(undefined);
          if (retry) { setReview(true); setNotice('Original save acknowledgment recovered. Load current settings before editing again.'); return; }
          setRecord(updated); setDraft(fields(updated.organization)); setNotice('Organization settings saved.'); return;
        }
        const code = result.body && typeof result.body === 'object' && 'code' in result.body ? result.body.code : undefined;
        if (result.status === 400) {
          setIntent(undefined);
          setError(code === 'invalid_organization_logo_url' ? 'Use a secure HTTPS logo URL without embedded credentials, or leave it empty.' : 'Check the Organization fields and try again.'); return;
        }
        setReview(true);
        if ([409, 429].includes(result.status)) setIntent(undefined);
        else setIntent(proposed);
        setError([409, 429].includes(result.status) ? 'The Organization changed elsewhere or the save was refused. Load current settings to review your draft.' : 'Your save could not be confirmed. Retry the original save to recover its acknowledgment.');
      }, controller.signal);
    } catch {
      if (mounted.current && pending.current === controller) {
        setReview(true); setLatest(undefined);
        if (submitted || intent) {
          setIntent(proposed); setError('Your save could not be confirmed. Retry the original save to recover its acknowledgment.');
        } else setError('No save was sent. Your draft is preserved. Load current settings before saving again.');
      }
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={`/app/${organizationId}`}>Organization boards</Button>
    <Typography component="h1" variant="h4">Organization settings</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {notice && <Alert severity="success" role="status">{notice}</Alert>}
    {liveNotice && <Typography role="status">{liveNotice}</Typography>}
    {busy && <CircularProgress aria-label="Loading Organization settings" />}
    {!unavailable && <Button disabled={busy || !!intent} onClick={() => { setNotice(undefined); void load(true); }}>Load current settings</Button>}
    {intent && <><Typography role="status">The original edit is preserved until its acknowledgment is recovered. Later edits may have changed current settings.</Typography>
      <Button disabled={busy} onClick={() => void save(undefined, true)}>Retry original save</Button></>}
    {latest && <Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={1}>
      <Typography component="h2" variant="h6">Current saved settings</Typography>
      <Typography>Name: {latest.organization.name}</Typography>
      <Typography>Description: {latest.organization.description || 'None'}</Typography>
      <Typography>Logo URL: {latest.organization.logoUrl || 'None'}</Typography>
      <Typography>Review the saved settings before replacing them with your draft.</Typography>
      <Button disabled={busy || !!intent} onClick={() => { setRecord(latest); setDraft(fields(latest.organization)); setLatest(undefined); setReview(false); setError(undefined); }}>Discard draft and use current settings</Button>
      <Button disabled={busy || !!intent} onClick={() => { setRecord(latest); setLatest(undefined); setReview(false); setError(undefined); setNotice('Draft retained. Saving will replace the current settings you reviewed.'); }}>Keep draft after review</Button>
    </Stack></Paper>}
    {record && draft && !unavailable && <Box component="form" onSubmit={event => void save(event)}><Stack spacing={2}>
      <TextField label="Organization name" required value={draft.name} disabled={busy && !backgroundReading || !!intent} slotProps={{ htmlInput: { maxLength: 160 } }} onChange={event => { setDraft({ ...draft, name: event.target.value }); setNotice(undefined); }} />
      <TextField label="Description" multiline minRows={3} value={draft.description ?? ''} disabled={busy && !backgroundReading || !!intent} onChange={event => { setDraft({ ...draft, description: event.target.value }); setNotice(undefined); }} />
      <TextField label="Logo URL" value={draft.logoUrl ?? ''} disabled={busy && !backgroundReading || !!intent} helperText="Optional secure HTTPS URL, or leave empty." onChange={event => { setDraft({ ...draft, logoUrl: event.target.value }); setNotice(undefined); }} />
      <Button type="submit" disabled={busy || review}>Save Organization settings</Button>
    </Stack></Box>}
  </Stack></Container>;
}
