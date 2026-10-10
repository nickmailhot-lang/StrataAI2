import { CssBaseline, ThemeProvider } from '@mui/material';
import type { PropsWithChildren } from 'react';
import { appTheme } from '../theme/appTheme';

// Match the application's MUI defaults, including shared autofill styles.
// Using render's wrapper also retains the theme during fixture rerenders.
export function AppThemeFixture({ children }: PropsWithChildren) {
  return <ThemeProvider theme={appTheme}><CssBaseline />{children}</ThemeProvider>;
}
