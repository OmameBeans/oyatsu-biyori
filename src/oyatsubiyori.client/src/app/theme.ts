import { createTheme } from '@mui/material/styles';
import type { PaletteOptions, ThemeOptions } from '@mui/material/styles';
import { COLOR_MODE, COLOR_THEME } from '../shared/type';
import type { ColorMode, ColorTheme, ResolvedColorMode } from '../shared/type';
import type { ColorModeOption, ColorThemeOption } from '../shared/lib';

interface PrimaryColors {
    main: string;
    dark: string;
    light: string;
    contrastText: string;
}

/**
 * 色のテーマごとの primary（#2）を、明るさ（#4）ごとに持つ。色の値はここにだけ書く。
 * - 赤・黄・緑に寄る色は使わない（アレルゲンの 🔴🟡🟢 と見分けるため）
 * - この画面では light を「淡い面」（サイドバーの選ばれた項目・チャットの吹き出しの背景）、
 *   dark を「その面や背景の上の文字・線」として使う。ダークでは light を暗い色みの面、dark を明るい色みの文字にする
 * - contrastText と main、dark と light、main と背景（default・paper）、文字（text.primary）と light は、
 *   どれも 4.5:1 以上（WCAG AA）にする
 * - dark は warning.dark（🟡 の注記の文字）とも色相を離す（オレンジのテーマの dark は、ライトでは赤茶、ダークでは桃色に寄せる）
 */
const COLOR_THEME_DEFINITIONS: Record<ColorTheme, { label: string; primary: Record<ResolvedColorMode, PrimaryColors> }> = {
    [COLOR_THEME.ORANGE]: {
        label: 'オレンジ',
        primary: {
            light: { main: '#b5541c', dark: '#873a22', light: '#fbe3d2', contrastText: '#ffffff' },
            // ダークの main は error（#ff7b72）・warning（#e3b341）と見分けるため、ΔE2000 で error と 22.2・warning と 21.9 離した濃いオレンジ。
            // （ダークで両方から 25 以上離れ、背景と 4.5:1 以上のオレンジはない。ユーザーが決めた）
            // main と paper 4.52・default 4.93、contrastText と main 5.18、dark と light 8.18、text.primary と light 10.26
            dark: { main: '#c3722b', dark: '#ffc2a6', light: '#4a2b1c', contrastText: '#1f0c03' },
        },
    },
    [COLOR_THEME.BLUE]: {
        label: 'あお（いつもの色）',
        primary: {
            light: { main: '#2f6db5', dark: '#1d4f8a', light: '#dfeaf7', contrastText: '#ffffff' },
            dark: { main: '#8ab4f0', dark: '#cfe0fa', light: '#1e3150', contrastText: '#0b1d33' },
        },
    },
    [COLOR_THEME.PURPLE]: {
        label: 'むらさき',
        primary: {
            light: { main: '#7a4fa8', dark: '#5a3683', light: '#eee4f6', contrastText: '#ffffff' },
            dark: { main: '#c3a3e6', dark: '#e6d6f7', light: '#3a2a4f', contrastText: '#24123a' },
        },
    },
    [COLOR_THEME.GRAY]: {
        label: 'グレー',
        primary: {
            light: { main: '#4f6577', dark: '#344654', light: '#e2e8ee', contrastText: '#ffffff' },
            dark: { main: '#a9bccb', dark: '#dde6ee', light: '#2f3a44', contrastText: '#18232c' },
        },
    },
};

/** 色のテーマの選択肢（並びはこの順）。見本の丸は、今の明るさでの primary.main */
export const colorThemeOptionsFor = (mode: ResolvedColorMode): readonly ColorThemeOption[] =>
    (Object.keys(COLOR_THEME_DEFINITIONS) as ColorTheme[]).map((id) => ({
        id,
        label: COLOR_THEME_DEFINITIONS[id].label,
        swatch: COLOR_THEME_DEFINITIONS[id].primary[mode].main,
    }));

/** 明るさの選択肢（並びはこの順。#4） */
export const COLOR_MODE_OPTIONS: readonly ColorModeOption[] = [
    { id: COLOR_MODE.LIGHT, label: 'ライト' },
    { id: COLOR_MODE.DARK, label: 'ダーク' },
    { id: COLOR_MODE.SYSTEM, label: 'システム（端末の設定に合わせる）' },
];

