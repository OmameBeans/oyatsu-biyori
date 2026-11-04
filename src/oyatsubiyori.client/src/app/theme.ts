import { createTheme } from '@mui/material/styles';
import type { PaletteColorOptions, ThemeOptions } from '@mui/material/styles';
import { COLOR_THEME } from '../shared/type';
import type { ColorTheme } from '../shared/type';
import type { ColorThemeOption } from '../shared/lib';

/**
 * 色のテーマごとの primary（#2）。色の値はここにだけ書く。
 * - 赤・黄・緑に寄る色は使わない（アレルゲンの 🔴🟡🟢 と見分けるため）
 * - 白の文字と main、dark の文字と light、main の文字と背景（default）は、どれも 4.5:1 以上（WCAG AA）にする
 *   （サイドバーの選ばれた項目は light の背景に dark の文字。ボタンは main の背景に contrastText の文字）
 * - dark は warning.dark（🟡 の注記の文字）とも色相を離す（橙のテーマの dark は赤茶にする）
 */
const COLOR_THEME_DEFINITIONS: Record<ColorTheme, { label: string; primary: { main: string; dark: string; light: string; contrastText: string } }> = {
    [COLOR_THEME.ORANGE]: { label: 'オレンジ（いつもの色）', primary: { main: '#b5541c', dark: '#873a22', light: '#fbe3d2', contrastText: '#ffffff' } },
    [COLOR_THEME.BLUE]: { label: 'あお', primary: { main: '#2f6db5', dark: '#1d4f8a', light: '#dfeaf7', contrastText: '#ffffff' } },
    [COLOR_THEME.PURPLE]: { label: 'むらさき', primary: { main: '#7a4fa8', dark: '#5a3683', light: '#eee4f6', contrastText: '#ffffff' } },
    [COLOR_THEME.GRAY]: { label: 'グレー', primary: { main: '#4f6577', dark: '#344654', light: '#e2e8ee', contrastText: '#ffffff' } },
};

/** 設定の「見た目」タブに並べる選択肢（並びはこの順） */
export const COLOR_THEME_OPTIONS: readonly ColorThemeOption[] = (Object.keys(COLOR_THEME_DEFINITIONS) as ColorTheme[]).map((id) => ({
    id,
    label: COLOR_THEME_DEFINITIONS[id].label,
    swatch: COLOR_THEME_DEFINITIONS[id].primary.main,
}));

/**
 * どのテーマでも同じ部分：「おやつ日和」に合わせた、やわらかい色と丸み（T0-10）。
 * アレルゲンの色は区分で決める（error：使えません / warning：要確認 / success：使えます）。テーマで変えない。
 */
const BASE_THEME_OPTIONS = {
    palette: {
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
} satisfies ThemeOptions;

/** テーマを組み立てるときの選択（明るさ（#4）を足すときは、ここに足す） */
export interface AppThemeSelection {
    colorTheme: ColorTheme;
}

/** 選んだ見た目からテーマを作る。primary だけをテーマごとに差し替え、error / warning / success は共通のまま */
export const createAppTheme = ({ colorTheme }: AppThemeSelection) => {
    const primary: PaletteColorOptions = COLOR_THEME_DEFINITIONS[colorTheme].primary;
    return createTheme({
        ...BASE_THEME_OPTIONS,
        palette: { ...BASE_THEME_OPTIONS.palette, primary },
    });
};
