/** 判定の区分（色を決める。ステータスでは決めない。03 §6.1） */
export const JUDGEMENT_CATEGORY = {
    BLOCKED: 'Blocked',
    WARNING: 'Warning',
    SAFE: 'Safe',
} as const;
export type JudgementCategory = (typeof JUDGEMENT_CATEGORY)[keyof typeof JUDGEMENT_CATEGORY];

/** 細かい判定の種類 */
export const JUDGEMENT_KIND = {
    CONTAINS: 'Contains',
    MAY_CONTAIN: 'MayContain',
    UNKNOWN: 'Unknown',
    REFERENCE: 'Reference',
    RECHECK_NEEDED: 'RecheckNeeded',
    UNCONFIRMED_ITEMS: 'UnconfirmedItems',
    SAFE: 'Safe',
} as const;
export type JudgementKind = (typeof JUDGEMENT_KIND)[keyof typeof JUDGEMENT_KIND];

/** 確認の記録の情報源 */
export const CHECK_SOURCE = {
    PACKAGE: 'Package',
    COOP_SITE: 'CoopSite',
    FLYER: 'Flyer',
    OTHER: 'Other',
} as const;
export type CheckSource = (typeof CHECK_SOURCE)[keyof typeof CHECK_SOURCE];

/** 探す言葉が当たった場所 */
export type KeywordHitSource = 'ProductName' | 'RawIngredientsOcr' | 'RawIngredients';

/** 当たった探す言葉 */
export interface KeywordHit {
    allergenName: string;
    keyword: string;
    matchedText: string;
    source: KeywordHitSource;
}

/** アレルゲンの判定（サーバーの AllergenJudgementView。文言は共通部品で作る） */
export interface AllergenJudgement {
    category: JudgementCategory;
    kind: JudgementKind;
    containedAllergens: string[];
    keywordHits: KeywordHit[];
    unconfirmedAllergens: string[];
    manufacturerCheckAllergens: string[];
    referenceSource: CheckSource | null;
    referenceHasNoLabel: boolean;
    referenceAfterConfirmed: boolean;
    containsFromReference: boolean;
    daysSinceConfirmed: number | null;
    lastConfirmedDate: string | null;
}
