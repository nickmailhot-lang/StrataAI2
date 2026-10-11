import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Alert, Button, Checkbox, FormControlLabel, Stack, TextField, Typography } from '@mui/material';
import { WorkInputError, WorkRequestError } from '../../api/workManagement';
import { readRetentionSourceReview, RetentionSourceClassificationIntent, type RetentionSourceKind,
  type RetentionSourceReceipt, type RetentionSourceReview } from './retentionSourceClient';
import { completeRetentionSourceClassification, forgetRetentionSourceClassifications,
  forgetRetentionSourceClassificationForSource, restoreRetentionSourceClassification, retainRetentionSourceClassification } from './retentionSourceRecovery';
import { ownsRecoveryFocus, parkRecoveryFocus } from '../kanban/focusRecovery';

export type RetentionSourceControlProps = {
  organizationId: string; actorId: string; kind: RetentionSourceKind; recordId: string;
  cardId: string; boardId: string; cardVersion: number; sourceVersion: number;
  authority: 'admitted' | 'uncertain' | 'denied';
  accessLost: (error: unknown) => void; completed: (receipt: RetentionSourceReceipt) => void;
};
// Composition into actual Card/Comment/Attachment context follows verification of
// both server providers and canonical runtime capabilities. No fabricated seed.
export function RetentionSourceControl(props: RetentionSourceControlProps) {
  return <SourceControl key={`${props.actorId}/${props.organizationId}/${props.kind}/${props.recordId}`} {...props} />;
}
function SourceControl(props: RetentionSourceControlProps) {
  const [review, setReview] = useState<RetentionSourceReview>();
  const [category, setCategory] = useState(''), [jurisdiction, setJurisdiction] = useState('');
  const [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false);
  const [intent, setIntent] = useState<RetentionSourceClassificationIntent>();
  const [blocked, setBlocked] = useState(false), [refused, setRefused] = useState(false);
  const [error, setError] = useState<string>(), [notice, setNotice] = useState<string>();
  const [categoryError, setCategoryError] = useState<string>();
  const [classifiedHere, setClassifiedHere] = useState(false);
  const controller = useRef<AbortController | undefined>(undefined), epoch = useRef(0);
  const loaded = useRef(false), alive = useRef(true), callbacks = useRef(props); callbacks.current = props;
  const refresh = useRef<HTMLButtonElement>(null), retry = useRef<HTMLButtonElement>(null), categoryInput = useRef<HTMLInputElement>(null);
  const success = useRef<HTMLDivElement>(null);
  const focusOwner = useRef<HTMLElement | null>(null), focusDialog = useRef<HTMLElement | null>(null);
  const focusNext = useRef<'category' | 'success' | 'retry' | undefined>(undefined);
  const stale = review && (review.cardVersion !== props.cardVersion || review.sourceVersion !== props.sourceVersion
    || review.boardId !== props.boardId || review.cardId !== props.cardId);
  useEffect(() => { alive.current = true; return () => { alive.current = false; ++epoch.current; controller.current?.abort(); }; }, []);
  useEffect(() => {
    if (busy || props.authority !== 'admitted' || !focusNext.current) return;
    const target = focusNext.current === 'category' ? categoryInput.current : focusNext.current === 'retry' ? retry.current : success.current;
    if (!target || 'disabled' in target && target.disabled) return;
    focusNext.current = undefined;
    if (ownsRecoveryFocus(document.activeElement, focusOwner.current, focusDialog.current)) target.focus({ preventScroll: true });
  }, [busy, review, intent, error, notice, props.authority]);
  useEffect(() => {
    if (props.authority !== 'admitted') {
      ++epoch.current; controller.current?.abort(); setBusy(false);
      if (props.authority === 'denied') {
        forgetRetentionSourceClassificationForSource(props.actorId, props.organizationId, props.kind, props.recordId);
        setReview(undefined); setCategory(''); setJurisdiction(''); setConfirmed(false); setIntent(undefined);
        setError(undefined); setNotice(undefined); setCategoryError(undefined); setBlocked(false); setRefused(false);
      }
      return;
    }
    if (loaded.current) return;
    loaded.current = true;
    try { setIntent(restoreRetentionSourceClassification(sessionStorage, props.actorId, props.organizationId)); }
    catch { setBlocked(true); setError('Original classification recovery is unavailable. New classifications are blocked.'); }
  }, [props.authority, props.actorId, props.organizationId]);
  function live(attempt: number) { return alive.current && epoch.current === attempt && callbacks.current.authority === 'admitted'; }
  function captureFocus() {
    focusOwner.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    focusDialog.current = focusOwner.current?.closest('[role="dialog"][data-mui-focusable]') ?? null;
    parkRecoveryFocus(focusOwner.current);
  }
  function failure(value: unknown, pending: boolean) {
    if (value instanceof WorkRequestError && [401, 403, 404].includes(value.status)) {
      if (value.status === 401) forgetRetentionSourceClassifications(props.actorId, props.organizationId);
      else {
        const source = pending && intent ? intent.reviewed().review : { kind: props.kind, recordId: props.recordId };
        forgetRetentionSourceClassificationForSource(props.actorId, props.organizationId, source.kind, source.recordId);
      }
      setBlocked(true);
      setReview(undefined); setCategory(''); setJurisdiction(''); setConfirmed(false); setIntent(undefined);
      setNotice(undefined); setError(undefined); callbacks.current.accessLost(value); return;
    }
    const definitive = value instanceof WorkRequestError && [400, 409].includes(value.status);
    setRefused(definitive && pending);
    setError(definitive ? 'The original submission was refused. Do not replace its retry key; review the source or contact support.'
      : pending ? 'The acknowledgment could not be confirmed. The original submission is retained for retry.'
        : 'Source evidence could not be confirmed. Retry the review.');
  }
  async function load() {
    if (busy || intent || blocked || classifiedHere || props.authority !== 'admitted') return;
    captureFocus();
    const active = new AbortController(), attempt = ++epoch.current; controller.current = active;
    setBusy(true); setError(undefined); setNotice(undefined); setConfirmed(false); setReview(undefined);
    try {
      const result = await readRetentionSourceReview(props.organizationId, props.kind, props.recordId, active.signal, props.actorId);
      if (!live(attempt)) return;
      const current = callbacks.current, evidence = result.review;
      if (evidence.cardId !== current.cardId || evidence.boardId !== current.boardId
        || evidence.cardVersion !== current.cardVersion || evidence.sourceVersion !== current.sourceVersion) {
        setError('The source changed during review. Refresh its context before confirming.'); return;
      }
      focusNext.current = 'category'; setReview(evidence);
    } catch (value) { if (live(attempt)) failure(value, false); }
    finally { if (alive.current && epoch.current === attempt) { controller.current = undefined; setBusy(false); } }
  }
  async function submit(event?: FormEvent) {
    event?.preventDefault();
    if (busy || blocked || refused || props.authority !== 'admitted' || !intent && (!review || stale || !confirmed)) return;
    captureFocus();
    let original = intent;
    try {
      original ??= RetentionSourceClassificationIntent.review(props.actorId, review!, category, jurisdiction || null, confirmed);
      // Verify storage before delivery; an uncertain original cannot be replaced.
      retainRetentionSourceClassification(sessionStorage, original);
    } catch (value) {
      if (original) {
        // Even a partially successful storage write keeps this attempt's key.
        // If another control already retained an original, recover that one.
        let retained = original;
        try { retained = restoreRetentionSourceClassification(sessionStorage, props.actorId, props.organizationId) ?? original; }
        catch { /* Preserve the unsent original until storage can be verified. */ }
        setIntent(retained); setReview(undefined); setCategory(''); setJurisdiction(''); setConfirmed(false);
        setError('Browser recovery could not be verified. No classification was sent. Retry the original after recovery is available.');
        return;
      }
      setCategoryError(value instanceof WorkInputError ? value.message : 'The original submission could not be retained.');
      categoryInput.current?.focus(); return;
    }
    const active = new AbortController(), attempt = ++epoch.current; controller.current = active;
    setIntent(original); setReview(undefined); setCategory(''); setJurisdiction(''); setConfirmed(false);
    setBusy(true); setError(undefined); setNotice(undefined); setCategoryError(undefined);
    try {
      const receipt = await original.submit(active.signal);
      if (!live(attempt)) return;
      completeRetentionSourceClassification(sessionStorage, original); setIntent(undefined);
      setClassifiedHere(receipt.reference.recordId === props.recordId && receipt.reference.kind === props.kind);
      setNotice('Classification confirmed. The original source and audit evidence are preserved.');
      callbacks.current.completed(receipt);
      focusNext.current = 'success';
    } catch (value) {
      if (live(attempt)) {
        failure(value, true); focusNext.current = 'retry';
      }
    } finally { if (alive.current && epoch.current === attempt) { controller.current = undefined; setBusy(false); } }
  }
  if (props.authority !== 'admitted') return <Alert severity="warning">
    {props.authority === 'denied' ? 'Source classification access is unavailable.' : 'Checking source classification access. Original work is retained.'}
  </Alert>;
  return <Stack component="section" aria-label="Source retention classification" spacing={2} sx={{ minWidth: 0 }}>
    <Typography variant="h6">Retention classification</Typography>
    {error && <Alert severity="warning">{error}</Alert>}
    {notice && <Alert ref={success} tabIndex={-1} severity="success" role="status">{notice}</Alert>}
    {intent ? <>
      <Typography>An original classification in this Organization needs acknowledgment recovery. Its source evidence and retry key are preserved.</Typography>
      <Button ref={retry} disabled={busy || refused} onClick={() => void submit()}>Retry original classification</Button>
      {refused && <Typography>Keep the original for support. A new submission must not replace it.</Typography>}
    </> : <>
      <Button ref={refresh} disabled={busy || blocked || classifiedHere} onClick={() => void load()}>Review source classification</Button>
      {review && <Stack component="form" spacing={2} onSubmit={event => void submit(event)}>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Source: {review.kind} · {review.recordId}</Typography>
        <Typography>Source revision {review.sourceVersion}; Card revision {review.cardVersion}; List revision {review.listVersion}.</Typography>
        <Typography>Source {review.sourceState}; Card {review.cardState}; List {review.listState}.</Typography>
        <Typography sx={{ overflowWrap: 'anywhere' }}>Reviewed Board: {review.boardId}. Created: {review.createdAt}.</Typography>
        <Typography>{review.kind === 'attachment' ? 'Document record' : 'Operational work record'}. Classification preserves the source; it does not dispose of it.</Typography>
        {stale && <Alert severity="warning">The source changed. Review its current revision before confirming. Your category draft is retained.</Alert>}
        <TextField inputRef={categoryInput} label="Record category" value={category} required disabled={busy || !!stale}
          error={!!categoryError} helperText={categoryError ?? 'Use 1–64 letters, numbers, periods, underscores or hyphens.'}
          onChange={event => { setCategory(event.target.value); setConfirmed(false); setCategoryError(undefined); }} />
        <TextField label="Jurisdiction (optional)" value={jurisdiction} disabled={busy || !!stale}
          helperText="Leave blank for Organization applicability; no jurisdiction is inferred."
          onChange={event => { setJurisdiction(event.target.value); setConfirmed(false); }} />
        <FormControlLabel control={<Checkbox checked={confirmed} disabled={busy || !!stale}
          onChange={event => setConfirmed(event.target.checked)} />} label="I reviewed the source evidence and confirm this classification" />
        <Button type="submit" disabled={busy || !!stale || !confirmed}>Confirm classification</Button>
      </Stack>}
    </>}
    {busy && <Typography role="status">Confirming current source evidence…</Typography>}
  </Stack>;
}
