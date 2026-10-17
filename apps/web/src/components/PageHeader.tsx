import { Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { designTokens } from '../theme/designTokens';

export function PageHeader({ title, description, level = 'h2', variant = 'h4' }: {
  title: string; description?: ReactNode; level?: 'h1' | 'h2'; variant?: 'h4' | 'h5';
}) {
  return <Stack spacing={designTokens.contentGap} sx={{ minWidth: 0 }}>
    <Typography component={level} variant={variant}>{title}</Typography>
    {description && <Typography>{description}</Typography>}
  </Stack>;
}
