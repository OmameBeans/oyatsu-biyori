import { createContext, useContext } from 'react';
import { DEFAULT_COLOR_THEME, isColorTheme } from '../type';
import type { ColorTheme } from '../type';

/** 色のテーマの選択肢（表示名と見本の色。値は app/theme.ts から渡す） */
export interface ColorThemeOption {
    id: ColorTheme;
    label: string;
    /** 見本の丸の色（そのテーマの primary.main） */
    swatch: string;
}

/**
 * 画面の見た目の設定（色のテーマ。#2）。状態は App が持ち、設定の「見た目」タブで変える。
 * 明るさ（ライト・ダーク。#4）を足すときは、ここに並べて足す。
 */
export interface AppearanceValue {
    colorTheme: ColorTheme;
    colorThemeOptions: readonly ColorThemeOption[];
    setColorTheme: (colorTheme: ColorTheme) => void;
}

export const AppearanceContext = createContext<AppearanceValue>({
    colorTheme: DEFAULT_COLOR_THEME,
    colorThemeOptions: [],
    setColorTheme: () => undefined,
});

/** 見た目の設定を読む・変える */
export const useAppearance = () => useContext(AppearanceContext);

/** 色のテーマを、この端末のブラウザに保存するキー。画面の好みだけで、データは入れない */
const COLOR_THEME_STORAGE_KEY = 'oyatsu-biyori.color-theme';

/** 保存した色のテーマを読む。保存がない・読めない（ブラウザの設定で使えないなど）・知らない値のときは、既定の色にする */
export const readStoredColorTheme = (): ColorTheme => {
    try {
        const stored = window.localStorage.getItem(COLOR_THEME_STORAGE_KEY);
        return isColorTheme(stored) ? stored : DEFAULT_COLOR_THEME;
    } catch {
        return DEFAULT_COLOR_THEME;
    }
};

/** 色のテーマを保存する。保存できなくても、選んだ色はこの画面の中では効く */
export const writeStoredColorTheme = (colorTheme: ColorTheme) => {
    try {
        window.localStorage.setItem(COLOR_THEME_STORAGE_KEY, colorTheme);
    } catch {
        // 保存できないブラウザでは、再読み込みすると既定の色に戻る
    }
};
