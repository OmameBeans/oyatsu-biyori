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
 * 一覧の行の状態（サーバーの FlyerItemStatus）。値の順が一覧の並び（再入力 → 要確認 → 使える → 使えない → 確定済み。09 の 6）。
 * 取り込みの分類（09 の 3.1）に「確定済み」を足したもの。どの状態かはサーバーが決める（画面では判定しない）
 */
export const FLYER_ITEM_STATUS = {
    NEEDS_INPUT: 'NeedsInput',
    NEW: 'New',
    USABLE: 'Usable',
    UNUSABLE: 'Unusable',
    CONFIRMED: 'Confirmed',
} as const;
export type FlyerItemStatus = (typeof FLYER_ITEM_STATUS)[keyof typeof FLYER_ITEM_STATUS];

/** 取り込みの分類（サーバーの FlyerItemCategory。確定済みの行は null） */
export type FlyerItemCategory = 'Unusable' | 'NeedsInput' | 'New' | 'Usable';

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
} as const;
export type FlyerItemField = (typeof FLYER_ITEM_FIELD)[keyof typeof FLYER_ITEM_FIELD];

/** 理由の種類（サーバーの FlyerItemReasonCode） */
export const FLYER_REASON_CODE = {
    // 使えない
    PIECES_UNKNOWN: 'PiecesUnknown',
    AVOIDED_IN_LABEL: 'AvoidedInLabel',
    AVOIDED_IN_DESCRIPTION: 'AvoidedInDescription',
    AVOIDED_IN_NAME: 'AvoidedInName',
    AVOIDED_IN_MASTER: 'AvoidedInMaster',
    DISABLED: 'Disabled',
    // 再入力
    UNMAPPED_TERM: 'UnmappedTerm',
    LABEL_ABSENT_BUT_MASTER_CONTAINS: 'LabelAbsentButMasterContains',
    REVIEW_NOTE: 'ReviewNote',
    MISSING_NAME: 'MissingName',
    MISSING_MAKER: 'MissingMaker',
    MISSING_PRICE: 'MissingPrice',
    TAX_MISMATCH: 'TaxMismatch',
    MISSING_VOLUME: 'MissingVolume',
    PIECES_MISMATCH: 'PiecesMismatch',
    MISSING_ORDER_NO: 'MissingOrderNo',
    MASTER_VOLUME_OR_PIECES_CHANGED: 'MasterVolumeOrPiecesChanged',
    DUPLICATE_CONFLICT: 'DuplicateConflict',
    // 知らせるだけ
    MASTER_DISCREPANCY: 'MasterDiscrepancy',
} as const;
export type FlyerReasonCode = (typeof FLYER_REASON_CODE)[keyof typeof FLYER_REASON_CODE];

/** 分類の理由（サーバーが決めた文。並びはアレルゲンに関わるものが先） */
export interface FlyerItemReason {
    code: FlyerReasonCode;
    /** 「このままでよい」に使う鍵（行の中で一意） */
    key: string;
    level: 'Unusable' | 'NeedsInput' | 'Notice';
    message: string;
    /** 当てはまる項目。読み取りの検証の指摘（ReviewNote）では Claude の項目名（「価格」「アレルゲン」など）、ほかは FlyerItemField の値 */
    field: string | null;
    isAllergenRelated: boolean;
    /** 「このままでよい」を押せる */
    canAccept: boolean;
}

/** まとめての確定・登録に入らない理由（サーバーの FlyerBulkExclusion。09 の 5.2） */
export type FlyerBulkExclusion =
    | 'LabelAbsentNotCompared'
    | 'Manual'
    | 'PiecesEnteredByStaff'
    | 'ReviewFailed'
    | 'Editing'
    | 'MissingNewProductFields';

/** 確認済みの商品とチラシの表記の食い違い（09 の 3.5。確定すると「参考」の記録を足す） */
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

/** 一致した商品（名前＋メーカーの完全一致。確定済みの行は確定に使った商品） */
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

/** 確定した内容 */
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
    isManual: boolean;
    /** 使えない（入数が分からない）から入数を入れて戻した（まとめに入らない） */
    isPiecesEnteredByStaff: boolean;
    /** 今の値（直した値） */
    read: FlyerReadProduct;
    /** Claude が読んだままの値 */
    originalRead: FlyerReadProduct;
    /** 職員が直した項目（FlyerItemField の値） */
    editedFields: string[];
    /** 使えない・再入力の理由（アレルゲンに関わるものが先） */
    reasons: FlyerItemReason[];
    /** 知らせるだけのこと（パッケージでの確認との食い違い） */
    notices: FlyerItemReason[];
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
    isLabelAbsent: boolean;
    /** 表記なしのまとめての見比べに並べる */
    canMarkLabelAbsentCompared: boolean;
    isLabelAbsentCompared: boolean;
    matchedProduct: FlyerMatchedProduct | null;
    /** ほかに同じ名前の商品が n 件（FR-IMPORT-04） */
    otherMatchCount: number;
    notMatchableReason: 'MissingName' | 'MissingMaker' | null;
    /** 使う入数（候補 → 一致した商品の入数） */
    piecesPerPack: number | null;
    duplicateConflicts: FlyerDuplicateConflict[];
    /** まとめた読み取りの数 */
    sourceCount: number;
    /** まとめての確定・登録に入る */
    canBulk: boolean;
    bulkExclusions: FlyerBulkExclusion[];
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
    /** 確認済みの商品との食い違いの品目の名前（次の確定で「参考」の記録を足す。09 の 3.5） */
    discrepancyAllergens: string[];
}

/** 状態ごとの件数（タブに出す） */
export interface FlyerCategoryCounts {
    needsInput: number;
    new: number;
    usable: number;
    unusable: number;
    confirmed: number;
    removed: number;
}

/** まとめに入らない理由ごとの件数（1行が複数の理由に数えられる） */
export interface FlyerBulkExclusionCount {
    exclusion: FlyerBulkExclusion;
    message: string;
    count: number;
}

/** まとめての確定・登録の対象（サーバーが決める） */
export interface FlyerBulkSummary {
    count: number;
    itemIds: string[];
    excludedCount: number;
    exclusions: FlyerBulkExclusionCount[];
}

/** 今週の商品に入らない件数（unconfirmed は要確認＋使えるの未確定） */
export interface FlyerNotListedCounts {
    total: number;
    unusable: number;
    needsInput: number;
    unconfirmed: number;
}

/** 確定前の読み取り結果 */
export interface FlyerSession {
    id: string;
    isReimport: boolean;
    reviewFailed: boolean;
    missedProducts: { name: string; reason: string }[];
    /** 外した掲載品を除く。並び：再入力 → 要確認 → 使える → 使えない → 確定済み */
    items: FlyerItem[];
    removedItems: FlyerRemovedItem[];
    counts: FlyerCategoryCounts;
    bulkUsable: FlyerBulkSummary;
    bulkNew: FlyerBulkSummary;
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

/** 確定の結果 */
export interface FlyerConfirmResult {
    confirmedCount: number;
    /** まとめての確定で、条件から外れて確定しなかった件数 */
    skippedCount: number;
    confirmedItemIds: string[];
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
