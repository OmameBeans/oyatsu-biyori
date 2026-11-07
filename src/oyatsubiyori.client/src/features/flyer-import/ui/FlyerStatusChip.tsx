import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import type { FlyerItem } from '../../../entities/flyer';
import { allergenChipsOf, FLYER_STATUS_COLOR, FLYER_STATUS_LABEL, shortReasonsOf } from '../model/flyerRows';

interface FlyerStatusChipProps {
    item: FlyerItem;
}

/**
 * 取り込みの分類のチップ（文字だけ。アレルゲンの3つの表示のアイコンは付けない。09 の 3.1）。
 * 読み上げでは「取り込みの分類：使える」と読む（アレルゲンの表示と取り違えないように）
 */
export const FlyerStatusChip = ({ item }: FlyerStatusChipProps) => (
    <Chip
        size="small"
        color={FLYER_STATUS_COLOR[item.status]}
        variant={item.status === 'Confirmed' ? 'outlined' : 'filled'}
        label={FLYER_STATUS_LABEL[item.status]}
        aria-label={`取り込みの分類：${FLYER_STATUS_LABEL[item.status]}`}
        sx={{ fontWeight: 700, flexShrink: 0 }}
    />
);

interface FlyerStatusCellProps {
    item: FlyerItem;
    /** 理由を1行に切り詰める（表の PC の列）。false なら折り返す */
    isSingleLine?: boolean;
}

/** 分類のチップと短い理由（アレルゲンに関わる理由が先。サーバーの並びのまま） */
export const FlyerStatusCell = ({ item, isSingleLine = true }: FlyerStatusCellProps) => {
    const reasons = shortReasonsOf(item);
    const fullText = item.reasons.map((reason) => reason.message).join('\n');
    return (
        <Stack sx={{ minWidth: 0, alignItems: 'flex-start' }}>
            <FlyerStatusChip item={item} />
            {reasons.length > 0 && (
                <Typography
                    variant="caption"
                    color="text.secondary"
                    component="div"
                    title={fullText}
                    sx={{
                        mt: 0.25,
                        maxWidth: '100%',
                        ...(isSingleLine ? { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' } : { overflowWrap: 'anywhere' }),
                    }}
                >
                    {reasons.join('・')}
                </Typography>
            )}
        </Stack>
    );
};

interface FlyerAllergenChipsProps {
    item: FlyerItem;
    /** 表記の原文を1行に切り詰める（表の PC の列） */
    isSingleLine?: boolean;
}

/** アレルゲン表記の原文と、補正した品目（麦→小麦）・表記なし・品目不明・説明文・食い違いのチップ */
export const FlyerAllergenChips = ({ item, isSingleLine = true }: FlyerAllergenChipsProps) => {
    const labelText = item.read.allergenLabelText?.trim();
    const chips = allergenChipsOf(item);
    return (
        <Box sx={{ minWidth: 0 }}>
            {/* 読み取った表記の原文（チラシと見比べられるように。FR-FLYER-04a） */}
            {labelText && (
                <Typography
                    variant="body2"
                    title={labelText}
                    sx={isSingleLine ? { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' } : { overflowWrap: 'anywhere' }}
                >
                    「{labelText}」
                </Typography>
            )}
            {chips.length > 0 && (
                <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', mt: labelText ? 0.5 : 0 }}>
                    {chips.map((chip) => (
                        // 「表記なし」は読み落としのこともある。点線の枠で、ほかの品目と見分ける
                        <Chip
                            key={chip.key}
                            size="small"
                            color={chip.color}
                            variant={chip.variant}
                            label={chip.label}
                            title={chip.label}
                            sx={{ maxWidth: '100%', ...(chip.isDashed ? { borderStyle: 'dashed' } : {}) }}
                        />
                    ))}
                </Stack>
            )}
        </Box>
    );
};
