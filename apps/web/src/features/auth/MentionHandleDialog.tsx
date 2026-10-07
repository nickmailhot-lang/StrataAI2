import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material';
import { apiFetch } from '../../api/apiFetch';
import { createMentionHandleIntent, parseMentionHandleAcknowledgment, parseMentionHandleSetting,
  type MentionHandleIntent, type MentionHandleSetting } from './mentionHandle';

class SessionDenied extends Error {}
async function bounded<T>(controller: AbortController, work: () => Promise<T>) {
  let timer: ReturnType<typeof setTimeout> | undefined; let abort: (() => void) | undefined;
  try {
    return await Promise.race([work(), new Promise<never>((_, reject) => {
      abort = () => reject(new Error('Handle request interrupted'));
      controller.signal.addEventListener('abort', abort, { once: true });
      timer = setTimeout(() => controller.abort(), 15_000);
    })]);
  } finally { clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort); }
}
export function MentionHandleDialog({ open, subject, onClose, onDenied }: {
  open: boolean; subject: string; onClose: () => void; onDenied: () => void;
}) {
  const [setting, setSetting] = useState<MentionHandleSetting>(); const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [error, setError] = useState<string>(); const [saved, setSaved] = useState(false);
  const pending = useRef<MentionHandleIntent | undefined>(undefined);
  const operation = useRef<AbortController | undefined>(undefined); const epoch = useRef(0);
  const field = useRef<HTMLInputElement | null>(null); const mounted = useRef(false);
  const dialog = useRef<HTMLDivElement | null>(null); const retry = useRef<HTMLButtonElement | null>(null);
  const recoverFocus = useRef(false);
  const deny = useEffectEvent(() => { pending.current = undefined; setSetting(undefined); setDraft(''); setUncertain(false); onDenied(); });
  async function account(controller: AbortController) {
    if (controller.signal.aborted) throw new Error('Account request interrupted');
    const response = await apiFetch('/me', { signal: controller.signal, cache: 'no-store' });
    if (response.status === 401) throw new SessionDenied();
    if (!response.ok) throw new Error('Account admission unavailable');
    const value = await response.json() as { id?: unknown; version?: unknown; status?: unknown };
    if (controller.signal.aborted) throw new Error('Account request interrupted');
    if (value.id !== subject) throw new SessionDenied();
    if (!Number.isSafeInteger(value.version) || Number(value.version) < 1 || !['ACTIVE', 'active'].includes(String(value.status)))
      throw new SessionDenied();
    return { id: subject, version: Number(value.version) };
  }
  async function current(controller: AbortController) {
    const before = await account(controller);
    const response = await apiFetch('/me/mention-handle', { signal: controller.signal, cache: 'no-store' });
    if (response.status === 401) throw new SessionDenied();
    if (!response.ok) throw new Error('Handle setting unavailable');
    const handle = parseMentionHandleSetting(await response.json(), before.id, before.version);
    if (controller.signal.aborted) throw new Error('Handle request interrupted');
    const after = await account(controller);
    if (after.version !== before.version) throw new Error('Account changed during read');
    return handle;
  }
  const load = useEffectEvent(async () => {
    if (operation.current || pending.current) return;
    const controller = new AbortController(); operation.current = controller; const generation = ++epoch.current;
    setBusy(true); setError(undefined); setSetting(undefined); setSaved(false);
    try {
      const value = await bounded(controller, () => current(controller));
      if (mounted.current && generation === epoch.current) { setSetting(value); setDraft(value.handle); }
    } catch (failure) {
      if (mounted.current && generation === epoch.current) {
        if (failure instanceof SessionDenied) deny();
        else setError('Unable to load your current handle. Review the account setting before saving.');
      }
    } finally {
      if (operation.current === controller) operation.current = undefined;
      if (mounted.current && generation === epoch.current) setBusy(false);
    }
  });
  useEffect(() => {
    mounted.current = open;
    if (open) {
      if (pending.current && pending.current.original.userId !== subject) deny();
      else void load();
    }
    return () => { mounted.current = false; epoch.current++; operation.current?.abort(); operation.current = undefined; };
  }, [open, subject]);
  useEffect(() => {
    if (!busy && uncertain && recoverFocus.current) {
      recoverFocus.current = false;
      if (document.visibilityState === 'visible' && (dialog.current?.contains(document.activeElement) || document.activeElement === document.body)) retry.current?.focus();
    }
  }, [busy, uncertain]);
  async function save() {
    if (operation.current || !setting && !pending.current) return;
    if (pending.current && pending.current.original.userId !== subject) { deny(); return; }
    let intent = pending.current;
    if (!intent) {
      try { intent = createMentionHandleIntent(setting!, draft); }
      catch { setError('Use 3–40 letters, digits or underscores, beginning with a letter. Reserved names cannot be chosen.'); return; }
    }
    const controller = new AbortController(); operation.current = controller; const generation = ++epoch.current;
    recoverFocus.current = dialog.current?.contains(document.activeElement) ?? false;
    const active = () => mounted.current && generation === epoch.current;
    setBusy(true); setError(undefined); setSaved(false);
    try {
      const result = await bounded(controller, async () => {
        const admitted = await account(controller);
        if (!pending.current && admitted.version !== intent.original.userVersion) return { refused: 'version_conflict' };
        pending.current = intent;
        const response = await apiFetch('/me/mention-handle', { method: 'PATCH', signal: controller.signal,
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': intent.key, 'X-StrataAI-Expected-User': intent.original.userId }, body: intent.body });
        if (controller.signal.aborted) throw new Error('Handle request interrupted');
        if (response.status === 401) throw new SessionDenied();
        if ([400, 403, 404, 409, 429].includes(response.status)) {
          const problem = await response.json() as { code?: string }; return { refused: problem.code ?? 'refused' };
        }
        if (!response.ok) throw new Error('Handle change unconfirmed');
        const ack = parseMentionHandleAcknowledgment(await response.json(), intent);
        const fresh = await current(controller);
        return { ack, fresh };
      });
      if (!active()) return;
      pending.current = undefined; setUncertain(false);
      if ('refused' in result) {
        setSetting(undefined);
        const messages: Record<string, string> = {
          mention_handle_invalid: 'This handle cannot be chosen.',
          mention_handle_unavailable: 'This handle or original change is unavailable.',
          mention_handle_claim_refused: 'Your handle reservation limit has been reached. Choose a previously owned handle.',
          idempotency_key_expired: 'The original attempt has expired.',
          version_conflict: 'Your account changed elsewhere.',
        };
        setError(`${messages[result.refused ?? ''] ?? 'The handle change was refused.'} Review the current setting before saving again.`);
      } else {
        setSetting(result.fresh); setDraft(result.fresh.handle);
        if (result.fresh.handleVersion === result.ack.handleVersion && result.fresh.handle === result.ack.handle) setSaved(true);
        else setError('The original change was confirmed, then your handle changed elsewhere. The current setting is shown.');
      }
    } catch (failure) {
      if (active()) {
        if (failure instanceof SessionDenied) deny();
        else if (pending.current) { setUncertain(true); setError('Unable to confirm your handle change. Retry the original attempt to check whether it completed.'); }
        else { setSetting(undefined); setError('Unable to verify your account. Review the current setting before saving.'); }
      }
    } finally {
      if (operation.current === controller) operation.current = undefined;
      if (active()) setBusy(false);
    }
  }
  const foreignSetting = setting !== undefined && setting.userId !== subject;
  const foreignIntent = pending.current !== undefined && pending.current.original.userId !== subject;
  return <Dialog ref={dialog} open={open} onClose={() => { if (!busy && !pending.current) onClose(); }} fullWidth maxWidth="sm" aria-labelledby="mention-handle-title"
    slotProps={{ transition: { onEntered: () => field.current?.focus() } }}>
    <DialogTitle id="mention-handle-title">Your mention handle</DialogTitle>
    <DialogContent><Stack component="form" id="mention-handle-form" aria-label="Change mention handle" onSubmit={event => { event.preventDefault(); void save(); }} spacing={2} sx={{ pt: 1 }} aria-busy={busy}>
      <Typography>Your unique handle stays reserved to your account after you change it. You can return to a previously owned handle.</Typography>
      {error && <Alert severity="error">{error}</Alert>}
      {saved && <Alert severity="success" role="status">Handle change confirmed.</Alert>}
      {busy && <CircularProgress aria-label="Checking account handle" />}
      <TextField inputRef={field} label="Mention handle" value={foreignSetting || foreignIntent ? '' : draft} disabled={busy || uncertain || !setting}
        onChange={event => { setDraft(event.target.value); setSaved(false); }} slotProps={{ htmlInput: { maxLength: 40 } }}
        helperText="3–40 letters, digits or underscores. Start with a letter. Names are case insensitive." autoComplete="off" />
      {!setting && !uncertain && <Button disabled={busy} onClick={() => void load()}>Review current setting</Button>}
    </Stack></DialogContent>
    <DialogActions>
      <Button disabled={busy || uncertain} onClick={onClose}>Close</Button>
      <Button ref={retry} type="submit" form="mention-handle-form" variant="contained" disabled={busy || !setting && !uncertain}>{uncertain ? 'Retry original handle change' : 'Save handle'}</Button>
    </DialogActions>
  </Dialog>;
}
