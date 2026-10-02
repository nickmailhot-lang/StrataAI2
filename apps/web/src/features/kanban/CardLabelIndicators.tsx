import { Box, Chip, Typography } from '@mui/material';
import type { BoardSnapshot } from '../../api/workManagement';

const palette: Record<string, string> = { green: '#b7e4c7', yellow: '#ffe69a', orange: '#ffd0a8', red: '#ffc9c9', purple: '#e2c8f5', blue: '#bfdcff', sky: '#c2efff', lime: '#dcedab', pink: '#f9cce3', black: '#333333' };
export function CardLabelIndicators({ preview }: { preview?: NonNullable<BoardSnapshot['cardLabels']>[string] }) {
  if (!preview || !Array.isArray(preview.items) || !Number.isSafeInteger(preview.total) || preview.total < preview.items.length
    || preview.items.length > 6 || new Set(preview.items.map(item => item?.id)).size !== preview.items.length
    || preview.items.some(item => !item || typeof item.id !== 'string' || typeof item.name !== 'string' || item.name.length > 160
      || !Object.hasOwn(palette, item.color))) return null;
  if (preview.total === 0) return null;
  return <Box component="span" aria-label="Card label indicators" sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mt: 1 }}>
    {preview.items.map(label => <Chip key={label.id} component="span" size="small" label={label.name || `${label.color} label`}
      aria-label={`${label.name || 'Unnamed label'}, ${label.color}`} sx={{ maxWidth: '100%', backgroundColor: palette[label.color], color: label.color === 'black' ? '#fff' : '#172b4d' }} />)}
    {preview.total > preview.items.length && <Typography component="span" variant="caption">+{preview.total - preview.items.length} more labels</Typography>}
  </Box>;
}
