import { useState } from 'react';
import { Box, Typography } from '@mui/material';
import type { WorkCard } from '../../api/workManagement';
import { notificationUuid } from '../notifications/notificationInbox';

type Props = { organizationId: string; boardId: string; card: WorkCard; unavailable: boolean; detail?: boolean };
// The snapshot hint avoids probing every Card. It never authorizes bytes: the
// Card-only image route admits the current selection and current viewer again.
export function CardCoverImage(props: Props) {
  if (props.unavailable || props.card.hasCover !== true || !notificationUuid(props.organizationId) || !notificationUuid(props.boardId)
    || !notificationUuid(props.card.id) || !Number.isSafeInteger(props.card.version) || props.card.version < 1) return null;
  return <Image key={`${props.organizationId}/${props.boardId}/${props.card.id}/${props.card.version}`} {...props} />;
}
function Image({ card, detail }: Props) {
  const [failed, setFailed] = useState(false);
  if (failed) return <Typography variant="caption" sx={{ display: 'block', px: 2 }}>Card cover unavailable.</Typography>;
  return <Box component="img" alt="Card cover" loading={detail ? 'eager' : 'lazy'} decoding="async" referrerPolicy="no-referrer"
    src={`/cards/${encodeURIComponent(card.id)}/cover/image?cardVersion=${card.version}`}
    sx={{ display: 'block', width: '100%', height: detail ? 240 : 160, objectFit: 'cover', borderRadius: detail ? 1 : 0 }}
    onError={() => setFailed(true)} />;
}
