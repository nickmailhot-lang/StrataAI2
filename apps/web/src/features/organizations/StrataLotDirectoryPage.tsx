import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Box, Button, Container, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { Link, useBlocker, useParams } from 'react-router-dom';
import { WorkInputError, WorkRequestError } from '../../api/workManagement';
import { formatUserDateTime } from '../auth/userDateTime';
import { watchOrganizationMetadata } from './organizationMetadataLive';
import { emptyLot, forgetLotChanges, lotErrors, lotFields, LotIntent, readLot, readLotHistory, readLots, recoveryKey, retainLotIntent,
  type Lot, type LotData, type LotRevision } from './strataLotClient';

const denied = (error: unknown) => error instanceof WorkRequestError && [401, 403, 404].includes(error.status);
function message(error: unknown) {
  if (error instanceof WorkInputError) return error.message;
  if (error instanceof WorkRequestError) {
    if (error.code === 'strata_lot_identifier_unavailable') return 'This unit or lot identifier is already in use. Review your draft.';
    if (error.code === 'version_conflict') return 'This unit changed elsewhere. Load its current revision and review your draft before saving again.';
    if (error.code === 'idempotency_key_expired' || error.code === 'idempotency_key_conflict') return 'The original change was refused. Load the current unit before reviewing another change.';
    if (error.code?.startsWith('invalid_strata_lot_')) return 'Check the unit fields and review the change again.';
  }
  return 'The result could not be confirmed. Your draft and original submission are preserved. Retry the original change to recover its acknowledgment.';
}
function Values({ data }: { data: LotData }) {
  return <Box component="dl" sx={{ m: 0, overflowWrap: 'anywhere' }}>{lotFields.map(([key, label]) =>
    <Box key={key} sx={{ mb: 1 }}><Typography component="dt" sx={{ fontWeight: 600 }}>{label}</Typography><Typography component="dd" sx={{ m: 0 }}>{data[key] ?? 'Not recorded'}</Typography></Box>)}
    <Typography component="dt" sx={{ fontWeight: 600 }}>Status</Typography><Typography component="dd" sx={{ m: 0 }}>{data.status === 'ACTIVE' ? 'Active' : 'Inactive'}</Typography></Box>;
}
export function StrataLotDirectoryPage() {
  const { organizationId = '' } = useParams();
  return <Directory key={organizationId.toLowerCase()} organization={organizationId.toLowerCase()} />;
}
function Directory({ organization }: { organization: string }) {
  const [page, setPage] = useState<{ items: Lot[]; nextCursor: string | null; preferences: { locale: string; timezone: string } }>();
  const [cursor, setCursor] = useState<string>();
  const [actor, setActor] = useState<string>(); const actorRef = useRef<string | undefined>(undefined);
  const [ready, setReady] = useState(false); const [busy, setBusy] = useState(false);
  const [unavailable, setUnavailable] = useState(false); const [failure, setFailure] = useState<string>(); const [notice, setNotice] = useState<string>();
  const [editor, setEditor] = useState<{ id: string; source?: Lot; draft: LotData }>();
  const [original, setOriginal] = useState<LotIntent>(); const [review, setReview] = useState(false);
  const [errors, setErrors] = useState<ReturnType<typeof lotErrors>>({}); const [conflict, setConflict] = useState(false);
  const [needsCurrent, setNeedsCurrent] = useState(false);
  const [history, setHistory] = useState<{ items: LotRevision[]; nextBeforeVersion: number | null }>();
  const [discard, setDiscard] = useState(false);
  const operation = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const refreshQueued = useRef(false);
  const focusAfterCheck = useRef<{ id: string; owner: Element } | undefined>(undefined);
  function rememberFocus() {
    const active = document.activeElement;
    if (!focusAfterCheck.current && active?.id.startsWith('unit-')) focusAfterCheck.current = { id: active.id, owner: active };
  }
  const opening = useRef<string | undefined>(undefined);
  const owner = useRef<HTMLElement | null>(null); const heading = useRef<HTMLHeadingElement>(null);
  const editorHeading = useRef<HTMLHeadingElement>(null);
  const reviewCancel = useRef<HTMLButtonElement>(null); const leaveCancel = useRef<HTMLButtonElement>(null);
  const focusSafeDefault = (button: HTMLButtonElement | null) => {
    // MUI's dialog paper may take focus after native button autofocus. Move it
    // only from that paper, never from a control the participant already chose.
    if (button && document.activeElement === button.closest('[role="dialog"]')) button.focus();
  };
  const latest = useRef({ cursor, editor, original, busy }); latest.current = { cursor, editor, original, busy };
  const dirty = !!original || !!editor && (!editor.source || JSON.stringify(editor.draft) !== JSON.stringify(editor.source.data));
  const blocker = useBlocker(dirty);
  function withdraw() {
    try { if (actorRef.current) sessionStorage.removeItem(recoveryKey(organization, actorRef.current)); } catch { /* Withdrawal never depends on storage. */ }
    setPage(undefined); setEditor(undefined); setOriginal(undefined); setHistory(undefined); setReview(false);
    opening.current = undefined;
    focusAfterCheck.current = undefined;
    setReady(false); setUnavailable(true); setFailure(undefined); setNotice(undefined); setActor(undefined);
  }
  function fail(reason: unknown) {
    if (denied(reason)) withdraw();
    else { setFailure(message(reason)); if (reason instanceof WorkRequestError && reason.correlationId) setNotice(`Support reference: ${reason.correlationId}`); }
  }
  const load = useCallback(async (after?: string, automatic = false) => {
    if (automatic && latest.current.busy) return;
    operation.current?.abort(); const controller = new AbortController(); operation.current = controller;
    setBusy(true); if (!automatic) { rememberFocus(); setReady(false); }
    try {
      const result = await readLots(organization, controller.signal, actorRef.current, after);
      const selected = latest.current.editor;
      const selectedId = selected?.source?.id ?? (!selected ? opening.current : undefined);
      const current = selectedId ? await readLot(organization, result.actorId, selectedId, controller.signal) : undefined;
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      forgetLotChanges(undefined, result.actorId);
      const encoded = sessionStorage.getItem(recoveryKey(organization, result.actorId));
      const restored = encoded === null ? undefined : LotIntent.restore(encoded);
      if (restored && (restored.organization !== organization || restored.actorId !== result.actorId)) throw new WorkInputError('The saved unit submission has an invalid context.');
      actorRef.current = result.actorId; setActor(result.actorId); setPage(result); setCursor(after);
      setReady(true); setUnavailable(false); setFailure(undefined);
      setNeedsCurrent(false);
      if (restored && !latest.current.original) {
        setOriginal(restored); setEditor({ id: restored.lotId, draft: { ...restored.data } });
        setNotice('An original unit change needs recovery. Retry it explicitly; a fresh read does not acknowledge it.');
      }
      if (!selected && current && opening.current === current.id && !restored) {
        opening.current = undefined; setEditor({ id: current.id, source: current, draft: { ...current.data } });
      }
      if (selected?.source && current && current.id === selected.id && !latest.current.original && current.version !== selected.source.version) {
        setEditor({ ...selected, source: current }); setConflict(true); setHistory(undefined);
        setReview(false);
        setNotice('This unit changed elsewhere. Compare the current values with your preserved draft.');
      }
    } catch (reason) {
      if (mounted.current && !controller.signal.aborted && operation.current === controller) { setReady(false); setHistory(undefined); fail(reason); }
    } finally {
      if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); }
    }
    // Route-keyed controller and refs fence late responses without rebasing drafts.
  }, [organization]);
  useEffect(() => {
    mounted.current = true; void load();
    const foreground = () => { if (document.visibilityState === 'visible') void load(latest.current.cursor, true); };
    const timer = setInterval(foreground, 15_000);
    window.addEventListener('focus', foreground); document.addEventListener('visibilitychange', foreground);
    return () => { mounted.current = false; operation.current?.abort(); clearInterval(timer); window.removeEventListener('focus', foreground); document.removeEventListener('visibilitychange', foreground); };
  }, [load]);
  useEffect(() => {
    if (!actor) return;
    const recheck = () => {
      rememberFocus();
      setReady(false);
      // Never cancel a submitted command to start a read. Its original receipt
      // and final server admission must settle before the queued recheck.
      if (operation.current || latest.current.busy) refreshQueued.current = true;
      else void load(latest.current.cursor);
    };
    return watchOrganizationMetadata({ organizationId: organization, userId: actor, invalidate: recheck, reset: recheck, unavailable: recheck });
  }, [organization, actor, load]);
  useEffect(() => {
    if (!busy && refreshQueued.current) { refreshQueued.current = false; void load(latest.current.cursor); }
  }, [busy, load]);
  useLayoutEffect(() => {
    if (!ready || busy || !focusAfterCheck.current) return;
    const pending = focusAfterCheck.current; focusAfterCheck.current = undefined;
    const target = document.getElementById(pending.id);
    if (target && !target.matches(':disabled') && (document.activeElement === document.body || document.activeElement === pending.owner)) target.focus({ preventScroll: true });
  }, [ready, busy, history]);
  useEffect(() => {
    if (!dirty) return;
    const leaving = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', leaving); return () => window.removeEventListener('beforeunload', leaving);
  }, [dirty]);
  async function open(lot?: Lot) {
    if (!ready || busy || editor || original) return;
    owner.current = document.activeElement as HTMLElement; setFailure(undefined); setNotice(undefined); setHistory(undefined); setConflict(false); setErrors({});
    if (!lot) { opening.current = undefined; setEditor({ id: crypto.randomUUID(), draft: { ...emptyLot } }); return; }
    opening.current = lot.id;
    const controller = new AbortController(); operation.current = controller; setBusy(true);
    try {
      const source = await readLot(organization, actorRef.current!, lot.id, controller.signal);
      if (mounted.current && !controller.signal.aborted) { opening.current = undefined; setEditor({ id: source.id, source, draft: { ...source.data } }); }
    } catch (reason) { if (!controller.signal.aborted && mounted.current) { opening.current = undefined; fail(reason); } }
    finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  useEffect(() => { if (editor) editorHeading.current?.focus(); }, [editor?.id]);
  function close() {
    opening.current = undefined;
    setEditor(undefined); setHistory(undefined); setFailure(undefined); setConflict(false); setDiscard(false);
    const target = owner.current; requestAnimationFrame(() => { if (target?.isConnected) target.focus(); else heading.current?.focus(); });
  }
  async function submit() {
    if (!ready || busy || !editor || !actorRef.current) return;
    let intent = original;
    try {
      intent ??= new LotIntent(organization, actorRef.current, editor.id, editor.source?.version ?? 0, editor.draft);
      retainLotIntent(intent);
    } catch (reason) { fail(reason); setReview(false); return; }
    setOriginal(intent); setReview(false); setBusy(true); setFailure(undefined);
    const controller = new AbortController(); operation.current = controller;
    try {
      await intent.submit(controller.signal);
      if (!mounted.current || controller.signal.aborted) return;
      // Remove only our exact original, and only after its acknowledgment.
      const key = recoveryKey(organization, intent.actorId);
      if (sessionStorage.getItem(key) !== JSON.stringify(intent)) throw new WorkInputError('Another original submission needs review.');
      sessionStorage.removeItem(key); setOriginal(undefined); latest.current.original = undefined;
      setReady(false);
      const current = await readLot(organization, intent.actorId, intent.lotId, controller.signal);
      if (!mounted.current || controller.signal.aborted) return;
      setEditor({ id: current.id, source: current, draft: { ...current.data } }); setHistory(undefined); setConflict(false);
      setReady(true);
      setNotice(`Change acknowledged. Current unit revision ${current.version}.`); editorHeading.current?.focus();
    } catch (reason) {
      if (!mounted.current || controller.signal.aborted) return;
      if (reason instanceof WorkRequestError && [400, 409].includes(reason.status)) {
        sessionStorage.removeItem(recoveryKey(organization, intent.actorId)); setOriginal(undefined); setConflict(true);
        setNeedsCurrent(true);
      }
      fail(reason);
    } finally {
      if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  async function showHistory(before?: number) {
    if (!ready || busy || !editor?.source || original) return;
    const focusOwner = document.activeElement;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setFailure(undefined);
    try {
      const result = await readLotHistory(organization, actorRef.current!, editor.id, controller.signal, before);
      if (mounted.current && !controller.signal.aborted) {
        // Authority may retire the section before the next paint. Keep this
        // owned request until readmission; a later navigation choice wins.
        if (focusOwner) focusAfterCheck.current = { id: 'unit-history-title', owner: focusOwner };
        setHistory(result);
      }
    } catch (reason) { if (!controller.signal.aborted && mounted.current) { setHistory(undefined); fail(reason); } }
    finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  return <Container maxWidth="md" sx={{ py: 3, overflowWrap: 'anywhere' }}>
    <Stack spacing={2}>
      <Typography variant="h4" component="h1" id="unit-directory-title" tabIndex={-1} ref={heading}>Unit directory</Typography>
      <Typography>Administrative unit records for this Organization. Common property is a work scope, not a unit. Unit records do not grant ownership or Portal access.</Typography>
      <Button component={Link} to={`/app/${organization}`} sx={{ alignSelf: 'flex-start' }}>Return to Organization</Button>
      <Box role="status" aria-live="polite">{busy ? 'Checking current unit records…' : notice}</Box>
      {failure && <Alert severity="error">{failure}</Alert>}
      {unavailable ? <Alert severity="warning">The unit directory is unavailable to your account.</Alert> : <Button disabled={busy} onClick={() => void load(cursor)}>Refresh current records</Button>}
      {ready && !unavailable && <>
        {!editor && <>
          {original ? <><Alert severity="warning">An original unit change still needs recovery.</Alert><Button onClick={() => setEditor({ id: original.lotId, draft: { ...original.data } })}>Recover original unit change</Button></>
            : <Button variant="contained" disabled={busy} onClick={() => void open()}>Add unit</Button>}
          {!page?.items.length && <Alert severity="info">{cursor ? 'No units remain on this page. Return to the first page or refresh.' : 'No units have been recorded. Add the first unit if you are authorized to administer this Organization.'}</Alert>}
          <Box component="ul" sx={{ p: 0, listStyle: 'none' }}>{page?.items.map(lot => <Box component="li" key={lot.id} sx={{ py: 1, borderBottom: 1, borderColor: 'divider' }}>
            <Button disabled={busy || !!original} onClick={() => void open(lot)} sx={{ textAlign: 'left', overflowWrap: 'anywhere', maxWidth: '100%' }}>Unit {lot.data.unitIdentifier} · Lot {lot.data.lotIdentifier}</Button>
            <Typography>{lot.data.status === 'ACTIVE' ? 'Active' : 'Inactive'} · Revision {lot.version}</Typography>
          </Box>)}</Box>
          <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
            <Button disabled={busy || !cursor} onClick={() => { void load(); heading.current?.focus(); }}>First directory page</Button>
            <Button disabled={busy || !page?.nextCursor} onClick={() => { void load(page!.nextCursor!); heading.current?.focus(); }}>Next directory page</Button>
          </Stack>
        </>}
        {editor && <Box component="section" aria-label="Unit editor">
          <Typography variant="h5" component="h2" id="unit-editor-title" tabIndex={-1} ref={editorHeading}>{editor.source ? `Unit ${editor.source.data.unitIdentifier} · revision ${editor.source.version}` : original ? 'Recover original unit change' : 'Add unit'}</Typography>
          <Typography>Correcting identifiers preserves this record and its history. Inactive units remain available for historical context.</Typography>
          <Box component="form" noValidate onSubmit={event => { event.preventDefault(); const issues = lotErrors(editor.draft); setErrors(issues); if (!Object.keys(issues).length) setReview(true); }}>
            <Stack spacing={2} sx={{ mt: 2 }}>
              {lotFields.map(([key, label, maximum]) => <TextField key={key} id={`unit-field-${key}`} label={label} required={key === 'unitIdentifier' || key === 'lotIdentifier'}
                value={editor.draft[key] ?? ''} disabled={busy || !!original} error={!!errors[key]} helperText={errors[key]}
                slotProps={{ htmlInput: { maxLength: maximum } }} onChange={event => setEditor({ ...editor, draft: { ...editor.draft, [key]: event.target.value === '' && key !== 'unitIdentifier' && key !== 'lotIdentifier' ? null : event.target.value } })} />)}
              <TextField select id="unit-field-status" label="Unit status" value={editor.draft.status} disabled={busy || !!original} onChange={event => setEditor({ ...editor, draft: { ...editor.draft, status: event.target.value as LotData['status'] } })}>
                <MenuItem value="ACTIVE">Active</MenuItem><MenuItem value="INACTIVE">Inactive</MenuItem>
              </TextField>
              {original ? <Button variant="contained" disabled={busy} onClick={() => void submit()}>Retry original unit change</Button>
                : <Button type="submit" variant="contained" disabled={busy || conflict || !dirty}>Review unit change</Button>}
              {conflict && <>
                <Alert severity="warning">Your draft is preserved. Refresh current records, compare the current values below, then explicitly keep your draft or use current values.</Alert>
                {editor.source && !needsCurrent && <><Values data={editor.source.data} /><Button disabled={busy} onClick={() => { setConflict(false); setFailure(undefined); }}>Keep draft after review</Button>
                  <Button disabled={busy} onClick={() => { setEditor({ ...editor, draft: { ...editor.source!.data } }); setConflict(false); setFailure(undefined); }}>Use current values</Button></>}
                {!editor.source && !needsCurrent && <Button disabled={busy} onClick={() => { setConflict(false); setFailure(undefined); }}>Keep new unit draft after review</Button>}
              </>}
              <Button disabled={busy || !!original || !editor.source} onClick={() => void showHistory()}>View unit history</Button>
              <Button disabled={busy} onClick={() => dirty && !original ? setDiscard(true) : close()}>{original ? 'Leave editor; retain original for recovery' : 'Close unit editor'}</Button>
            </Stack>
          </Box>
          {history && <Box component="section" aria-label="Unit history" sx={{ mt: 3 }}>
            <Typography variant="h5" component="h2" id="unit-history-title" tabIndex={-1}>Unit history</Typography>
            {!history.items.length && <Typography>No earlier revisions on this page.</Typography>}
            {history.items.map(revision => <Box key={revision.lot.version} sx={{ my: 2 }}><Typography variant="h6" component="h3">Revision {revision.lot.version}</Typography>
              <Typography component="time" dateTime={revision.lot.updatedAt}>{page && formatUserDateTime(revision.lot.updatedAt, page.preferences) || 'Date unavailable'}</Typography><Values data={revision.lot.data} /></Box>)}
            <Button disabled={busy || history.nextBeforeVersion === null} onClick={() => void showHistory(history.nextBeforeVersion!)}>Earlier unit history</Button>
          </Box>}
        </Box>}
      </>}
    </Stack>
    <Dialog open={review && ready && !!editor && !conflict} onClose={() => setReview(false)} slotProps={{ transition: { onEntered: () => focusSafeDefault(reviewCancel.current) } }} fullWidth maxWidth="sm" aria-labelledby="unit-review-title">
      <DialogTitle id="unit-review-title">Review unit change</DialogTitle><DialogContent>{editor && <><Typography>{editor.draft.status === 'INACTIVE' ? 'Mark this unit inactive while preserving all history.' : 'Save these unit details.'}</Typography><Values data={editor.draft} /></>}</DialogContent>
      <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}><Button autoFocus ref={reviewCancel} onClick={() => setReview(false)}>Return to draft</Button><Button disabled={busy} onClick={() => void submit()}>Confirm unit change</Button></DialogActions>
    </Dialog>
    <Dialog open={discard || blocker.state === 'blocked'} slotProps={{ transition: { onEntered: () => focusSafeDefault(leaveCancel.current) } }} onClose={() => { setDiscard(false); if (blocker.state === 'blocked') blocker.reset(); }} aria-labelledby="unit-leave-title">
      <DialogTitle id="unit-leave-title">Leave unit draft?</DialogTitle><DialogContent>{original ? 'The original submitted change will remain saved in this tab for explicit recovery.' : 'Unsubmitted changes will be discarded.'}</DialogContent>
      <DialogActions><Button autoFocus ref={leaveCancel} onClick={() => { setDiscard(false); if (blocker.state === 'blocked') blocker.reset(); }}>Keep editing</Button><Button onClick={() => { if (blocker.state === 'blocked') blocker.proceed(); else close(); }}>Leave unit draft</Button></DialogActions>
    </Dialog>
  </Container>;
}
