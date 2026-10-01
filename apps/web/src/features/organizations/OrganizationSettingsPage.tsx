import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';

type Organization = { id: string; name: string; description: string | null; logoUrl: string | null; status: number; version: number };
type Draft = Pick<Organization, 'name' | 'description' | 'logoUrl'>;
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
async function command(path: string, options: RequestInit, controller: AbortController) {
  let deadline: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([
      apiFetch(path, { ...options, signal: controller.signal }).then(async response => ({
        status: response.status, body: response.status === 401 ? undefined : await response.json().catch(() => undefined) as unknown,
      })),
      new Promise<never>((_, reject) => {
        abort = () => reject(new Error('Organization request interrupted'));
        controller.signal.addEventListener('abort', abort, { once: true });
        deadline = setTimeout(() => controller.abort(), 15_000);
      }),
    ]);
  } finally { clearTimeout(deadline); if (abort) controller.signal.removeEventListener('abort', abort); }
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
  const pending = useRef<AbortController | undefined>(undefined);
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true; void load(false);
    return () => { mounted.current = false; pending.current?.abort(); pending.current = undefined; };
    // The keyed route remounts when the Organization changes.
  }, []);
  function deny(status: number) {
    setRecord(undefined); setDraft(undefined); setLatest(undefined); setUnavailable(true);
    setError('Organization settings are unavailable to your account.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function load(preserve: boolean) {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined);
    try {
      const result = await command('/organizations', {}, controller);
      if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
      if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
      if (result.status !== 200 || !Array.isArray(result.body) || !result.body.every(valid)
        || new Set(result.body.map(item => item.organization.id)).size !== result.body.length) throw new Error('Invalid settings response');
      const found = result.body.find(item => item.organization.id === organizationId);
      if (!found || found.role > 1 || found.organization.status !== 0) { deny(404); return; }
      setUnavailable(false);
      if (!preserve || !draft) {
        setRecord(found); setDraft(fields(found.organization)); setLatest(undefined); setReview(false);
      } else if (matches(found.organization, draft)) {
        setRecord(found); setDraft(fields(found.organization)); setLatest(undefined); setReview(false);
        setNotice('Current Organization settings match your draft. The earlier save acknowledgment was unavailable.');
      } else { setLatest(found); setReview(true); }
    } catch {
      if (mounted.current && pending.current === controller) setError('Unable to load current settings. Your draft is preserved. Please retry.');
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (pending.current || review || !record || !draft) return;
    if (!draft.name.trim() || draft.name.trim().length > 160) { setError('Enter an Organization name of at most 160 characters.'); return; }
    const controller = new AbortController(); pending.current = controller; setBusy(true); setError(undefined); setNotice(undefined);
    try {
      const result = await command(`/organizations/${encodeURIComponent(organizationId)}`, {
        method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...draft, version: record.organization.version }),
      }, controller);
      if (!mounted.current || pending.current !== controller || controller.signal.aborted) return;
      if ([401, 403, 404].includes(result.status)) { deny(result.status); return; }
      const updated = { organization: result.body, role: record.role };
      if (result.status === 200 && valid(updated) && updated.organization.id === organizationId
        && updated.organization.status === 0 && updated.organization.version === record.organization.version + 1 && matches(updated.organization, draft)) {
        setRecord(updated); setDraft(fields(updated.organization)); setNotice('Organization settings saved.'); return;
      }
      const code = result.body && typeof result.body === 'object' && 'code' in result.body ? result.body.code : undefined;
      if (result.status === 400) {
        setError(code === 'invalid_organization_logo_url' ? 'Use a secure HTTPS logo URL without embedded credentials, or leave it empty.' : 'Check the Organization fields and try again.'); return;
      }
      setReview(true);
      setError(result.status === 409 ? 'The Organization changed elsewhere. Load current settings to review your draft.' : 'Your save could not be confirmed. Load current settings before saving again.');
    } catch {
      if (mounted.current && pending.current === controller) { setReview(true); setError('Your save could not be confirmed. Load current settings before saving again.'); }
    } finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); } }
  }
  return <Container maxWidth="md" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={`/app/${organizationId}`}>Organization boards</Button>
    <Typography component="h1" variant="h4">Organization settings</Typography>
    {error && <Alert severity="error">{error}</Alert>}
    {notice && <Alert severity="success" role="status">{notice}</Alert>}
    {busy && <CircularProgress aria-label="Loading Organization settings" />}
    {!unavailable && <Button disabled={busy} onClick={() => { setNotice(undefined); void load(true); }}>Load current settings</Button>}
    {latest && <Paper variant="outlined" sx={{ p: 2 }}><Stack spacing={1}>
      <Typography component="h2" variant="h6">Current saved settings</Typography>
      <Typography>Name: {latest.organization.name}</Typography>
      <Typography>Description: {latest.organization.description || 'None'}</Typography>
      <Typography>Logo URL: {latest.organization.logoUrl || 'None'}</Typography>
      <Typography>Review the saved settings before replacing them with your draft.</Typography>
      <Button disabled={busy} onClick={() => { setRecord(latest); setDraft(fields(latest.organization)); setLatest(undefined); setReview(false); setError(undefined); }}>Discard draft and use current settings</Button>
      <Button disabled={busy} onClick={() => { setRecord(latest); setLatest(undefined); setReview(false); setError(undefined); setNotice('Draft retained. Saving will replace the current settings you reviewed.'); }}>Keep draft after review</Button>
    </Stack></Paper>}
    {record && draft && !unavailable && <Box component="form" onSubmit={event => void save(event)}><Stack spacing={2}>
      <TextField label="Organization name" required value={draft.name} disabled={busy} slotProps={{ htmlInput: { maxLength: 160 } }} onChange={event => { setDraft({ ...draft, name: event.target.value }); setNotice(undefined); }} />
      <TextField label="Description" multiline minRows={3} value={draft.description ?? ''} disabled={busy} onChange={event => { setDraft({ ...draft, description: event.target.value }); setNotice(undefined); }} />
      <TextField label="Logo URL" value={draft.logoUrl ?? ''} disabled={busy} helperText="Optional secure HTTPS URL, or leave empty." onChange={event => { setDraft({ ...draft, logoUrl: event.target.value }); setNotice(undefined); }} />
      <Button type="submit" disabled={busy || review}>Save Organization settings</Button>
    </Stack></Box>}
  </Stack></Container>;
}
