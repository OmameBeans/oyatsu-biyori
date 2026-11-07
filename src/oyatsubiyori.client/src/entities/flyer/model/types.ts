import type { JobStatus } from '../../../shared/api';
import type { AllergenJudgement, SnackType } from '../../../shared/type';

/** 読み取った1商品（直した値か、Claude が読んだままの値） */
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
    /** 食品以外の掲載品（外した掲載品になる。09 の 3.7） */
    isNonFood: boolean;
    nonFoodReason: string | null;
}

/** 品目（id と表示名） */
export interface AllergenRef {
    id: string;
    name: string;
}

/**
 * 一覧の行の状態（サーバーの FlyerItemStatus）。値の順が一覧の並び（再入力 → 取り込める → 使えない → 確定済み。09 の 6）。
 * 取り込みの分類（09 の 3.1。D-101）に「確定済み」を足したもの。どの状態かはサーバーが決める（画面では判定しない）
 */
export const FLYER_ITEM_STATUS = {
    NEEDS_INPUT: 'NeedsInput',
    READY: 'Ready',
    UNUSABLE: 'Unusable',
    CONFIRMED: 'Confirmed',
} as const;
export type FlyerItemStatus = (typeof FLYER_ITEM_STATUS)[keyof typeof FLYER_ITEM_STATUS];

/** 取り込みの分類（サーバーの FlyerItemCategory。確定済みの行は null） */
export type FlyerItemCategory = 'Unusable' | 'NeedsInput' | 'Ready';

/** 行の項目（サーバーの FlyerItemField。理由の field・直した項目に使う） */
export const FLYER_ITEM_FIELD = {
    NAME: 'name',
    MAKER: 'maker',
    VOLUME: 'volume',
    PRICE_INCLUDING_TAX: 'priceIncludingTax',
    PRICE_EXCLUDING_TAX: 'priceExcludingTax',
    ORDER_NO: 'orderNo',
    PIECES_PER_PACK: 'piecesPerPack',
    ALLERGEN_LABEL: 'allergenLabel',
    SNACK_TYPE: 'snackType',
} as const;
export type FlyerItemField = (typeof FLYER_ITEM_FIELD)[keyof typeof FLYER_ITEM_FIELD];

/** 理由・注意の種類（サーバーの FlyerItemReasonCode） */
export const FLYER_REASON_CODE = {
    // 使えない（表記の読み違いを直したとき・入数を入れたときだけ変わる）
    PIECES_UNKNOWN: 'PiecesUnknown',
    AVOIDED_IN_LABEL: 'AvoidedInLabel',
    AVOIDED_IN_DESCRIPTION: 'AvoidedInDescription',
    AVOIDED_IN_NAME: 'AvoidedInName',
    AVOIDED_IN_MASTER: 'AvoidedInMaster',
    DISABLED: 'Disabled',
    // 再入力（直すと分類し直す）
    MISSING_NAME: 'MissingName',
    MISSING_MAKER: 'MissingMaker',
    MISSING_PRICE: 'MissingPrice',
    UNMAPPED_TERM: 'UnmappedTerm',
    LABEL_ABSENT_BUT_MASTER_CONTAINS: 'LabelAbsentButMasterContains',
    MISSING_SNACK_TYPE: 'MissingSnackType',
    // 注意（行に出すだけで分類は変えない。09 の 3.8）
    MASTER_DISCREPANCY: 'MasterDiscrepancy',
    REVIEW_NOTE: 'ReviewNote',
    MASTER_VOLUME_OR_PIECES_CHANGED: 'MasterVolumeOrPiecesChanged',
    DUPLICATE_CONFLICT: 'DuplicateConflict',
    TAX_MISMATCH: 'TaxMismatch',
    MISSING_VOLUME: 'MissingVolume',
    MISSING_ORDER_NO: 'MissingOrderNo',
    PIECES_MISMATCH: 'PiecesMismatch',
    NOT_REVIEWED: 'NotReviewed',
} as const;
export type FlyerReasonCode = (typeof FLYER_REASON_CODE)[keyof typeof FLYER_REASON_CODE];

