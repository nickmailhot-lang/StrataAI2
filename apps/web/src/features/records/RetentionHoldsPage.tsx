import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, Container, FormControlLabel, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { RetentionHoldDirectory } from './RetentionHoldDirectory';
import { watchRetentionHold } from './retentionHoldLive';
import { WorkRequestError } from '../../api/workManagement';
import { readRetentionPolicies } from './retentionPolicyClient';
import { recordClasses } from './RetentionPolicyEditor';
import { type RetentionSourceKind, type RetentionSourceReview } from './retentionSourceClient';
import { forgetHoldIntent, forgetForeignHoldIntents, readHold, readHoldTimeline, restoreHoldIntent, retainHoldIntent, validateHoldIntent, reviewHoldSource, submitHoldIntent,
  type HoldClass, type HoldIntent, type RetentionHold, type HoldTimeline } from './retentionHoldClient';

export function RetentionHoldsPage() {
  const { organizationId } = useParams();
  return <HoldScreen key={organizationId} organizationId={organizationId?.toLowerCase() ?? ''} />;
}
function HoldScreen({ organizationId }: { organizationId: string }) {
  const checking = useRef(false);
  const [actor, setActor] = useState<string>(); const actorRef = useRef<string | undefined>(undefined);
  const [admitted, setAdmitted] = useState(false), [denied, setDenied] = useState(false), [busy, setBusy] = useState(false);
  const [scope, setScope] = useState('organization'), [recordClass, setClass] = useState<HoldClass>('document');
  const [category, setCategory] = useState(''), [recordId, setRecordId] = useState(''), [reason, setReason] = useState('');
  const [kind, setKind] = useState<RetentionSourceKind>('attachment'), [review, setReview] = useState<RetentionSourceReview>();
  const [holdId, setHoldId] = useState(''), [hold, setHold] = useState<RetentionHold>();
  const [timeline,setTimeline]=useState<HoldTimeline>();
  const [holdConfirmed,setHoldConfirmed]=useState(false),[liveNotice,setLiveNotice]=useState<string>();
  const currentHold=useRef<RetentionHold | undefined>(undefined); currentHold.current=hold;
  const [confirmed, setConfirmed] = useState(false), [releaseConfirmed, setReleaseConfirmed] = useState(false);
  const [intent, setIntent] = useState<HoldIntent>(), [refused, setRefused] = useState(false), [recoveryBlocked, setRecoveryBlocked] = useState(false);
  const [error, setError] = useState<string>(), [notice, setNotice] = useState<string>();
  const alive = useRef(true), active = useRef<AbortController | undefined>(undefined), generation = useRef(0);
  const heading = useRef<HTMLHeadingElement>(null);
  const withdraw = useCallback((lost: boolean) => {
    ++generation.current; active.current?.abort(); active.current = undefined; setBusy(false); setAdmitted(false); setReview(undefined); setHold(undefined); setNotice(undefined);
    setConfirmed(false); setReleaseConfirmed(false); setHoldConfirmed(false); setTimeline(undefined); setLiveNotice(undefined);
    if (lost) {
      if (actorRef.current) { try { forgetHoldIntent(actorRef.current, organizationId); } catch { /* Access remains withdrawn. */ } }
      setReason(''); setRecordId(''); setCategory(''); setHoldId(''); setIntent(undefined); setDenied(true);
      setError('Retention holds are no longer available with your current access.'); heading.current?.focus();
    }
  }, [organizationId]);
  const admit = useCallback(async () => {
    if (checking.current) return; checking.current = true;
    const controller = new AbortController();
    try {
      const result = await readRetentionPolicies(organizationId,controller.signal,actorRef.current,'ffffffff-ffff-ffff-ffff-ffffffffffff');
      if (!alive.current) return;
      if (!actorRef.current) {
        actorRef.current = result.actorId; setActor(result.actorId);
        try { forgetForeignHoldIntents(result.actorId); setIntent(restoreHoldIntent(result.actorId,organizationId)); }
        catch { setRecoveryBlocked(true); setError('Original hold recovery is unavailable. New commands are blocked.'); }
      }
      setAdmitted(true); setDenied(false);
    } catch (value) {
      if (!alive.current) return;
      withdraw(value instanceof WorkRequestError && [401,403,404].includes(value.status));
    } finally { checking.current = false; }
  }, [organizationId,withdraw]);
  useEffect(() => {
    alive.current = true; void admit();
    const timer = window.setInterval(() => { if (!active.current) void admit(); }, 5000);
    const focus = () => { if (!active.current) void admit(); }; window.addEventListener('focus',focus);
    return () => { alive.current = false; ++generation.current; active.current?.abort(); window.clearInterval(timer); window.removeEventListener('focus',focus); };
  }, [admit]);
  function failure(value: unknown, sent: boolean) {
    if (value instanceof WorkRequestError && [401,403,404].includes(value.status)) { withdraw(true); return; }
    const definite = value instanceof WorkRequestError && [400,409].includes(value.status);
    setRefused(sent && definite); setError(sent
      ? definite ? 'The original submission was refused. Review the current hold or source before starting a new request.'
        : 'The acknowledgment could not be confirmed. Retry the retained original request.'
      : 'The requested hold or source could not be confirmed. Check its identifier and retry.');
  }
  async function run(task: (signal: AbortSignal) => Promise<void>, sent = false) {
    if (busy || !admitted || !actor) return;
    const controller = new AbortController(), at = ++generation.current; active.current = controller;
    setBusy(true); setError(undefined); setNotice(undefined);
    try { await task(controller.signal); }
    catch (value) { if (alive.current && at === generation.current) failure(value,sent); }
    finally { if (alive.current && at === generation.current) { active.current = undefined; setBusy(false); } }
  }
  const watchedHoldId=hold?.id;
  useEffect(() => {
    if (!watchedHoldId || !actor || !admitted || intent) return;
    const source=currentHold.current!;
    return watchRetentionHold({source,actorId:actor,
      pending:()=>{setHoldConfirmed(false);setTimeline(undefined);setReleaseConfirmed(false);},
      confirmed:next=>{
        const previous=currentHold.current;
        if (!previous || previous.id!==watchedHoldId || next.version<previous.version) return;
        if (next.version!==previous.version) {setReleaseConfirmed(false);setTimeline(undefined);setLiveNotice('This hold changed. Its current status is shown.');}
        currentHold.current=next;setHold(next);setHoldConfirmed(true);
      },accessLost:()=>withdraw(true)});
  },[watchedHoldId,actor,admitted,intent,withdraw]);
  async function submit(original: HoldIntent) {
    try { original = validateHoldIntent(original,original.actorId,original.organizationId); }
    catch { setError('Provide a valid hold scope, category, reason and source review.'); return; }
    try { retainHoldIntent(original); setIntent(original); setHold(undefined); setHoldConfirmed(false); setReleaseConfirmed(false); setLiveNotice(undefined); }
    catch { setRecoveryBlocked(true); setError('The original request could not be retained. No request was sent.'); return; }
    await run(async signal => {
      const result = await submitHoldIntent(original,signal); signal.throwIfAborted(); if (!alive.current) return;
      forgetHoldIntent(original.actorId,organizationId); setIntent(undefined); setRefused(false);
      // A recovered apply receipt may precede a release. Read the current head.
      setHold(undefined); setHoldId(result.id); setConfirmed(false); setReleaseConfirmed(false); setReason(''); setReview(undefined);
      setNotice('Original request acknowledged. Load the current hold to review its present status.');
    },true);
  }
  const disabled = busy || !admitted || !!intent || recoveryBlocked;
  return <Container maxWidth="md"><Stack spacing={2} sx={{ py:3 }}>
    <Typography variant="h4" component="h1" ref={heading} tabIndex={-1}>Retention holds</Typography>
    <Button component={Link} to={`/app/${organizationId}`}>Back to Organization</Button>
    {error && <Alert severity="error">{error}</Alert>}{notice && <Alert severity="success" role="status">{notice}</Alert>}
    {!admitted && !denied && <Alert severity="info">Confirming current access. <Button onClick={() => void admit()}>Retry access</Button></Alert>}
    {!denied && <>
      {intent && <Paper variant="outlined" sx={{ p:2 }}><Stack spacing={1}>
        <Typography>Original {intent.operation} request awaiting acknowledgment</Typography>
        <Typography sx={{ overflowWrap:'anywhere' }}>Hold identifier: {intent.holdId}</Typography>
        <Button disabled={busy || !admitted || refused || recoveryBlocked} onClick={() => void submit(intent)}>Retry original request</Button>
        {refused && <Button disabled={busy || !admitted} onClick={() => {
          try { forgetHoldIntent(actor!,organizationId); setIntent(undefined); setRefused(false); setConfirmed(false); setReleaseConfirmed(false); setReview(undefined); setHold(undefined); }
          catch { setRecoveryBlocked(true); }
        }}>Dismiss refused request and review again</Button>}
      </Stack></Paper>}
      <Paper variant="outlined" sx={{ p:2 }}><Stack spacing={2}>
        <Typography variant="h6" component="h2">Apply a hold</Typography>
        <Typography>Active holds suspend ordinary disposition. Review the scope and explain why preservation is required.</Typography>
        <TextField select label="Hold scope" value={scope} disabled={disabled} onChange={e => { setScope(e.target.value); setReview(undefined); setConfirmed(false); }}>
          <MenuItem value="organization">Entire Organization</MenuItem><MenuItem value="class">Record class</MenuItem>
          <MenuItem value="category">Record category</MenuItem><MenuItem value="record">Individual classified record</MenuItem>
        </TextField>
        {(scope === 'class' || scope === 'category') && <TextField select label="Record class" value={recordClass} disabled={disabled} onChange={e => { setClass(e.target.value as HoldClass); setConfirmed(false); }}>
          {Object.entries(recordClasses).map(([value,label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
        </TextField>}
        {scope === 'category' && <TextField label="Category" value={category} disabled={disabled} onChange={e => { setCategory(e.target.value); setConfirmed(false); }} />}
        {scope === 'record' && <>
          <TextField select label="Source type" value={kind} disabled={disabled} onChange={e => { setKind(e.target.value as RetentionSourceKind); setReview(undefined); setConfirmed(false); }}>
            <MenuItem value="card">Card</MenuItem><MenuItem value="cardComment">Comment</MenuItem><MenuItem value="attachment">Attachment</MenuItem>
          </TextField>
          <TextField label="Classified record identifier" value={recordId} disabled={disabled} onChange={e => { setRecordId(e.target.value); setReview(undefined); setConfirmed(false); }} />
          <Button disabled={disabled || !recordId} onClick={() => void run(async signal => {
            const result = await reviewHoldSource(organizationId,actor!,kind,recordId.toLowerCase(),signal);
            signal.throwIfAborted(); if (alive.current) setReview(result);
          })}>Review classified source</Button>
          {review && <Alert severity="info" sx={{ overflowWrap:'anywhere' }}>Reviewed {review.kind}: {review.recordId}. Board: {review.boardId}. Source version {review.sourceVersion}; Card version {review.cardVersion}.</Alert>}
        </>}
        <TextField label="Preservation reason" multiline minRows={2} value={reason} disabled={disabled} onChange={e => { setReason(e.target.value); setConfirmed(false); }} />
        <FormControlLabel control={<Checkbox checked={confirmed} disabled={disabled || scope === 'record' && !review} onChange={e => setConfirmed(e.target.checked)} />} label="I reviewed this scope and confirm preservation under this hold." />
        <Button disabled={disabled || !confirmed || !reason.trim() || scope === 'record' && !review} onClick={() => void submit({ actorId:actor!,organizationId,
          holdId:crypto.randomUUID(),key:crypto.randomUUID(),operation:'apply',body:{ recordClass:scope === 'organization' ? null : scope === 'record' ? kind === 'attachment' ? 'document' : 'operationalWork' : recordClass,
            category:scope === 'category' ? category : null,recordId:scope === 'record' ? recordId.toLowerCase() : null,reason:reason.trim(),review:scope === 'record' ? review! : null,confirmed:true } })}>Apply confirmed hold</Button>
      </Stack></Paper>
      {actor && <RetentionHoldDirectory key={`${organizationId}:${actor}`} organizationId={organizationId} actorId={actor}
        enabled={admitted && (!hold || holdConfirmed) && !intent && !recoveryBlocked} accessLost={()=>withdraw(true)} select={selected=>{
          setHoldId(selected.id);setHoldConfirmed(false);setTimeline(undefined);setLiveNotice(undefined);setReleaseConfirmed(false);setHold(selected);
        }}/>}
      <Paper variant="outlined" sx={{ p:2 }}><Stack spacing={2}>
        <Typography variant="h6" component="h2">Review or release a hold</Typography>
        <TextField label="Hold identifier" value={holdId} disabled={disabled} onChange={e => { setHoldId(e.target.value); setHold(undefined); setHoldConfirmed(false); setTimeline(undefined); setLiveNotice(undefined); setReleaseConfirmed(false); }} />
        <Button disabled={disabled || !holdId} onClick={() => void run(async signal => {
          const result = await readHold(organizationId,holdId.toLowerCase(),actor!,signal); signal.throwIfAborted();
          if (alive.current) { setHoldConfirmed(false); setTimeline(undefined); setLiveNotice(undefined); setHold(result); setReleaseConfirmed(false); }
        })}>Load current hold</Button>
        {liveNotice && <Alert severity="info" role="status">{liveNotice}</Alert>}
        {hold && !holdConfirmed && <Alert severity="info">Confirming current hold status. Release is unavailable until its authority is confirmed.</Alert>}
        {hold && holdConfirmed && <>
          <Typography>Status: {hold.status}. Version: {hold.version}.</Typography>
          <Button disabled={disabled} onClick={() => void run(async signal=>{
            const result=await readHoldTimeline(organizationId,hold.id,actor!,signal);signal.throwIfAborted();
            if (alive.current && currentHold.current?.id===hold.id && result.current.version>=currentHold.current.version) {
              setHold(result.current);setTimeline(result);setReleaseConfirmed(false);
            }
          })}>View hold history</Button>
          {timeline && <Stack spacing={1} component="section" aria-label="Retained hold history">
            <Typography variant="h6" component="h3">Retained hold history</Typography>
            {timeline.revisions.map(row=><Typography key={row.eventId} sx={{overflowWrap:'anywhere'}}>
              {row.hold.status==='active'?'Applied':'Released'} by {row.actorLabel} as Organization {row.actingCapacity===0?'Owner':'Administrator'} · {row.createdAt}
            </Typography>)}
          </Stack>}
          <Typography>Scope: {hold.recordId ?? hold.category ?? (hold.recordClass ? recordClasses[hold.recordClass] : 'Entire Organization')}</Typography>
          <Typography sx={{ whiteSpace:'pre-wrap',overflowWrap:'anywhere' }}>Reason: {hold.reason}</Typography>
          {hold.status === 'active' && <>
            <FormControlLabel control={<Checkbox checked={releaseConfirmed} disabled={disabled} onChange={e => setReleaseConfirmed(e.target.checked)} />} label="I reviewed this version and confirm release of this hold." />
            <Button disabled={disabled || !releaseConfirmed} onClick={() => void submit({ actorId:actor!,organizationId,holdId:hold.id,key:crypto.randomUUID(),operation:'release',body:{version:hold.version,confirmed:true} })}>Release confirmed hold</Button>
          </>}
        </>}
      </Stack></Paper>
    </>}
  </Stack></Container>;
}
