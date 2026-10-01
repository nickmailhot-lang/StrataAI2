import { useEffect, useEffectEvent, useRef, useState } from 'react';
import { Alert, Button, CircularProgress, Container, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { Link, useParams } from 'react-router-dom';
import { apiFetch } from '../../api/apiFetch';
import { watchBoard, type LiveStatus } from '../../api/boardLive';

type Board = { id: string; organizationId: string; name: string; lifecycleState: string; visibility: string; version: number };
const choices = ['PRIVATE', 'ORGANIZATION', 'PUBLIC'];
function validBoard(value: unknown, org: string, id: string): value is Board {
  const b = value as Board | undefined;
  return !!b && b.id === id && b.organizationId === org && typeof b.name === 'string' && !!b.name.trim()
    && b.lifecycleState === 'active' && choices.includes(b.visibility) && Number.isSafeInteger(b.version) && b.version > 0;
}
export function BoardVisibilityPage() {
  const { organizationId = '', boardId = '' } = useParams();
  return <Visibility key={`${organizationId}:${boardId}`} org={organizationId} id={boardId} />;
}
function Visibility({ org, id }: { org: string; id: string }) {
  const [board, setBoard] = useState<Board>(); const [draft, setDraft] = useState('');
  const [busy, setBusy] = useState(false); const [review, setReview] = useState(false);
  const [error, setError] = useState<string>(); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false); const cancel = useRef<HTMLButtonElement>(null);
  const action = useRef<HTMLButtonElement>(null); const refresh = useRef<HTMLButtonElement>(null);
  const focusRequested = useRef(false);
  const restoreFocus = () => {
    const target = action.current && !action.current.disabled ? action.current : refresh.current;
    focusRequested.current = !target || target.disabled;
    if (target && !target.disabled) target.focus();
  };
  useEffect(() => { if (!busy && !review && focusRequested.current) restoreFocus(); }, [busy, review]);
  const [subscribed, setSubscribed] = useState(false); const [liveStatus, setLiveStatus] = useState<LiveStatus>('connecting');
  const [retryRead, setRetryRead] = useState(false); const queued = useRef(false);
  const mutationWarning = useRef<string | undefined>(undefined);
  const invalidate = useEffectEvent(() => {
    if (pending.current) { queued.current = true; return; }
    setReview(false); setBoard(undefined); setDraft(''); void run(undefined, true);
  });
  useEffect(() => {
    if (!subscribed) return;
    return watchBoard({ organizationId: org, boardId: id, invalidate: () => invalidate(), status: setLiveStatus });
  }, [org, id, subscribed]);
  const retryLatest = useEffectEvent(() => { void run(undefined, true); });
  useEffect(() => {
    if (!retryRead || busy) return;
    const timer = setTimeout(() => retryLatest(), 10_000);
    return () => clearTimeout(timer);
  }, [retryRead, busy]);
  async function request(path: string, options: RequestInit, controller: AbortController) {
    const timer = setTimeout(() => controller.abort(), 15_000);
    let abort: (() => void) | undefined;
    try {
      return await Promise.race([
        apiFetch(path, { ...options, signal: controller.signal }).then(async response => ({
          status: response.status, body: await response.json().catch(() => undefined) as unknown,
        })),
        new Promise<never>((_, reject) => {
          abort = () => reject(new Error('Board request interrupted'));
          controller.signal.addEventListener('abort', abort, { once: true });
        }),
      ]);
    } finally { clearTimeout(timer); if (abort) controller.signal.removeEventListener('abort', abort); }
  }
  async function run(change?: { visibility: string; version: number }, preserveError = false) {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setRetryRead(false);
    if (!preserveError) mutationWarning.current = undefined;
    setError(mutationWarning.current); setNotice(undefined);
    const current = () => mounted.current && pending.current === controller && !controller.signal.aborted;
    const deny = () => { mutationWarning.current = undefined; setSubscribed(false); setRetryRead(false); queued.current = false; setBoard(undefined); setDraft(''); setReview(false); setError('Board visibility administration is unavailable.'); };
    try {
      if (change) {
        const result = await request(`/boards/${encodeURIComponent(id)}/visibility`, { method: 'PATCH',
          headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(change) }, controller);
        if (!current()) return;
        if ([401, 403, 404].includes(result.status)) { deny(); return; }
        if (result.status !== 200 || !validBoard(result.body, org, id) || result.body.visibility !== change.visibility
          || result.body.version <= change.version) {
          setBoard(undefined); setDraft(''); mutationWarning.current = result.status === 409 ? 'The Board changed. Check current visibility before making another change.'
            : 'The change could not be confirmed. Check current visibility before making another change.';
          setError(mutationWarning.current); return;
        }
      }
      const result = await request(`/boards/${encodeURIComponent(id)}`, {}, controller);
      if (!current()) return;
      const scope = result.body as { board?: unknown; access?: { canAdminister?: boolean } } | undefined;
      if ([401, 403, 404].includes(result.status)) { deny(); return; }
      if (result.status !== 200) throw new Error('Board read unavailable');
      if (!validBoard(scope?.board, org, id) || scope?.access?.canAdminister !== true) { deny(); return; }
      setBoard(scope.board); setDraft(scope.board.visibility); setSubscribed(true);
      setNotice(change ? 'Visibility change acknowledged. Current visibility loaded.' : 'Current Board visibility loaded.');
    } catch {
      if (mounted.current && pending.current === controller) {
        setBoard(undefined); setDraft(''); setRetryRead(!change);
        const message = 'Unable to confirm current Board visibility. Please check again.';
        if (change) mutationWarning.current = message;
        setError(message);
      }
    } finally {
      if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); setReview(false);
        const drain = queued.current && !change; queued.current = false;
        if (drain) queueMicrotask(() => { if (mounted.current && !pending.current) void run(undefined, true); }); }
    }
  }
  useEffect(() => { mounted.current = true; void run(); return () => { mounted.current = false; pending.current?.abort(); }; }, []);
  return <Container maxWidth="sm" sx={{ py: 3 }}><Stack spacing={2}>
    <Button component={Link} to={`/app/${org}/boards/${id}`}>Back to Board</Button>
    <Typography component="h1" variant="h4">Board visibility</Typography>
    {board && <Typography component="h2" variant="h6">{board.name}</Typography>}
    {error && <Alert severity="error">{error}</Alert>}{notice && <Alert severity="info">{notice}</Alert>}
    {subscribed && <Typography role="status">{liveStatus === 'live' ? 'Live visibility updates connected.' : 'Visibility updates are reconnecting or checking periodically.'}</Typography>}
    {busy && <CircularProgress aria-label="Checking Board visibility" />}
    <Button ref={refresh} disabled={busy} onClick={() => void run()}>Check current visibility</Button>
    {board && <><Typography>Private Boards require authorized access. Organization Boards can be discovered by eligible members. Public Boards can be read by anyone. Visibility never grants edit access.</Typography>
      <TextField select label="Board visibility" value={draft} disabled={busy} onChange={event => setDraft(event.target.value)}>
        {choices.map(value => <MenuItem key={value} value={value}>{value === 'PRIVATE' ? 'Private' : value === 'ORGANIZATION' ? 'Organization' : 'Public'}</MenuItem>)}
      </TextField><Button ref={action} disabled={busy || draft === board.visibility} onClick={() => setReview(true)}>Review visibility change</Button>
      {board.visibility === 'PUBLIC' && <>
        <TextField label="Public Board link" value={`${window.location.origin}/app/${encodeURIComponent(org)}/boards/${encodeURIComponent(id)}`}
          slotProps={{ input: { readOnly: true } }} helperText="Anyone with this link can read the Board. Editing requires separate permission." />
        <Button component="a" href={`/app/${encodeURIComponent(org)}/boards/${encodeURIComponent(id)}`} target="_blank" rel="noopener noreferrer">Open public Board</Button>
      </>}</>}
    <Dialog open={review && !!board} onClose={() => { if (!busy) setReview(false); }} aria-labelledby="visibility-title"
      slotProps={{ transition: { onEntered: () => cancel.current?.focus(), onExited: restoreFocus } }}>
      <DialogTitle id="visibility-title">Change Board visibility?</DialogTitle>
      <DialogContent><Typography>{board?.name}: {draft}</Typography><Typography>{draft === 'PUBLIC' ? 'Anyone, including people who are not signed in, can read this Board.' : 'This changes who can discover and read this Board.'} Existing membership and edit permissions are managed separately.</Typography></DialogContent>
      <DialogActions><Button ref={cancel} disabled={busy} onClick={() => setReview(false)}>Cancel</Button>
        <Button disabled={busy || !board} onClick={() => board && void run({ visibility: draft.toUpperCase(), version: board.version })}>Confirm visibility change</Button></DialogActions>
    </Dialog>
  </Stack></Container>;
}
