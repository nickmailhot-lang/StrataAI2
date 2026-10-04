import { useEffect, useRef, useState } from 'react';
import { Button, Stack, TextField, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { normalizeMentionPrefix, parseCardMentionOptions, type CardMentionOption, type CardMentionOptions } from './cardMentionOptions';
import type { AttachmentScope } from './attachments';
import { activityEvent, activityResult } from './activityTelemetry';

type Props = AttachmentScope & { actor: string; version: number; disabled: boolean;
  onSelect: (option: CardMentionOption) => void; onBusyChange: (busy: boolean) => void };
export function CommentMentionPicker(props: Props) {
  const [prefix, setPrefix] = useState(''); const [page, setPage] = useState<CardMentionOptions>();
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string>();
  const current = useRef(props); current.current = props;
  const pending = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); current.current.onBusyChange(false); }; }, []);
  async function search(after?: string) {
    if (pending.current || props.disabled) return;
    let normalized: string; try { normalized = normalizeMentionPrefix(prefix); } catch { setPage(undefined); setNotice('Use a username prefix, up to 40 letters, digits or underscores.'); return; }
    if (after && (!page || page.prefix !== normalized || page.nextCursor !== after)) return;
    const controller = new AbortController(); pending.current = controller; setBusy(true); setPage(undefined); setNotice(undefined); props.onBusyChange(true);
    const captured = props;
    const started = performance.now(); activityEvent('mention_read', 'use');
    try {
      const result = await boundedWorkRead(async signal => {
        const profile = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(profile) || profile.id.toLowerCase() !== captured.actor.toLowerCase()) throw new WorkRequestError(401, null);
        const result = parseCardMentionOptions(await workRequest<unknown>(`/cards/${encodeURIComponent(captured.cardId)}/mention-options?prefix=${encodeURIComponent(normalized)}${after ? '&after=' + encodeURIComponent(after) : ''}`, { signal }), captured, captured.version, normalized, after);
        const checked = await workRequest<unknown>('/me', { signal });
        if (!isNotificationProfile(checked) || checked.id.toLowerCase() !== captured.actor.toLowerCase()) throw new WorkRequestError(401, null);
        return result;
      }, controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (current.current.version !== captured.version || current.current.actor !== captured.actor || current.current.disabled) throw new Error('Teammate context changed');
      activityResult('mention_read', true, started); setPrefix(normalized); setPage(result);
    } catch (error) { if (mounted.current && pending.current === controller) {
      activityResult('mention_read', false, started); if (!(error instanceof WorkRequestError)) activityEvent('mention_read', 'exception');
      setPage(undefined); setNotice('Unable to review current teammates. Refresh the Card before selecting a mention.');
    } }
    finally { if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); current.current.onBusyChange(false); } }
  }
  const admitted = page && page.cardVersion === props.version && !props.disabled;
  return <Stack component="section" aria-label="Mention a teammate" spacing={1}>
    <TextField label="Teammate username prefix" value={prefix} disabled={busy || props.disabled} slotProps={{ htmlInput: { maxLength: 40 } }}
      onChange={event => { setPrefix(event.target.value); setPage(undefined); setNotice(undefined); }} />
    <Button disabled={busy || props.disabled} onClick={() => void search()}>Find teammates</Button>
    {busy && <Typography role="status">Checking current teammates…</Typography>}
    {notice && <Typography role="status">{notice}</Typography>}
    {admitted && <>
      {page.items.length === 0 && <Typography>No current teammates match this username prefix.</Typography>}
      {page.items.map(option => <Button key={option.userId} sx={{ justifyContent: 'flex-start', overflowWrap: 'anywhere', textAlign: 'left' }}
        onClick={() => { if (page.cardVersion === current.current.version && !current.current.disabled) { activityEvent('mention_selection', 'use'); props.onSelect(option); } }}>Mention {option.displayName} (@{option.handle})</Button>)}
      <Button disabled={busy || !page.nextCursor} onClick={() => void search(page.nextCursor!)}>Next teammates</Button>
    </>}
  </Stack>;
}
