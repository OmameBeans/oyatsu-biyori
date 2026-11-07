import { FLYER_ITEM_FIELD, FLYER_ITEM_STATUS, FLYER_REASON_CODE } from '../../../entities/flyer';
import type { FlyerConfirmResult, FlyerItem, FlyerItemReason, FlyerItemStatus, FlyerSession } from '../../../entities/flyer';

/*
 * 読み取り結果の一覧の表示だけを受け持つ（09 の 6）。
 * 分類・理由・まとめの可否はサーバー（Biz）が決めて返すので、ここでは判定しない。行の状態（status）・件数・並びもサーバーの値をそのまま使う。
 */

/**
 * 取り込みの分類のチップの名前。アレルゲンの3つの表示（🔴🟡🟢）とは別のもので、取り込みの画面の中だけで使う（09 の 3.1）
 */
export const FLYER_STATUS_LABEL: Record<FlyerItemStatus, string> = {
    NeedsInput: '再入力',
    New: '要確認',
    Usable: '使える',
    Unusable: '使えない',
    Confirmed: '確定済み',
};

/**
 * 取り込みの分類のチップの色（使えない＝赤・再入力＝橙・要確認＝青・使える＝緑）。
 * 要確認を黄にしないのは、アレルゲンの 🟡 要確認と見分けるため（09 の 3.1）
 */
export const FLYER_STATUS_COLOR: Record<FlyerItemStatus, 'error' | 'warning' | 'info' | 'success' | 'default'> = {
    NeedsInput: 'warning',
    New: 'info',
    Usable: 'success',
    Unusable: 'error',
    Confirmed: 'default',
};

/** 表の上のタブ（「すべて」と各状態。並びは一覧の並びと同じ：再入力 → 要確認 → 使える → 使えない → 確定済み） */
export const FLYER_ROW_TAB = {
    ALL: 'All',
    ...FLYER_ITEM_STATUS,
} as const;
export type FlyerRowTab = (typeof FLYER_ROW_TAB)[keyof typeof FLYER_ROW_TAB];

export const FLYER_ROW_TABS: FlyerRowTab[] = [
    FLYER_ROW_TAB.ALL,
    FLYER_ROW_TAB.NEEDS_INPUT,
    FLYER_ROW_TAB.NEW,
    FLYER_ROW_TAB.USABLE,
    FLYER_ROW_TAB.UNUSABLE,
    FLYER_ROW_TAB.CONFIRMED,
];

export const flyerTabLabel = (tab: FlyerRowTab) => (tab === FLYER_ROW_TAB.ALL ? 'すべて' : FLYER_STATUS_LABEL[tab]);

/** タブの件数（サーバーの counts をそのまま出す） */
export const flyerTabCount = (session: FlyerSession, tab: FlyerRowTab): number => {
    switch (tab) {
        case FLYER_ROW_TAB.ALL:
            return session.items.length;
        case FLYER_ROW_TAB.NEEDS_INPUT:
            return session.counts.needsInput;
        case FLYER_ROW_TAB.NEW:
            return session.counts.new;
        case FLYER_ROW_TAB.USABLE:
            return session.counts.usable;
        case FLYER_ROW_TAB.UNUSABLE:
            return session.counts.unusable;
        case FLYER_ROW_TAB.CONFIRMED:
            return session.counts.confirmed;
    }
};

/** タブで絞り込む（並びはサーバーが返したまま） */
export const filterItems = (items: FlyerItem[], tab: FlyerRowTab): FlyerItem[] => (tab === FLYER_ROW_TAB.ALL ? items : items.filter((item) => item.status === tab));

/** 行を開いて確かめる対象（確定できる・直せる行）。使えない・確定済みは確定しないので、確定のあとに次の行として開かない */
export const isOpenableNext = (item: FlyerItem) =>
    item.status === FLYER_ITEM_STATUS.NEEDS_INPUT || item.status === FLYER_ITEM_STATUS.NEW || item.status === FLYER_ITEM_STATUS.USABLE;

/** 「未確定を1件ずつ確かめる」の対象（再入力と要確認。09 の 6） */
export const isReviewTarget = (item: FlyerItem) => item.status === FLYER_ITEM_STATUS.NEEDS_INPUT || item.status === FLYER_ITEM_STATUS.NEW;

/**
 * 確定したあとに開く行：今の行より下にある、確かめる対象の行。なければ上から探す。どれもなければ null（閉じる）
 * items は確定する前の並び（確定した行は、このあと一番下へ移る）
 */
