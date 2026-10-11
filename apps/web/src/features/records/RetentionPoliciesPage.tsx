import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Container, Paper, Stack, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { WorkRequestError } from '../../api/workManagement';
import { watchOrganizationMetadata } from '../organizations/organizationMetadataLive';
import { readRetentionHistory, readRetentionPolicies, type PolicyHistory, type PolicyPage, type RetentionPolicy } from './retentionPolicyClient';
import { PolicyRule, recordClasses as classes, RetentionPolicyEditor, type PolicyEditorCommand } from './RetentionPolicyEditor';

export function RetentionPoliciesPage() {
  const { organizationId } = useParams();
  return <PolicyScreen key={organizationId} organizationId={organizationId ?? ''} />;
}
function PolicyScreen({ organizationId }: { organizationId: string }) {
  const [directory, setDirectory] = useState<PolicyPage>(); const [actorId, setActorId] = useState<string>();
  const [history, setHistory] = useState<PolicyHistory>(); const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>(); const [denied, setDenied] = useState(false);
  const [editor, setEditor] = useState<PolicyEditorCommand | null>(null); const editorSequence = useRef(0);
  const request = useRef<AbortController | null>(null); const epoch = useRef(0);
  const heading = useRef<HTMLHeadingElement | null>(null); const historyTrigger = useRef<HTMLButtonElement | null>(null);
  const pageHeading = useRef<HTMLHeadingElement | null>(null); const focusAfterRefresh = useRef(false);
  useEffect(() => { if (history) heading.current?.focus(); }, [history]);
  useEffect(() => {
    if (directory && focusAfterRefresh.current) { focusAfterRefresh.current = false; pageHeading.current?.focus(); }
    if (denied) pageHeading.current?.focus();
  }, [directory, denied]);
  const withdraw = useCallback(() => { setDirectory(undefined); setHistory(undefined); }, []);
  const failure = useCallback((value: unknown) => {
    withdraw(); const lost = value instanceof WorkRequestError && [401, 403, 404].includes(value.status);
    if (lost) setEditor(null);
    setDenied(lost); setError(lost ? 'Retention policies are no longer available with your current access.' : 'Retention policies could not be confirmed. Retry when the connection is available.');
  }, [withdraw]);
  const load = useCallback(async (after?: string, expected?: string) => {
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    const current = ++epoch.current; setBusy(true); setError(undefined); setDenied(false); withdraw();
    try {
      const result = await readRetentionPolicies(organizationId, controller.signal, expected, after);
      if (!controller.signal.aborted && current === epoch.current) { setDirectory(result.page); setActorId(result.actorId); }
    } catch (value) { if (!controller.signal.aborted && current === epoch.current) failure(value); }
    finally { if (!controller.signal.aborted && current === epoch.current) setBusy(false); }
  }, [organizationId, withdraw, failure]);
  useEffect(() => { void load(); return () => { ++epoch.current; request.current?.abort(); }; }, [load]);
  useEffect(() => {
    if (!actorId) return;
    const recheck = () => { void load(undefined, actorId); };
    const stop = watchOrganizationMetadata({ organizationId, userId: actorId, invalidate: recheck, reset: recheck, unavailable: recheck });
    window.addEventListener('focus', recheck);
    return () => { stop(); window.removeEventListener('focus', recheck); };
  }, [actorId, organizationId, load]);
  const showHistory = async (policyId: string, before?: number) => {
    if (!actorId) return;
    request.current?.abort(); const controller = new AbortController(); request.current = controller;
    const current = ++epoch.current; setBusy(true); setHistory(undefined); setError(undefined);
    try {
      const result = await readRetentionHistory(organizationId, actorId, policyId, controller.signal, before);
      if (!controller.signal.aborted && current === epoch.current) setHistory(result);
    } catch (value) { if (!controller.signal.aborted && current === epoch.current) failure(value); }
    finally { if (!controller.signal.aborted && current === epoch.current) setBusy(false); }
  };
  const edit = (policy: RetentionPolicy | null) => setEditor({ sequence: ++editorSequence.current, policy });
  return <Container maxWidth="md" sx={{ py: 3 }}>
    <Stack spacing={2}>
      <Typography ref={pageHeading} tabIndex={-1} variant="h4" component="h1">Retention policies</Typography>
      <Typography>Explicit policies describe which records are preserved and when another action may become eligible. A policy alone does not authorize disposition or override a hold.</Typography>
      <Alert severity="info" role="note">Policies are recorded for review. Retention enforcement and automatic disposition are not available yet.</Alert>
      <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap' }}>
        <Button component={Link} to={`/app/${organizationId}`}>Back to Organization</Button>
        <Button disabled={busy} onClick={() => void load(undefined, actorId)}>Refresh policies</Button>
        {directory && <Button disabled={busy} onClick={() => edit(null)}>Create policy</Button>}
      </Stack>
      {actorId && <RetentionPolicyEditor key={actorId} organizationId={organizationId} actorId={actorId} admitted={!!directory && !busy && !denied}
        denied={denied} command={editor} completed={() => { focusAfterRefresh.current = true; void load(undefined, actorId); }}
        accessLost={value => { ++epoch.current; request.current?.abort(); setBusy(false); setEditor(null); failure(value); }} />}
      {busy && <Box role="status"><CircularProgress size={20} aria-label="Loading retention policies" /> Loading policies</Box>}
      {error && <Alert severity={denied ? 'warning' : 'error'}>{error}</Alert>}
      {error && !denied && <Button onClick={() => void load(undefined, actorId)}>Retry policy read</Button>}
      {directory?.items.length === 0 && <Typography role="status">No retention policies are recorded in this page. An Organization administrator must establish an explicit policy.</Typography>}
      {directory?.items.map(policy => <Paper key={policy.id} component="section" aria-label={`${classes[policy.recordClass]} policy ${policy.category}, ${policy.jurisdiction ?? 'Organization-wide'}`} variant="outlined" sx={{ p: 2 }}>
        <Stack spacing={1}>
          <Typography variant="h6" component="h2">{policy.category} · Revision {policy.version}</Typography>
          <PolicyRule policy={policy} />
          <Button disabled={busy} onClick={() => edit(policy)} aria-label={`Edit policy for ${classes[policy.recordClass]} ${policy.category}, ${policy.jurisdiction ?? 'Organization-wide'}`}>Edit policy</Button>
          <Button disabled={busy} onClick={event => { historyTrigger.current = event.currentTarget; void showHistory(policy.id); }} aria-label={`View history for ${classes[policy.recordClass]} ${policy.category}, ${policy.jurisdiction ?? 'Organization-wide'}`}>View history</Button>
        </Stack>
      </Paper>)}
      {directory?.nextAfter && <Button disabled={busy} onClick={() => void load(directory.nextAfter!, actorId)}>More policies</Button>}
      {history && <Paper component="section" aria-label="Retained policy history" variant="outlined" sx={{ p: 2 }}>
        <Stack spacing={2}>
          <Typography ref={heading} variant="h5" component="h2" tabIndex={-1}>Retained policy history</Typography>
          {history.items.length === 0 && <Typography role="status">No older revisions exist in this page.</Typography>}
          {history.items.map(row => <Box key={row.eventId}>
            <Typography variant="h6" component="h3">Revision {row.policy.version}</Typography>
            <Typography>{row.actorLabel} · {row.actingCapacity === 0 ? 'Organization owner' : 'Organization administrator'} · {new Date(row.policy.updatedAt).toLocaleString(undefined, { timeZone: 'UTC' })} UTC</Typography>
            <PolicyRule policy={row.policy} />
          </Box>)}
          {history.nextBeforeVersion !== null && <Button disabled={busy} onClick={() => void showHistory(history.policyId, history.nextBeforeVersion!)}>Older history</Button>}
          <Button onClick={() => { setHistory(undefined); if (historyTrigger.current?.isConnected) historyTrigger.current.focus(); }}>Close history</Button>
        </Stack>
      </Paper>}
    </Stack>
  </Container>;
}
