import { Stack, type StackProps } from '@mui/material';
import { designTokens } from '../theme/designTokens';

// Layout only: consumers retain button refs, disabled state and command logic.
export function ActionRow({ sx, ...props }: StackProps) {
  return <Stack direction="row" useFlexGap {...props}
    sx={[{ gap: designTokens.actionGap, flexWrap: 'wrap', alignItems: 'center' }, ...(Array.isArray(sx) ? sx : [sx])]} />;
}
