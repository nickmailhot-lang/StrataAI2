import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { WorkInputError, WorkRequestError } from '../../api/workManagement';
import { publicCorrelationReference } from '../../api/correlationReference';
import { OrganizationConfigurationForm, ConfigurationValues, type IntakeOption } from './OrganizationConfigurationForm';
import { configurationDraft, reviewedDraft, type ConfigurationDraft } from './organizationConfigurationDraft';
import { ConfigurationChangeIntent, readConfigurationIntakeLists, readConfigurationIntakeBoards, readOrganizationConfiguration,
  readOrganizationConfigurationHistory } from './organizationConfigurationClient';
import type { ConfigurationHistory, ConfigurationView, OrganizationConfiguration } from './organizationConfiguration';
import { watchOrganizationMetadata } from './organizationMetadataLive';
import { organizationTypeLabel } from './organizationTypes';
import { completeConfigurationChange, forgetConfigurationChanges, forgetForeignConfigurationChanges,
  restoreConfigurationChange, retainConfigurationChange } from './organizationConfigurationRecovery';

function denied(error: unknown) { return error instanceof WorkRequestError && [401, 403, 404].includes(error.status); }
function commandFailure(error: unknown) {
  if (error instanceof WorkInputError) return error.message;
  if (error instanceof WorkRequestError) {
    if (error.code === 'configuration_identifier_unavailable') return 'The corporation or registration identifier is unavailable in this jurisdiction.';
    if (error.code === 'configuration_intake_unavailable') return 'Choose an active intake Board and a List belonging to it.';
    if (error.code === 'version_conflict') return 'Configuration changed elsewhere. Your draft is preserved; review it against the current revision.';
    if (error.code === 'idempotency_key_expired') return 'The original acknowledgment expired. Check current configuration before reviewing another change.';
    if (error.code === 'idempotency_key_conflict') return 'The original submission was refused. Check current configuration before reviewing another change.';
    if (error.code?.startsWith('invalid_configuration_')) return 'Correct the configuration field: ' + error.code.slice('invalid_configuration_'.length) + '.';
    if (error.status === 400) return 'Check the configuration fields before reviewing again.';
  }
  return 'The change could not be confirmed. Retry the original submission to recover its acknowledgment.';
}
export function OrganizationConfigurationPage() {
  const { organizationId } = useParams();
  return <ConfigurationPage key={organizationId} organizationId={organizationId ?? ''} />;
}
function ConfigurationPage({ organizationId }: { organizationId: string }) {
  const navigate = useNavigate();
  const [context, setContext] = useState<{ actorId: string; view: ConfigurationView }>();
  const [draft, setDraft] = useState<ConfigurationDraft>();
  const [review, setReview] = useState<ConfigurationChangeIntent>();
  const [original, setOriginal] = useState<ConfigurationChangeIntent>();
  const [history, setHistory] = useState<ConfigurationHistory>();
  const [boards, setBoards] = useState<IntakeOption[]>([]); const [boardCursor, setBoardCursor] = useState<string | null>(null);
  const [selectedBoard, setSelectedBoard] = useState<IntakeOption>(); const [lists, setLists] = useState<IntakeOption[]>([]);
  const [listCursor, setListCursor] = useState<string | null>(null);
  const [intakeBusy, setIntakeBusy] = useState(false); const [intakeError, setIntakeError] = useState<string>();
  const [intakeReload, setIntakeReload] = useState(0);
  const [busy, setBusy] = useState(false); const [needsCheck, setNeedsCheck] = useState(false); const [unavailable, setUnavailable] = useState(false);
  const [failure, setFailure] = useState<{ message: string; reference: string | null }>(); const [notice, setNotice] = useState<string>();
  const [authorityCheck, setAuthorityCheck] = useState(false); const [invalidation, setInvalidation] = useState(0);
  const refreshQueued = useRef(false); const focusAfter = useRef<'submission' | 'history' | undefined>(undefined);
  const loadButton = useRef<HTMLButtonElement>(null); const reviewButton = useRef<HTMLButtonElement>(null); const retryButton = useRef<HTMLButtonElement>(null);
  const historyHeading = useRef<HTMLHeadingElement>(null); const heading = useRef<HTMLHeadingElement>(null);
  const actor = useRef<string | undefined>(undefined); const mounted = useRef(false);
  const operation = useRef<AbortController | undefined>(undefined);
  const directoryRead = useRef<AbortController | undefined>(undefined); const boardRead = useRef<AbortController | undefined>(undefined);
  const current = useRef({ draft, original }); current.current = { draft, original };
  function error(message: string, reason?: unknown) { setFailure({ message, reference: reason instanceof WorkRequestError ? reason.correlationId : null }); }
  function withdraw(reason: unknown) {
    operation.current?.abort(); boardRead.current?.abort(); directoryRead.current?.abort();
    if (actor.current) forgetConfigurationChanges(actor.current, organizationId);
    actor.current = undefined; setContext(undefined); setDraft(undefined); setReview(undefined); setOriginal(undefined);
    setHistory(undefined); setBoards([]); setLists([]); setSelectedBoard(undefined); setBoardCursor(null); setListCursor(null);
    setNotice(undefined); setIntakeError(undefined); setUnavailable(true); setBusy(false);
    error('Organization configuration is unavailable to your account.');
    if (reason instanceof WorkRequestError && reason.status === 401) navigate('/login', { replace: true });
  }
  async function load(preserve = true) {
    if (operation.current) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setReview(undefined);
    try {
      const result = await readOrganizationConfiguration(organizationId, controller.signal, actor.current);
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      actor.current = result.actorId; setContext(result); setNeedsCheck(false); setUnavailable(false);
      setAuthorityCheck(false);
      forgetForeignConfigurationChanges(sessionStorage, result.actorId);
      const recovered = current.current.original ?? restoreConfigurationChange(sessionStorage, result.actorId, organizationId);
      if (recovered) {
        current.current.original = recovered; setOriginal(recovered);
        if (!current.current.draft) setDraft(configurationDraft(recovered.reviewedConfiguration()));
        setNotice('An original configuration submission is saved. Review its preserved values and retry explicitly to recover its acknowledgment.');
      } else if ((!preserve || !current.current.draft)) setDraft(configurationDraft(result.view.revision?.configuration));
      if (!current.current.original) setFailure(undefined);
    } catch (reason) {
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      if (denied(reason)) withdraw(reason);
      else { setNeedsCheck(true); error(reason instanceof WorkInputError ? reason.message : 'Current configuration could not be checked. Your draft and any original submission are preserved.', reason); }
    } finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  useEffect(() => {
    mounted.current = true; void load(false);
    return () => { mounted.current = false; operation.current?.abort(); operation.current = undefined;
      directoryRead.current?.abort(); directoryRead.current = undefined; boardRead.current?.abort(); boardRead.current = undefined; };
    // This route is keyed by Organization. All late responses are fenced above.
  }, []);
  useEffect(() => {
    if (!context?.actorId) return;
    const recheck = () => {
      refreshQueued.current = true; setAuthorityCheck(true); setNeedsCheck(true); setReview(undefined);
      setNotice('Organization authority changed. Checking current configuration before approval.'); setInvalidation(value => value + 1);
    };
    return watchOrganizationMetadata({ organizationId, userId: context.actorId,
      invalidate: recheck, reset: recheck, unavailable: recheck });
  }, [organizationId, context?.actorId]);
  useEffect(() => {
    if (!refreshQueued.current || busy || operation.current) return;
    refreshQueued.current = false; void load();
  }, [invalidation, busy]);
  useLayoutEffect(() => {
    if (busy || !focusAfter.current) return;
    const target = unavailable ? heading.current : focusAfter.current === 'history' ? historyHeading.current
      : original ? retryButton.current : needsCheck ? loadButton.current : reviewButton.current;
    if (target) { target.focus(); focusAfter.current = undefined; }
  }, [busy, original, needsCheck, unavailable, history, authorityCheck]);
  async function directory(after?: string) {
    if (!actor.current || directoryRead.current) return;
    const controller = new AbortController(); directoryRead.current = controller;
    try {
      const result = await readConfigurationIntakeBoards(organizationId, actor.current, controller.signal, after);
      if (mounted.current && !controller.signal.aborted && directoryRead.current === controller) { setBoards(result.items); setBoardCursor(result.nextCursor); }
    } catch (reason) {
      if (!mounted.current || controller.signal.aborted) return;
      if (denied(reason)) withdraw(reason); else setIntakeError('Intake Boards could not be checked. Retry intake discovery.');
    } finally { if (directoryRead.current === controller) directoryRead.current = undefined; }
  }
  useEffect(() => { if (context?.actorId) void directory(); }, [context?.actorId]);
  useEffect(() => {
    boardRead.current?.abort(); setSelectedBoard(undefined); setLists([]); setListCursor(null); setIntakeError(undefined);
    if (!draft?.intakeBoardId || !context?.actorId) { setIntakeBusy(false); return; }
    const controller = new AbortController(); boardRead.current = controller; setIntakeBusy(true);
    void readConfigurationIntakeLists(organizationId, context.actorId, draft.intakeBoardId, controller.signal).then(result => {
      if (mounted.current && !controller.signal.aborted && boardRead.current === controller) { setSelectedBoard(result.board); setLists(result.lists); setListCursor(result.nextAfterRank); }
    }).catch((reason: unknown) => {
      if (!mounted.current || controller.signal.aborted || boardRead.current !== controller) return;
      if (denied(reason)) withdraw(reason);
      else { setIntakeError('The selected intake Board or its Lists are unavailable. Clear the destination or choose active records.'); void load(); }
    }).finally(() => { if (boardRead.current === controller) { boardRead.current = undefined; if (mounted.current) setIntakeBusy(false); } });
    return () => controller.abort();
  }, [organizationId, context?.actorId, draft?.intakeBoardId, intakeReload]);
  async function moreLists() {
    if (!draft?.intakeBoardId || !context?.actorId || !listCursor || boardRead.current || busy || original || review) return;
    const controller = new AbortController(); boardRead.current = controller; setIntakeBusy(true); setIntakeError(undefined);
    const pinned = lists.find(row => row.id === draft.intakeListId);
    try {
      const result = await readConfigurationIntakeLists(organizationId, context.actorId, draft.intakeBoardId, controller.signal, listCursor);
      if (!mounted.current || controller.signal.aborted || boardRead.current !== controller) return;
      setSelectedBoard(result.board); setLists(pinned && !result.lists.some(row => row.id === pinned.id) ? [pinned, ...result.lists] : result.lists);
      setListCursor(result.nextAfterRank);
    } catch (reason) {
      if (!mounted.current || controller.signal.aborted || boardRead.current !== controller) return;
      if (denied(reason)) withdraw(reason);
      else setIntakeError('The next intake List page could not be checked. Your draft and selected destination are preserved.');
    } finally { if (boardRead.current === controller) { boardRead.current = undefined; if (mounted.current) setIntakeBusy(false); } }
  }
  const boardOptions = selectedBoard && !boards.some(row => row.id === selectedBoard.id) ? [selectedBoard, ...boards] : boards;
  async function prepareReview() {
    if (!draft || !context || operation.current || original || intakeBusy) return;
    let values: OrganizationConfiguration;
    try {
      values = reviewedDraft(draft);
      if (values.intakeBoardId && selectedBoard?.id !== values.intakeBoardId
        || values.intakeListId && !lists.some(row => row.id === values.intakeListId)) throw new WorkInputError('Verify an active intake destination before reviewing.');
    } catch (reason) { error(commandFailure(reason)); return; }
    const controller = new AbortController(); operation.current = controller; setBusy(true); setFailure(undefined);
    try {
      const latest = await readOrganizationConfiguration(organizationId, controller.signal, context.actorId);
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      setContext(latest); setNeedsCheck(false);
      setReview(ConfigurationChangeIntent.review(organizationId, context.actorId, latest.view.version, values));
    } catch (reason) { if (mounted.current && !controller.signal.aborted) { if (denied(reason)) withdraw(reason); else { setNeedsCheck(true); error('No change was submitted. Check current configuration before reviewing again.', reason); } } }
    finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  async function submit(intent: ConfigurationChangeIntent, retry = false) {
    if (operation.current) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); setReview(undefined); setFailure(undefined); focusAfter.current = 'submission';
    let sent = retry; let acknowledged = false;
    try {
      await intent.submit(controller.signal, () => {
        retainConfigurationChange(sessionStorage, intent);
        sent = true; current.current.original = intent; if (mounted.current) setOriginal(intent);
      }); acknowledged = true;
      completeConfigurationChange(sessionStorage, intent);
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      setOriginal(undefined); current.current.original = undefined;
      setNotice('The original change was acknowledged. Checking the current configuration.');
      const latest = await readOrganizationConfiguration(organizationId, controller.signal, intent.actorId);
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      setContext(latest); setDraft(configurationDraft(latest.view.revision?.configuration)); setHistory(undefined); setNeedsCheck(false);
      setNotice(`Change acknowledged. Current configuration revision ${latest.view.version}.`);
    } catch (reason) {
      if (!mounted.current || controller.signal.aborted || operation.current !== controller) return;
      if (denied(reason)) withdraw(reason);
      else if (acknowledged) { setNeedsCheck(true); error('Change acknowledged, but current configuration could not be checked. Your draft is preserved; load current configuration.', reason); }
      else if (reason instanceof WorkRequestError && [400, 409].includes(reason.status)) {
        try { completeConfigurationChange(sessionStorage, intent); }
        catch (storageFailure) { setNeedsCheck(true); error(commandFailure(storageFailure)); return; }
        setOriginal(undefined); current.current.original = undefined; setNeedsCheck(true); error(commandFailure(reason), reason);
      } else if (sent) { setOriginal(intent); error('The change could not be confirmed. Retry the original submission to recover its acknowledgment.', reason); }
      else { setNeedsCheck(true); error(reason instanceof WorkInputError ? reason.message : 'No change was submitted. Your draft is preserved; check current configuration before reviewing again.', reason); }
    } finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  async function historyPage(before?: number) {
    if (!context || operation.current) return;
    const controller = new AbortController(); operation.current = controller; setBusy(true); focusAfter.current = 'history';
    try {
      const result = await readOrganizationConfigurationHistory(organizationId, context.actorId, controller.signal, before);
      if (mounted.current && !controller.signal.aborted && operation.current === controller) { setHistory(result); if (!original) setFailure(undefined); }
    } catch (reason) { if (mounted.current && !controller.signal.aborted) { if (denied(reason)) withdraw(reason); else error('History could not be checked. Your draft and original submission are preserved.', reason); } }
    finally { if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); } }
  }
  return <Container maxWidth="md" sx={{ py: 3 }} data-testid="configuration-authority"
    data-generation={invalidation} data-ready={!!context && !authorityCheck && !busy && !unavailable && !needsCheck}>
    <Stack spacing={2}>
      <Button component={Link} to={`/app/${organizationId}`}>Back to Organization</Button>
      <Typography variant="h4" component="h2" ref={heading} tabIndex={-1}>Organization configuration</Typography>
      <Typography>Private to current Organization administrators. Each approved change creates a historical revision.</Typography>
      {failure && <Alert severity="error">{failure.message}{failure.reference && <Typography>Support reference: {failure.reference}</Typography>}</Alert>}
      {notice && <Alert severity="info" role="status">{notice}</Alert>}
      {busy && <CircularProgress aria-label="Checking organization configuration" />}
      {!unavailable && <Button ref={loadButton} disabled={busy} onClick={() => void load()}>Load current configuration</Button>}
      {authorityCheck && !unavailable && <Alert severity="info">Private configuration is hidden until current authority is confirmed. Your draft and original submission are preserved.</Alert>}
      {!unavailable && !authorityCheck && context && draft && <>
        <Typography>Current revision: {context.view.version}</Typography>
        {!context.view.revision && <Alert severity="info">No configuration has been recorded. Enter reviewed legal details and optional operating settings, then review before approval.</Alert>}
        {needsCheck && <Alert severity="warning">Current authority or configuration needs checking before another approval. Your draft is preserved.</Alert>}
        {original && <Alert severity="warning" action={<Button ref={retryButton} disabled={busy} onClick={() => void submit(original, true)}>Retry original submission</Button>}>
          Original submission is unresolved. Its reviewed values, account, revision and retry key are preserved. A current-state read does not confirm that submission.
        </Alert>}
        {intakeError && <Alert severity="warning" action={<Button disabled={busy || intakeBusy} onClick={() => { void directory(); setIntakeReload(value => value + 1); }}>Retry intake discovery</Button>}>{intakeError}</Alert>}
        <OrganizationConfigurationForm draft={draft} onChange={value => { if (!original && !busy && !review) { setDraft(value); setNotice(undefined); } }}
          disabled={busy || !!original || !!review} boards={boardOptions} lists={lists} intakeBusy={intakeBusy}
          onBoardChange={id => { if (!original && !busy && !review) setDraft({ ...draft, intakeBoardId: id, intakeListId: '' }); }}
          onMoreBoards={boardCursor ? () => void directory(boardCursor) : undefined}
          onMoreLists={listCursor ? () => void moreLists() : undefined} />
        <Button ref={reviewButton} variant="contained" disabled={busy || !!original || needsCheck || intakeBusy} onClick={() => void prepareReview()}>Review configuration change</Button>
        <Button disabled={busy} onClick={() => void historyPage()}>View configuration history</Button>
        {history && <Box component="section" aria-label="Configuration history">
          <Typography variant="h5" component="h3" ref={historyHeading} tabIndex={-1}>Configuration history</Typography>
          {!history.items.length && <Typography>No configuration revisions exist in this history page.</Typography>}
          {history.items.map(row => <Box key={row.version} sx={{ my: 3 }}>
            <Typography variant="h6" component="h4">Revision {row.version}</Typography>
            <Typography>Recorded Organization: {row.organizationName}; {organizationTypeLabel(row.organizationType)}; parent revision {row.organizationVersion}.</Typography>
            <Typography>Actor reference: {row.actorId}</Typography>
            <Typography>Recorded at: <time dateTime={row.updatedAt}>{row.updatedAt}</time></Typography>
            {publicCorrelationReference(row.correlationId) && <Typography>Support reference: {publicCorrelationReference(row.correlationId)}</Typography>}
            <ConfigurationValues configuration={row.configuration} boards={boardOptions} lists={lists} />
          </Box>)}
          {history.nextBeforeVersion !== null && <Button disabled={busy} onClick={() => void historyPage(history.nextBeforeVersion!)}>Older configuration revisions</Button>}
        </Box>}
      </>}
    </Stack>
    <Dialog open={!!review} onClose={() => { if (!busy) setReview(undefined); }} fullWidth maxWidth="md" aria-labelledby="configuration-review-title">
      <DialogTitle id="configuration-review-title">Review configuration change</DialogTitle>
      <DialogContent dividers>
        {review && context && <Stack spacing={2}>
          <Alert severity="warning">Approval replaces configuration at revision {review.expectedVersion} and retains a new historical revision. Verify the source and every proposed value. Configuration does not establish legal compliance.</Alert>
          <Typography variant="h6" component="h3">Current configuration</Typography>
          {context.view.revision ? <ConfigurationValues configuration={context.view.revision.configuration} boards={boardOptions} lists={lists} /> : <Typography>No recorded configuration.</Typography>}
          <Typography variant="h6" component="h3">Proposed configuration</Typography>
          <ConfigurationValues configuration={review.reviewedConfiguration()} boards={boardOptions} lists={lists} />
        </Stack>}
      </DialogContent>
      <DialogActions><Button autoFocus disabled={busy} onClick={() => setReview(undefined)}>Return to draft</Button>
        <Button disabled={busy || !review} onClick={() => { if (review) void submit(review); }}>Approve configuration change</Button></DialogActions>
    </Dialog>
  </Container>;
}