export const nextOpenableItemId = (items: FlyerItem[], currentId: string): string | null => {
    const index = items.findIndex((item) => item.id === currentId);
    const isTarget = (item: FlyerItem) => item.id !== currentId && isOpenableNext(item);
    return items.slice(index + 1).find(isTarget)?.id ?? items.find(isTarget)?.id ?? null;
};

/** 項目の表示名（理由の field・二重の読み取りの食い違いに使う） */
export const FIELD_LABEL: Record<string, string> = {
    [FLYER_ITEM_FIELD.NAME]: '商品名',
    [FLYER_ITEM_FIELD.MAKER]: 'メーカー',
    [FLYER_ITEM_FIELD.VOLUME]: '内容量',
    [FLYER_ITEM_FIELD.PRICE_INCLUDING_TAX]: '税込価格',
    [FLYER_ITEM_FIELD.PRICE_EXCLUDING_TAX]: '税抜価格',
    [FLYER_ITEM_FIELD.ORDER_NO]: '注文番号',
    [FLYER_ITEM_FIELD.PIECES_PER_PACK]: '入数',
    [FLYER_ITEM_FIELD.ALLERGEN_LABEL]: 'アレルゲン表記',
};

/** 鍵の「種類:値」の値の部分（品目の分からない語・食い違いの項目） */
const keyValueOf = (key: string) => key.slice(key.indexOf(':') + 1);

/** 文の、最初の「（」より前（「卵を含みます（チラシの表記）」→「卵を含みます」） */
const beforeParen = (message: string) => message.split('（')[0];

/**
 * 理由の短い表示（表の分類のチップの下に出す）。文はサーバーの理由から作り、条件の判定はしない。
 * 詳しい文は、行を開いた先の確定のフォームに出す
 */
export const shortReasonOf = (reason: FlyerItemReason): string => {
    switch (reason.code) {
        case FLYER_REASON_CODE.PIECES_UNKNOWN:
            return '入数が分からない';
        case FLYER_REASON_CODE.AVOIDED_IN_LABEL:
        case FLYER_REASON_CODE.AVOIDED_IN_DESCRIPTION:
        case FLYER_REASON_CODE.AVOIDED_IN_NAME:
        case FLYER_REASON_CODE.AVOIDED_IN_MASTER:
            return beforeParen(reason.message);
        case FLYER_REASON_CODE.DISABLED:
            return '「使わない」の商品';
        case FLYER_REASON_CODE.UNMAPPED_TERM:
            return `「${keyValueOf(reason.key)}」の品目が分からない`;
        case FLYER_REASON_CODE.LABEL_ABSENT_BUT_MASTER_CONTAINS:
            return '表記なしだが記録と違う';
        case FLYER_REASON_CODE.REVIEW_NOTE:
            return `${reason.field ?? '読み取り'}を確認`;
        case FLYER_REASON_CODE.MISSING_NAME:
            return '商品名が読めない';
        case FLYER_REASON_CODE.MISSING_MAKER:
            return 'メーカーが読めない';
        case FLYER_REASON_CODE.MISSING_PRICE:
            return '税込価格が読めない';
        case FLYER_REASON_CODE.TAX_MISMATCH:
            return '税込と税抜が合わない';
        case FLYER_REASON_CODE.MISSING_VOLUME:
            return '内容量が読めない';
        case FLYER_REASON_CODE.PIECES_MISMATCH:
            return '入数が内容量と合わない';
        case FLYER_REASON_CODE.MISSING_ORDER_NO:
            return '注文番号が読めない';
        case FLYER_REASON_CODE.MASTER_VOLUME_OR_PIECES_CHANGED:
            return '内容量・入数が商品一覧と違う';
        case FLYER_REASON_CODE.DUPLICATE_CONFLICT:
            return `${FIELD_LABEL[keyValueOf(reason.key)] ?? '読んだ値'}が食い違い`;
        default:
            return beforeParen(reason.message);
    }
};

/** 短い理由を並べる（サーバーの並び＝アレルゲンに関わるものが先。同じ文は1つにまとめる） */
export const shortReasonsOf = (item: FlyerItem): string[] => Array.from(new Set(item.reasons.map(shortReasonOf)));

/** 入数が分からない（使えない）の行（行に入数の欄を出す。FR-IMPORT-11） */
export const isPiecesUnknown = (item: FlyerItem) => item.reasons.some((reason) => reason.code === FLYER_REASON_CODE.PIECES_UNKNOWN);

/** 表の「アレルゲン表記」の列に並べるチップ */
export interface AllergenChip {
    key: string;
    label: string;
    color: 'default' | 'warning' | 'error';
    variant: 'filled' | 'outlined';
    isDashed: boolean;
}

