import { createTheme } from '@mui/material/styles'

export const entitlexTheme = createTheme({
  cssVariables: true,
  palette: {
    mode: 'light',
    primary: {
      main: '#0F6E56',
      dark: '#0A4F3D',
      light: '#3D9B7F',
      contrastText: '#FFFFFF',
    },
    secondary: {
      main: '#1B3A4B',
      dark: '#102633',
      light: '#3A5A6C',
    },
    background: {
      default: '#F3F6F4',
      paper: '#FFFFFF',
    },
    error: { main: '#B42318' },
    warning: { main: '#B54708' },
    success: { main: '#067647' },
    info: { main: '#175CD3' },
    divider: 'rgba(15, 40, 32, 0.12)',
    text: {
      primary: '#10241C',
      secondary: '#3D5248',
    },
  },
  typography: {
    fontFamily: '"IBM Plex Sans", "Segoe UI", sans-serif',
    h1: { fontWeight: 700, letterSpacing: '-0.02em' },
    h2: { fontWeight: 700, letterSpacing: '-0.02em' },
    h3: { fontWeight: 650, letterSpacing: '-0.015em' },
    h4: { fontWeight: 650 },
    h5: { fontWeight: 600 },
    h6: { fontWeight: 600 },
    button: { textTransform: 'none', fontWeight: 600 },
    overline: {
      fontFamily: '"IBM Plex Mono", monospace',
      letterSpacing: '0.08em',
    },
  },
  shape: { borderRadius: 10 },
  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          backgroundImage:
            'radial-gradient(1200px 600px at 0% -10%, rgba(15, 110, 86, 0.10), transparent 55%), radial-gradient(900px 500px at 100% 0%, rgba(27, 58, 75, 0.08), transparent 50%)',
          backgroundAttachment: 'fixed',
        },
        ':focus-visible': {
          outline: '3px solid #0F6E56',
          outlineOffset: 2,
        },
      },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
    },
    MuiAppBar: {
      styleOverrides: {
        root: {
          backgroundImage: 'none',
          backgroundColor: '#0C2C24',
        },
      },
    },
    MuiTableCell: {
      styleOverrides: {
        head: {
          fontWeight: 600,
          backgroundColor: 'rgba(15, 110, 86, 0.06)',
        },
      },
    },
  },
})
