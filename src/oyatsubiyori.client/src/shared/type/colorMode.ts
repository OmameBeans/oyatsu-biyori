/**
 * 画面の明るさ（設定の「見た目」タブで選ぶ。#4）。システムは端末の設定（ライト・ダーク）に合わせる。
 * 明るさを変えても、アレルゲンの 🔴🟡🟢 の区分と色の対応（error / warning / success）は変えない。
 * 色の値は app/theme.ts にだけ書く。
 */
export const COLOR_MODE = {
    LIGHT: 'light',
    DARK: 'dark',
    SYSTEM: 'system',
} as const;
export type ColorMode = (typeof COLOR_MODE)[keyof typeof COLOR_MODE];

/** テーマを組み立てるときの明るさ（システムを端末の設定で決めたあとのもの） */
export type ResolvedColorMode = Exclude<ColorMode, typeof COLOR_MODE.SYSTEM>;

/** 既定の明るさ（保存が使えないとき・保存した値が読めないときもこれ） */
export const DEFAULT_COLOR_MODE: ColorMode = COLOR_MODE.LIGHT;

/** 明るさの値として正しいか（ブラウザに保存した値を確かめる） */
export const isColorMode = (value: unknown): value is ColorMode =>
    typeof value === 'string' && (Object.values(COLOR_MODE) as string[]).includes(value);
