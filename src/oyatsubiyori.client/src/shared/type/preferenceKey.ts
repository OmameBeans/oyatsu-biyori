/**
 * 画面の好み（色のテーマ・明るさ・サイドバーの開閉・列の幅）を、この端末のブラウザ（localStorage）に保存するときのキー。
 * 明るさのキーは index.html の最初のスクリプトでも読む（読み込み中の画面を暗くするため。TS を import できないので文字で書いてある）。
 * キーを変えるときは index.html も直す。E2E（e2e/）はこの定数を import して使う。
 */
export const PREFERENCE_KEY = {
    COLOR_THEME: 'oyatsu-biyori.color-theme',
    COLOR_MODE: 'oyatsu-biyori.color-mode',
    SIDEBAR_COLLAPSED: 'oyatsu-biyori.sidebar-collapsed',
    /** 献立案の画面の、使える商品・チャットの列の幅（px。「左,右」） */
    MENU_COLUMN_WIDTHS: 'oyatsu-biyori.menu-column-widths',
    /** 献立案の画面の、AI チャットの列を閉じているか（true / false。広い PC だけ） */
    MENU_CHAT_COLLAPSED: 'oyatsu-biyori.menu-chat-collapsed',
    /** 献立案の画面の、使える商品の列を閉じているか（true / false。広い PC だけ） */
    MENU_PRODUCTS_COLLAPSED: 'oyatsu-biyori.menu-products-collapsed',
    /** 献立一覧の表示（list か calendar） */
    MENU_LIST_VIEW: 'oyatsu-biyori.menu-list-view',
} as const;
export type PreferenceKey = (typeof PREFERENCE_KEY)[keyof typeof PREFERENCE_KEY];
