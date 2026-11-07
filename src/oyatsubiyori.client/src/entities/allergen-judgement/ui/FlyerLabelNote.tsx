import Typography from '@mui/material/Typography';

/** 掲載品の写しで「表記なし」を表す値（サーバーと同じ文字列） */
const FLYER_LABEL_ABSENT = '表記なし';

interface FlyerLabelNoteProps {
    /** 今週のチラシのアレルゲン表記の原文（掲載品の写し。古い写し・写しにない商品は null） */
    labelText: string | null | undefined;
}

/**
 * 確認のチェック（献立の確定・確定済みの献立の確認し直し）に添える、今週のチラシの表記の原文（09 の 5.4。D-101）。
 * Claude が読んだ文なので、テキストとしてだけ出す。null（古いデータ）なら何も出さない。表示だけで、判定・確定の条件は変えない
 */
export const FlyerLabelNote = ({ labelText }: FlyerLabelNoteProps) => {
    if (labelText === null || labelText === undefined) {
        return null;
    }
    return (
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
            今週のチラシの表記：{labelText === FLYER_LABEL_ABSENT ? FLYER_LABEL_ABSENT : `「${labelText}」`}。チラシの表記と見比べて、パッケージが届いたら原材料を確かめてください
        </Typography>
    );
};
