import { useEffect, useId, useRef, useState } from 'react';
import { Box, Button, Link, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { attachmentUrl, parseAttachmentPage, type AttachmentPage, type AttachmentScope } from './attachments';

type Props = AttachmentScope & { version: number; unavailable: boolean; onRefresh: () => void };
export function CardAttachments(props: Props) {
  return <AttachmentDisclosure key={`${props.organizationId}/${props.boardId}/${props.cardId}`} {...props} />;
}
function AttachmentDisclosure(props: Props) {
  const [open, setOpen] = useState(false); const region = useId(); const toggle = useRef<HTMLButtonElement>(null);
  return <Box sx={{ my: 2 }}>
    <Button ref={toggle} aria-expanded={open} aria-controls={region} onClick={() => setOpen(value => !value)}>
      {open ? 'Hide attachments' : 'Show attachments'}
    </Button>
    {open && <Stack id={region} component="section" aria-label="Card attachments" spacing={1}>
      {props.unavailable ? <Typography role="status">Checking current Card access…</Typography>
        : <AttachmentContent key={props.version} {...props} returnToToggle={() => toggle.current?.focus({ preventScroll: true })} />}
    </Stack>}
  </Box>;
}
function AttachmentContent(props: Props & { returnToToggle: () => void }) {
  const [cursor, setCursor] = useState<string>(); const [attempt, setAttempt] = useState(0);
  const [page, setPage] = useState<AttachmentPage>(); const [loading, setLoading] = useState(true); const [error, setError] = useState(false);
  const next = useRef<HTMLButtonElement>(null); const first = useRef<HTMLButtonElement>(null); const retry = useRef<HTMLButtonElement>(null);
  const focus = useRef<'page' | 'retry' | undefined>(undefined);
  const { organizationId, boardId, cardId, version } = props;
  useEffect(() => {
    let active = true; const controller = new AbortController(); setPage(undefined); setError(false); setLoading(true);
    const path = `/cards/${encodeURIComponent(cardId)}/attachments${cursor ? `?after=${encodeURIComponent(cursor)}` : ''}`;
    void boundedWorkRead(signal => workRequest<unknown>(path, { signal }), controller.signal).then(value => {
      if (!active) return;
      const current = parseAttachmentPage(value, { organizationId, boardId, cardId }, cursor);
      if (current.cardVersion !== version) throw new WorkRequestError(409, null);
      setPage(current);
    }).catch(() => { if (active) { setPage(undefined); setError(true); } }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; controller.abort(); };
  }, [organizationId, boardId, cardId, version, cursor, attempt]);
  useEffect(() => {
    if (loading || !focus.current) return;
    // A user moving to another control owns that focus; an async read only
    // returns it when native disabling/removal left the document body focused.
    if (document.activeElement !== document.body) return;
    const target = error ? retry.current : page?.nextCursor ? next.current : cursor ? first.current : undefined;
    if (target && !target.disabled) target.focus({ preventScroll: true }); else props.returnToToggle();
  }, [loading, error, page, cursor, props]);
  const trackFocus = () => { focus.current = 'page'; };
  const trackBlur = (event: React.FocusEvent) => { if (event.relatedTarget !== null) focus.current = undefined; };
  return <>
    {loading && <Typography role="status">Loading attachments…</Typography>}
    {error && <>
      <Typography role="alert">Unable to load current attachments. Refresh the Card to check your access or try again.</Typography>
      <Button ref={retry} onFocus={() => { focus.current = 'retry'; }} onBlur={trackBlur}
        onClick={() => { focus.current = 'retry'; setAttempt(value => value + 1); }}>Retry attachments</Button>
      <Button onClick={props.onRefresh}>Refresh Card for attachments</Button>
    </>}
    {!loading && page && <>
      {!page.canEdit && <Typography>Read-only attachments.</Typography>}
      {page.items.length === 0 && <Typography>No attachments on this page.</Typography>}
      {page.items.map(item => <Link key={item.id} href={attachmentUrl(item.url)} target="_blank" rel="noopener noreferrer" referrerPolicy="no-referrer">
        {item.displayName} (opens in a new tab)
      </Link>)}
    </>}
    <Stack direction="row" spacing={1}>
      <Button ref={first} disabled={loading || !cursor || error} onFocus={trackFocus} onBlur={trackBlur}
        onClick={() => { focus.current = 'page'; setCursor(undefined); }}>First attachment page</Button>
      <Button ref={next} disabled={loading || !page?.nextCursor || error} onFocus={trackFocus} onBlur={trackBlur}
        onClick={() => { focus.current = 'page'; setCursor(page!.nextCursor!); }}>Next attachment page</Button>
    </Stack>
  </>;
}
