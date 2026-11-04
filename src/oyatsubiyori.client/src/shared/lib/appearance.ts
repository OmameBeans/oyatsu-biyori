import { createContext, useContext } from 'react';
import { DEFAULT_COLOR_MODE, DEFAULT_COLOR_THEME, isColorMode, isColorTheme, PREFERENCE_KEY } from '../type';
import type { ColorMode, ColorTheme } from '../type';
import { readPreference, writePreference } from './preferenceStorage';

/** 色のテーマの選択肢（表示名と見本の色。値は app/theme.ts から渡す） */
export interface ColorThemeOption {
    id: ColorTheme;
    label: string;
    /** 見本の丸の色（そのテーマの、今の明るさでの primary.main） */
    swatch: string;
}

/** 明るさの選択肢（表示名。値は app/theme.ts から渡す） */
export interface ColorModeOption {
    id: ColorMode;
    label: string;
}

/**
 * 画面の見た目の設定（色のテーマ：#2、明るさ：#4）。状態は App が持ち、設定の「見た目」タブで変える。
 */
export interface AppearanceValue {
    colorTheme: ColorTheme;
    colorThemeOptions: readonly ColorThemeOption[];
    setColorTheme: (colorTheme: ColorTheme) => void;
    colorMode: ColorMode;
    colorModeOptions: readonly ColorModeOption[];
    setColorMode: (colorMode: ColorMode) => void;
}

export const AppearanceContext = createContext<AppearanceValue>({
    colorTheme: DEFAULT_COLOR_THEME,
    colorThemeOptions: [],
    setColorTheme: () => undefined,
    colorMode: DEFAULT_COLOR_MODE,
    colorModeOptions: [],
    setColorMode: () => undefined,
});

/** 見た目の設定を読む・変える */
export const useAppearance = () => useContext(AppearanceContext);

/** 保存した色のテーマを読む。保存がない・読めない・知らない値のときは、既定の色にする */
export const readStoredColorTheme = (): ColorTheme => readPreference(PREFERENCE_KEY.COLOR_THEME, isColorTheme, DEFAULT_COLOR_THEME);

/** 色のテーマを保存する */
export const writeStoredColorTheme = (colorTheme: ColorTheme) => writePreference(PREFERENCE_KEY.COLOR_THEME, colorTheme);

/** 保存した明るさを読む。保存がない・読めない・知らない値のときは、既定（ライト）にする */
export const readStoredColorMode = (): ColorMode => readPreference(PREFERENCE_KEY.COLOR_MODE, isColorMode, DEFAULT_COLOR_MODE);

/** 明るさを保存する */
export const writeStoredColorMode = (colorMode: ColorMode) => writePreference(PREFERENCE_KEY.COLOR_MODE, colorMode);