/**
 * 明るさごとに変える部分（primary のほか）。どの色のテーマでも同じ。
 * アレルゲンの色は区分で決める（error：使えません / warning：要確認 / success：使えます）。テーマでも明るさでも区分と色の対応は変えない。
 * ダークでは背景が暗いので、error / warning / success / info は明るめにし、上に載る文字（contrastText）を暗い色にする。
 * warning.dark は 🟡 の注記の文字に使うので、ダークでは背景の上で読める明るい黄色にする。
 *
 * ダークで 4.5:1 以上（WCAG AA）を計算で確かめた組（背景は default・paper の両方。Dialog なども paper のまま。DARK_COMPONENTS）：
 * - text.primary・text.secondary・各テーマの primary.main・error.main・warning.dark・success.main と、背景
 * - contrastText と main（primary・secondary・error・warning・success・info。塗りのバッジ・ボタン・filled の Alert）
 * - primary.dark と primary.light（サイドバーの選ばれた項目）、text.primary と primary.light（チャットの吹き出し）
 * - standard の Alert の中に置く文字（text.primary・text.secondary・error.main・warning.dark・success.main。
 *   🔴🟡🟢 の表示の部品を Alert の中に置く画面があるため）と、Alert の背景（区分の main を 14% 敷いた色）。
 *   いちばん低いのは warning の Alert の中の error.main で 4.89:1。Alert 自身の文字・アイコン・リンクは DARK_COMPONENTS に書いた
 *   （primary.main は Alert の中では文字に使わない。リンクと、文字・枠のボタンは Alert の文字の色にする）
 */
const MODE_PALETTES: Record<ResolvedColorMode, PaletteOptions> = {
    light: {
        mode: 'light',
        secondary: { main: '#5b8f6b', contrastText: '#ffffff' },
        // 画面の背景は薄い灰色、カードなどの面（paper）は白にし、部品の境目は罫線（divider）で示す。
        // default と各テーマの primary.main・error・warning.dark・success は 4.5:1 以上（いちばん低いのはオレンジの 4.56:1）
        background: { default: '#f4f6f8', paper: '#ffffff' },
        error: { main: '#c62828' },
        warning: { main: '#ed8c00', dark: '#8a4b00' },
        success: { main: '#2e7d32' },
        text: { primary: 'rgba(40, 28, 20, 0.9)', secondary: 'rgba(40, 28, 20, 0.62)' },
    },
    dark: {
        mode: 'dark',
        secondary: { main: '#8cc29a', contrastText: '#10241a' },
        background: { default: '#1a1613', paper: '#241e1a' },
        error: { main: '#ff7b72', contrastText: '#2b0907' },
        warning: { main: '#e3b341', dark: '#f2cc60', contrastText: '#2b1d00' },
        success: { main: '#81c784', contrastText: '#0c2410' },
        info: { main: '#6cc5ee', contrastText: '#04202e' },
        text: { primary: 'rgba(255, 245, 235, 0.92)', secondary: 'rgba(255, 245, 235, 0.7)', disabled: 'rgba(255, 245, 235, 0.5)' },
        divider: 'rgba(255, 245, 235, 0.14)',
    },
};

/**
 * どのテーマ・明るさでも同じ部分：丸みと文字（T0-10）。
 * 見た目は MUI のマテリアルデザインの既定に寄せる（角は少し丸める・背景は白・境目は罫線・カードは既定から大きく変えない）。
 * sx の borderRadius の数値は、この borderRadius の倍数になる（2 なら 8px）
 */
