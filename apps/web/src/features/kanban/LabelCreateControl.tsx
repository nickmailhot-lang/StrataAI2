import { publicCorrelationReference } from '../../api/correlationReference';
import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError, type BoardSnapshot } from '../../api/workManagement';

const palette = ['green', 'yellow', 'orange', 'red', 'purple', 'blue', 'sky', 'lime', 'pink', 'black'];
type Intent = { name: string; color: string; key: string };
type FailureNotice = { message: string; reference: string | null };
type Props = { snapshot: BoardSnapshot; disabled: boolean; onBusyChange: (busy: boolean) => void;
  onRecoveryChange: (pending: boolean) => void; onRefresh: () => void; onReturnFocus: () => void };
export function LabelCreateControl({ snapshot, disabled, onBusyChange, onRecoveryChange, onRefresh, onReturnFocus }: Props) {
  const [open, setOpen] = useState(false); const [name, setName] = useState(''); const [color, setColor] = useState('green');
  const [intent, setIntent] = useState<Intent>(); const [busy, setBusy] = useState(false); const [notice, setFailure] = useState<FailureNotice>();
  function setNotice(message?: string, reason?: unknown) {
    setFailure(message ? { message, reference: reason instanceof WorkRequestError
      ? publicCorrelationReference(reason.correlationId) : null } : undefined);
  }
  const pending = useRef<AbortController | undefined>(undefined); const epoch = useRef(0);
  const available = snapshot.access.canEdit && snapshot.board.lifecycleState === 'active';
  useEffect(() => { onRecoveryChange(!!intent); return () => onRecoveryChange(false); }, [intent, onRecoveryChange]);
  useEffect(() => {
    epoch.current++;
    if (!available) { setOpen(false); setIntent(undefined); setNotice(undefined); setBusy(false); }
    return () => { epoch.current++; pending.current?.abort(); pending.current = undefined; onBusyChange(false); };
  }, [available, snapshot.board.id, snapshot.board.organizationId, onBusyChange]);
  const close = () => { if (!busy && !intent) setOpen(false); };
  async function submit() {
    if (!available || disabled || pending.current) return;
    if (!intent && (name.trim().length > 160 || !palette.includes(color))) { setNotice('Use at most 160 characters and choose a label color.'); return; }
    const command = intent ?? { name: name.trim(), color, key: crypto.randomUUID() };
    const controller = new AbortController(); pending.current = controller; const admittedEpoch = epoch.current;
    setBusy(true); onBusyChange(true); setNotice(undefined);
    try {
      const value = await boundedWorkRead(signal => workRequest<unknown>(`/boards/${encodeURIComponent(snapshot.board.id)}/labels`, {
        method: 'POST', signal, headers: { 'Content-Type': 'application/json', 'Idempotency-Key': command.key }, body: JSON.stringify({ name: command.name, color: command.color }),
      }), controller.signal);
      if (admittedEpoch !== epoch.current) return;
      const label = value as Record<string, unknown> | null;
      if (!label || typeof label !== 'object' || typeof label.id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(label.id)
        || label.id === '00000000-0000-0000-0000-000000000000' || label.boardId !== snapshot.board.id || label.organizationId !== snapshot.board.organizationId
        || label.name !== command.name || label.color !== command.color || label.version !== 1 || label.deleted !== false
        || typeof label.rank !== 'string' || !/^\d{30}$/.test(label.rank)) throw new Error('Unconfirmed label response');
      setIntent(undefined); setOpen(false); setName(''); onRefresh();
    } catch (error) {
      if (admittedEpoch !== epoch.current) return;
      if (error instanceof WorkRequestError && [401, 403, 404].includes(error.status)) {
        setIntent(undefined); setOpen(false); onRefresh();
      } else if (error instanceof WorkRequestError && [400, 409].includes(error.status)) {
        setIntent(undefined); setNotice(error.message, error);
      } else {
        setIntent(command); setNotice('The label may have been created. Retry this same submission to confirm the result.', error);
      }
    } finally {
      if (admittedEpoch === epoch.current) { pending.current = undefined; setBusy(false); onBusyChange(false); }
    }
  }
  return <>
    {available && <Button disabled={disabled || busy || !!intent} onClick={() => { setNotice(undefined); setOpen(true); }}>Create label</Button>}
    <Dialog open={open && available} onClose={close} fullWidth maxWidth="xs" disableRestoreFocus slotProps={{ transition: { onExited: onReturnFocus } }}>
      <DialogTitle>Create Board label</DialogTitle>
      {open && <form onSubmit={event => { event.preventDefault(); void submit(); }}>
        <DialogContent><Stack spacing={2}>
          {notice && <Alert severity={intent ? 'warning' : 'error'}><span>{notice.message}</span>
            {notice.reference && <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>Reference: {notice.reference}</Typography>}</Alert>}
          {intent ? <Typography>Confirm creation of {intent.name || 'an unnamed label'} ({intent.color}).</Typography> : <>
            <TextField autoFocus label="Label name (optional)" value={name} onChange={event => setName(event.target.value)} disabled={busy} slotProps={{ htmlInput: { maxLength: 160 } }} />
            <TextField select label="Label color" value={color} onChange={event => setColor(event.target.value)} disabled={busy}>
              {palette.map(item => <MenuItem key={item} value={item}>{item[0].toUpperCase() + item.slice(1)}</MenuItem>)}
            </TextField>
          </>}
        </Stack></DialogContent>
        <DialogActions><Button disabled={busy || !!intent} onClick={close}>Cancel</Button><Button type="submit" disabled={busy || disabled}>{busy ? 'Creating…' : intent ? 'Retry label creation' : 'Create'}</Button></DialogActions>
      </form>}
    </Dialog>
  </>;
}
