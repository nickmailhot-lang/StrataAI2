import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Button, Link, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { parseAttachmentDownloadOptions, type AttachmentDownloadOptions, type AttachmentScope, type FileAttachment } from './attachments';

type Props = AttachmentScope & { version: number; file: FileAttachment; onRefresh: () => void };
export function FileAttachmentDownloadControl(props: Props) {
  return <Download key={`${props.organizationId}/${props.boardId}/${props.cardId}/${props.version}/${props.file.id}/${props.file.version}`} {...props} />;
}
function Download(props: Props) {
  const [options, setOptions] = useState<AttachmentDownloadOptions>(); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const [failed, setFailed] = useState(false);
  const mounted = useRef(true); const pending = useRef<AbortController | undefined>(undefined); const focus = useRef(false);
  const review = useRef<HTMLButtonElement>(null); const link = useRef<HTMLAnchorElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); }; }, []);
  useEffect(() => {
    if (!options) return;
    const timer = window.setTimeout(() => {
      focus.current = document.activeElement === link.current; setOptions(undefined);
      setNotice('Download review expired. Check current access again.');
    }, 60000);
    return () => window.clearTimeout(timer);
  }, [options]);
  useLayoutEffect(() => {
    if (busy || !focus.current) return;
    if (document.activeElement === document.body || document.activeElement === review.current || document.activeElement === link.current) {
      (options ? link.current : review.current)?.focus({ preventScroll: true }); focus.current = false;
    }
  }, [busy, options]);
  async function check() {
    if (pending.current || props.file.scanStatus !== 2) return;
    const controller = new AbortController(); pending.current = controller; focus.current = true;
    setBusy(true); setOptions(undefined); setFailed(false); setNotice(undefined);
    try {
      const profile = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(profile)) throw new Error();
      const value = await boundedWorkRead(signal => workRequest<unknown>(`/cards/${encodeURIComponent(props.cardId)}/attachments/${encodeURIComponent(props.file.id)}/download-options`, { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const admitted = parseAttachmentDownloadOptions(value, props, props.version, props.file, profile.id);
      const current = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(current) || current.id.toLowerCase() !== profile.id.toLowerCase()) throw new Error();
      setOptions(admitted); setNotice('Current file access checked. Your browser handles the download.');
    } catch {
      if (mounted.current && pending.current === controller) { setFailed(true); setNotice('File download is unavailable. Check current access again or refresh the Card.'); }
    } finally {
      if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); }
    }
  }
  if (props.file.scanStatus !== 2) return null;
  return <Stack spacing={0.5}>
    <Button ref={review} disabled={busy} onBlur={event => { if (event.relatedTarget !== null) focus.current = false; }} onClick={() => { void check(); }}>
      {busy ? 'Checking file download…' : 'Check file download access'}
    </Button>
    {busy && <Button onClick={() => { pending.current?.abort(); pending.current = undefined; setBusy(false); setNotice('Download review stopped.'); }}>Stop download review</Button>}
    {options && <Link ref={link} href={`/cards/${encodeURIComponent(props.cardId)}/attachments/${encodeURIComponent(props.file.id)}/download?actorId=${encodeURIComponent(options.actorId)}&attachmentVersion=${options.attachmentVersion}`}
      target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer" sx={{ overflowWrap: 'anywhere' }}
      onClick={() => setNotice('Download requested. Your browser will report whether it completes.')}>
      Download {props.file.displayName} (opens in a new tab)
    </Link>}
    {notice && <Typography role={failed ? 'alert' : 'status'}>{notice}</Typography>}
    {failed && <Button onClick={props.onRefresh}>Refresh Card for file download</Button>}
  </Stack>;
}