const BASE_THEME_OPTIONS = {
    shape: { borderRadius: 4 },
    typography: {
        fontFamily: '"Noto Sans JP", "Yu Gothic UI", "Meiryo", "Hiragino Sans", sans-serif',
        // 文字は MUI の既定より少し小さくする（rem で指定。1rem = 16px）
        h1: { fontSize: '1.5rem', fontWeight: 700 },
        h2: { fontSize: '1.2rem', fontWeight: 700 },
        h3: { fontSize: '1rem', fontWeight: 700 },
        subtitle1: { fontSize: '0.9375rem' },
        subtitle2: { fontSize: '0.8125rem' },
        body1: { fontSize: '0.9375rem' },
        body2: { fontSize: '0.8125rem' },
        caption: { fontSize: '0.6875rem' },
        overline: { fontSize: '0.6875rem' },
        button: { fontSize: '0.8125rem', textTransform: 'none', fontWeight: 600 },
    },
    components: {
        MuiButton: {
            defaultProps: { disableElevation: true },
            styleOverrides: { root: { minHeight: 40 } },
        },
        MuiCard: {
            // 影ではなく罫線で背景と分ける（ダークでは影が見えないため）。丸みは既定のまま
            defaultProps: { variant: 'outlined' },
        },
        MuiChip: {
            styleOverrides: { root: {} },
        },
        MuiTextField: {
            defaultProps: { size: 'small' },
        },
    },
} satisfies ThemeOptions;

/** ダークの standard の Alert の背景に敷く、区分の色（main）の濃さ。上の 4.5:1 の計算はこの値で行った */
const DARK_ALERT_TINT = 0.14;
/**
 * ダークの standard の Alert の文字：区分の色（main）を白に 60% 寄せた色。区分の色みを残して目立たせる。
 * Alert の背景との比は 9.0:1 以上（error の Alert を paper の上に置いたときがいちばん低い）
 */
const DARK_ALERT_TEXT_LIGHTEN = 0.6;
/**
 * ダークで、塗りの部品（contained の Button・クリックできる塗りの Chip・Fab）にマウスを載せたとき・キーボードで選んだときの背景：
 * main を白に 12% 寄せた色。MUI はここに dark を使うが、ダークでは dark を「淡い文字の色」にしているので、そのままだと淡いピーチなどに跳ねる。
 * 暗い contrastText との比は、どの色でも 6.15:1 以上（オレンジのテーマの primary がいちばん低い）
 */
const DARK_HOVER_LIGHTEN = 0.12;

/** 区分やテーマの色（palette の中で main を持つもの）か。'inherit'・'default' などは false */
const isPaletteColor = (color: unknown): color is 'primary' | 'secondary' | 'error' | 'warning' | 'success' | 'info' =>
    color === 'primary' || color === 'secondary' || color === 'error' || color === 'warning' || color === 'success' || color === 'info';

/**
 * ダークのときだけの部品の見た目。
 * - 塗りの Button・Chip・Fab：ホバーとキーボードで選んだときの背景を、dark ではなく main を少し明るくした色にする（DARK_HOVER_LIGHTEN）
 * - Paper：MUI はダークで、浮いた部品（Dialog など）に白を重ねて明るくする。重ねると文字との比が計算と合わなくなるので重ねない。
 *   浮いた部品は、枠線で背景と分ける
 * - standard の Alert：MUI のままだと背景がほぼ黒で、info の Alert などが溶け込むので、区分の色を薄く敷く。
 *   文字は区分の色を白に寄せた色、アイコンは区分の色（main。背景と 5.1:1 以上）にする
 * - filled の Alert（🔴 の大事な知らせ）：MUI のままだと背景が error.dark を暗くした色、文字が黒になり 4.5:1 に届かないので、
 *   背景を main、文字を contrastText にする（error で 7.27:1）
 */
