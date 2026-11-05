import { CATEGORY_LABEL } from '../model/text';
import { AllergenCategoryIcon, AllergenCategoryMark } from './AllergenCategoryMark';

/**
 * アイコンを含むアレルゲンの知らせ（T4-01）。文言はこの部品からだけ出す。
 */

interface KeywordHitNoticeProps {
    words: { keyword: string; allergenName: string }[];
    where: '原材料' | '読み取った全文' | '注意書き';
}

/** SC-08：全文で避ける品目の言葉が当たったときの知らせ（「含まない」にしても「使えません」は消えない。SR-14・03 §6.2） */
export const KeywordHitNotice = ({ words, where }: KeywordHitNoticeProps) => {
    if (words.length === 0) {
        return null;
    }
    const list = [...new Set(words.map((w) => `『${w.keyword}』（${w.allergenName}）`))].join('・');
    if (where === '注意書き') {
        return <>注意書きに、避ける品目の言葉があります：{list}。判断は職員が行ってください（判定は変わりません）</>;
    }
    const prefix = where === '原材料' ? '原材料' : 'AI が読み取った全文';
    return (
        <>
            {prefix}に、避ける品目の言葉があります：{list}。アレルゲン確認で『含まない』にしても、<AllergenCategoryMark category="Blocked" /> の表示は消えません
        </>
    );
};

/** 使えない商品が今日以降の献立に入っているときの知らせ（塗りの Alert の中に置くので、アイコンは文字の色に合わせる） */
export const BlockedInMenuNotice = () => (
    <>
        <AllergenCategoryIcon category="Blocked" isInherit /> 今日以降の献立に、使えない商品が入っています。別の商品に替えてください
    </>
);

/**
 * AI チャットの返答の下に出す注意（D-91・SR-18）。Claude の文章はアレルゲンの判定ではないため、判定は画面の表示とパッケージで確かめてもらう。
 * 打ち消しの文なので、アイコンは付けない。
 */
export const AiAnswerAllergenNotice = () =>
    `AI の返答はアレルゲンの判定ではありません。アレルゲンは、献立の「${CATEGORY_LABEL.Blocked}」「${CATEGORY_LABEL.Warning}」「${CATEGORY_LABEL.Safe}」の表示と、パッケージの原材料表示で確かめてください`;

/**
 * パッケージ以外の情報源を選んだときの知らせ（FR-PROD-06b。画面に「参考」とは出さない）。
 * 「使えます にはならない」と打ち消す文なので、アイコンは付けない（緑のチェックを見て安全と読み違えないように）。
 */
export const NonPackageSourceNotice = () =>
    `パッケージ以外の情報では「${CATEGORY_LABEL.Safe}」にはなりません（献立では「${CATEGORY_LABEL.Warning}」か「${CATEGORY_LABEL.Blocked}」になります）。「含む」の品目だけが記録されます`;
