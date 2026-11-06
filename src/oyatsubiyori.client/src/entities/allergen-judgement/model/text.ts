import { CHECK_SOURCE, JUDGEMENT_CATEGORY, JUDGEMENT_KIND } from '../../../shared/type';
import type { AllergenJudgement, JudgementCategory } from '../../../shared/type';

/**
 * アレルゲンの表示の文言（03 §6.1・6.2 の表を写したもの。T4-01）。
 * アレルゲンの文言は、この部品からだけ出す（各画面に書かない）。ステータスの名前（確認済み・参考）はここでは使わない。
 */

/** 3つの表示 */
export const CATEGORY_LABEL: Record<JudgementCategory, string> = {
    Blocked: '使えません',
    Warning: '要確認',
    Safe: '使えます',
};

/** MUI の色（区分で決める） */
export const CATEGORY_COLOR: Record<JudgementCategory, 'error' | 'warning' | 'success'> = {
    Blocked: 'error',
    Warning: 'warning',
    Safe: 'success',
};

/** 理由と次にすること */
export interface JudgementText {
    reasons: string[];
    nextSteps: string[];
}

const PACKAGE_CHECK = 'パッケージの原材料表示を撮って確認してください';

const referenceReason = (judgement: AllergenJudgement): string => {
    if (judgement.referenceAfterConfirmed) {
        return 'チラシの表記が、パッケージでの確認と食い違っています';
    }
    switch (judgement.referenceSource) {
        case CHECK_SOURCE.FLYER:
            return judgement.referenceHasNoLabel ? 'チラシにアレルゲンの表記がありません' : 'チラシの情報だけです';
        case CHECK_SOURCE.COOP_SITE:
            return 'コープのサイトの情報だけです';
        default:
            return 'パッケージ以外の情報だけです';
    }
};

/**
 * 理由と次にすることを作る。🔴 の理由は、含む品目と当たった言葉をすべて並べる（SR-10）。
 * @param isInMenu 献立に入っている商品か（入っていれば「別の商品に替えてください」）
 */
export const judgementText = (judgement: AllergenJudgement, isInMenu = false): JudgementText => {
    const reasons: string[] = [];
    const nextSteps: string[] = [];

    if (judgement.category === JUDGEMENT_CATEGORY.BLOCKED) {
        if (judgement.containedAllergens.length > 0) {
            reasons.push(`${judgement.containedAllergens.join('・')}を含みます`);
        }
        // 同じ品目・同じ場所で当たった言葉は1つにまとめ、いちばん長い言葉を見せる（改行・空白は詰める。表示だけで判定は変えない）
        const byPlace = new Map<string, { where: string; allergenName: string; text: string }>();
        judgement.keywordHits.forEach((hit) => {
            const where = hit.source === 'ProductName' ? '商品名' : '原材料';
            const text = hit.matchedText.replace(/\s+/g, '');
            const key = `${where}|${hit.allergenName}`;
            const current = byPlace.get(key);
            if (!current || text.length > current.text.length) {
                byPlace.set(key, { where, allergenName: hit.allergenName, text });
            }
        });
        byPlace.forEach(({ where, allergenName, text }) => reasons.push(`${where}に『${text}』があります（${allergenName}）`));
        if (judgement.kind === JUDGEMENT_KIND.UNKNOWN) {
            reasons.push('アレルゲンの情報がありません');
            nextSteps.push(PACKAGE_CHECK);
        } else {
            nextSteps.push(isInMenu ? '献立には入れられません。別の商品に替えてください' : '献立には入れられません');
            if (judgement.keywordHits.length > 0) {
                nextSteps.push('アレルゲン確認で『含まない』にしても、この表示は消えません');
            }
            if (judgement.containsFromReference && judgement.keywordHits.length === 0) {
                nextSteps.push('パッケージで確認し直せば、表示が変わることがあります');
            }
        }
        return { reasons, nextSteps };
    }

    switch (judgement.kind) {
        case JUDGEMENT_KIND.REFERENCE:
            reasons.push(referenceReason(judgement));
            nextSteps.push('届いたらパッケージの原材料表示を撮って確認してください');
            break;
        case JUDGEMENT_KIND.RECHECK_NEEDED:
            reasons.push(`前回の確認から${judgement.daysSinceConfirmed ?? '何'}日たちました`);
            nextSteps.push('パッケージの原材料表示を撮って、もう一度確認してください');
            break;
        case JUDGEMENT_KIND.UNCONFIRMED_ITEMS:
            reasons.push(`${judgement.unconfirmedAllergens.join('・')}をまだ確認していません`);
            nextSteps.push(`パッケージの原材料表示を撮って、${judgement.unconfirmedAllergens.join('・')}を確認してください`);
            break;
        case JUDGEMENT_KIND.UNKNOWN:
            reasons.push('アレルゲンの情報がありません');
            nextSteps.push(PACKAGE_CHECK);
            break;
        default:
            break;
    }
    return { reasons, nextSteps };
};

/** 確定のときのチェックの1文（「チラシの情報だけです。届いたらパッケージで確認します」。SR-04・03 §6.3） */
export const acknowledgementSentence = (judgement: AllergenJudgement): string => {
    const { reasons } = judgementText(judgement);
    const reason = reasons[0] ?? 'アレルゲンの確認が必要です';
    const promise =
        judgement.kind === JUDGEMENT_KIND.UNCONFIRMED_ITEMS
            ? `パッケージで${judgement.unconfirmedAllergens.join('・')}を確認します`
            : judgement.kind === JUDGEMENT_KIND.RECHECK_NEEDED
              ? 'パッケージでもう一度確認します'
              : '届いたらパッケージで確認します';
    return `${reason}。${promise}`;
};

/**
 * 献立の商品の確認の種類ごとの、何をすればよいかの説明（商品の確認のダイアログ。D-90）。
 * 商品ごとの理由と次にすることは judgementText で出すので、ここでは種類のまとまりの説明だけを書く（言い切らない）。
 */
export const ISSUE_GUIDE = {
    BLOCKED: '献立に入れられない商品です。商品ごとの理由と次にすることを確かめてください。確認しても使えないときは、献立を開いて別の商品に替えてください。',
    RECHECK: '献立を確定したときの確認から、判定が変わった商品です（確定のときは確認が要らなかった商品も含みます）。理由を読んで確かめたら、チェックを付けてください。献立は確定のままです。',
    WARNING_CONFIRMED: '献立を確定したときに確認が済んだ商品です。商品が届いたら、パッケージの原材料表示を撮って確認してください。',
    WARNING_DRAFT: '献立を確定するときに、商品ごとの確認が要ります。届いた商品があれば、パッケージの原材料表示を撮って確認してください。',
    MISSING_PRODUCT: '商品が見つかりません。献立を開いて、別の商品に替えてください',
} as const;

/** 「パッケージで確認する商品」か（🟡 と、情報がないため 🔴 の商品） */
export const needsPackageCheck = (judgement: AllergenJudgement): boolean =>
    judgement.category === JUDGEMENT_CATEGORY.WARNING || (judgement.category === JUDGEMENT_CATEGORY.BLOCKED && judgement.kind === JUDGEMENT_KIND.UNKNOWN);
