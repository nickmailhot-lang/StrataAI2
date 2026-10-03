import { useEffect, useId, useRef, useState } from 'react';
import { Alert, Box, Button, LinearProgress, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { checklistEvent, checklistResult } from './checklistTelemetry';
import { parseChecklistItemPage, parseChecklistPage, type ChecklistItemPage, type ChecklistPage, type ChecklistScope, type ChecklistSummary } from './checklists';

type Props = ChecklistScope & { version: number; unavailable: boolean; onRefresh: () => void; reconnectSequence?: number };
type Disclosures = { openItems: Set<string>; onItemToggle: (id: string) => void };
function usePage(props: Props, open: boolean, cursor: string | undefined, attempt: number, checklistId?: string) {
  const [result, setResult] = useState<ChecklistPage | ChecklistItemPage>();
  const [error, setError] = useState<string>(); const [loading, setLoading] = useState(false);
  const { organizationId, boardId, cardId, version, unavailable } = props;
  useEffect(() => {
    if (!open || unavailable) return;
    let active = true; const controller = new AbortController();
    const action = checklistId ? 'item_read' : 'read'; const started = performance.now();
    checklistEvent(action, 'use');
    setResult(undefined); setError(undefined); setLoading(true);
    const scope = { organizationId, boardId, cardId };
    const path = `/cards/${encodeURIComponent(cardId)}/checklists${checklistId ? `/${encodeURIComponent(checklistId)}/items` : ''}${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`;
    void boundedWorkRead(signal => workRequest<unknown>(path, { signal }), controller.signal).then(value => {
      if (!active) return;
      const page = checklistId ? parseChecklistItemPage(value, scope, checklistId, cursor) : parseChecklistPage(value, scope, cursor);
      if (page.cardVersion !== version) throw new WorkRequestError(409, null);
      checklistResult(action, true, started);
      setResult(page);
    }).catch(reason => {
      if (!active) return;
      checklistResult(action, false, started);
      if (reason instanceof WorkRequestError && reason.status === 409) checklistEvent(action, 'conflict');
      if (!(reason instanceof WorkRequestError)) checklistEvent(action, 'exception');
      setError(reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)
        ? 'These checklists are unavailable. Refresh the Board to check your access.'
        : 'Unable to load current checklists. Refresh the Board or try again.');
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [open, unavailable, organizationId, boardId, cardId, version, cursor, attempt, checklistId]);
  return { result, error, loading };
}
export function CardChecklists(props: Props) {
  return <ChecklistDisclosure key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function ChecklistDisclosure(props: Props) {
  const [open, setOpen] = useState(false); const region = useId();
  const previousReconnect = useRef(props.reconnectSequence ?? 0);
  useEffect(() => {
    const current = props.reconnectSequence ?? 0;
    if (open && Number.isSafeInteger(current) && current > previousReconnect.current)
      for (let index = 0; index < Math.min(current - previousReconnect.current, 100); index++) checklistEvent('realtime', 'reconnect');
    previousReconnect.current = current;
  }, [open, props.reconnectSequence]);
  const [openItems, setOpenItems] = useState<Set<string>>(() => new Set());
  function onItemToggle(id: string) { setOpenItems(previous => { const next = new Set(previous); if (next.has(id)) next.delete(id); else next.add(id); return next; }); }
  return <Box sx={{ my: 2 }}>
    <Button aria-expanded={open} aria-controls={region} onClick={() => { if (!open) checklistEvent('disclosure', 'open'); if (open) setOpenItems(new Set()); setOpen(value => !value); }}>
      {open ? 'Hide checklists' : 'Show checklists'}
    </Button>
    {open && <Stack id={region} component="section" aria-label="Card checklists" spacing={2}>
      {props.unavailable ? <Typography role="status">Checking current Card access…</Typography>
        : <ChecklistContent key={props.version} {...props} openItems={openItems} onItemToggle={onItemToggle} />}
    </Stack>}
  </Box>;
}
function ChecklistContent(props: Props & Disclosures) {
  const [cursor, setCursor] = useState<string>(); const [attempt, setAttempt] = useState(0);
  const { result, error, loading } = usePage(props, true, cursor, attempt);
  const page = result && !('summary' in result) ? result : undefined;
  return <>
    {loading && <Typography role="status">Loading checklists…</Typography>}
    {error && <ReadError message={error} retry={() => { checklistEvent('read', 'retry'); setAttempt(value => value + 1); }} refresh={props.onRefresh} />}
    {page && <>
      {!page.canEdit && <Typography>Read-only checklists.</Typography>}
      {page.items.length === 0 && <Typography>No checklists on this page.</Typography>}
      {page.items.map(summary => <ChecklistItems key={summary.checklist.id} {...props} summary={summary}
        open={props.openItems.has(summary.checklist.id)} onToggle={() => props.onItemToggle(summary.checklist.id)} />)}
      <Pages cursor={cursor} next={page.nextCursor} change={setCursor} kind="checklists" />
    </>}
  </>;
}
function ChecklistItems(props: Props & { summary: ChecklistSummary; open: boolean; onToggle: () => void }) {
  const { open } = props; const [cursor, setCursor] = useState<string>(); const [attempt, setAttempt] = useState(0);
  const region = useId(); const heading = useId(); const { checklist } = props.summary;
  const { result, error, loading } = usePage(props, open, cursor, attempt, checklist.id);
  const page = result && 'summary' in result ? result : undefined;
  const progress = page?.summary ?? props.summary;
  return <Stack component="section" aria-labelledby={heading} spacing={1} sx={{ overflowWrap: 'anywhere' }}>
    <Typography id={heading} component="h3" variant="subtitle1">{checklist.title}</Typography>
    <Typography>{progress.completed} of {progress.total} items complete ({Number(progress.percent.toFixed(1))}%)</Typography>
    <LinearProgress variant="determinate" value={progress.percent} aria-label={`Progress for ${checklist.title}`} />
    <Button aria-expanded={open} aria-controls={region} onClick={() => { if (!open) checklistEvent('item_disclosure', 'open'); setCursor(undefined); props.onToggle(); }}>
      {open ? `Hide items in ${checklist.title}` : `Show items in ${checklist.title}`}
    </Button>
    {open && <Stack id={region} spacing={1}>
      {loading && <Typography role="status">Loading checklist items…</Typography>}
      {error && <ReadError message={error} retry={() => { checklistEvent('item_read', 'retry'); setAttempt(value => value + 1); }} refresh={props.onRefresh} />}
      {page && <>
        {page.items.length === 0 && <Typography>No items on this page.</Typography>}
        {page.items.map(item => <Typography key={item.id}>{item.completed ? 'Complete' : 'Incomplete'}: {item.text}</Typography>)}
        <Pages cursor={cursor} next={page.nextCursor} change={setCursor} kind={`items in ${checklist.title}`} />
      </>}
    </Stack>}
  </Stack>;
}
function ReadError(props: { message: string; retry: () => void; refresh: () => void }) {
  return <><Alert severity="warning">{props.message}</Alert><Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
    <Button onClick={props.retry}>Retry checklist read</Button><Button onClick={props.refresh}>Refresh Board</Button>
  </Stack></>;
}
function Pages(props: { cursor?: string; next: string | null; change: (value: string | undefined) => void; kind: string }) {
  return <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
    {props.next && <Button onClick={() => props.change(props.next!)}>Next {props.kind}</Button>}
    {props.cursor && <Button onClick={() => props.change(undefined)}>First {props.kind}</Button>}
  </Stack>;
}
