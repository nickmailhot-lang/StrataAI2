import { useEffect, useId, useState } from 'react';
import { Alert, Avatar, Box, Button, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';

type Props = { organizationId: string; boardId: string; cardId: string; version: number; unavailable: boolean; onRefresh: () => void };
type Assignee = { userId: string; displayName: string; assignedBy: string; assignedAt: string };
type Page = { items: Assignee[]; next: string | null };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
function parse(value: unknown, props: Props, cursor?: string): Page {
  const p = value as Record<string, unknown> | null;
  if (!p || p.organizationId !== props.organizationId || p.boardId !== props.boardId || p.cardId !== props.cardId
    || p.cardVersion !== props.version || typeof p.canEdit !== 'boolean' || !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid assignee scope');
  const items = p.items.map((value: unknown) => {
    const a = value as Record<string, unknown> | null;
    if (!a || !uuid(a.userId) || (cursor && a.userId.toLowerCase() <= cursor.toLowerCase()) || !uuid(a.assignedBy) || typeof a.displayName !== 'string'
      || a.displayName.length > 160 || typeof a.assignedAt !== 'string' || !Number.isFinite(Date.parse(a.assignedAt))) throw new Error('Invalid assignee');
    return { userId: a.userId, displayName: a.displayName, assignedBy: a.assignedBy, assignedAt: a.assignedAt };
  });
  if (new Set(items.map(a => a.userId.toLowerCase())).size !== items.length
    || items.some((a, i) => i > 0 && a.userId.toLowerCase() <= items[i - 1].userId.toLowerCase())
    || (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.userId))) throw new Error('Invalid assignee cursor');
  return { items, next: p.nextCursor as string | null };
}
export function CardAssignees(props: Props) {
  return <AssigneeDisclosure key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function AssigneeDisclosure(props: Props) {
  const [open, setOpen] = useState(false);
  return <AssigneeContent key={`${props.version}/${props.unavailable}`} {...props} open={open} onToggle={() => setOpen(value => !value)} />;
}
function AssigneeContent(props: Props & { open: boolean; onToggle: () => void }) {
  const region = useId(); const { open } = props; const [cursor, setCursor] = useState<string>();
  const [attempt, setAttempt] = useState(0); const [result, setResult] = useState<Page>();
  const [error, setError] = useState<string>(); const [loading, setLoading] = useState(false);
  const { organizationId, boardId, cardId, version, unavailable } = props;
  useEffect(() => {
    if (!open || unavailable) return;
    let active = true; const controller = new AbortController(); setResult(undefined); setError(undefined); setLoading(true);
    void boundedWorkRead(signal => workRequest<unknown>(`/cards/${encodeURIComponent(cardId)}/members${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal }), controller.signal)
      .then(value => { if (active) setResult(parse(value, { organizationId, boardId, cardId, version, unavailable, onRefresh: () => {} }, cursor)); })
      .catch(reason => {
        if (!active) return;
        setError(reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)
          ? 'Assignees are unavailable. Refresh the Board to check your access.'
          : 'Unable to load current assignees. Refresh the Board or try again.');
      }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [open, unavailable, organizationId, boardId, cardId, version, cursor, attempt]);
  return <Box sx={{ mt: 2 }}>
    <Button aria-expanded={open} aria-controls={region} disabled={unavailable} onClick={() => { setCursor(undefined); setResult(undefined); props.onToggle(); }}>{open ? 'Hide assignees' : 'Show assignees'}</Button>
    {open && <Stack id={region} component="section" aria-label="Card assignees" spacing={1}>
      {loading && <Typography role="status">Loading assignees…</Typography>}
      {error && <Alert severity="warning">{error}</Alert>}
      {result && <>
        {result.items.length === 0 && <Typography>No assignees on this page.</Typography>}
        {result.items.map(a => <Stack key={a.userId} direction="row" spacing={1} sx={{ alignItems: 'center', overflowWrap: 'anywhere' }}>
          <Avatar aria-hidden="true">{a.displayName.trim().split(/\s+/).slice(0, 2).map(n => Array.from(n)[0] ?? '').join('').toUpperCase() || '?'}</Avatar>
          <Typography>{a.displayName.trim() || 'Unnamed member'}</Typography>
        </Stack>)}
        <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          {result.next && <Button onClick={() => setCursor(result.next!)}>Next assignees</Button>}
          {cursor && <Button onClick={() => setCursor(undefined)}>First assignees</Button>}
        </Stack>
      </>}
      {error && <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Button onClick={() => setAttempt(n => n + 1)}>Retry assignees</Button><Button onClick={props.onRefresh}>Refresh Board</Button>
      </Stack>}
    </Stack>}
  </Box>;
}
