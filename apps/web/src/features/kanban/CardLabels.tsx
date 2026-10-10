import { useEffect, useId, useRef, useState } from 'react';
import { publicCorrelationReference } from '../../api/correlationReference';
import { Alert, Box, Button, Chip, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';

const colors: Record<string, string> = { green: '#b7e4c7', yellow: '#ffe69a', orange: '#ffd0a8', red: '#ffc9c9', purple: '#e2c8f5', blue: '#bfdcff', sky: '#c2efff', lime: '#dcedab', pink: '#f9cce3', black: '#333333' };
type Label = { id: string; name: string; color: string; rank: string };
type Props = { organizationId: string; boardId: string; cardId: string; version: number; unavailable: boolean; onRefresh: () => void };
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
function page(value: unknown, props: Props): { items: Label[]; next: string | null } {
  if (!value || typeof value !== 'object') throw new Error('Invalid label page');
  const p = value as Record<string, unknown>;
  if (p.organizationId !== props.organizationId || p.boardId !== props.boardId || p.cardId !== props.cardId
    || p.cardVersion !== props.version || !Array.isArray(p.items) || p.items.length > 50 || typeof p.canEdit !== 'boolean') throw new Error('Invalid label scope');
  const seen = new Set<string>();
  const items = p.items.map((value: unknown) => {
    if (!value || typeof value !== 'object') throw new Error('Invalid label');
    const label = value as Record<string, unknown>;
    if (!uuid(label.id) || seen.has(label.id) || label.organizationId !== props.organizationId || label.boardId !== props.boardId
      || label.deleted !== false || typeof label.name !== 'string' || label.name.length > 160 || typeof label.color !== 'string'
      || !Object.hasOwn(colors, label.color) || typeof label.rank !== 'string' || !/^\d{30}$/.test(label.rank)
      || !Number.isSafeInteger(label.version) || (label.version as number) < 1) throw new Error('Invalid label');
    seen.add(label.id);
    return { id: label.id, name: label.name, color: label.color, rank: label.rank };
  });
  if (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.id)) throw new Error('Invalid label cursor');
  return { items, next: p.nextCursor as string | null };
}

export function CardLabels(props: Props) {
  return <LabelDisclosure key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function LabelDisclosure(props: Props) {
  const [open, setOpen] = useState(false);
  return <CardLabelContent key={`${props.version}/${props.unavailable}`} {...props} open={open} onToggle={() => setOpen(value => !value)} />;
}
function CardLabelContent(props: Props & { open: boolean; onToggle: () => void }) {
  const region = useId();
  const { open } = props;
  const [cursor, setCursor] = useState<string>();
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ items: Label[]; next: string | null; cursor?: string }>();
  const accumulated = useRef<{ items: Label[]; next: string | null }>({ items: [], next: null });
  const [error, setError] = useState<{ message: string; reference: string | null }>();
  const [loading, setLoading] = useState(false);
  const { organizationId, boardId, cardId, version, unavailable } = props;
  useEffect(() => {
    if (!open || unavailable) return;
    const controller = new AbortController(); let active = true;
    setLoading(true); setError(undefined);
    void boundedWorkRead(signal => workRequest<unknown>(`/cards/${encodeURIComponent(cardId)}/labels${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`, { signal }), controller.signal)
      .then(value => {
        if (!active) return;
        const parsed = page(value, { organizationId, boardId, cardId, version, unavailable, onRefresh: () => {} });
        const previous = accumulated.current;
        const prior = cursor ? previous.items : [];
        if (cursor && (previous.next !== cursor || parsed.items.some(item => prior.some(old => old.id === item.id)))) throw new Error('Inconsistent label page');
        accumulated.current = { items: [...prior, ...parsed.items], next: parsed.next };
        setResult({ ...accumulated.current, cursor });
      }).catch(reason => {
        if (!active) return;
        setResult(undefined);
        setError({ message: reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)
          ? 'Labels are unavailable. Refresh the Board to check your access.'
          : 'Unable to load current labels. Refresh the Board or try again.',
          reference: reason instanceof WorkRequestError ? publicCorrelationReference(reason.correlationId) : null });
      }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [open, unavailable, organizationId, boardId, cardId, version, cursor, attempt]);
  return <Box sx={{ mt: 2 }}>
    <Button aria-expanded={open} aria-controls={region} disabled={unavailable} onClick={() => { setCursor(undefined); setResult(undefined); props.onToggle(); }}>{open ? 'Hide labels' : 'Show labels'}</Button>
    {open && <Stack id={region} component="section" aria-label="Card labels" spacing={1}>
      {unavailable ? <Typography>Refreshing label access…</Typography> : <>
        {loading && <Typography role="status">Loading labels…</Typography>}
        {error && <Alert severity="warning"><span>{error.message}</span>
          {error.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {error.reference}</Typography>}
        </Alert>}
        {!error && result && <>
          <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>{[...result.items].sort((a, b) => a.rank.localeCompare(b.rank) || a.id.localeCompare(b.id)).map(label =>
            <Chip key={label.id} label={label.name || `${label.color} label`} aria-label={`${label.name || 'Unnamed label'}, ${label.color}`} sx={{ backgroundColor: colors[label.color], color: label.color === 'black' ? '#ffffff' : '#172b4d' }} />)}</Stack>
          {!loading && result.items.length === 0 && <Typography>No labels assigned.</Typography>}
          {result.next && <Button disabled={loading} onClick={() => setCursor(result.next!)}>Load more labels</Button>}
        </>}
        {error && <Stack direction="row"><Button onClick={() => { setCursor(undefined); setAttempt(value => value + 1); }}>Retry labels</Button><Button onClick={props.onRefresh}>Refresh Board</Button></Stack>}
      </>}
    </Stack>}
  </Box>;
}
