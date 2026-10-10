import { createTheme } from '@mui/material/styles';

export const appTheme = createTheme({
  cssVariables: true,
  palette: {
    mode: 'light',
    primary: {
      main: '#0f4c81',
    },
    secondary: {
      main: '#3d6f8e',
    },
    background: {
      default: '#f5f7fa',
      paper: '#ffffff',
    },
  },
  shape: {
    borderRadius: 10,
  },
  typography: {
    fontFamily:
      'Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
    h4: {
      fontWeight: 700,
    },
  },
  components: {
    MuiTypography: {
      styleOverrides: {
        // Preserve complete canonical names and account addresses at narrow
        // widths, including unbroken words. Do not clip or truncate the text.
        root: { overflowWrap: 'anywhere' },
      },
    },
    MuiCardContent: {
      styleOverrides: {
        // Card titles can be plain text rather than Typography. Keep complete
        // canonical labels inside the preview instead of clipping at the Card.
        root: { overflowWrap: 'anywhere' },
      },
    },
    MuiCssBaseline: {
      styleOverrides: {
        // Non-empty keyframes retain MUI's autofill notification in production.
        '@keyframes mui-auto-fill': { from: { animationName: 'mui-auto-fill' } },
        '@keyframes mui-auto-fill-cancel': { from: { animationName: 'mui-auto-fill-cancel' } },
      },
    },
    MuiInputBase: {
      defaultProps: { disableInjectingGlobalStyles: true },
      styleOverrides: {
        // MUI also disables these selectors with per-input style injection.
        // Keep the existing detection while CssBaseline owns the shared rules.
        input: {
          animationName: 'mui-auto-fill-cancel',
          animationDuration: '10ms',
          '&:-webkit-autofill': {
            animationName: 'mui-auto-fill',
            animationDuration: '5000s',
          },
        },
      },
    },
    MuiInputLabel: {
      styleOverrides: {
        // Interpolating disabled color after admission briefly leaves an active
        // field label below readable contrast. Keep the floating-label motion.
        root: ({ theme }) => ({ transition: theme.transitions.create(['transform', 'max-width'], {
          duration: theme.transitions.duration.shorter, easing: theme.transitions.easing.easeOut,
        }) }),
      },
    },
    MuiButton: {
      styleOverrides: {
        // Enabled text/background colors must become readable together. Preserve
        // shadow/border animation without tweening exempt disabled-state colors.
        root: ({ theme }) => ({ overflowWrap: 'anywhere',
          transition: theme.transitions.create(['box-shadow', 'border-color']) }),
      },
    },
    MuiAvatar: {
      styleOverrides: {
        colorDefault: ({ theme }) => ({
          backgroundColor: theme.palette.primary.main,
          color: theme.palette.primary.contrastText,
        }),
      },
    },
    MuiButtonBase: {
      styleOverrides: {
        root: {
          '&.Mui-focusVisible': {
            outline: '2px solid currentColor',
            outlineOffset: '3px',
          },
        },
      },
      defaultProps: {
        disableRipple: false,
      },
    },
  },
});
