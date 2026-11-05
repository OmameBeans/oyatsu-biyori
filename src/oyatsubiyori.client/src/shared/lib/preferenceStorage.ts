import type { PreferenceKey } from '../type';

/**
 * 画面の好み（色のテーマ・明るさ・サイドバーの開閉・列の幅）を、この端末のブラウザ（localStorage）に保存する。
 * 入れるのは画面の好みだけで、データ（商品・献立・画像など）は入れない（D-41）。
 * ブラウザの設定で保存が使えなくても、画面は既定の値で動く。キーは shared/type の PREFERENCE_KEY。
 */

/** 保存した値を読む。保存がない・読めない・知らない値のときは fallback を返す */
export const readPreference = <T extends string>(key: PreferenceKey, isValid: (value: unknown) => value is T, fallback: T): T => {
    try {
        const stored = window.localStorage.getItem(key);
        return isValid(stored) ? stored : fallback;
    } catch {
        return fallback;
    }
};

/** 値を保存する。保存できなくても、選んだものはこの画面の中では効く（再読み込みすると既定に戻る） */
export const writePreference = (key: PreferenceKey, value: string) => {
    try {
        window.localStorage.setItem(key, value);
    } catch {
        // 保存できないブラウザでは何もしない
    }
};
