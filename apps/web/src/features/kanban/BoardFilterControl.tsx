import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot, type WorkCard } from '../../api/workManagement';

const uuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000';
const palette = ['green', 'yellow', 'orange', 'red', 'purple', 'blue', 'sky', 'lime', 'pink', 'black'];
type Criteria = { keyword: string; labels: string[]; match: 'all' | 'any' };
type Label = { id: string; name: string; color: string };
type Result = { items: (WorkCard & { listId: string })[]; next: string | null };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onRefresh: () => void };
const empty = (): Criteria => ({ keyword: '', labels: [], match: 'all' });
function saved(key: string): Criteria {
  try {
    const c = JSON.parse(sessionStorage.getItem(key) ?? 'null') as Criteria | null;
    if (c && typeof c.keyword === 'string' && c.keyword.length <= 160 && (c.match === 'all' || c.match === 'any')
      && Array.isArray(c.labels) && c.labels.length <= 25 && c.labels.every(uuid) && new Set(c.labels).size === c.labels.length) return c;
  } catch { /* Storage is optional; admitted server reads remain authoritative. */ }
  return empty();
}
export function BoardFilterControl({ snapshot, disabled, onRefresh }: Props) {
  const [open, setOpen] = useState(false); const [criteria, setCriteria] = useState<Criteria>(empty);
  const [applied, setApplied] = useState<Criteria>(); const [labels, setLabels] = useState<Label[]>([]);
  const [labelCursor, setLabelCursor] = useState<string | null>(null); const [result, setResult] = useState<Result>();
  const [cursor, setCursor] = useState<string>(); const [retry, setRetry] = useState(0);
  const [identity, setIdentity] = useState<string>(); const [loading, setLoading] = useState(false);
  const [labelLoading, setLabelLoading] = useState(false); const [notice, setNotice] = useState<string>();
  const [labelNotice, setLabelNotice] = useState<string>();
  const epoch = useRef(0); const pending = useRef<AbortController | undefined>(undefined);
  const org = snapshot.board.organizationId, board = snapshot.board.id;
  const available = snapshot.access.canView && snapshot.board.lifecycleState === 'active';
  const storageKey = (actor: string) => `strataai:board-filter:v1:${actor}:${org}:${board}`;
  useEffect(() => () => { epoch.current++; pending.current?.abort(); }, []);
  useEffect(() => {
    epoch.current++; pending.current?.abort(); pending.current = undefined;
    setLabels([]); setLabelCursor(null); setResult(undefined); setIdentity(undefined); setApplied(undefined); setLoading(false); setLabelLoading(false);
    setNotice(undefined); setLabelNotice(undefined);
    setCriteria(empty()); setOpen(false);
  }, [org, board, available]);
  async function loadLabels(after?: string, opening = false) {
    if (!available || disabled || pending.current) return;
    const controller = new AbortController(); pending.current = controller; const ticket = epoch.current;
    if (opening) { setOpen(true); setCriteria(empty()); setIdentity(undefined); setResult(undefined); setApplied(undefined); setNotice(undefined); }
    setLabelLoading(true); setLabelNotice(undefined); setLabels([]); setLabelCursor(null);
    try {
      if (opening) {
        const me = await boundedWorkRead(signal => workRequest<{ id: unknown }>('/me', { signal }), controller.signal);
        if (ticket !== epoch.current) return;
        if (!uuid(me?.id)) throw new Error('Invalid identity');
        setIdentity(me.id); setCriteria(saved(storageKey(me.id)));
      }
      const p = await boundedWorkRead(signal => workRequest<Record<string, unknown>>(`/boards/${encodeURIComponent(board)}/labels${after ? `?after=${encodeURIComponent(after)}` : ''}`, { signal }), controller.signal);
      if (ticket !== epoch.current) return;
      if (p.organizationId !== org || p.boardId !== board || !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid labels');
      const items = p.items.map((value: unknown) => {
        const l = value as Record<string, unknown> | null;
        if (!l || !uuid(l.id) || l.organizationId !== org || l.boardId !== board || l.deleted !== false || typeof l.name !== 'string'
          || l.name.length > 160 || typeof l.color !== 'string' || !palette.includes(l.color)) throw new Error('Invalid label');
        return { id: l.id, name: l.name, color: l.color };
      });
      if (new Set(items.map(l => l.id)).size !== items.length || items.some(l => l.id === after)
        || (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.id))) throw new Error('Invalid cursor');
      setLabels(items); setLabelCursor(p.nextCursor as string | null);
    } catch (error) {
      if (ticket !== epoch.current) return;
      setLabelNotice('Label choices could not be loaded. Reload choices to continue.');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) { setLabelNotice('Filters are unavailable. Sign in or refresh the Board to check your access.'); setOpen(false); setIdentity(undefined); setResult(undefined); onRefresh(); }
    } finally { if (ticket === epoch.current) { pending.current = undefined; setLabelLoading(false); } }
  }
  const refreshChoices = useEffectEvent(() => {
    if (!open || !identity || disabled || !available) return;
    epoch.current++; pending.current?.abort(); pending.current = undefined;
    void loadLabels();
  });
  useEffect(() => { refreshChoices(); }, [snapshot, disabled]);
  // Every canonical refresh invalidates the result page, including realtime
  // changes and recovery after disconnect. Never filter the six face indicators.
  useEffect(() => {
    if (!open || !applied || !identity || disabled || !available) { setResult(undefined); return; }
    let active = true; const controller = new AbortController(); setResult(undefined); setLoading(true); setNotice(undefined);
    const query = new URLSearchParams({ keyword: applied.keyword, labels: applied.labels.join(','), match: applied.match });
    if (cursor) query.set('after', cursor);
    void boundedWorkRead(signal => workRequest<Record<string, unknown>>(`/boards/${encodeURIComponent(board)}/cards?${query}`, { signal }), controller.signal).then(p => {
      if (!active) return;
      if (p.organizationId !== org || p.boardId !== board || !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid result');
      const items = p.items.map((value: unknown) => {
        const c = value as Record<string, unknown> | null;
        if (!c || !uuid(c.id) || !uuid(c.listId) || c.organizationId !== org || c.boardId !== board || c.lifecycleState !== 'active'
          || typeof c.title !== 'string' || !c.title.trim() || c.title.length > 500 || (c.description !== null && typeof c.description !== 'string')
          || !Number.isSafeInteger(c.version) || (c.version as number) < 1 || typeof c.rank !== 'string' || !/^\d{30}$/.test(c.rank)) throw new Error('Invalid Card');
        return c as unknown as WorkCard & { listId: string };
      });
      if (new Set(items.map(c => c.id)).size !== items.length || items.some(c => c.id === cursor)
        || (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.id))) throw new Error('Invalid cursor');
      setResult({ items, next: p.nextCursor as string | null });
    }).catch(error => {
      if (!active) return;
      setResult(undefined); setNotice('Filtered Cards could not be loaded. Try again or refresh the Board.');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) { setOpen(false); setIdentity(undefined); setApplied(undefined); onRefresh(); }
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); setLoading(false); };
  }, [open, applied, identity, disabled, available, org, board, snapshot, cursor, retry, onRefresh]);
  function apply() {
    if (!identity || disabled || labelLoading) return;
    const next = { ...criteria, keyword: criteria.keyword.trim(), labels: [...criteria.labels] };
    try { sessionStorage.setItem(storageKey(identity), JSON.stringify(next)); } catch { /* Optional persistence. */ }
    setApplied(next); setCursor(undefined);
  }
  function close() { epoch.current++; pending.current?.abort(); pending.current = undefined; setLabelLoading(false); setOpen(false); setResult(undefined); }
  return <>
    {available && <Button disabled={disabled} onClick={() => void loadLabels(undefined, true)}>Filter Board Cards</Button>}
    {!open && (notice || labelNotice) && <Typography role="status">{notice || labelNotice}</Typography>}
    <Dialog open={open && available} onClose={close} fullWidth maxWidth="sm">
      <DialogTitle>Filter Board Cards</DialogTitle>
      <DialogContent><Stack spacing={2}>
        <TextField label="Card keyword" value={criteria.keyword} onChange={e => setCriteria(c => ({ ...c, keyword: e.target.value }))} disabled={disabled || labelLoading || !identity} slotProps={{ htmlInput: { maxLength: 160 } }} />
        <TextField select label="Match filters" value={criteria.match} onChange={e => setCriteria(c => ({ ...c, match: e.target.value as Criteria['match'] }))} disabled={disabled || labelLoading || !identity}>
          <MenuItem value="all">Match ALL</MenuItem><MenuItem value="any">Match ANY</MenuItem>
        </TextField>
        <Typography>Choose up to 25 labels. Selected labels stay selected when you change choice pages.</Typography>
        {labelLoading && <Typography role="status">Loading filter choices…</Typography>}
        {labelNotice && <Alert severity="warning">{labelNotice}</Alert>}
        {!labelLoading && !labelNotice && identity && labels.length === 0 && <Typography>No labels on this choice page. Use a keyword, or reload choices.</Typography>}
        {labels.map(l => <FormControlLabel key={l.id} label={`${l.name || 'Unnamed label'} (${l.color})`} control={<Checkbox checked={criteria.labels.includes(l.id)} disabled={disabled || labelLoading || (!criteria.labels.includes(l.id) && criteria.labels.length >= 25)} onChange={e => setCriteria(c => ({ ...c, labels: e.target.checked ? [...c.labels, l.id] : c.labels.filter(id => id !== l.id) }))} />} />)}
        <Typography>{criteria.labels.length} selected labels</Typography>
        <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          <Button disabled={disabled || labelLoading} onClick={() => void loadLabels()}>Reload label choices</Button>
          {labelCursor && <Button disabled={disabled || labelLoading} onClick={() => void loadLabels(labelCursor!)}>Next label choices</Button>}
          <Button disabled={disabled || labelLoading || !identity} onClick={apply}>Apply filters</Button>
          <Button disabled={disabled || labelLoading || !identity} onClick={() => { setCriteria(empty()); setApplied(undefined); setResult(undefined); setCursor(undefined); try { sessionStorage.removeItem(storageKey(identity!)); } catch { /* Optional storage. */ } }}>Clear filters</Button>
        </Stack>
        {loading && <Typography role="status">Loading filtered Cards…</Typography>}
        {notice && <Alert severity="warning">{notice}</Alert>}
        {notice && <Button disabled={disabled || loading} onClick={() => setRetry(n => n + 1)}>Retry filtered Cards</Button>}
        {result && <Stack component="section" aria-label="Filtered Cards" spacing={1}>
          <Typography variant="body2">Results use the last applied filters. Apply again after changing the fields.</Typography>
          <Typography role="status">{result.items.length ? `${result.items.length} Cards on this page` : 'No Cards match these filters.'}</Typography>
          {result.items.map(c => <Button key={c.id} component={Link} to={`/app/${org}/boards/${board}/cards/${c.id}`} onClick={close}>{c.title} — {snapshot.lists.find(l => l.list.id === c.listId)?.list.name ?? 'Board List'}</Button>)}
          {result.next && <Button onClick={() => setCursor(result.next!)}>Next filtered Cards</Button>}
          {cursor && <Button onClick={() => setCursor(undefined)}>First filtered Cards</Button>}
        </Stack>}
      </Stack></DialogContent><DialogActions><Button onClick={close}>Close filters</Button></DialogActions>
    </Dialog>
  </>;
}
