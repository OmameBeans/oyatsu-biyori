import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { ROUTE } from '../../../shared/type';
import { CATEGORY_COLOR, CATEGORY_EMOJI, CATEGORY_LABEL, judgementText, manufacturerCheckText } from '../model/text';
import type { AllergenJudgement } from '../../../shared/type';

interface AllergenBadgeProps {
    judgement: AllergenJudgement;
    size?: 'small' | 'medium';
}

/** 3つの表示のバッジ（色・絵文字・文字の3つで示す。D-83） */
export const AllergenBadge = ({ judgement, size = 'small' }: AllergenBadgeProps) => (
    <Chip
        size={size}
        color={CATEGORY_COLOR[judgement.category]}
        variant={judgement.category === 'Safe' ? 'outlined' : 'filled'}
        label={`${CATEGORY_EMOJI[judgement.category]} ${CATEGORY_LABEL[judgement.category]}`}
        sx={{ fontWeight: 700 }}
    />
);

interface AllergenJudgementLineProps {
    judgement: AllergenJudgement;
    isInMenu?: boolean;
    showManufacturerCheck?: boolean;
    showNextSteps?: boolean;
}

/**
 * アレルゲンの表示と理由・次にすること（T4-01）。文言はこの部品からだけ出す。
 * 「メーカーへの確認が必要」は、献立の日ごとのカードでは出さない（showManufacturerCheck を false にする。03 §6.3）。
 */
export const AllergenJudgementLine = ({ judgement, isInMenu = false, showManufacturerCheck = false, showNextSteps = true }: AllergenJudgementLineProps) => {
    const { reasons, nextSteps } = judgementText(judgement, isInMenu);
    const manufacturer = showManufacturerCheck ? manufacturerCheckText(judgement.manufacturerCheckAllergens) : null;
    return (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.25, minWidth: 0 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                <AllergenBadge judgement={judgement} />
                {reasons.length > 0 && (
                    <Typography variant="body2" component="span" sx={{ color: judgement.category === 'Blocked' ? 'error.main' : 'text.primary', fontWeight: 500 }}>
                        {reasons.join('／')}
                    </Typography>
                )}
            </Box>
            {showNextSteps && nextSteps.length > 0 && (
                <Typography variant="caption" color="text.secondary">
                    {nextSteps.join('。')}
                </Typography>
            )}
            {manufacturer && (
                <Typography variant="caption" color="text.secondary">
                    {manufacturer}
                </Typography>
            )}
        </Box>
    );
};

/** 「避ける品目が設定されていません」（03 §6.3。判定は変えない） */
export const NoAvoidedAllergenNotice = () => (
    <Alert severity="warning" sx={{ mb: 2 }}>
        避ける品目が設定されていません。
        <Link component={RouterLink} to={ROUTE.AVOIDED_TAB} sx={{ ml: 1 }}>
            設定で避ける品目を選ぶ
        </Link>
    </Alert>
);