/** 理由・注意の重さ（サーバーの FlyerItemReasonLevel） */
export type FlyerItemReasonLevel = 'Unusable' | 'NeedsInput' | 'Note';

/** 分類の理由・注意（サーバーが決めた文。並びはアレルゲンに関わるものが先） */
export interface FlyerItemReason {
    code: FlyerReasonCode;
    /** 行の中で一意の鍵（React の key に使う） */
    key: string;
    level: FlyerItemReasonLevel;
    /** 画面に出す文（ReviewNote は Claude の文。テキストとしてだけ出す） */
    message: string;
    /** 当てはまる項目。読み取りの検証の指摘（ReviewNote）では Claude の項目名（「価格」「アレルゲン」など）、ほかは FlyerItemField の値 */
    field: string | null;
    isAllergenRelated: boolean;
    /** LabelAbsentButMasterContains で、商品一覧の記録で含む特定原材料（ほかは空） */
    allergens: AllergenRef[];
}

/** 確認済みの商品とチラシの表記の食い違い（09 の 3.5。取り込みを終えると「参考」の記録を足す） */
export interface FlyerDiscrepancy {
    allergen: AllergenRef;
    isAvoided: boolean;
    onlyFromDescription: boolean;
}

/** 略記から当てた品目（「麦→小麦」） */
export interface FlyerAbbreviationHit {
    term: string;
    allergen: AllergenRef;
}

/** 品目の分からない語に職員が選んだ品目（空は「対応する品目なし」） */
export interface FlyerResolvedTerm {
    term: string;
    allergens: AllergenRef[];
}

/** 一致した商品（名前＋メーカーの完全一致。確定済みの行は取り込みに使った商品） */
export interface FlyerMatchedProduct {
    id: string;
    name: string;
    maker: string | null;
    /** 商品一覧の内容量 */
    volume: string | null;
    /** 商品一覧の入数 */
    piecesPerPack: number | null;
    isDisabled: boolean;
    listedWeekStart: string | null;
    /** 商品一覧でのアレルゲンの判定（🔴🟡🟢 と理由は共通部品で出す） */
    judgement: AllergenJudgement;
}

/** 二重に読んだ商品の食い違い（FR-IMPORT-06） */
export interface FlyerDuplicateConflict {
    /** FlyerItemField の値 */
    field: string;
    values: string[];
}

/** 取り込んだ内容 */
export interface FlyerItemConfirmation {
    kind: 'UpdatedExisting' | 'CreatedNew';
    productId: string;
    productName: string;
    addedReference: boolean;
}

/** 取り込みの1商品（サーバーの FlyerItemView） */
export interface FlyerItem {
    id: string;
    status: FlyerItemStatus;
    category: FlyerItemCategory | null;
    /** 新しい商品の印（未確定：一致する商品がない。確定済み：新しい商品として登録した） */
    isNew: boolean;
    isManual: boolean;
    /** 使えない（入数が分からない）に職員が入数を入れた */
    isPiecesEnteredByStaff: boolean;
    /** 今の値（直した値） */
    read: FlyerReadProduct;
    /** Claude が読んだままの値 */
    originalRead: FlyerReadProduct;
    /** 職員が直した項目（FlyerItemField の値） */
    editedFields: string[];
    /** 使えない・再入力の理由（アレルゲンに関わるものが先） */
    reasons: FlyerItemReason[];
    /** 注意（分類は変えない。アレルゲンに関わるものが先。09 の 3.8） */
    notes: FlyerItemReason[];
    labelAllergens: AllergenRef[];
    abbreviationHits: FlyerAbbreviationHit[];
    unmappedTerms: string[];
    resolvedTerms: FlyerResolvedTerm[];
    unresolvedTerms: string[];
    descriptionHitWords: string[];
    flyerAllergens: AllergenRef[];
    discrepancies: FlyerDiscrepancy[];
    /** 食い違いで「参考」の記録を足した品目 */
    recordedDiscrepancyAllergens: AllergenRef[];
    /** アレルゲンの表記がない（表記の品目も品目の分からない語もない） */
    isLabelAbsent: boolean;
    /** 取り込みを終えると掲載品の写しに入る表記の原文（献立の確定で見比べる表記） */
    listingLabelText: string;
    matchedProduct: FlyerMatchedProduct | null;
    /** ほかに同じ名前の商品が n 件（FR-IMPORT-04） */
    otherMatchCount: number;
    notMatchableReason: 'MissingName' | 'MissingMaker' | null;
    /** 使う入数（候補 → 一致した商品の入数） */
    piecesPerPack: number | null;
    duplicateConflicts: FlyerDuplicateConflict[];
    /** まとめた読み取りの数 */
    sourceCount: number;
    confirmation: FlyerItemConfirmation | null;
}

