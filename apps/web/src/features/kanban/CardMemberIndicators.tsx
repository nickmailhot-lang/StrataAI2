import { Avatar, Box, Tooltip, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';

const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
export function CardMemberIndicators({ preview, version, compact = false }: { preview?: NonNullable<BoardSnapshot['cardMembers']>[string]; version: number; compact?: boolean }) {
  if (!preview || !Number.isSafeInteger(version) || version < 1 || preview.cardVersion !== version
    || !Array.isArray(preview.items) || !Number.isSafeInteger(preview.total) || preview.total < preview.items.length
    || preview.items.length > 6 || preview.items.length !== Math.min(preview.total, 6)
    || preview.items.some((item, i) => !item || !uuid(item.userId) || typeof item.displayName !== 'string' || item.displayName.length > 160
      || (i > 0 && item.userId.toLowerCase() <= preview.items[i - 1].userId.toLowerCase()))) return compact ? <Typography variant="body2" sx={{ mt: 1 }}>Assignees unavailable</Typography> : null;
  if (preview.total === 0) return compact ? <Typography variant="body2" sx={{ mt: 1 }}>Unassigned</Typography> : null;
  if (compact) return <Box component="span" role="group" aria-label="Card assignee indicators" sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}>
    <Box component="span" sx={{ display: 'flex', gap: 0.5, mb: 0.5 }}>{preview.items.slice(0, 2).map(member => <Avatar key={member.userId} component="span" role="img"
      aria-label={`Assigned to ${member.displayName.trim() || 'Unnamed member'}`} sx={{ width: 24, height: 24, fontSize: '0.75rem' }}>
      {member.displayName.trim().split(/\s+/).slice(0, 2).map(name => Array.from(name)[0] ?? '').join('').toUpperCase() || '?'}
    </Avatar>)}</Box>
    <Typography component="span" variant="body2">Assigned to {preview.items.slice(0, 2).map(member => member.displayName.trim() || 'Unnamed member').join(', ')}
      {preview.total > 2 && ` +${preview.total - 2} more assignees`}</Typography>
  </Box>;
  return <Box component="span" role="group" aria-label="Card assignee indicators" sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 0.5, mt: 1 }}>
    {preview.items.map(member => {
      const name = member.displayName.trim() || 'Unnamed member';
      const initials = member.displayName.trim().split(/\s+/).slice(0, 2).map(n => Array.from(n)[0] ?? '').join('').toUpperCase() || '?';
      return <Tooltip key={member.userId} title={name}><Avatar component="span" role="img" aria-label={`Assigned to ${name}`} sx={{ width: 28, height: 28, fontSize: '0.75rem' }}>{initials}</Avatar></Tooltip>;
    })}
    {preview.total > preview.items.length && <Typography component="span" variant="caption">+{preview.total - preview.items.length} more assignees</Typography>}
  </Box>;
}
