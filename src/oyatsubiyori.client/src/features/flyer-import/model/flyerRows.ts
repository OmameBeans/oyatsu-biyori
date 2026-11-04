import type { FlyerChoice, FlyerDiscrepancy, FlyerItem } from '../../../entities/flyer';
import { verdictLabel } from './labels';

/**
 * 読み取り結果の一覧の、行の状態（画面の並べ方と絞り込みのためだけに使う。確定できるかどうかはサーバーが決める）
 * - 警告あり：パッケージでの確認との食い違い・説明文の避ける品目の言葉・品目に当てはめられない語・読み取りの検証の指摘がある
 * - 要確認：同じ商品かどうかが決まっていない（似ている商品・AI の判定がある、判定ができなかった）か、アレルゲンの表記が「表記なし」と読まれた
 * - 未確定：それ以外で、まだ確定していない
 * - 確定済み：確定した（選び直せる。D-53）
 */
export const FLYER_ROW_STATUS = {
    WARNING: 'Warning',
    NEEDS_CHECK: 'NeedsCheck',
    UNCONFIRMED: 'Unconfirmed',
    CONFIRMED: 'Confirmed',
} as const;
export type FlyerRowStatus = (typeof FLYER_ROW_STATUS)[keyof typeof FLYER_ROW_STATUS];

/** 並べる順（小さいほど上。確定済みは必ず下） */
const STATUS_RANK: Record<FlyerRowStatus, number> = {
    Warning: 0,
    NeedsCheck: 1,
    Unconfirmed: 2,
    Confirmed: 3,
};

export const FLYER_ROW_STATUS_LABEL: Record<FlyerRowStatus, string> = {
    Warning: '警告あり',
    NeedsCheck: '要確認',
    Unconfirmed: '未確定',
    Confirmed: '確定済み',
};

/** 状態のバッジの色（テーマの palette から取る） */
export const FLYER_ROW_STATUS_COLOR: Record<FlyerRowStatus, 'warning' | 'info' | 'default' | 'success'> = {
    Warning: 'warning',
    NeedsCheck: 'info',
    Unconfirmed: 'default',
    Confirmed: 'success',
};

/** 表の上のタブ（「すべて」と各状態） */
export const FLYER_ROW_TAB = {
    ALL: 'All',
    ...FLYER_ROW_STATUS,
} as const;
export type FlyerRowTab = (typeof FLYER_ROW_TAB)[keyof typeof FLYER_ROW_TAB];

/**
 * チラシにアレルゲンの表記がない（「表記なし」と読まれた）。読み落としのこともあるので、チラシと見比べてもらう（FR-FLYER-04a）。
 * 「なし」「－」のように、文字はあっても品目が1つも出ない表記も、確認する側に倒して「表記なし」として示す（表示だけ。記録はサーバーが決める）
 */
export const isLabelAbsent = (item: FlyerItem) =>
    item.read.allergenLabelAbsent || !item.read.allergenLabelText?.trim() || (item.labelAllergens.length === 0 && item.unmappedTerms.length === 0);

/**
 * パッケージでの確認とチラシの表記の食い違い。勧める商品との食い違い（discrepancies）に加えて、
 * 「同じ商品」の候補ごとの食い違い（plansByProduct）も拾う（どの候補を選んでも見落とさないように。SR-20）
 */
export const discrepanciesOf = (item: FlyerItem): FlyerDiscrepancy[] => {
    const all = [...item.discrepancies, ...Object.values(item.plansByProduct).flatMap((plan) => plan.discrepancies)];
    return all.filter((d, index) => all.findIndex((x) => x.allergen.id === d.allergen.id) === index);
};

/** 表の「アレルゲン表記」の列に並べるチップ */
export interface AllergenChip {
    key: string;
    label: string;
    color: 'default' | 'warning' | 'error';
    variant: 'filled' | 'outlined';
    isDashed: boolean;
}

/**
 * 行を開かなくても見落とさないように、表記の品目に加えて、表記なし・品目に当てはめられない語・説明文の避ける品目の言葉・
 * パッケージでの確認との食い違い（避ける品目なら error）もチップにする
 */
export const allergenChipsOf = (item: FlyerItem): AllergenChip[] => [
    ...(isLabelAbsent(item) ? [{ key: 'absent', label: '表記なし', color: 'warning', variant: 'outlined', isDashed: true } as const] : []),
    ...item.labelAllergens.map((allergen) => ({ key: `label-${allergen.id}`, label: allergen.name, color: 'default', variant: 'filled', isDashed: false }) as const),
    ...item.unmappedTerms.map((term) => ({ key: `unmapped-${term}`, label: `「${term}」品目不明`, color: 'warning', variant: 'outlined', isDashed: false }) as const),
    ...item.descriptionHitWords.map((word) => ({ key: `hit-${word}`, label: `説明文：${word}`, color: 'warning', variant: 'outlined', isDashed: false }) as const),
    ...(discrepanciesOf(item).length > 0
        ? [
              {
                  key: 'discrepancy',
                  label: `パッケージでの確認と違う：${discrepanciesOf(item).map((d) => d.allergen.name).join('・')}`,
                  color: discrepanciesOf(item).some((d) => d.isAvoided) ? 'error' : 'warning',
                  variant: 'outlined',
                  isDashed: false,
              } as const,
          ]
        : []),
];

