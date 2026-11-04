/** 画面の URL */
export const ROUTE = {
    LANDING: '/',
    LOGIN: '/login',
    MENUS: '/menus',
    /** 過去の献立（SC-14） */
    MENU_HISTORY: '/menus/history',
    PRODUCTS: '/products',
    SETTINGS: '/settings',
    /** パスワード変更のタブ（初期パスワードのあいだは、ここ以外に進めない。FR-AUTH-03b） */
    PASSWORD_TAB: '/settings?tab=password',
    AVOIDED_TAB: '/settings?tab=avoided',
} as const;

/** 献立の画面（SC-09） */
export const menuPath = (menuId: string, step?: number) => (step ? `/menus/${menuId}?step=${step}` : `/menus/${menuId}`);

/** 商品詳細（SC-07） */
export const productPath = (productId: string) => `/products/${productId}`;

/** アレルゲン確認（SC-08） */
export const allergenCheckPath = (productId: string) => `/products/${productId}/check`;