const DARK_COMPONENTS = {
    // MuiButton・MuiChip は BASE_THEME_OPTIONS にもあるので、そちらの指定（丸み・高さなど）を引き継いでから足す
    // （createAppTheme では部品ごとに置き換えるため、引き継がないと丸みが消える）
    MuiButton: {
        ...BASE_THEME_OPTIONS.components.MuiButton,
        styleOverrides: {
            ...BASE_THEME_OPTIONS.components.MuiButton.styleOverrides,
            contained: ({ ownerState, theme }) =>
                isPaletteColor(ownerState.color)
                    ? { '@media (hover: hover)': { '&:hover': { '--variant-containedBg': theme.lighten(theme.palette[ownerState.color].main, DARK_HOVER_LIGHTEN) } } }
                    : {},
        },
    },
    MuiChip: {
        ...BASE_THEME_OPTIONS.components.MuiChip,
        styleOverrides: {
            // 塗りの Chip だけ（枠の Chip のホバーは MUI のまま、main を薄く敷く）
            root: ({ ownerState, theme }) => {
                const base = BASE_THEME_OPTIONS.components.MuiChip.styleOverrides.root;
                if (!isPaletteColor(ownerState.color) || ownerState.variant === 'outlined') {
                    return base;
                }
                const hoverBg = theme.lighten(theme.palette[ownerState.color].main, DARK_HOVER_LIGHTEN);
                return {
                    ...base,
                    ...(ownerState.clickable ? { '&:hover': { backgroundColor: hoverBg } } : {}),
                    '&.Mui-focusVisible': { backgroundColor: hoverBg },
                };
            },
        },
    },
    MuiFab: {
        styleOverrides: {
            root: ({ ownerState, theme }) =>
                isPaletteColor(ownerState.color) ? { '&:hover': { backgroundColor: theme.lighten(theme.palette[ownerState.color].main, DARK_HOVER_LIGHTEN) } } : {},
        },
    },
    MuiPaper: {
        styleOverrides: { root: { backgroundImage: 'none' } },
    },
    MuiDialog: {
        styleOverrides: { paper: ({ theme }) => ({ border: `1px solid ${theme.palette.divider}` }) },
    },
    MuiPopover: {
        styleOverrides: { paper: ({ theme }) => ({ border: `1px solid ${theme.palette.divider}` }) },
    },
    MuiAutocomplete: {
        styleOverrides: { paper: ({ theme }) => ({ border: `1px solid ${theme.palette.divider}` }) },
    },
    MuiAlert: {
        styleOverrides: {
            standard: ({ ownerState, theme }) => {
                const color = ownerState.color ?? ownerState.severity ?? 'success';
                return {
                    backgroundColor: theme.alpha(theme.palette[color].main, DARK_ALERT_TINT),
                    color: theme.lighten(theme.palette[color].main, DARK_ALERT_TEXT_LIGHTEN),
                    '& .MuiAlert-icon': { color: theme.palette[color].main },
                    // Alert の中のリンクと、文字・枠のボタンは Alert の文字の色にする（オレンジのテーマの primary.main は Alert の背景と 3.38:1 で足りないため）
                    '& .MuiLink-root': { color: 'inherit', textDecorationColor: 'currentColor' },
                    '& .MuiButton-text, & .MuiButton-outlined': { color: 'inherit' },
                };
            },
            filled: ({ ownerState, theme }) => {
                const color = ownerState.color ?? ownerState.severity ?? 'success';
                return { backgroundColor: theme.palette[color].main, color: theme.palette[color].contrastText };
            },
        },
    },
} satisfies ThemeOptions['components'];

/** テーマを組み立てるときの選択 */
export interface AppThemeSelection {
    colorTheme: ColorTheme;
    /** 明るさ（システムは、端末の設定でライトかダークに決めてから渡す） */
    mode: ResolvedColorMode;
}

/** システムを選んだときは端末の設定で、ライトかダークに決める */
export const resolveColorMode = (colorMode: ColorMode, prefersDark: boolean): ResolvedColorMode => {
    if (colorMode === COLOR_MODE.SYSTEM) {
        return prefersDark ? COLOR_MODE.DARK : COLOR_MODE.LIGHT;
    }
    return colorMode;
};

/** 選んだ見た目からテーマを作る。primary をテーマと明るさで、ほかを明るさで差し替える */
export const createAppTheme = ({ colorTheme, mode }: AppThemeSelection) =>
    createTheme({
        ...BASE_THEME_OPTIONS,
        palette: { ...MODE_PALETTES[mode], primary: COLOR_THEME_DEFINITIONS[colorTheme].primary[mode] },
        components: mode === 'dark' ? { ...BASE_THEME_OPTIONS.components, ...DARK_COMPONENTS } : BASE_THEME_OPTIONS.components,
    });

/**
 * ブラウザの上の帯の色（<meta name="theme-color">）。ライトはテーマの色、ダークは背景の色にする。
 * index.html の初期値は、既定（あお・ライト）の primary.main と同じにしておく。
 */
export const browserBarColorOf = (theme: ReturnType<typeof createAppTheme>) =>
    theme.palette.mode === 'dark' ? theme.palette.background.default : theme.palette.primary.main;