export const flyerRowStatusOf = (item: FlyerItem): FlyerRowStatus => {
    if (item.confirmation) {
        return FLYER_ROW_STATUS.CONFIRMED;
    }
    if (discrepanciesOf(item).length > 0 || item.descriptionHitWords.length > 0 || item.unmappedTerms.length > 0 || item.issues.length > 0) {
        return FLYER_ROW_STATUS.WARNING;
    }
    const isMatchUndecided =
        !item.canBulkConfirm && (item.judgement !== null || item.judgementSkipped || item.candidates.length > 0 || item.volumeVariants.length > 0);
    if (isMatchUndecided || isLabelAbsent(item)) {
        return FLYER_ROW_STATUS.NEEDS_CHECK;
    }
    return FLYER_ROW_STATUS.UNCONFIRMED;
};

export interface FlyerItemRow {
    id: string;
    item: FlyerItem;
    status: FlyerRowStatus;
}

/** 状態の順に並べる（同じ状態の中では読み取った順のまま） */
export const toSortedRows = (items: FlyerItem[]): FlyerItemRow[] =>
    items
        .map((item, index) => ({ row: { id: item.id, item, status: flyerRowStatusOf(item) }, index }))
        .sort((a, b) => STATUS_RANK[a.row.status] - STATUS_RANK[b.row.status] || a.index - b.index)
        .map(({ row }) => row);

export const filterRows = (rows: FlyerItemRow[], tab: FlyerRowTab): FlyerItemRow[] =>
    tab === FLYER_ROW_TAB.ALL ? rows : rows.filter((row) => row.status === tab);

/**
 * 確定したあとに開く行：今の行より下にある未確定の行。なければ上から探す。どれもなければ null（閉じる）
 * rows は確定する前の並び（確定した行は、このあと一番下へ移る）
 */
export const nextUnconfirmedRowId = (rows: FlyerItemRow[], currentId: string): string | null => {
    const index = rows.findIndex((row) => row.id === currentId);
    const isOpenable = (row: FlyerItemRow) => row.id !== currentId && row.status !== FLYER_ROW_STATUS.CONFIRMED;
    return rows.slice(index + 1).find(isOpenable)?.id ?? rows.find(isOpenable)?.id ?? null;
};

export const CHOICE_LABEL: Record<FlyerChoice, string> = {
    Same: '登録済みの商品と同じ',
    Variant: '味違い・内容量違いとして登録',
    New: '新しい商品として登録',
    Skip: '取り込まない',
};

/** 「同じ商品か」の列の2行（1行目：結果、2行目：相手の商品など） */
export const sameProductSummary = (item: FlyerItem): { title: string; detail: string } => {
    const confirmation = item.confirmation;
    if (confirmation) {
        return {
            title: CHOICE_LABEL[confirmation.choice],
            detail:
                confirmation.choice === 'Same'
                    ? (confirmation.productName ?? '')
                    : confirmation.choice === 'Skip'
                      ? 'おやつに使わない'
                      : '別の商品として登録',
        };
    }
    const same = item.sameProducts[0];
    if (same) {
        return { title: `登録済みの商品と同じ${same.makerCompared ? '' : '（メーカー未確認）'}`, detail: same.name };
    }
    const variant = item.volumeVariants[0];
    if (variant) {
        return { title: '内容量違い', detail: variant.name };
    }
    if (item.judgement) {
        return { title: `AI の判定：${verdictLabel(item.judgement.verdict)}`, detail: item.judgement.productName ?? '' };
    }
    if (item.candidates.length > 0) {
        return { title: '似ている商品があります', detail: item.candidates.map((c) => c.name).join('・') };
    }
    return { title: '新しい商品', detail: '似ている商品はありません' };
};

/** 読み取りの検証の指摘を出す先（入力欄）。どれにも当たらない指摘は other */
export type IssueTarget = 'name' | 'maker' | 'volume' | 'orderNo' | 'price' | 'priceExcluding' | 'pieces' | 'snackType' | 'allergens' | 'other';

/** 指摘の項目名（サーバーの field）に含まれる言葉から、入力欄を決める。「アレルゲン」をいちばん先に、「税抜価格」が「価格」より先に当たるように並べる */
const ISSUE_TARGET_WORDS: [string, IssueTarget][] = [
    ['アレルゲン', 'allergens'],
    ['入数', 'pieces'],
    ['内容量', 'volume'],
    ['税抜', 'priceExcluding'],
    ['価格', 'price'],
    ['商品名', 'name'],
    ['メーカー', 'maker'],
    ['注文番号', 'orderNo'],
    ['分類', 'snackType'],
];
export const issueTargetOf = (field: string): IssueTarget => ISSUE_TARGET_WORDS.find(([word]) => field.includes(word))?.[1] ?? 'other';

