/**
 * 色のテーマ（設定の「見た目」タブで選ぶ。#2）。変わるのは primary だけで、
 * アレルゲンの 🔴🟡🟢（error / warning / success）の色はどのテーマでも変えない。
 * 色の値と表示名は app/theme.ts にだけ書く。
 */
export const COLOR_THEME = {
    ORANGE: 'orange',
    BLUE: 'blue',
    PURPLE: 'purple',
    GRAY: 'gray',
} as const;
export type ColorTheme = (typeof COLOR_THEME)[keyof typeof COLOR_THEME];

/** 既定の色のテーマ（保存が使えないとき・保存した値が読めないときもこれ） */
export const DEFAULT_COLOR_THEME: ColorTheme = COLOR_THEME.ORANGE;

/** 色のテーマの値として正しいか（ブラウザに保存した値を確かめる） */
export const isColorTheme = (value: unknown): value is ColorTheme =>
    typeof value === 'string' && (Object.values(COLOR_THEME) as string[]).includes(value);