/** 外した掲載品（09 の 3.7） */
export interface FlyerRemovedItem {
    id: string;
    name: string;
    maker: string | null;
    volume: string | null;
    reason: string;
    isNonFood: boolean;
    /** 確認済みの商品との食い違いの品目の名前（取り込みを終えると「参考」の記録を足す。09 の 3.5） */
    discrepancyAllergens: string[];
}

/** 状態ごとの件数（タブに出す） */
export interface FlyerCategoryCounts {
    needsInput: number;
    ready: number;
    unusable: number;
    confirmed: number;
    removed: number;
}

/** 「取り込みを終える」で取り込む件数（readyCount ＝ 取り込める。newCount ＝ そのうち新しい商品、existingCount ＝ 登録済みの商品） */
export interface FlyerFinishSummary {
    readyCount: number;
    newCount: number;
    existingCount: number;
}

/** 今週の商品に入らない件数（total ＝ 使えない ＋ 再入力。外した掲載品は数えない） */
export interface FlyerNotListedCounts {
    total: number;
    unusable: number;
    needsInput: number;
}

/** 取り込みの結果（取り込みを終える前の一覧） */
export interface FlyerSession {
    id: string;
    isReimport: boolean;
    reviewFailed: boolean;
    missedProducts: { name: string; reason: string }[];
    /** 外した掲載品を除く。並び：再入力 → 取り込める → 使えない → 確定済み */
    items: FlyerItem[];
    removedItems: FlyerRemovedItem[];
    counts: FlyerCategoryCounts;
    finish: FlyerFinishSummary;
    notListed: FlyerNotListedCounts;
    /** 確定済み・外したを除く行数 */
    unconfirmedCount: number;
}

/** 取り込みの状態 */
export interface FlyerImport {
    job: JobStatus<string> | null;
    session: FlyerSession | null;
}

/** 食い違いで「参考」の記録を足した商品 */
export interface FlyerRecordedProduct {
    productId: string;
    productName: string;
    allergens: AllergenRef[];
}

/** 「取り込みを終える」の結果 */
export interface FlyerFinishResult {
    /** 新しく登録した件数 */
    createdCount: number;
    /** 登録済みの商品を上書きした件数 */
    updatedCount: number;
    /** 使えない（今週の商品に入らない）件数 */
    notListedUnusableCount: number;
    /** 再入力（今週の商品に入らない）件数 */
    notListedNeedsInputCount: number;
    confirmedItemIds: string[];
    /** 確認済みとの食い違いで「参考」の記録を足した商品（09 の 3.5） */
    recordedProducts: FlyerRecordedProduct[];
}

/**
 * 行を直す入力（行のすべての値。今の値と違う項目だけを、サーバーが「直した項目」にする）。
 * 文字は空なら値なし。タグ・品目は null（省く）なら変えない
 */
export interface FlyerItemEditInput {
    itemId: string;
    name: string;
    maker: string | null;
    volume: string | null;
    priceIncludingTax: number | null;
    priceExcludingTax: number | null;
    orderNo: string | null;
    piecesPerPack: number | null;
    snackType: SnackType | null;
    targetAgeLabel: string | null;
    characterTags?: string[] | null;
    flavorTags?: string[] | null;
    labelAllergenIds?: string[] | null;
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
