import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { WorkInputError, WorkRequestError } from '../../api/workManagement';
import { parseRetentionPolicyInput, readRetentionPolicies, readRetentionPolicy, RetentionPolicyChangeIntent,
  type RetentionPolicy, type RetentionPolicyInput } from './retentionPolicyClient';
import { completeRetentionPolicyChange, forgetForeignRetentionPolicyChanges, forgetRetentionPolicyChanges,
  restoreRetentionPolicyChange, retainRetentionPolicyChange } from './retentionPolicyRecovery';

export const recordClasses = { operationalWork: 'Operational work', governance: 'Governance', audit: 'Audit', correspondence: 'Correspondence', document: 'Document', ai: 'AI' };
const anchors = { created: 'creation', archived: 'archive', closed: 'closure', approved: 'approval' };
const actions = { preserve: 'Preserve', delete: 'Delete when eligible', anonymize: 'Anonymize when eligible' };
type Draft = { recordClass: string; category: string; jurisdiction: string; anchor: string; retentionDays: string; action: string; source: string; notes: string };
const empty = (): Draft => ({ recordClass: '', category: '', jurisdiction: '', anchor: '', retentionDays: '', action: '', source: '', notes: '' });
function draftOf(input: RetentionPolicyInput): Draft {
  return { recordClass: input.recordClass, category: input.category, jurisdiction: input.jurisdiction ?? '',
    anchor: input.anchor, retentionDays: input.retentionDays?.toString() ?? '', action: input.action, source: input.source, notes: input.notes ?? '' };
}
export function PolicyRule({ policy }: { policy: RetentionPolicyInput }) {
  return <Stack spacing={1}>
    <Typography>{recordClasses[policy.recordClass]} · {policy.category}</Typography>
    <Typography>{policy.jurisdiction ?? 'Organization-wide applicability'}</Typography>
    <Typography>{policy.retentionDays === null ? 'Indefinite preservation' : `${policy.retentionDays} elapsed UTC days after ${anchors[policy.anchor]}`} · {actions[policy.action]}</Typography>
    <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>Source: {policy.source}</Typography>
    {policy.notes && <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>Notes: {policy.notes}</Typography>}
  </Stack>;
}
function errorsFor(draft: Draft): Partial<Record<keyof Draft, string>> {
  const errors: Partial<Record<keyof Draft, string>> = {};
  if (!(draft.recordClass in recordClasses)) errors.recordClass = 'Choose a record class.';
  if (!/^[A-Za-z0-9_.-]{1,64}$/.test(draft.category)) errors.category = 'Use 1–64 letters, numbers, dots, underscores or hyphens.';
  if (!(draft.anchor in anchors)) errors.anchor = 'Choose the record clock that starts the period.';
  if (!(draft.action in actions)) errors.action = 'Choose the disposition action.';
  if (draft.retentionDays === '' ? draft.action !== 'preserve' : !/^\d+$/.test(draft.retentionDays) || Number(draft.retentionDays) > 3_652_500)
    errors.retentionDays = 'Enter 0–3,652,500 whole days. Blank days require Preserve.';
  const validText = (value: string, max: number, multiline: boolean) => value.length <= max && value.trim() === value
    && !Array.from(value).some(character => { const code = character.codePointAt(0)!;
      return (code < 32 || code >= 127 && code <= 159) && !(multiline && ['\r', '\n', '\t'].includes(character)); });
  if (!validText(draft.jurisdiction, 120, false)) errors.jurisdiction = 'Use up to 120 characters without surrounding spaces or controls, or leave blank.';
  if (!draft.source || !validText(draft.source, 1000, true)) errors.source = 'Provide a reviewed source, up to 1,000 characters, without surrounding spaces or unsupported controls.';
  if (!validText(draft.notes, 2000, true)) errors.notes = 'Use up to 2,000 characters without surrounding spaces or unsupported controls, or leave blank.';
  return errors;
}
function inputOf(draft: Draft) {
  return parseRetentionPolicyInput({ ...draft, retentionDays: draft.retentionDays === '' ? null : Number(draft.retentionDays),
    jurisdiction: draft.jurisdiction || null, notes: draft.notes || null });
}
export type PolicyEditorCommand = { sequence: number; policy: RetentionPolicy | null };
export function RetentionPolicyEditor({ organizationId, actorId, admitted, denied, command, completed, accessLost }: {
  organizationId: string; actorId: string; admitted: boolean; denied: boolean; command: PolicyEditorCommand | null;
  completed: () => void; accessLost: (error: unknown) => void;
}) {
  const [open, setOpen] = useState(false); const [draft, setDraft] = useState<Draft>(); const [base, setBase] = useState<RetentionPolicy>();
  const [intent, setIntent] = useState<RetentionPolicyChangeIntent>(); const [pending, setPending] = useState(false);
  const [review, setReview] = useState(false); const [checked, setChecked] = useState(false); const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>(); const [refusal, setRefusal] = useState(false);
  const [fields, setFields] = useState<Partial<Record<keyof Draft, string>>>({});
  const recoveryLoaded = useRef(false); const seen = useRef<number | undefined>(undefined); const controller = useRef<AbortController | null>(null);
  const epoch = useRef(0); const form = useRef<HTMLFormElement | null>(null);
  const currentDraft = useRef(draft); currentDraft.current = draft;
  useEffect(() => () => { ++epoch.current; controller.current?.abort(); }, []);
  useEffect(() => {
    if (admitted) return;
    ++epoch.current; controller.current?.abort(); setBusy(false);
    if (denied) {
      forgetRetentionPolicyChanges(actorId, organizationId); setOpen(false); setDraft(undefined); setBase(undefined);
      setIntent(undefined); setPending(false); setReview(false); setChecked(false); setError(undefined); setNotice(undefined); setFields({});
    }
  }, [admitted, denied, actorId, organizationId]);
  const recover = useCallback(() => {
    try {
      forgetForeignRetentionPolicyChanges(sessionStorage, actorId);
      const original = restoreRetentionPolicyChange(sessionStorage, actorId, organizationId);
      if (original) { setIntent(original); setDraft(draftOf(original.reviewedPolicy())); setPending(true); setReview(true); setOpen(true); }
      setError(undefined);
    } catch { setError('The original policy submission could not be restored. New changes are blocked until recovery is available.'); }
  }, [actorId, organizationId]);
  useEffect(() => {
    if (!admitted || recoveryLoaded.current) return;
    recoveryLoaded.current = true; recover();
  }, [admitted, recover]);
  useEffect(() => {
    if (!admitted || !command || seen.current === command.sequence) return;
    seen.current = command.sequence;
    // Never replace private unsent work or an unresolved original with a new target.
    if (currentDraft.current) { setOpen(true); return; }
    try {
      if (restoreRetentionPolicyChange(sessionStorage, actorId, organizationId)) {
        recover(); return;
      }
      setBase(command.policy ?? undefined); setDraft(command.policy ? draftOf(command.policy) : empty());
      setIntent(undefined); setReview(false); setChecked(false); setFields({}); setError(undefined); setNotice(undefined); setRefusal(false); setOpen(true);
    } catch { setError('Policy recovery is unavailable. No new change was started.'); }
  }, [command, admitted, actorId, organizationId, recover]);
  function fail(value: unknown) {
    if (value instanceof WorkRequestError && [401, 403, 404].includes(value.status)) { accessLost(value); return; }
    const definitive = value instanceof WorkRequestError && [400, 409].includes(value.status); setRefusal(definitive);
    setError(definitive ? value.code === 'retention_policy_scope_unavailable'
      ? 'A policy already reserves this applicability. Review current policies before choosing another scope or editing the existing policy.'
      : 'This submission was refused. Review the current policy and your preserved draft before approving a new change.'
      : 'The acknowledgment could not be confirmed. Your original submission is retained. Retry it with the same reviewed values.');
  }
  function beginReview(event: FormEvent) {
    event.preventDefault(); if (!draft || !admitted || busy || pending) return;
    const errors = errorsFor(draft); setFields(errors); setError(undefined); setNotice(undefined);
    if (Object.keys(errors).length) { queueMicrotask(() => form.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()); return; }
    try { setIntent(RetentionPolicyChangeIntent.review(organizationId, actorId, base?.id ?? crypto.randomUUID(), base?.version ?? 0, inputOf(draft)));
      setChecked(false); setReview(true);
    } catch { setError('The draft could not be reviewed. Check the policy fields.'); }
  }
  async function submit() {
    if (!intent || !admitted || busy || !pending && !checked || refusal) return;
    const original = intent; const active = new AbortController(); controller.current?.abort(); controller.current = active;
    const attempt = ++epoch.current; setBusy(true); setError(undefined);
    try {
      // Persist and verify before any network delivery can become uncertain.
      setPending(true); retainRetentionPolicyChange(sessionStorage, original);
      await original.submit(active.signal);
      if (active.signal.aborted || attempt !== epoch.current) return;
      completeRetentionPolicyChange(sessionStorage, original);
      setIntent(undefined); setPending(false); setDraft(undefined); setBase(undefined); setOpen(false); setReview(false); setChecked(false);
      setNotice('The original policy change was acknowledged. Refreshing current policies.'); completed();
    } catch (value) {
      if (!active.signal.aborted && attempt === epoch.current) {
        if (value instanceof WorkInputError) setError('Browser recovery could not be confirmed. The original submission is preserved; retry recovery before another change.');
        else fail(value);
      }
    } finally { if (!active.signal.aborted && attempt === epoch.current) setBusy(false); }
  }
  async function reconcile() {
    if (!intent || !pending || !refusal || !admitted || busy) return;
    const original = intent; const active = new AbortController(); controller.current?.abort(); controller.current = active;
    const attempt = ++epoch.current; setBusy(true); setError(undefined);
    try {
      const current = original.expectedVersion > 0
        ? await readRetentionPolicy(organizationId, actorId, original.policyId, active.signal)
        : (await readRetentionPolicies(organizationId, active.signal, actorId), undefined);
      if (active.signal.aborted || attempt !== epoch.current) return;
      const reviewed = original.reviewedPolicy();
      if (current && (current.recordClass !== reviewed.recordClass || current.category.toUpperCase() !== reviewed.category.toUpperCase()
        || current.jurisdiction !== reviewed.jurisdiction)) throw new WorkRequestError(503, null);
      completeRetentionPolicyChange(sessionStorage, original);
      setBase(current); setIntent(undefined); setPending(false); setReview(false); setChecked(false); setRefusal(false);
      setNotice(current ? `Current revision ${current.version} loaded. Your draft is preserved. Compare both before reviewing a new change.`
        : 'Current policy access confirmed. Your draft is preserved. Choose an available applicability or return to edit an existing policy.');
    } catch (value) {
      if (!active.signal.aborted && attempt === epoch.current) {
        if (value instanceof WorkRequestError && [401, 403, 404].includes(value.status)) accessLost(value);
        else setError('Current policy and recovery could not be confirmed. Your original submission and draft are retained. Retry this review when available.');
      }
    }
    finally { if (!active.signal.aborted && attempt === epoch.current) setBusy(false); }
  }
  const change = (field: keyof Draft, value: string) => { if (draft && !busy && !pending) setDraft({ ...draft, [field]: value }); };
  const field = (name: keyof Draft, label: string, extra: Record<string, unknown> = {}) => <TextField key={name} label={label}
    value={draft?.[name] ?? ''} onChange={event => change(name, event.target.value)} error={!!fields[name]} helperText={fields[name]}
    fullWidth disabled={busy || pending || !!base && ['recordClass', 'category', 'jurisdiction'].includes(name)} {...extra} />;
  return <>
    {admitted && !denied && notice && <Alert severity="success" role="status">{notice}</Alert>}
    {!denied && !open && error && <Alert severity="error">{error}</Alert>}
    {!draft && error && <Button disabled={!admitted} onClick={recover}>Retry policy recovery</Button>}
    {draft && !open && <Button disabled={!admitted} onClick={() => setOpen(true)}>{pending ? 'Recover original policy submission' : 'Resume policy draft'}</Button>}
    {draft && !admitted && !denied && <Alert severity="info" role="status">Your policy work is retained while current access is checked. Retry the policy read to continue.</Alert>}
    <Dialog open={open && admitted && !denied} onClose={() => { if (!busy) setOpen(false); }} fullWidth maxWidth="sm" transitionDuration={0}
      aria-labelledby="retention-editor-title" aria-describedby="retention-editor-description">
      {admitted && !denied && <>
        <DialogTitle id="retention-editor-title">{review ? pending ? 'Recover original policy submission' : 'Review policy change' : base ? 'Edit retention policy' : 'Create retention policy'}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ pt: 1 }}>
            <Typography id="retention-editor-description">A policy records an explicit rule and reviewed source. It does not authorize disposition or override legal holds or governance obligations.</Typography>
            {error && <Alert severity="error">{error}</Alert>}
            {notice && <Alert severity="info">{notice}</Alert>}
            {busy && <Typography role="status">Confirming policy submission</Typography>}
            {review && intent ? <>
              <Typography>{intent.expectedVersion === 0 ? 'New policy' : `Reviewed revision ${intent.expectedVersion}`} → Revision {intent.expectedVersion + 1}</Typography>
              <PolicyRule policy={intent.reviewedPolicy()} />
              <Alert severity="warning" role="note">This records a governance-significant policy change. Enforcement and automatic disposition are not available yet.</Alert>
              {pending ? <Typography>Retry recovers only the original reviewed change. A newer policy read cannot acknowledge it.</Typography>
                : <FormControlLabel control={<Checkbox checked={checked} onChange={event => setChecked(event.target.checked)} />} label="I reviewed the source and applicability." />}
            </> : draft && <>
              {base && <Box component="section" aria-label="Current policy for comparison"><Typography component="h3">Current revision {base.version}</Typography><PolicyRule policy={base} /></Box>}
              <Box component="form" ref={form} aria-label="Retention policy draft" onSubmit={beginReview} noValidate>
                <Stack spacing={2}>
                  {field('recordClass', 'Record class', { select: true, children: [<MenuItem key="empty" value="">Choose a class</MenuItem>, ...Object.entries(recordClasses).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)] })}
                  {field('category', 'Category')}
                  {field('jurisdiction', 'Jurisdiction (blank = Organization-wide)')}
                  {base && <Typography>Applicability is fixed for this retained identity. Create a separate policy for a different scope.</Typography>}
                  {field('anchor', 'Period starts at', { select: true, children: [<MenuItem key="empty" value="">Choose a clock</MenuItem>, ...Object.entries(anchors).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)] })}
                  {field('retentionDays', 'Elapsed UTC days (blank = indefinite preservation)', { slotProps: { htmlInput: { inputMode: 'numeric' } } })}
                  {field('action', 'Disposition action', { select: true, children: [<MenuItem key="empty" value="">Choose an action</MenuItem>, ...Object.entries(actions).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)] })}
                  {field('source', 'Reviewed source', { multiline: true, minRows: 2 })}
                  {field('notes', 'Notes (optional)', { multiline: true, minRows: 2 })}
                  <Button type="submit" disabled={busy || pending}>Review policy change</Button>
                </Stack>
              </Box>
            </>}
          </Stack>
        </DialogContent>
        <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
          {review && !pending && <Button disabled={busy} onClick={() => { setReview(false); setIntent(undefined); setChecked(false); }}>Back to draft</Button>}
          {review && !refusal && <Button disabled={busy || !pending && !checked} onClick={() => void submit()}>{pending ? 'Retry original submission' : 'Confirm policy change'}</Button>}
          {pending && refusal && <Button disabled={busy} onClick={() => void reconcile()}>Review current policy</Button>}
          {!pending && <Button disabled={busy} onClick={() => { setDraft(undefined); setBase(undefined); setIntent(undefined); setReview(false); setFields({}); setError(undefined); setOpen(false); }}>Discard draft</Button>}
          <Button disabled={busy} onClick={() => setOpen(false)}>Close and retain work</Button>
        </DialogActions>
      </>}
    </Dialog>
  </>;
}