/** 確かめること（表の状態の列には short、詳細欄には guide を出す） */
export interface CheckPoint {
    key: string;
    /** 状態のチップの下に出す短い理由 */
    short: string;
    /** 詳細欄に出す、どこを見て何をすればよいか */
    guide: string;
}

/**
 * その商品で職員に確かめてほしいこと（状態が「警告あり」「要確認」になる理由と、することの案内）。
 * 並びは、アレルゲンに関わる理由を先にする（食い違い・説明文・品目の分からない語・表記の読み落とし・表記なし → 読み取りの指摘 → 同じ商品か）
 */
export const checkPointsOf = (item: FlyerItem): CheckPoint[] => {
    const points: CheckPoint[] = [];
    const discrepancies = discrepanciesOf(item);
    if (discrepancies.length > 0) {
        points.push({
            key: 'discrepancy',
            short: 'パッケージの確認と違う',
            guide: `パッケージで確認した内容と、チラシのアレルゲン表記が違います（${discrepancies.map((d) => d.allergen.name).join('・')}）。チラシの表記を見直してください。同じ商品なら、「登録済みの商品と同じ」を選んだときに出る説明に沿って扱いを選んでください。`,
        });
    }
    if (item.descriptionHitWords.length > 0) {
        points.push({
            key: 'hit-words',
            short: '説明文に避ける品目',
            guide: `商品名・説明文に、避ける品目の言葉（${item.descriptionHitWords.join('・')}）があります。含むとして記録します。チラシで、この商品の説明を確かめてください。`,
        });
    }
    if (item.unmappedTerms.length > 0) {
        points.push({
            key: 'unmapped',
            short: '品目の分からない語',
            guide: `アレルゲン表記の「${item.unmappedTerms.join('」「')}」が、どの品目か分かりませんでした。下の「品目に当てはめられなかった表記の語」で品目を選ぶか、「対応する品目なし」を選んでください（選ぶまで確定できません）。`,
        });
    }
    if (item.issues.some((issue) => issueTargetOf(issue.field) === 'allergens')) {
        points.push({
            key: 'allergen-issues',
            short: '表記の読み落とし',
            guide: 'アレルゲン表記を読み落としているかもしれません。下の「チラシのアレルゲン表記」の指摘を読み、チラシと見比べて、足りない品目があれば「品目を足す」で足してください。',
        });
    }
    if (isLabelAbsent(item)) {
        points.push({
            key: 'label-absent',
            short: '表記なし',
            guide: 'チラシにアレルゲンの表記がないと読みました。読み落としのことがあるので、チラシでこの商品のアレルゲン表記を探してください。表記があれば、下の「品目を足す」で足してください。なければ、そのままで構いません（献立に使う前に、パッケージでの確認が必要です）。',
        });
    }
    const fieldIssueCount = new Set(item.issues.map((issue) => issueTargetOf(issue.field)).filter((target) => target !== 'other' && target !== 'allergens')).size;
    if (fieldIssueCount > 0) {
        points.push({
            key: 'field-issues',
            short: '読み取りを確認',
            guide: `読み取りが合っているか確かめてほしい欄が ${fieldIssueCount}か所あります（黄色の枠の欄）。チラシと見比べて、違っていれば直してください。合っていれば、そのままで構いません。`,
        });
    }
    item.issues
        .filter((issue) => issueTargetOf(issue.field) === 'other')
        .forEach((issue, index) =>
            points.push({
                key: `other-${index}`,
                short: '読み取りを確認',
                guide: `${issue.field}：${issue.message}（チラシと見比べて、違っていれば下の欄で直してください）`,
            }),
        );
    if (!item.canBulkConfirm) {
        const similar = [...item.sameProducts, ...item.volumeVariants, ...item.candidates].map((p) => p.name);
        const similarText = similar.length > 0 ? `（${Array.from(new Set(similar)).join('・')}）` : '';
        if (item.judgementSkipped) {
            points.push({
                key: 'match',
                short: '同じ商品か選ぶ',
                guide: `似ている登録済みの商品${similarText}がありますが、同じ商品かどうかを自動で判定できませんでした。見比べて、同じなら「登録済みの商品と同じ」、違えば「新しい商品として登録」を選んでください。`,
            });
        } else if (item.judgement) {
            points.push({
                key: 'match',
                short: '同じ商品か選ぶ',
                guide: `AI は「${verdictLabel(item.judgement.verdict)}」と判定しました（理由は「同じ商品かどうか」の下）。合っているか確かめて、同じ商品かどうかを選んでください。`,
            });
        } else if (item.volumeVariants.length > 0) {
            points.push({
                key: 'match',
                short: '同じ商品か選ぶ',
                guide: `内容量が違う登録済みの商品${similarText}があります。別の商品として登録するので、「味違い・内容量違いとして登録」を選んでください（アレルゲンの確認は引き継ぎません）。`,
            });
        } else if (item.candidates.length > 0) {
            points.push({
                key: 'match',
                short: '同じ商品か選ぶ',
                guide: `似ている登録済みの商品${similarText}があります。同じ商品かどうかを選んでください。`,
            });
        }
    }
    return points;
};
