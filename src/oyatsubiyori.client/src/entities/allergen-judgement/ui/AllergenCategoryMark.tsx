import Box from '@mui/material/Box';
import { CATEGORY_ICON, categoryIconTestId } from '../model/icon';
import { CATEGORY_COLOR, CATEGORY_LABEL } from '../model/text';
import type { JudgementCategory } from '../../../shared/type';

interface AllergenCategoryIconProps {
    category: JudgementCategory;
    /** 塗りの Alert の中など、まわりの文字の色に合わせるとき（区分の色のままだと背景に溶ける） */
    isInherit?: boolean;
}

/**
 * 3つの表示のアイコン（文字の大きさに合わせる）。文言と一緒に使う。
 * 要確認は warning.main だと背景との比が 3:1 に届かないので、要確認の注記の文字と同じ warning.dark にする（区分の色のまま）。
 */
export const AllergenCategoryIcon = ({ category, isInherit = false }: AllergenCategoryIconProps) => {
    const Icon = CATEGORY_ICON[category];
    const color = isInherit ? 'inherit' : category === 'Warning' ? 'warning.dark' : `${CATEGORY_COLOR[category]}.main`;
    return <Icon fontSize="inherit" data-testid={categoryIconTestId(category)} sx={{ color, verticalAlign: '-0.15em' }} />;
};

/** 文の中に置く、アイコンと表示名（「使えません」など） */
export const AllergenCategoryMark = ({ category, isInherit = false }: AllergenCategoryIconProps) => (
    <Box component="span" sx={{ whiteSpace: 'nowrap', '& > svg': { mr: '0.2em' } }}>
        <AllergenCategoryIcon category={category} isInherit={isInherit} />
        {CATEGORY_LABEL[category]}
    </Box>
);
