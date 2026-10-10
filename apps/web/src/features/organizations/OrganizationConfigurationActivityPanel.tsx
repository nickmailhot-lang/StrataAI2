import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, Stack, Typography } from '@mui/material';
import { WorkRequestError } from '../../api/workManagement';
import { readOrganizationConfigurationActivity } from './organizationConfigurationClient';
import type { ConfigurationActivityPage } from './organizationConfigurationActivity';

export function OrganizationConfigurationActivityPanel({ organizationId, actorId, disabled, onDenied }: {
  organizationId: string; actorId: string; disabled: boolean; onDenied: (reason: unknown) => void;
}) {
  const [page, setPage] = useState<ConfigurationActivityPage>(); const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>(); const [attempt, setAttempt] = useState<{ cursor?: string; beforeVersion?: number }>({});
  const operation = useRef<AbortController | undefined>(undefined); const mounted = useRef(false);
  const heading = useRef<HTMLHeadingElement>(null); const focus = useRef(false);
  const section = useRef<HTMLElement>(null);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; operation.current?.abort(); }; }, []);
  useLayoutEffect(() => {
    if (!busy && !disabled && focus.current) {
      if (section.current?.contains(document.activeElement) || document.activeElement === document.body) heading.current?.focus();
      focus.current = false;
    }
  }, [busy, disabled]);
  async function read(cursor?: string, beforeVersion?: number) {
    if (disabled || operation.current) return;
    const controller = new AbortController(); operation.current = controller;
    setBusy(true); setPage(undefined); setFailure(undefined); setAttempt({ cursor, beforeVersion }); focus.current = true;
    try {
      const result = await readOrganizationConfigurationActivity(organizationId, actorId, controller.signal, cursor, beforeVersion);
      if (mounted.current && !controller.signal.aborted) setPage(result);
    } catch (reason) {
      if (mounted.current && !controller.signal.aborted) {
        if (reason instanceof WorkRequestError && [401, 403, 404].includes(reason.status)) onDenied(reason);
        else setFailure('Activity could not be confirmed. Your configuration draft and original submission are preserved.');
      }
    } finally {
      if (operation.current === controller) { operation.current = undefined; if (mounted.current) setBusy(false); }
    }
  }
  return <Box component="section" aria-label="Configuration Activity" ref={section} sx={{ overflowWrap: 'anywhere' }}>
    <Stack spacing={2}>
      <Typography variant="h5" component="h3" tabIndex={-1} ref={heading}>Configuration Activity</Typography>
      <Typography>Original change references, private to current administrators. Review configuration history for retained values.</Typography>
      <Button disabled={disabled || busy} onClick={() => void read()}>Load configuration Activity</Button>
      {busy && <CircularProgress aria-label="Checking configuration Activity" />}
      {failure && <Alert severity="warning" role="status" action={<Button disabled={disabled || busy} onClick={() => void read(attempt.cursor, attempt.beforeVersion)}>Retry Activity read</Button>}>{failure}</Alert>}
      {page && <>
        <Typography role="status">{page.items.length ? `${page.items.length} configuration changes in this page.` : 'No configuration changes exist in this Activity page.'}</Typography>
        {page.items.map(item => <Box key={item.eventId}>
          <Typography variant="h6" component="h4">Configuration changed at revision {item.version}</Typography>
          <Typography>Original actor reference: {item.actorId}</Typography>
          <Typography>Original event reference: {item.eventId}</Typography>
          <Typography>Recorded at: <time dateTime={item.createdAt}>{item.createdAt}</time></Typography>
        </Box>)}
        {page.nextCursor && <Button disabled={disabled || busy} onClick={() => void read(page.nextCursor!, page.items.at(-1)!.version)}>Older configuration Activity</Button>}
      </>}
    </Stack>
  </Box>;
}
