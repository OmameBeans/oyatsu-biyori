import type { AllergenJudgement, MenuStatus, SnackType } from '../../../shared/type';

/** タグ */
export interface ProductTag {
    kind: 'Character' | 'Flavor';
    value: string;
}

/** 商品一覧の絞り込み（アレルゲンの表示で。ステータスでは絞り込まない。D-83） */
export const PRODUCT_FILTER = {
    ALL: 'All',
    BLOCKED: 'Blocked',
    WARNING: 'Warning',
    SAFE: 'Safe',
    PACKAGE: 'Package',
    OTHER_PACKAGE: 'OtherPackage',
    FUTURE_PACKAGE: 'FuturePackage',
} as const;
export type ProductFilter = (typeof PRODUCT_FILTER)[keyof typeof PRODUCT_FILTER];

/** 商品一覧の1件 */
export interface ProductListItem {
    id: string;
    name: string;
    maker: string | null;
    volume: string | null;
    snackType: SnackType;
    tags: ProductTag[];
    judgement: AllergenJudgement;
    lastCheckedDate: string | null;
    isDisabled: boolean;
    isUsedInFutureMenus: boolean;
}

/** 商品一覧 */
export interface ProductList {
    items: ProductListItem[];
    isAvoidedEmpty: boolean;
}

/** 記録のステータス（画面に名前を出すのは、商品詳細の履歴だけ） */
export type CheckStatus = 'Confirmed' | 'Reference';

/** 情報源 */
export type CheckSource = 'Package' | 'CoopSite' | 'Flyer' | 'Other';

/** 確認の履歴の1件 */
export interface CheckHistory {
    id: string;
    checkedAt: string;
    checkedBy: string;
    source: CheckSource;
    status: CheckStatus;
    containedAllergens: string[];
    contaminationNote: string | null;
    sourceNote: string | null;
    rawIngredients: string | null;
}

/** この商品を使う今日以降の献立の日 */
export interface ProductUsage {
    menuId: string;
    weekStart: string;
    date: string;
    status: MenuStatus;
}

/** 確認の対象の品目（29品目と、避ける品目にある施設の追加品目） */
export interface CheckTarget {
    id: string;
    name: string;
    category: 'Specified' | 'Recommended' | 'FacilityAdded';
    isAvoided: boolean;
    labelNotGuaranteed: boolean;
}

/** 商品詳細 */
export interface ProductDetail {
    id: string;
    name: string;
    maker: string | null;
    volume: string | null;
    piecesPerPack: number | null;
    defaultPerChildCount: number;
    price: number | null;
    priceExcludingTax: number | null;
    orderNo: string | null;
    targetAgeLabel: string | null;
    listedWeekStart: string | null;
    janCode: string | null;
    snackType: SnackType;
    memo: string | null;
    isDisabled: boolean;
    tags: ProductTag[];
    judgement: AllergenJudgement;
    status: 'Confirmed' | 'RecheckNeeded' | 'Reference' | 'Unknown';
    lastCheckedDate: string | null;
    checks: CheckHistory[];
    usages: ProductUsage[];
    checkTargets: CheckTarget[];
    flavorVocabulary: string[];
    isAvoidedEmpty: boolean;
}

/** 基本情報の入力 */
export interface ProductUpdateInput {
    name: string;
    maker: string | null;
    volume: string | null;
    piecesPerPack: number | null;
    defaultPerChildCount: number | null;
    price: number | null;
    janCode: string | null;
    snackType: SnackType;
    memo: string | null;
    isDisabled: boolean;
    characterTags: string[];
    flavorTags: string[];
}

/** 探す言葉の当たった範囲（元の全文での位置。サーバーで求める） */
export interface KeywordRange {
    allergenId: string;
    allergenName: string;
    keyword: string;
    start: number;
    length: number;
    matchedText: string;
}

/** 原材料表示の読み取り結果（候補） */
export interface AllergenExtraction {
    rawIngredients: string;
    containsAllergenIds: string[];
    contaminationNote: string | null;
    characterTags: string[];
    flavorTags: string[];
}

/** 確認の記録の入力（職員が確定した内容） */
export interface CheckRecordInput {
    source: CheckSource;
    sourceNote: string | null;
    rawIngredients: string | null;
    contaminationNote: string | null;
    items: { allergenId: string; contains: boolean }[];
    characterTags: string[];
    flavorTags: string[];
}

/** 記録したあとの結果 */
export interface CheckRecorded {
    judgement: AllergenJudgement;
    usages: ProductUsage[];
}
