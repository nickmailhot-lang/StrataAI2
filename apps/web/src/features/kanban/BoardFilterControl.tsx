import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot, type WorkCard } from '../../api/workManagement';
import { filterPageMatchesSnapshot, type BoardCanvasFilter } from './boardFilterCanvas';

const uuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000';
const palette = ['green', 'yellow', 'orange', 'red', 'purple', 'blue', 'sky', 'lime', 'pink', 'black'];
type Criteria = { keyword: string; labels: string[]; members: string[]; match: 'all' | 'any' };
type Label = { id: string; name: string; color: string };
type Member = { userId: string; displayName: string };
type Result = { items: (WorkCard & { listId: string })[]; next: string | null };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onRefresh: () => void; onCanvasChange?: (page: BoardCanvasFilter | undefined) => void };
const empty = (): Criteria => ({ keyword: '', labels: [], members: [], match: 'all' });
function saved(key: string): { criteria: Criteria; canvas: boolean } {
  try {
    const c = JSON.parse(sessionStorage.getItem(key) ?? 'null') as (Criteria & { canvas?: unknown }) | null;
    if (c && typeof c.keyword === 'string' && c.keyword.length <= 160 && (c.match === 'all' || c.match === 'any')
      && Array.isArray(c.labels) && c.labels.length <= 25 && c.labels.every(uuid) && new Set(c.labels).size === c.labels.length
      && (c.members === undefined || (Array.isArray(c.members) && c.members.length <= 25 && c.members.every(uuid) && new Set(c.members.map(id => id.toLowerCase())).size === c.members.length)))
      return { criteria: { keyword: c.keyword, labels: c.labels, members: (c.members ?? []).map(id => id.toLowerCase()), match: c.match }, canvas: c.canvas === true };
  } catch { /* Storage is optional; admitted server reads remain authoritative. */ }
  return { criteria: empty(), canvas: false };
}
export function BoardFilterControl({ snapshot, disabled, onRefresh, onCanvasChange }: Props) {
  const [canvasMode, setCanvasMode] = useState(false);
  const [openingPending, setOpeningPending] = useState<string>();
  const [open, setOpen] = useState(false); const [criteria, setCriteria] = useState<Criteria>(empty);
  const [applied, setApplied] = useState<Criteria>(); const [labels, setLabels] = useState<Label[]>([]);
  const [labelCursor, setLabelCursor] = useState<string | null>(null); const [result, setResult] = useState<Result>();
  const [cursor, setCursor] = useState<string>(); const [retry, setRetry] = useState(0);
  const [identity, setIdentity] = useState<string>(); const [loading, setLoading] = useState(false);
  const [labelLoading, setLabelLoading] = useState(false); const [notice, setNotice] = useState<string>();
  const [labelNotice, setLabelNotice] = useState<string>();
  const [memberOpen, setMemberOpen] = useState(false); const [members, setMembers] = useState<Member[]>([]);
  const [memberCursor, setMemberCursor] = useState<string | null>(null); const [memberLoading, setMemberLoading] = useState(false);
  const [memberNotice, setMemberNotice] = useState<string>(); const memberPending = useRef<AbortController | undefined>(undefined);
  const epoch = useRef(0); const pending = useRef<AbortController | undefined>(undefined);
  const org = snapshot.board.organizationId, board = snapshot.board.id;
  const available = snapshot.access.canView && snapshot.board.lifecycleState === 'active';
  const storageKey = (actor: string) => `strataai:board-filter:v1:${actor}:${org}:${board}`;
  useEffect(() => {
    onCanvasChange?.(canvasMode && applied && available ? { snapshot, items: !disabled && result ? result.items : [] } : undefined);
  }, [canvasMode, applied, available, disabled, result, snapshot, onCanvasChange]);
  useEffect(() => () => onCanvasChange?.(undefined), [onCanvasChange]);
  useEffect(() => () => { epoch.current++; pending.current?.abort(); memberPending.current?.abort(); }, []);
  useEffect(() => {
    epoch.current++; pending.current?.abort(); pending.current = undefined;
    memberPending.current?.abort(); memberPending.current = undefined; setMemberOpen(false); setMembers([]); setMemberCursor(null); setMemberLoading(false); setMemberNotice(undefined);
    setLabels([]); setLabelCursor(null); setResult(undefined); setIdentity(undefined); setApplied(undefined); setLoading(false); setLabelLoading(false);
    setNotice(undefined); setLabelNotice(undefined);
    setCriteria(empty()); setOpen(false); setCursor(undefined);
    setOpeningPending(undefined);
    setCanvasMode(false);
  }, [org, board, available]);
  async function loadLabels(after?: string, opening = false, restoring = false) {
    if (!available || disabled || pending.current) return;
    const controller = new AbortController(); pending.current = controller; const ticket = epoch.current;
    if (opening) {
      setOpeningPending(undefined);
      memberPending.current?.abort(); memberPending.current = undefined; setMemberOpen(false); setMembers([]); setMemberCursor(null); setMemberLoading(false); setMemberNotice(undefined);
      setCanvasMode(false); setOpen(!restoring); setCriteria(empty()); setIdentity(undefined); setResult(undefined); setApplied(undefined); setCursor(undefined); setNotice(undefined);
    }
    setLabelLoading(true); setLabelNotice(undefined); setLabels([]); setLabelCursor(null);
    try {
      if (opening) {
        const me = await boundedWorkRead(signal => workRequest<{ id: unknown }>('/me', { signal }), controller.signal);
        if (ticket !== epoch.current) return;
        if (!uuid(me?.id)) throw new Error('Invalid identity');
        const stored = saved(storageKey(me.id)); setIdentity(me.id); setCriteria(stored.criteria);
        if (restoring && stored.canvas) { setApplied(stored.criteria); setCanvasMode(true); }
        if (!restoring) try { sessionStorage.setItem(storageKey(me.id), JSON.stringify(stored.criteria)); } catch { /* Optional storage. */ }
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
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        memberPending.current?.abort(); memberPending.current = undefined; setMembers([]); setMemberLoading(false);
        setLabelNotice('Filters are unavailable. Sign in or refresh the Board to check your access.'); setOpen(false); setIdentity(undefined); setResult(undefined); setApplied(undefined); setCanvasMode(false); onRefresh();
      }
    } finally { if (ticket === epoch.current) { pending.current = undefined; setLabelLoading(false); } }
  }
  async function loadMembers(after?: string) {
    if (!available || disabled || !open || !identity || memberPending.current || snapshot.cardMembers === null) return;
    const controller = new AbortController(); memberPending.current = controller; const ticket = epoch.current;
    setMemberOpen(true); setMemberLoading(true); setMemberNotice(undefined); setMembers([]); setMemberCursor(null);
    try {
      const p = await boundedWorkRead(signal => workRequest<Record<string, unknown>>(`/boards/${encodeURIComponent(board)}/assignable-members${after ? `?after=${encodeURIComponent(after)}` : ''}`, { signal }), controller.signal);
      if (ticket !== epoch.current || controller.signal.aborted) return;
      if (p.organizationId !== org || p.boardId !== board || !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid member choices');
      let previous = after?.toLowerCase();
      const items = p.items.map((value: unknown): Member => {
        const m = value as Record<string, unknown> | null;
        if (!m || !uuid(m.userId) || typeof m.displayName !== 'string' || m.displayName.length > 160
          || (previous && m.userId.toLowerCase() <= previous)) throw new Error('Invalid member choice');
        previous = m.userId.toLowerCase(); return { userId: previous, displayName: m.displayName };
      });
      if (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.userId)) throw new Error('Invalid member cursor');
      setMembers(items); setMemberCursor(p.nextCursor as string | null);
    } catch (error) {
      if (ticket !== epoch.current || controller.signal.aborted) return;
      setMembers([]); setMemberNotice('Assignee choices could not be loaded. Reload choices to continue.');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        setOpen(false); setIdentity(undefined); setResult(undefined); setApplied(undefined); setCanvasMode(false); onRefresh();
      }
    } finally { if (ticket === epoch.current) { memberPending.current = undefined; setMemberLoading(false); } }
  }
  const restoreSavedCanvas = useEffectEvent(() => {
    if (!onCanvasChange || !available || disabled || openingPending || identity || pending.current) return;
    try {
      if (Object.keys(sessionStorage).some(key => key.startsWith('strataai:board-filter:v1:') && key.endsWith(`:${org}:${board}`) && saved(key).canvas))
        void loadLabels(undefined, true, true);
    } catch { /* Optional storage. */ }
  });
  useEffect(() => { restoreSavedCanvas(); }, [org, board, available, disabled, identity]);
  const admitOpening = useEffectEvent(() => {
    if (openingPending === `${org}/${board}` && available && !disabled && !pending.current) void loadLabels(undefined, true);
  });
  useEffect(() => { admitOpening(); }, [openingPending, available, disabled, labelLoading]);
  const refreshChoices = useEffectEvent(() => {
    if (!open || !identity || disabled || !available) return;
    epoch.current++; pending.current?.abort(); pending.current = undefined;
    memberPending.current?.abort(); memberPending.current = undefined;
    setMembers([]); setMemberCursor(null); setMemberLoading(false);
    void loadLabels();
    if (memberOpen) void loadMembers();
  });
  useEffect(() => { refreshChoices(); }, [snapshot, disabled]);
  // Every canonical refresh invalidates the result page, including realtime
  // changes and recovery after disconnect. Never filter the six face indicators.
  useEffect(() => {
    if ((!open && !canvasMode) || !applied || !identity || disabled || !available) { setResult(undefined); return; }
    let active = true; const controller = new AbortController(); setResult(undefined); setLoading(true); setNotice(undefined);
    const query = new URLSearchParams({ keyword: applied.keyword, labels: applied.labels.join(','), match: applied.match });
    if (applied.members.length) query.set('members', applied.members.join(','));
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
      if (onCanvasChange && !filterPageMatchesSnapshot(snapshot, items)) {
        setNotice('The Board changed while filters were loading. Checking the current Board.'); onRefresh(); return;
      }
      setResult({ items, next: p.nextCursor as string | null });
    }).catch(error => {
      if (!active) return;
      setResult(undefined); setNotice('Filtered Cards could not be loaded. Try again or refresh the Board.');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) { setOpen(false); setIdentity(undefined); setApplied(undefined); onRefresh(); }
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); setLoading(false); };
  }, [open, canvasMode, applied, identity, disabled, available, org, board, snapshot, cursor, retry, onRefresh, onCanvasChange]);
  function clear() {
    setCanvasMode(false); setCriteria(empty()); setApplied(undefined); setResult(undefined); setCursor(undefined);
    if (identity) try { sessionStorage.removeItem(storageKey(identity)); } catch { /* Optional storage. */ }
  }
  function apply() {
    if (!identity || disabled || labelLoading || memberLoading) return;
    const next = { ...criteria, keyword: criteria.keyword.trim(), labels: [...criteria.labels], members: [...criteria.members] };
    try { sessionStorage.setItem(storageKey(identity), JSON.stringify(next)); } catch { /* Optional persistence. */ }
    setApplied(next); setCursor(undefined);
  }
  function close() { epoch.current++; pending.current?.abort(); pending.current = undefined; memberPending.current?.abort(); memberPending.current = undefined; setMemberLoading(false); setMembers([]); setMemberOpen(false); setLabelLoading(false); setOpen(false); setOpeningPending(undefined); setResult(undefined); }
  return <>
    {available && <Button onClick={() => { setOpen(true); setOpeningPending(`${org}/${board}`); }}>Filter Board Cards</Button>}
    {canvasMode && !open && <Stack spacing={1}>
      <Typography role="status">{result ? `Filtered Board: ${result.items.length} matching Cards on this page.` : 'Checking filtered Board Cards…'}</Typography>
      <Typography variant="body2">Open a Card to move it, or clear filters to reorder the full Board.</Typography>
      <Typography variant="body2">List operations still apply to every Card in that List.</Typography>
      <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
        {result?.next && <Button disabled={disabled || loading} onClick={() => setCursor(result.next!)}>Next filtered Cards</Button>}
        {cursor && <Button disabled={disabled || loading} onClick={() => setCursor(undefined)}>First filtered Cards</Button>}
        <Button disabled={disabled} onClick={clear}>Clear Board filters</Button>
        {notice && <Button disabled={disabled || loading} onClick={() => setRetry(n => n + 1)}>Retry filtered Cards</Button>}
      </Stack>
    </Stack>}
    {!open && (notice || labelNotice) && <Typography role="status">{notice || labelNotice}</Typography>}
    <Dialog open={open && available} onClose={close} fullWidth maxWidth="sm">
      <DialogTitle>Filter Board Cards</DialogTitle>
      <DialogContent>{openingPending ? <Typography role="status">Checking current Board access…</Typography> : <Stack spacing={2}>
        <TextField label="Card keyword" value={criteria.keyword} onChange={e => { const keyword = e.target.value; setCriteria(c => ({ ...c, keyword })); }} disabled={disabled || labelLoading || !identity} slotProps={{ htmlInput: { maxLength: 160 } }} />
        <TextField select label="Match filters" value={criteria.match} onChange={e => { const match = e.target.value as Criteria['match']; setCriteria(c => ({ ...c, match })); }} disabled={disabled || labelLoading || !identity}>
          <MenuItem value="all">Match ALL</MenuItem><MenuItem value="any">Match ANY</MenuItem>
        </TextField>
        <Typography>Choose up to 25 labels. Selected labels stay selected when you change choice pages.</Typography>
        {labelLoading && <Typography role="status">Loading filter choices…</Typography>}
        {labelNotice && <Alert severity="warning">{labelNotice}</Alert>}
        {!labelLoading && !labelNotice && identity && labels.length === 0 && <Typography>No labels on this choice page. Use a keyword, or reload choices.</Typography>}
        {labels.map(l => <FormControlLabel key={l.id} label={`${l.name || 'Unnamed label'} (${l.color})`} control={<Checkbox checked={criteria.labels.includes(l.id)} disabled={disabled || labelLoading || (!criteria.labels.includes(l.id) && criteria.labels.length >= 25)} onChange={e => { const checked = e.target.checked; setCriteria(c => ({ ...c, labels: checked ? [...c.labels, l.id] : c.labels.filter(id => id !== l.id) })); }} />} />)}
        <Typography>{criteria.labels.length} selected labels</Typography>
        {snapshot.cardMembers !== null && <>
          <Typography>Choose up to 25 assignees. Selections stay selected when you change choice pages.</Typography>
          <Button disabled={disabled || memberLoading || !identity} onClick={() => void loadMembers()}>{memberOpen ? 'Reload assignee choices' : 'Choose assignees'}</Button>
          {memberLoading && <Typography role="status">Loading assignee choices…</Typography>}
          {memberNotice && <Alert severity="warning">{memberNotice}</Alert>}
          {!disabled && !memberLoading && members.map(m => <FormControlLabel key={m.userId} label={m.displayName.trim() || 'Unnamed member'} control={<Checkbox checked={criteria.members.includes(m.userId)}
            disabled={disabled || (!criteria.members.includes(m.userId) && criteria.members.length >= 25)} onChange={e => { const checked = e.target.checked; setCriteria(c => ({ ...c, members: checked ? [...c.members, m.userId] : c.members.filter(id => id !== m.userId) })); }} />} />)}
          {memberOpen && !memberLoading && !memberNotice && members.length === 0 && <Typography>No eligible assignees on this choice page.</Typography>}
          {memberCursor && <Button disabled={disabled || memberLoading} onClick={() => void loadMembers(memberCursor)}>Next assignee choices</Button>}
          <Typography>{criteria.members.length} selected assignees</Typography>
        </>}
        <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1 }}>
          <Button disabled={disabled || labelLoading} onClick={() => void loadLabels()}>Reload label choices</Button>
          {labelCursor && <Button disabled={disabled || labelLoading} onClick={() => void loadLabels(labelCursor!)}>Next label choices</Button>}
          <Button disabled={disabled || labelLoading || memberLoading || !identity} onClick={apply}>Apply filters</Button>
          <Button disabled={disabled || labelLoading || !identity} onClick={clear}>Clear filters</Button>
        </Stack>
        {loading && <Typography role="status">Loading filtered Cards…</Typography>}
        {notice && <Alert severity="warning">{notice}</Alert>}
        {notice && <Button disabled={disabled || loading} onClick={() => setRetry(n => n + 1)}>Retry filtered Cards</Button>}
        {result && <Stack component="section" aria-label="Filtered Cards" spacing={1}>
          {onCanvasChange && <Button onClick={() => {
            if (identity && applied) try { sessionStorage.setItem(storageKey(identity), JSON.stringify({ ...applied, canvas: true })); } catch { /* Optional storage. */ }
            setCanvasMode(true); close();
          }}>Show this page on Board</Button>}
          <Typography variant="body2">Results use the last applied filters. Apply again after changing the fields.</Typography>
          <Typography role="status">{result.items.length ? `${result.items.length} Cards on this page` : 'No Cards match these filters.'}</Typography>
          {result.items.map(c => <Button key={c.id} component={Link} to={`/app/${org}/boards/${board}/cards/${c.id}`} onClick={close}>{c.title} — {snapshot.lists.find(l => l.list.id === c.listId)?.list.name ?? 'Board List'}</Button>)}
          {result.next && <Button onClick={() => setCursor(result.next!)}>Next filtered Cards</Button>}
          {cursor && <Button onClick={() => setCursor(undefined)}>First filtered Cards</Button>}
        </Stack>}
      </Stack>}</DialogContent><DialogActions><Button onClick={close}>Close filters</Button></DialogActions>
    </Dialog>
  </>;
}
