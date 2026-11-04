import type { SnackType, Weekday } from '../../../shared/type';

/** 分類ごとの重複させない期間 */
export interface DuplicateAvoidRule {
    snackType: SnackType;
    days: number;
}

/** 条件設定 */
export interface Settings {
    defaultChildrenCount: number | null;
    defaultWeeklyBudget: number | null;
    servingWeekdays: Weekday[];
    recheckDays: number;
    duplicateAvoidDays: DuplicateAvoidRule[];
    duplicateRuleNote: string | null;
    isReadyForMenu: boolean;
}

export type SettingsInput = Omit<Settings, 'isReadyForMenu'>;

/** アレルゲンの区分 */
export const ALLERGEN_CATEGORY = {
    SPECIFIED: 'Specified',
    RECOMMENDED: 'Recommended',
    FACILITY_ADDED: 'FacilityAdded',
} as const;
export type AllergenCategory = (typeof ALLERGEN_CATEGORY)[keyof typeof ALLERGEN_CATEGORY];

/** 探す言葉 */
export interface AllergenKeyword {
    id: string;
    keyword: string;
    isSeed: boolean;
}

/** 品目（マスタはフロントの定数にせず、API から取る。D-80） */
export interface AllergenItem {
    id: string;
    code: string;
    name: string;
    category: AllergenCategory;
    labelNotGuaranteed: boolean;
    sortOrder: number;
    isAvoided: boolean;
    canDelete: boolean;
    keywords: AllergenKeyword[];
}

/** グループ */
export interface AllergenGroup {
    id: string;
    name: string;
    allergenIds: string[];
}

/** 品目のマスタ */
export interface AllergenMaster {
    allergens: AllergenItem[];
    groups: AllergenGroup[];
}
