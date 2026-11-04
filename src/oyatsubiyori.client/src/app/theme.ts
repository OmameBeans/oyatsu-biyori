import { createTheme } from '@mui/material/styles';

/**
 * テーマ：「おやつ日和」に合わせた、やわらかい色と丸み（T0-10）。
 * アレルゲンの色は区分で決める（error：使えません / warning：要確認 / success：使えます）。
 */
export const theme = createTheme({
    palette: {
        primary: { main: '#e07a3a', dark: '#b85d24', light: '#fbe3d2', contrastText: '#ffffff' },
        secondary: { main: '#5b8f6b', contrastText: '#ffffff' },
        background: { default: '#fbf7f2', paper: '#ffffff' },
        error: { main: '#c62828' },
        warning: { main: '#ed8c00', dark: '#8a4b00' },
        success: { main: '#2e7d32' },
        text: { primary: 'rgba(40, 28, 20, 0.9)', secondary: 'rgba(40, 28, 20, 0.62)' },
    },
    shape: { borderRadius: 12 },
    typography: {
        fontFamily: '"Noto Sans JP", "Yu Gothic UI", "Meiryo", "Hiragino Sans", sans-serif',
        h1: { fontSize: '1.6rem', fontWeight: 700 },
        h2: { fontSize: '1.3rem', fontWeight: 700 },
        h3: { fontSize: '1.1rem', fontWeight: 700 },
        button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
        MuiButton: {
            defaultProps: { disableElevation: true },
            styleOverrides: { root: { borderRadius: 999, minHeight: 40 } },
        },
        MuiCard: {
            defaultProps: { variant: 'outlined' },
            styleOverrides: { root: { borderRadius: 16 } },
        },
        MuiChip: {
            styleOverrides: { root: { borderRadius: 999 } },
        },
        MuiTextField: {
            defaultProps: { size: 'small' },
        },
    },
});
