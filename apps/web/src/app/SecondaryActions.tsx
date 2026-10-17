import { Box, Stack } from '@mui/material';
import type { ReactNode } from 'react';

// Keep command/recovery owners mounted while their entry points are collapsed.
// Portalled dialogs retain their independent focus and retry lifecycle.
export function SecondaryActions({ label, children }: { label: string; children: ReactNode }) {
  return <Box component="details" sx={{ minWidth: 0, maxWidth: '100%', '& > summary': { cursor: 'pointer', color: 'primary.main', p: 1,
    borderRadius: 1, minHeight: 44, '&:focus-visible': { outline: '2px solid', outlineOffset: 2 } } }}>
    <summary>{label}</summary>
    <Stack direction="row" useFlexGap sx={{ flexWrap: 'wrap', gap: 1, p: 1 }}>{children}</Stack>
  </Box>;
}
