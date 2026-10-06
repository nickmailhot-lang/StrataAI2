import { useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { boundedWorkRead } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';

type Intent = { actor: string; key: string; body: string };
const uuid = (value: unknown): value is string => typeof value === 'string'
  && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value)
  && value !== '00000000-0000-0000-0000-000000000000';
function summary(value: unknown, originalActor?: string): string | undefined {
  if (!value || typeof value !== 'object') return;
  const item = value as { organization?: { id?: unknown; name?: unknown; description?: unknown; status?: unknown;
    version?: unknown; ownerUserId?: unknown }; role?: unknown };
  const org = item.organization;
  if (!org || !uuid(org.id) || typeof org.name !== 'string' || !org.name.trim() || org.name.length > 160
    || org.description !== null && typeof org.description !== 'string' || org.status !== 0
    || !Number.isSafeInteger(org.version) || (org.version as number) < 1 || ![0, 1, 2].includes(item.role as number)
    || originalActor && (org.ownerUserId !== originalActor || org.version !== 1 || item.role !== 0)) return;
  return org.id;
}

// PRD-03-TC-05/06/07: one immutable account-bound intent survives uncertainty.
export function OrganizationCreationDialog({ actorId, onCancel, onCreated }: {
  actorId: string; onCancel(): void; onCreated(id: string): void;
}) {
  const [name, setName] = useState(''); const [description, setDescription] = useState('');
  const [intent, setIntent] = useState<Intent>(); const intentRef = useRef<Intent | undefined>(undefined);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const [stopped, setStopped] = useState(false);
  const active = useRef<AbortController | undefined>(undefined); const alive = useRef(true);
  const retry = useRef<HTMLButtonElement>(null); const focusRetry = useRef(false);
  const navigate = useNavigate();
  useEffect(() => { alive.current = true; return () => { alive.current = false; active.current?.abort(); }; }, []);
  useEffect(() => {
    if (!busy && intent && focusRetry.current) { focusRetry.current = false; retry.current?.focus(); }
  }, [busy, intent]);
  function current(controller: AbortController) { return alive.current && active.current === controller && !controller.signal.aborted; }
  async function request(path: string, options: RequestInit, controller: AbortController) {
    return boundedWorkRead(async signal => {
      const response = await apiFetch(path, { ...options, signal });
      const body: unknown = response.status === 401 ? undefined : await response.json().catch(() => undefined);
      return { status: response.status, body };
    }, controller.signal);
  }
  function denied(status: number) {
    intentRef.current = undefined; setIntent(undefined); setName(''); setDescription(''); setStopped(true);
    setError('Organization creation or current access is unavailable to this account. Return to the directory to check current access.');
    if (status === 401) navigate('/login', { replace: true });
  }
  async function submit(recover = false) {
    if (active.current || stopped || recover && !intentRef.current || !recover && intentRef.current) return;
    if (!recover && (!name.trim() || name.trim().length > 160 || !uuid(actorId))) {
      setError('Enter a name of up to 160 characters.'); return;
    }
    const original = intentRef.current ?? { actor: actorId, key: crypto.randomUUID(),
      body: JSON.stringify({ name: name.trim(), description }) };
    intentRef.current = original; setIntent(original);
    const controller = new AbortController(); active.current = controller; setBusy(true); setError(undefined);
    try {
      const before = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(before.status)) { denied(before.status); return; }
      if (before.status !== 200 || !isNotificationProfile(before.body)) throw new Error('Account check unavailable');
      if (before.body.id !== original.actor) { denied(401); return; }
      const result = await request(`/organizations?expectedActorId=${encodeURIComponent(original.actor)}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': original.key }, body: original.body,
      }, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(result.status)) { denied(result.status); return; }
      if ([400, 409, 429].includes(result.status)) {
        intentRef.current = undefined; setIntent(undefined); setName(''); setDescription(''); setStopped(true);
        setError('The original creation could not be acknowledged. Return to the directory and check current Organizations before starting another creation.'); return;
      }
      const id = result.status === 201 ? summary(result.body, original.actor) : undefined;
      if (!id) throw new Error('Creation acknowledgment unavailable');
      // An original receipt is historical: admit present membership/state separately.
      const canonical = await request(`/organizations/${id}`, {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(canonical.status)) { denied(canonical.status); return; }
      if (canonical.status !== 200 || summary(canonical.body) !== id) throw new Error('Current access unavailable');
      const after = await request('/me', {}, controller); if (!current(controller)) return;
      if ([401, 403, 404].includes(after.status)) { denied(after.status); return; }
      if (after.status !== 200 || !isNotificationProfile(after.body)) throw new Error('Final account check unavailable');
      if (after.body.id !== original.actor) { denied(401); return; }
      intentRef.current = undefined; setIntent(undefined); onCreated(id);
    } catch {
      if (current(controller)) {
        setError('The original creation is not yet acknowledged with current access. Retry the original creation; it may already have succeeded.');
        focusRetry.current = true;
      }
    } finally {
      if (current(controller)) { active.current = undefined; setBusy(false); }
    }
  }
  return <Dialog open fullWidth maxWidth="sm" aria-labelledby="organization-creation-title"
    onClose={() => { if (!busy && !intent) onCancel(); }}>
    <Box component="form" onSubmit={event => { event.preventDefault(); void submit(); }}>
      <DialogTitle id="organization-creation-title">Create organization</DialogTitle>
      <DialogContent>
        {error && <Alert severity="error">{error}</Alert>}
        {!stopped && <>
          <TextField name="name" label="Name" autoFocus required fullWidth margin="normal" value={name}
            onChange={event => setName(event.target.value)} disabled={busy || !!intent} slotProps={{ htmlInput: { maxLength: 160 } }} />
          <TextField name="description" label="Description" fullWidth multiline minRows={2} margin="normal" value={description}
            onChange={event => setDescription(event.target.value)} disabled={busy || !!intent} />
        </>}
      </DialogContent>
      <DialogActions>
        <Button disabled={busy || !!intent} onClick={onCancel}>{intent || stopped ? 'Return to Organizations' : 'Cancel'}</Button>
        {!stopped && (intent ? <Button ref={retry} disabled={busy} onClick={() => void submit(true)}>
          {busy ? 'Checking original creation…' : 'Retry original creation'}</Button>
          : <Button disabled={busy} type="submit">Create</Button>)}
      </DialogActions>
    </Box>
  </Dialog>;
}