/**
 * 行を開かなくても見落とさないように、表記の品目（略記から当てた品目は「麦→小麦」）に加えて、表記なし・品目の分からない語・
 * 職員が選んだ語・説明文の避ける品目の言葉・パッケージでの確認との食い違い（避ける品目なら error）もチップにする
 */
export const allergenChipsOf = (item: FlyerItem): AllergenChip[] => {
    const termsOf = (allergenId: string) =>
        Array.from(new Set(item.abbreviationHits.filter((hit) => hit.allergen.id === allergenId).map((hit) => hit.term)));
    const chips: AllergenChip[] = [];
    if (item.isLabelAbsent) {
        chips.push({ key: 'absent', label: '表記なし', color: 'warning', variant: 'outlined', isDashed: true });
    }
    item.labelAllergens.forEach((allergen) => {
        const terms = termsOf(allergen.id);
        chips.push({
            key: `label-${allergen.id}`,
            label: terms.length > 0 ? `${terms.join('・')}→${allergen.name}` : allergen.name,
            color: 'default',
            variant: 'filled',
            isDashed: false,
        });
    });
    item.resolvedTerms.forEach((resolved) =>
        chips.push({
            key: `resolved-${resolved.term}`,
            label: `「${resolved.term}」→${resolved.allergens.length > 0 ? resolved.allergens.map((a) => a.name).join('・') : '品目なし'}`,
            color: 'default',
            variant: 'outlined',
            isDashed: false,
        }),
    );
    item.unresolvedTerms.forEach((term) =>
        chips.push({ key: `unmapped-${term}`, label: `「${term}」品目不明`, color: 'warning', variant: 'outlined', isDashed: false }),
    );
    item.descriptionHitWords.forEach((word) =>
        chips.push({ key: `hit-${word}`, label: `説明文：${word}`, color: 'warning', variant: 'outlined', isDashed: false }),
    );
    if (item.discrepancies.length > 0) {
        chips.push({
            key: 'discrepancy',
            label: `パッケージでの確認と食い違い：${item.discrepancies.map((d) => d.allergen.name).join('・')}`,
            color: item.discrepancies.some((d) => d.isAvoided) ? 'error' : 'warning',
            variant: 'outlined',
            isDashed: false,
        });
    }
    return chips;
};

/** 表の上の「表記なしの見比べ」に並べる商品（サーバーが決めた canMarkLabelAbsentCompared。D-97） */
export const labelAbsentCompareItemsOf = (items: FlyerItem[]): FlyerItem[] => items.filter((item) => item.canMarkLabelAbsentCompared);

/** 「一致した商品」の列の2行（1行目：結果、2行目：補足） */
export const matchSummaryOf = (item: FlyerItem): { title: string; detail: string } => {
    if (item.confirmation) {
        return item.confirmation.kind === 'CreatedNew'
            ? { title: '新しい商品として登録しました', detail: item.confirmation.productName }
            : { title: '登録済みの商品に確定しました', detail: item.confirmation.productName };
    }
    if (item.matchedProduct) {
        return {
            title: item.matchedProduct.name,
            detail: item.otherMatchCount > 0 ? `ほかに同じ名前の商品が ${item.otherMatchCount}件あります` : '登録済みの商品と同じ',
        };
    }
    if (item.notMatchableReason === 'MissingMaker') {
        return { title: 'まだ分かりません', detail: 'メーカーを入れると、登録済みの商品と同じか調べます' };
    }
    if (item.notMatchableReason === 'MissingName') {
        return { title: 'まだ分かりません', detail: '商品名を入れると、登録済みの商品と同じか調べます' };
    }
    return { title: '新しい商品', detail: '登録済みの商品に、同じ名前とメーカーの商品はありません' };
};

/** 確定の結果の通知（確定した件数・確定しなかった件数・パッケージでの確認との食い違いで記録を足した商品） */
export const confirmResultMessages = (
    result: FlyerConfirmResult,
    confirmedText: string,
): { message: string; variant: 'success' | 'warning' | 'info' }[] => [
    { message: confirmedText, variant: 'success' },
    ...(result.skippedCount > 0
        ? [
              {
                  message: `${result.skippedCount}件は、まとめて確定できる条件から外れたため確定していません。表の行を開いて1件ずつ確かめてください`,
                  variant: 'warning' as const,
              },
          ]
        : []),
    ...result.recordedProducts.map((product) => ({
        message: `「${product.productName}」は、チラシの表記がパッケージでの確認と食い違っていたため、チラシの表記を記録しました（${product.allergens.map((a) => a.name).join('・')}）。届いたらパッケージで確かめてください`,
        variant: 'info' as const,
    })),
];
