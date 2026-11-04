import type { JobStatus } from '../../../shared/api';
import type { SnackType } from '../../../shared/type';

/** 読み取った1商品 */
export interface FlyerReadProduct {
    name: string;
    maker: string | null;
    volume: string | null;
    priceExcludingTax: number | null;
    priceIncludingTax: number | null;
    orderNo: string | null;
    targetAgeLabel: string | null;
    allergenLabelText: string | null;
    allergenLabelAbsent: boolean;
    allergenCodes: string[];
    unmappedAllergenTerms: string[];
    description: string | null;
    bulkNote: string | null;
    piecesPerPackCandidate: number | null;
    piecesPerPackReason: string | null;
    snackType: SnackType | null;
    characterTags: string[];
    flavorTags: string[];
}

/** 照合の候補 */
export interface FlyerMatchProduct {
    productId: string;
    name: string;
    maker: string | null;
    volume: string | null;
    makerCompared: boolean;
    isDisabled: boolean;
    score: number | null;
}

/** 取り込みの選択 */
export const FLYER_CHOICE = {
    SAME: 'Same',
    VARIANT: 'Variant',
    NEW: 'New',
    SKIP: 'Skip',
} as const;
export type FlyerChoice = (typeof FLYER_CHOICE)[keyof typeof FLYER_CHOICE];

/** 照合の判定（AI） */
export interface FlyerJudgement {
    verdict: 'Same' | 'FlavorVariant' | 'VolumeVariant' | 'Different';
    productId: string | null;
    productName: string | null;
    reason: string;
}

/** 確認済みの商品との食い違い（SR-20） */
export interface FlyerDiscrepancy {
    allergen: { id: string; name: string };
    isAvoided: boolean;
    onlyFromDescription: boolean;
}

/** 取り込みの1商品 */
export interface FlyerItem {
    id: string;
    isManual: boolean;
    read: FlyerReadProduct;
    labelAllergens: { id: string; name: string }[];
    unmappedTerms: string[];
    descriptionHitWords: string[];
    discrepancies: FlyerDiscrepancy[];
    referenceDecision: 'None' | 'Add' | 'AddForDiscrepancy' | 'StaffChoice';
    issues: { source: 'app' | 'review'; field: string; message: string }[];
    sameProducts: FlyerMatchProduct[];
    volumeVariants: FlyerMatchProduct[];
    candidates: FlyerMatchProduct[];
    judgement: FlyerJudgement | null;
    judgementSkipped: boolean;
    canBulkConfirm: boolean;
    /** 「同じ商品」として選べる商品（ルールで同じと決まったもの・AI が同じと判定したものだけ。SR-05） */
    sameChoiceProductIds: string[];
    /** 「同じ商品」の候補ごとの食い違いと「参考」の記録の扱い（選んだ候補で出し分ける。SR-20） */
    plansByProduct: Record<string, { discrepancies: FlyerDiscrepancy[]; referenceDecision: 'None' | 'Add' | 'AddForDiscrepancy' | 'StaffChoice' }>;
    suggestedChoice: FlyerChoice;
    suggestedProductId: string | null;
    confirmation: { choice: FlyerChoice; productId: string | null; productName: string | null; addedReference: boolean } | null;
}

/** 確定前の読み取り結果 */
export interface FlyerSession {
    id: string;
    isReimport: boolean;
    reviewFailed: boolean;
    missedProducts: { name: string; reason: string }[];
    items: FlyerItem[];
    unconfirmedCount: number;
}

/** 取り込みの状態 */
export interface FlyerImport {
    job: JobStatus<string> | null;
    session: FlyerSession | null;
}

/** 確定の入力 */
export interface FlyerConfirmInput {
    itemId: string;
    choice: FlyerChoice;
    productId: string | null;
    name: string;
    maker: string | null;
    volume: string | null;
    piecesPerPack: number | null;
    piecesEdited: boolean;
    snackType: SnackType | null;
    snackTypeEdited: boolean;
    priceIncludingTax: number | null;
    priceExcludingTax: number | null;
    orderNo: string | null;
    targetAgeLabel: string | null;
    characterTags: string[];
    flavorTags: string[];
    labelAllergenIds: string[];
    resolvedTerms: string[];
    treatAsFlyerError: boolean;
}

/** 手入力の商品 */
export interface FlyerManualInput {
    name: string;
    maker: string | null;
    volume: string | null;
    priceIncludingTax: number | null;
    priceExcludingTax: number | null;
    orderNo: string | null;
    piecesPerPack: number | null;
    snackType: SnackType | null;
    allergenLabelText: string | null;
}
