import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';

const palette = ['green', 'yellow', 'orange', 'red', 'purple', 'blue', 'sky', 'lime', 'pink', 'black'];
const uuid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000';
type Label = { id: string; organizationId: string; boardId: string; name: string; color: string; rank: string; version: number; deleted: boolean };
type Page = { items: Label[]; next: string | null; canDelete: boolean };
type Intent = { kind: 'edit' | 'delete' | 'move'; label: Label; name: string; color: string; before: string | null; key: string };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onBusyChange: (busy: boolean) => void;
  onRecoveryChange: (pending: boolean) => void; onRefresh: () => void; onReturnFocus: () => void };
function label(value: unknown, snapshot: BoardSnapshot): Label {
  if (!value || typeof value !== 'object') throw new Error('Invalid label');
  const l = value as Label;
  if (!uuid(l.id) || l.organizationId !== snapshot.board.organizationId || l.boardId !== snapshot.board.id
    || typeof l.name !== 'string' || l.name.length > 160 || !palette.includes(l.color) || typeof l.rank !== 'string' || !/^\d{30}$/.test(l.rank)
    || !Number.isSafeInteger(l.version) || l.version < 1 || typeof l.deleted !== 'boolean') throw new Error('Invalid label');
  return l;
}
function directory(value: unknown, snapshot: BoardSnapshot, after?: string): Page {
  if (!value || typeof value !== 'object') throw new Error('Invalid directory');
  const p = value as Record<string, unknown>;
  if (p.organizationId !== snapshot.board.organizationId || p.boardId !== snapshot.board.id || p.canEdit !== true
    || typeof p.canDelete !== 'boolean' || !Array.isArray(p.items) || p.items.length > 50) throw new Error('Invalid directory');
  const items = p.items.map(v => label(v, snapshot));
  if (items.some(l => l.deleted || l.id === after) || new Set(items.map(l => l.id)).size !== items.length
    || (p.nextCursor !== null && (!uuid(p.nextCursor) || items.length !== 50 || p.nextCursor !== items.at(-1)?.id || p.nextCursor === after))) throw new Error('Invalid page');
  return { items, next: p.nextCursor as string | null, canDelete: p.canDelete };
}
export function LabelManageControl({ snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [open, setOpen] = useState(false); const [page, setPage] = useState<Page>(); const [selected, setSelected] = useState<Label>();
  const [name, setName] = useState(''); const [color, setColor] = useState('green'); const [before, setBefore] = useState('');
  const [confirmed, setConfirmed] = useState(false); const [intent, setIntent] = useState<Intent>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined); const epoch = useRef(0);
  const restoreFocus = useRef(false);
  const available = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active';
  const admin = snapshot.access.canAdminister;
  useEffect(() => {
    if (restoreFocus.current && !open && !busy && !disabled && available) { restoreFocus.current = false; onReturnFocus(); }
  }, [open, busy, disabled, available, onReturnFocus]);
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => {
    epoch.current++; pending.current?.abort(); pending.current = undefined; onBusyChange(false); setBusy(false);
    setPage(undefined); setSelected(undefined); setIntent(undefined); setNotice(undefined); setConfirmed(false);
    if (!available) setOpen(false);
    return () => { epoch.current++; pending.current?.abort(); pending.current = undefined; onBusyChange(false); };
  }, [available, admin, snapshot.board.id, snapshot.board.organizationId, onBusyChange]);
  async function read(after?: string, keepSelection = false) {
    if (!available || disabled || pending.current || intent) return;
    const controller = new AbortController(); pending.current = controller; const ticket = epoch.current;
    setBusy(true); onBusyChange(true); setNotice(undefined); setPage(undefined); setBefore(''); setConfirmed(false);
    if (!keepSelection) setSelected(undefined);
    try {
      const value = await boundedWorkRead(signal => workRequest<unknown>(`/boards/${encodeURIComponent(snapshot.board.id)}/labels${after ? `?after=${encodeURIComponent(after)}` : ''}`, { signal }), controller.signal);
      if (ticket === epoch.current) setPage(directory(value, snapshot, after));
    } catch (error) {
      if (ticket !== epoch.current) return;
      setNotice('Unable to load current labels. Reload labels or refresh the Board.');
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) { setOpen(false); onRefresh(); }
    } finally { if (ticket === epoch.current) { pending.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  async function submit(kind: Intent['kind']) {
    if (!available || disabled || pending.current || (!intent && !selected)) return;
    if (!intent && (kind === 'delete' ? !admin || !page?.canDelete || !confirmed : name.trim().length > 160 || !palette.includes(color))) return;
    const command = intent ?? { kind, label: selected!, name: name.trim(), color, before: before || null, key: crypto.randomUUID() };
    if (command.kind === 'delete' && !admin) return;
    const base = `/labels/${encodeURIComponent(command.label.id)}`;
    const path = command.kind === 'delete' ? `${base}?version=${command.label.version}&confirmed=true` : command.kind === 'move' ? `${base}/move` : base;
    const body = command.kind === 'edit' ? JSON.stringify({ name: command.name, color: command.color, version: command.label.version })
      : command.kind === 'move' ? JSON.stringify({ beforeLabelId: command.before, version: command.label.version }) : undefined;
    const controller = new AbortController(); pending.current = controller; const ticket = epoch.current;
    setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await boundedWorkRead(signal => workRequest<unknown>(path, { signal,
        method: command.kind === 'delete' ? 'DELETE' : command.kind === 'move' ? 'POST' : 'PATCH',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body }), controller.signal);
      if (ticket !== epoch.current) return;
      const updated = label(value, snapshot);
      if (updated.id !== command.label.id || updated.version !== command.label.version + 1 || updated.deleted !== (command.kind === 'delete')
        || updated.name !== (command.kind === 'edit' ? command.name : command.label.name)
        || updated.color !== (command.kind === 'edit' ? command.color : command.label.color)
        || (command.kind !== 'move' && updated.rank !== command.label.rank)) throw new Error('Unconfirmed response');
      setIntent(undefined); setPage(undefined); setSelected(undefined); setConfirmed(false);
      setNotice('Label change confirmed. Reload labels to continue.'); onRefresh();
    } catch (error) {
      if (ticket !== epoch.current) return;
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        setIntent(undefined); setPage(undefined); setSelected(undefined); setOpen(false); onRefresh();
      } else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) {
        setIntent(undefined); setPage(undefined); setSelected(undefined); setConfirmed(false); setNotice(error.message); onRefresh();
      } else { setIntent(command); setNotice('The change may have completed. Retry this same change to confirm its result.'); }
    } finally { if (ticket === epoch.current) { pending.current = undefined; setBusy(false); onBusyChange(false); } }
  }
  const close = () => { if (!busy && !intent) { setOpen(false); setSelected(undefined); setPage(undefined); } };
  return <>
    {available && <Button disabled={disabled || busy || !!intent} onClick={() => { setOpen(true); void read(); }}>Manage labels</Button>}
    <Dialog open={open && available} onClose={close} fullWidth maxWidth="sm" disableRestoreFocus slotProps={{ transition: { onExited: () => {
      restoreFocus.current = available;
      if (available && !disabled && !busy) { restoreFocus.current = false; onReturnFocus(); }
    } } }}>
      <DialogTitle>Manage Board labels</DialogTitle>
      <DialogContent><Stack spacing={2}>
        {notice && <Alert severity={intent ? 'warning' : 'info'}>{notice}</Alert>}
        {busy && <Typography role="status">Loading label change…</Typography>}
        {intent ? <><Typography>Confirm {intent.kind} for {intent.label.name || 'an unnamed label'} ({intent.label.color}).</Typography>
          <Button disabled={disabled || busy} onClick={() => void submit(intent.kind)}>Retry label change</Button></> : <>
          <Button disabled={disabled || busy} onClick={() => void read()}>Reload labels</Button>
          {page && <>
            {!page.items.length && <Typography>No Board labels.</Typography>}
            {[...page.items].sort((a, b) => a.rank.localeCompare(b.rank) || a.id.localeCompare(b.id)).map(l =>
              <Button key={l.id} disabled={disabled || busy} onClick={() => { setSelected(l); setName(l.name); setColor(l.color); setBefore(''); setConfirmed(false); setNotice(undefined); }}>
                Edit {l.name || 'unnamed label'} ({l.color})
              </Button>)}
            {page.next && <Button disabled={disabled || busy} onClick={() => void read(page.next!, !!selected)}>{selected ? 'Next destination labels' : 'Next labels'}</Button>}
            {selected && <Stack component="section" aria-label="Selected label" spacing={2}>
              <TextField label="Label name (optional)" value={name} onChange={e => setName(e.target.value)} disabled={disabled || busy} slotProps={{ htmlInput: { maxLength: 160 } }} />
              <TextField select label="Label color" value={color} onChange={e => setColor(e.target.value)} disabled={disabled || busy}>
                {palette.map(c => <MenuItem key={c} value={c}>{c[0].toUpperCase() + c.slice(1)}</MenuItem>)}
              </TextField>
              <Button disabled={disabled || busy} onClick={() => void submit('edit')}>Save label</Button>
              <TextField select label="Move label before" value={before} onChange={e => setBefore(e.target.value)} disabled={disabled || busy}>
                <MenuItem value="">End of Board labels</MenuItem>
                {page.items.filter(l => l.id !== selected.id).map(l => <MenuItem key={l.id} value={l.id}>{l.name || 'Unnamed label'} ({l.color})</MenuItem>)}
              </TextField>
              <Button disabled={disabled || busy} onClick={() => void read(undefined, true)}>First destination labels</Button>
              <Typography variant="body2">Ordering uses the saved label name and color. Save field changes first. Choose a destination from this page, or the end of all Board labels.</Typography>
              <Button disabled={disabled || busy} onClick={() => void submit('move')}>Move label</Button>
              {admin && page.canDelete && <>
                <Typography>Deleting this label removes it from every Card on this Board.</Typography>
                <FormControlLabel control={<Checkbox checked={confirmed} onChange={e => setConfirmed(e.target.checked)} disabled={disabled || busy} />} label="Confirm removal from all Cards" />
                <Button color="error" disabled={disabled || busy || !confirmed} onClick={() => void submit('delete')}>Delete label</Button>
              </>}
            </Stack>}
          </>}
        </>}
      </Stack></DialogContent>
      <DialogActions><Button disabled={busy || !!intent} onClick={close}>Done</Button></DialogActions>
    </Dialog>
  </>;
}
