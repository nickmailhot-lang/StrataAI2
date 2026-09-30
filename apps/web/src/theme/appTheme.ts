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
    MuiButtonBase: {
      defaultProps: {
        disableRipple: false,
      },
    },
  },
});
