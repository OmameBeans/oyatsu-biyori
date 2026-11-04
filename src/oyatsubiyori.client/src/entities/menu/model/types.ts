import type { AllergenJudgement } from '../../../shared/type';
import type { DayType, MenuStatus, SnackType } from '../../../shared/type';
import type { JobStatus } from '../../../shared/api';

/** 献立一覧の1週 */
export interface MenuListRow {
    weekStart: string;
    menuId: string | null;
    status: MenuStatus | null;
    total: number | null;
    budget: number | null;
    /** 週の予算 − 週の実績（サーバーが計算。超えたら負。未作成の週は null） */
    remaining: number | null;
    blockedCount: number;
    warningCount: number;
    recheckCount: number;
    itemCount: number;
    relativeLabel: string | null;
    isOverBudget: boolean;
}

/** 使えなくなった商品を使う献立の日 */
export interface BlockedUsage {
    menuId: string;
    weekStart: string;
    date: string;
    productName: string;
    judgement: AllergenJudgement | null;
}

/** 献立一覧 */
export interface MenuList {
    rows: MenuListRow[];
    futurePackageCheckCount: number;
    otherPackageCheckCount: number;
    draftCount: number;
    blockedUsages: BlockedUsage[];
    isReadyForMenu: boolean;
    /** 今週の初日（月曜）。これより前の週を「過去の献立」に分ける */
    thisWeekStart: string;
}

/** タグ */
export interface Tag {
    kind: 'Character' | 'Flavor';
    value: string;
}

/** 候補から外す理由 */
export type CandidateExclusion =
    | 'ProductNotFound'
    | 'AllergenBlocked'
    | 'NotListed'
    | 'MissingPrice'
    | 'MissingPieces'
    | 'InvalidPerChildCount'
    | 'NotSnack'
    | 'ProductDisabled'
    | 'ExcludedThisWeek';

/** 今週の商品の1件 */
export interface ListedProduct {
    productId: string;
    name: string;
    maker: string | null;
    volume: string | null;
    snackType: SnackType;
    tags: Tag[];
    price: number | null;
    piecesPerPack: number | null;
    perChildCount: number;
    orderNo: string | null;
    judgement: AllergenJudgement;
    daysSinceServed: number | null;
    isExcludedThisWeek: boolean;
    isDisabled: boolean;
    exclusions: CandidateExclusion[];
    isCandidate: boolean;
}

/** 手順2 今週の商品 */
export interface ListedProducts {
    items: ListedProduct[];
    candidateCount: number;
    candidateWarningCount: number;
    blockedCount: number;
    excludedCount: number;
    isAvoidedEmpty: boolean;
}

/** 日ごとの商品 */
export interface MenuItem {
    id: string;
    productId: string;
    productName: string;
    snackType: SnackType | null;
    tags: Tag[];
    perChildCount: number;
    packCount: number;
    unitPrice: number;
    amount: number;
    orderNo: string | null;
    judgement: AllergenJudgement | null;
    isAcknowledged: boolean;
    needsRecheck: boolean;
    judgementFingerprint: string | null;
}

/** 日ごとの献立 */
export interface MenuDay {
    id: string;
    date: string;
    childrenCount: number;
    childrenCountSource: 'Default' | 'FromRequest' | 'EditedByStaff';
    dayType: DayType;
    dayTypeEdited: boolean;
    label: string | null;
    labelEdited: boolean;
    reason: string | null;
    amount: number;
    perChildAmount: number | null;
    excludedFromAmount: boolean;
    items: MenuItem[];
}

/** 献立の問題 */
export interface MenuIssue {
    severity: 'Error' | 'Warning';
    kind: string;
    date: string | null;
    menuItemId: string | null;
    productId: string | null;
    productName: string | null;
    message: string;
    blocksConfirmation: boolean;
}

/** 要望の読み取り結果 */
export interface InterpretedRequests {
    days: { date: string; childrenCount: number | null; dayType: DayType | null; label: string | null }[];
    policies: { text: string; days: string[] | null }[];
    rejected: { text: string; reason: string }[];
}

/** チャットの発言 */
export interface MenuMessage {
    role: 'Staff' | 'Claude';
    content: string;
    createdAt: string;
}

/** 献立の中身 */
export interface MenuDetail {
    id: string;
    weekStart: string;
    status: MenuStatus;
    rowVersion: string;
    weeklyBudget: number;
    requestText: string | null;
    interpretedRequests: InterpretedRequests | null;
    confirmedAt: string | null;
    confirmedBy: string | null;
    listedCount: number;
    excludedProductIds: string[];
    defaultChildrenCount: number | null;
    avoidedAllergenNames: string[];
    duplicateRules: { snackType: SnackType; days: number }[];
    days: MenuDay[];
    week: { total: number; budget: number; remaining: number; isOverBudget: boolean; isUnderBudget: boolean };
    issues: MenuIssue[];
    messages: MenuMessage[];
}

/** 確定のダイアログ */
export interface MenuConfirmation {
    canConfirm: boolean;
    blockingIssues: { date: string | null; productName: string | null; message: string | null; judgement: AllergenJudgement | null }[];
    acknowledgements: { menuItemId: string; date: string; productName: string; judgement: AllergenJudgement; isAcknowledged: boolean; fingerprint: string }[];
    displayWarnings: string[];
    manufacturerCheckAllergens: string[];
}

/** 職員が確かめた警告の商品（画面で読んだ判定の種類と中身を送り、変わっていれば確認済みにしない。SR-22） */
export interface AcknowledgementInput {
    menuItemId: string;
    kind: AllergenJudgement['kind'];
    fingerprint: string;
}

/** 献立案の作成・組み直しの結果 */
export interface MenuProposalOutcome {
    accepted: boolean;
    removedItems: string[];
    notAppliedRequests: string[];
    isBudgetFitRequest: boolean;
}

/** 入れ替え案の種類 */
export type BudgetFitKind = 'Replace' | 'Add' | 'PackIncrease' | 'PackDecrease';

/** 入れ替え案 */
export interface BudgetFitOption {
    kind: BudgetFitKind;
    date: string;
    menuItemId: string | null;
    oldProductId: string | null;
    newProductId: string;
    newPackCount: number;
    currentTotal: number;
    newTotal: number;
    difference: number;
    usesWarningProduct: boolean;
    hasShortage: boolean;
    description: string;
    explanation: string | null;
}

/** 予算に合わせる案 */
export interface BudgetFit {
    currentTotal: number;
    budget: number;
    options: BudgetFitOption[];
    note: string | null;
}

/** 献立案の作成（job）とチャットの組み直し（chatJob）の、いちばん新しい処理（なければ null） */
export interface LatestJob {
    job: JobStatus<MenuProposalOutcome> | null;
    chatJob: JobStatus<MenuProposalOutcome> | null;
}
