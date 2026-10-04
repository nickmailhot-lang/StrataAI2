import { useState } from 'react';
import { Box } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';
import { notificationUuid } from '../notifications/notificationInbox';

type Props = { snapshot: BoardSnapshot; unavailable: boolean };
export function BoardBackgroundImage({ snapshot, unavailable }: Props) {
  const b = snapshot.board;
  if (unavailable || !snapshot.access.canView || b.lifecycleState !== 'active' || b.backgroundType !== 'IMAGE'
    || !notificationUuid(b.backgroundValue) || !notificationUuid(b.id) || !notificationUuid(b.organizationId)
    || !Number.isSafeInteger(b.version) || Number(b.version) < 1) return null;
  return <Image key={`${b.organizationId}/${b.id}/${b.version}/${b.backgroundValue}`} boardId={b.id} version={b.version!} />;
}
function Image({ boardId, version }: { boardId: string; version: number }) {
  const [failed, setFailed] = useState(false);
  if (failed) return null;
  return <Box component="img" alt="" aria-hidden="true" decoding="async" referrerPolicy="no-referrer"
    src={`/boards/${encodeURIComponent(boardId)}/background/image?boardVersion=${version}`}
    onError={() => setFailed(true)}
    sx={{ position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'cover',
      borderRadius: 'inherit', opacity: 0.08, pointerEvents: 'none', zIndex: -1 }} />;
}
