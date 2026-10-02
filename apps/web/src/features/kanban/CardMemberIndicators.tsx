import { Avatar, Box, Tooltip, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';

const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
export function CardMemberIndicators({ preview, version }: { preview?: NonNullable<BoardSnapshot['cardMembers']>[string]; version: number }) {
  if (!preview || !Number.isSafeInteger(version) || version < 1 || preview.cardVersion !== version
    || !Array.isArray(preview.items) || !Number.isSafeInteger(preview.total) || preview.total < preview.items.length
    || preview.items.length > 6 || preview.items.length !== Math.min(preview.total, 6)
    || preview.items.some((item, i) => !item || !uuid(item.userId) || typeof item.displayName !== 'string' || item.displayName.length > 160
      || (i > 0 && item.userId.toLowerCase() <= preview.items[i - 1].userId.toLowerCase()))) return null;
  if (preview.total === 0) return null;
  return <Box component="span" role="group" aria-label="Card assignee indicators" sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 0.5, mt: 1 }}>
    {preview.items.map(member => {
      const name = member.displayName.trim() || 'Unnamed member';
      const initials = member.displayName.trim().split(/\s+/).slice(0, 2).map(n => Array.from(n)[0] ?? '').join('').toUpperCase() || '?';
      return <Tooltip key={member.userId} title={name}><Avatar component="span" role="img" aria-label={`Assigned to ${name}`} sx={{ width: 28, height: 28, fontSize: '0.75rem' }}>{initials}</Avatar></Tooltip>;
    })}
    {preview.total > preview.items.length && <Typography component="span" variant="caption">+{preview.total - preview.items.length} more assignees</Typography>}
  </Box>;
}
