import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Box, Button, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { ownsRecoveryFocus } from './focusRecovery';
import { parseAttachmentDownloadOptions, parseArchivedAttachmentDownloadOptions, type AttachmentDownloadOptions, type AttachmentScope, type FileAttachmentReview } from './attachments';

type Props = AttachmentScope & { version: number; onRefresh: () => void } & FileAttachmentReview;
export function FileAttachmentPreviewControl(props: Props) {
  if (props.file.scanStatus !== 2 || props.file.mimeType === 'application/pdf') return null;
  return <Preview key={[props.organizationId, props.boardId, props.cardId, props.version, props.file.id, props.file.version, props.archiveReview ? 'archive' : 'active'].join('/')} {...props} />;
}
function Preview(props: Props) {
  const [options, setOptions] = useState<AttachmentDownloadOptions>(); const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string>(); const [failed, setFailed] = useState(false);
  const mounted = useRef(true); const pending = useRef<AbortController | undefined>(undefined);
  const review = useRef<HTMLButtonElement>(null); const focus = useRef(false);
  const path = '/cards/' + encodeURIComponent(props.cardId) + '/attachments/' + (props.archiveReview ? 'archive/' : '') + encodeURIComponent(props.file.id) + '/preview';
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; pending.current?.abort(); }; }, []);
  useEffect(() => {
    if (!options) return;
    const timer = window.setTimeout(() => {
      setOptions(undefined); setNotice('Preview review expired. Check current access again.');
    }, 60000);
    return () => window.clearTimeout(timer);
  }, [options]);
  useLayoutEffect(() => {
    if (busy || !focus.current) return;
    if (ownsRecoveryFocus(document.activeElement, review.current)) review.current?.focus({ preventScroll: true });
    focus.current = false;
  }, [busy]);
  async function check() {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller; focus.current = true;
    setBusy(true); setOptions(undefined); setNotice(undefined); setFailed(false);
    try {
      const profile = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(profile)) throw new Error();
      const value = await boundedWorkRead(signal => workRequest<unknown>(path + '-options', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      const admitted = props.archiveReview
        ? parseArchivedAttachmentDownloadOptions(value, props, props.version, props.file, profile.id)
        : parseAttachmentDownloadOptions(value, props, props.version, props.file, profile.id);
      const current = await boundedWorkRead(signal => workRequest<unknown>('/me', { signal }), controller.signal);
      if (!mounted.current || pending.current !== controller) return;
      if (!isNotificationProfile(current) || current.id.toLowerCase() !== profile.id.toLowerCase()) throw new Error();
      setOptions(admitted); setNotice('Loading image preview…');
    } catch {
      if (mounted.current && pending.current === controller) {
        setFailed(true); setNotice('Image preview is unavailable. It may still be processing. Refresh the Card to check current access.');
      }
    } finally {
      if (mounted.current && pending.current === controller) { pending.current = undefined; setBusy(false); }
    }
  }
  return <Stack spacing={0.5}>
    <Button ref={review} disabled={busy} onBlur={event => { if (!ownsRecoveryFocus(event.relatedTarget, review.current)) focus.current = false; }}
      onClick={() => { void check(); }}>{busy ? 'Checking image preview…' : props.archiveReview ? 'Show archived image preview' : 'Show image preview'}</Button>
    {busy && <Button onClick={() => {
      pending.current?.abort(); pending.current = undefined; setBusy(false); setNotice('Preview review stopped.');
    }}>Stop preview review</Button>}
    {options && <>
      <Box component="img" alt={'Sanitized preview of ' + props.file.displayName} referrerPolicy="no-referrer"
        src={path + '?actorId=' + encodeURIComponent(options.actorId) + '&attachmentVersion=' + options.attachmentVersion}
        sx={{ maxWidth: '100%', maxHeight: 320, objectFit: 'contain', alignSelf: 'flex-start' }}
        onLoad={() => setNotice('Image preview loaded.')}
        onError={() => { setOptions(undefined); setFailed(true); setNotice('Image preview could not be loaded. Refresh the Card to check current access.'); }} />
      <Button onClick={() => { setOptions(undefined); setNotice('Image preview hidden.'); review.current?.focus({ preventScroll: true }); }}>Hide image preview</Button>
    </>}
    {notice && <Typography role={failed ? 'alert' : 'status'}>{notice}</Typography>}
    {failed && <Button onClick={props.onRefresh}>Refresh Card for image preview</Button>}
  </Stack>;
}
