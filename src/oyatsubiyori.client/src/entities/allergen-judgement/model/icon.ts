import BlockIcon from '@mui/icons-material/Block';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import WarningIcon from '@mui/icons-material/Warning';
import type { JudgementCategory } from '../../../shared/type';

/**
 * 3つの表示のアイコン（Material Icons。D-83）。色だけでなく形でも見分けられるように、区分ごとに形を変える。
 * 文言（CATEGORY_LABEL）と必ず一緒に使う。アイコンは読み上げない（MUI の SvgIcon は aria-hidden になる）。
 */
export const CATEGORY_ICON: Record<JudgementCategory, typeof BlockIcon> = {
    Blocked: BlockIcon,
    Warning: WarningIcon,
    Safe: CheckCircleIcon,
};

/** E2E でアイコンが出ていることを確かめるための印（MUI が付ける data-testid は本番のビルドでは付かないため） */
export const categoryIconTestId = (category: JudgementCategory): string => `allergen-icon-${category}`;
