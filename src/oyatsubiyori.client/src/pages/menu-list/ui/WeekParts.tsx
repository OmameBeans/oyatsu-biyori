import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import { CATEGORY_ICON, CATEGORY_LABEL } from '../../../entities/allergen-judgement';
import type { MenuListRow } from '../../../entities/menu';
import { formatWeekLabel, formatYen } from '../../../shared/lib';
import { MENU_STATUS } from '../../../shared/type';
import { hasIssues, TAG_SX } from './weekDisplay';

/**
 * 献立一覧（SC-10）と過去の献立（SC-14）で使う、1週の表示の部品。
 */

/** 予算を超えた額（サーバーが計算した「残り」をそのまま使う） */
const overBudgetText = (row: MenuListRow) => `${formatYen(-(row.remaining ?? 0))}オーバー`;

interface WeekStatusProps {
    row: MenuListRow;
}

/** 状態（色の付いた丸と文言。色だけに頼らない） */
export const WeekStatus = ({ row }: WeekStatusProps) => {
    const { label, color } =
        row.status === MENU_STATUS.CONFIRMED
            ? { label: '確定', color: 'success.main' }
            : row.status === MENU_STATUS.DRAFT
              ? { label: '下書き', color: 'primary.main' }
              : { label: 'まだ作成していません', color: 'text.disabled' };
    return (
        <Stack direction="row" spacing={0.75} component="span" sx={{ alignItems: 'center', whiteSpace: 'nowrap' }}>
            <Box
                component="span"
                aria-hidden="true"
                sx={{ width: 10, height: 10, borderRadius: '50%', flexShrink: 0, ...(row.status ? { bgcolor: color } : { border: 2, borderColor: color }) }}
            />
            <Typography component="span" variant="body2" sx={{ fontWeight: row.status ? 700 : 400, color: row.status ? 'text.primary' : 'text.secondary' }}>
                {label}
            </Typography>
        </Stack>
    );
};

interface IssueChipsProps {
    row: MenuListRow;
}

/** 商品の問題のバッジ（🔴 使えません・🟡 要確認・確認し直し。FR-MENU-34a） */
export const IssueChips = ({ row }: IssueChipsProps) => {
    const { Blocked: BlockedIcon, Warning: WarningIcon } = CATEGORY_ICON;
    return (
        <>
            {row.blockedCount > 0 && <Chip size="small" color="error" icon={<BlockedIcon />} label={`${CATEGORY_LABEL.Blocked} ${row.blockedCount}件`} sx={TAG_SX} />}
            {row.warningCount > 0 && <Chip size="small" color="warning" icon={<WarningIcon />} label={`${CATEGORY_LABEL.Warning} ${row.warningCount}件`} sx={TAG_SX} />}
            {row.recheckCount > 0 && (
                // warning.main の文字は白の上で 4.5:1 に届かないので、文字と枠は warning.dark にする（theme.ts）
                <Chip
                    size="small"
                    variant="outlined"
                    label={`確認し直し ${row.recheckCount}件`}
                    sx={{ ...TAG_SX, color: 'warning.dark', borderColor: 'warning.dark' }}
                />
            )}
        </>
    );
};

interface DraftMenuProps {
    row: MenuListRow;
    onDelete: () => void;
}

/** 下書きの行のメニュー（削除。FR-MENU-31a）。下書きでなければ何も出さない */
export const DraftMenu = ({ row, onDelete }: DraftMenuProps) => {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);
    if (row.status !== MENU_STATUS.DRAFT) return null;
    return (
        <>
            <IconButton aria-label={`${formatWeekLabel(row.weekStart)}のメニュー`} onClick={(event) => setAnchor(event.currentTarget)}>
                <MoreVertIcon />
            </IconButton>
            <Menu anchorEl={anchor} open={anchor !== null} onClose={() => setAnchor(null)}>
                <MenuItem
                    onClick={() => {
                        setAnchor(null);
                        onDelete();
                    }}
                    sx={{ color: 'error.main' }}
                >
                    下書きを削除
                </MenuItem>
            </Menu>
        </>
    );
};

interface WeekCardProps {
    row: MenuListRow;
    isThisWeek: boolean;
    isPending: boolean;
    onOpen: () => void;
    onDelete: () => void;
}

/** 1週のカード（今週とこれから。スマホでは過去の週も） */
export const WeekCard = ({ row, isThisWeek, isPending, onOpen, onDelete }: WeekCardProps) => (
    <Card
        component="section"
        variant="outlined"
        aria-label={(row.relativeLabel ? row.relativeLabel + ' ' : '') + formatWeekLabel(row.weekStart)}
        sx={isThisWeek ? { borderWidth: 2, borderColor: 'primary.main' } : undefined}
    >
        <CardContent
            sx={{
                display: 'flex',
                flexDirection: { xs: 'column', sm: 'row' },
                gap: { xs: 1.5, sm: 3 },
                alignItems: { sm: 'center' },
                px: { xs: 2, sm: 3 },
                py: isThisWeek ? 2.5 : 2,
                '&:last-child': { pb: isThisWeek ? 2.5 : 2 },
            }}
        >
            <Stack spacing={1.25} sx={{ flex: 1, minWidth: 0 }}>
                <Stack direction="row" spacing={1.5} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                    {row.relativeLabel && <Chip size="small" label={row.relativeLabel} color={isThisWeek ? 'primary' : 'default'} sx={TAG_SX} />}
                    <Typography variant="h3" component="h3" sx={isThisWeek ? { fontSize: '1.25rem' } : undefined}>
                        {formatWeekLabel(row.weekStart)}
                    </Typography>
                    <WeekStatus row={row} />
                </Stack>
                {row.menuId && (
                    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                        <Typography variant="body2">
                            週の実績{' '}
                            <Box component="strong" sx={{ fontSize: '1rem' }}>
                                {formatYen(row.total ?? 0)}
                            </Box>{' '}
                            / 週の予算 {formatYen(row.budget ?? 0)}
                        </Typography>
                        {row.isOverBudget && (
                            <Chip
                                size="small"
                                label={overBudgetText(row)}
                                sx={{ ...TAG_SX, color: 'error.main', bgcolor: (theme) => theme.alpha(theme.palette.error.main, 0.1) }}
                            />
                        )}
                    </Stack>
                )}
                {row.menuId && hasIssues(row) && (
                    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                        <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                            商品の確認：
                        </Typography>
                        <IssueChips row={row} />
                    </Stack>
                )}
            </Stack>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', alignSelf: { xs: 'flex-end', sm: 'center' } }}>
                <Button variant="outlined" onClick={onOpen} disabled={isPending} sx={{ minHeight: 44, px: 3.5, fontWeight: 700 }}>
                    {row.menuId ? '開く' : '献立を作る'}
                </Button>
                <DraftMenu row={row} onDelete={onDelete} />
            </Stack>
        </CardContent>
    </Card>
);
