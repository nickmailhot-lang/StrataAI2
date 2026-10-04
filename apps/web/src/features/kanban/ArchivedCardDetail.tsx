import { useEffect, useState } from 'react';
import { Alert, CircularProgress, Stack, Typography } from '@mui/material';
import { boundedWorkRead, workRequest, WorkRequestError } from '../../api/workManagement';
import { isNotificationProfile } from '../notifications/notificationInbox';
import { ActivityHistoryControl } from './ActivityHistoryControl';
import { CardCommentsControl } from './CardCommentsControl';
import { CardChecklists } from './CardChecklists';
import { CardAttachments } from './CardAttachments';

type Scope = { organizationId: string; boardId: string; cardId: string };
type Detail = Scope & { title: string; description: string | null; version: number };
type Props = Scope & { unavailable: boolean; refreshSequence: string; reconnectSequence: number;
  onDenied: (error: Error) => void; onRefresh: () => void };
const ignore = () => {};
export function parseArchivedCardDetail(value: unknown, scope: Scope): Detail {
  if (!value || typeof value !== 'object') throw new WorkRequestError(503, null);
  const row = value as Record<string, unknown>;
  if (row.organizationId !== scope.organizationId || row.boardId !== scope.boardId || row.cardId !== scope.cardId
    || typeof row.title !== 'string' || !row.title.trim() || row.title.length > 500
    || row.description !== null && typeof row.description !== 'string'
    || !Number.isSafeInteger(row.version) || (row.version as number) < 1)
    throw new WorkRequestError(503, null);
  return { ...scope, title: row.title, description: row.description as string | null, version: row.version as number };
}
export function ArchivedCardDetail(props: Props) {
  return <Reader key={`${props.organizationId}/${props.boardId}/${props.cardId}/${props.refreshSequence}/${props.unavailable}`} {...props} />;
}
function Reader(props: Props) {
  const [detail, setDetail] = useState<Detail>(); const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (props.unavailable) return;
    const controller = new AbortController();
    void boundedWorkRead(async signal => {
      const before = await workRequest<unknown>('/me', { signal });
      if (!isNotificationProfile(before)) throw new WorkRequestError(401, null);
      const result = parseArchivedCardDetail(await workRequest<unknown>(
        `/boards/${encodeURIComponent(props.boardId)}/cards/${encodeURIComponent(props.cardId)}/archived-details`, { signal }), props);
      const after = await workRequest<unknown>('/me', { signal });
      if (!isNotificationProfile(after) || before.id.toLowerCase() !== after.id.toLowerCase()) throw new WorkRequestError(401, null);
      return result;
    }, controller.signal).then(result => { if (!controller.signal.aborted) setDetail(result); }).catch(error => {
      if (controller.signal.aborted) return;
      setFailed(true);
      // A missing Card is local. A revoked session or Board read purges the
      // parent scope through its existing admission boundary.
      if (error instanceof WorkRequestError && [401, 403].includes(error.status)) props.onDenied(error);
    });
    return () => controller.abort();
  }, [props]);
  if (props.unavailable || failed) return <Alert severity="info">This card is unavailable in this board.</Alert>;
  if (!detail) return <CircularProgress size={24} aria-label="Reading archived Card" />;
  return <Stack spacing={2}>
    <Alert severity="info">This Card or its List is archived. Details are read-only.</Alert>
    <Typography component="h3" variant="h6" sx={{ overflowWrap: 'anywhere' }}>{detail.title}</Typography>
    {detail.description && <Typography sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{detail.description}</Typography>}
    <CardChecklists {...props} version={detail.version} />
    <CardAttachments {...props} version={detail.version} />
    <ActivityHistoryControl organizationId={props.organizationId} boardId={props.boardId} kind="CARD" targetId={props.cardId}
      unavailable={false} refreshSequence={props.refreshSequence} onDenied={props.onDenied} />
    <CardCommentsControl {...props} version={detail.version} editable={false} disabled={false} canAdminister={false}
      onBusyChange={ignore} onRecoveryChange={ignore} />
  </Stack>;
}
