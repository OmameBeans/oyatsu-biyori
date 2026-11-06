import { useRef, useState } from 'react';
import type { DragEvent } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import SwapHorizIcon from '@mui/icons-material/SwapHoriz';
import TuneIcon from '@mui/icons-material/Tune';
import { useSnackbar } from 'notistack';
import { AllergenCategoryMark, AllergenJudgementLine, acknowledgementSentence } from '../../../entities/allergen-judgement';
import {
    dropTargetSx,
    endMenuDrag,
    startMenuDrag,
    useAcknowledge,
    useAddItem,
    useIsMenuEditing,
    useMenuDropTarget,
    useMoveItem,
    useRemoveItem,
    useReplaceItem,
} from '../../../entities/menu';
import type { ListedProduct, MenuDay, MenuDetail, MenuIssue, MenuItem as MenuItemData } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatPerChild, formatYen } from '../../../shared/lib';
import { allergenCheckPath, DAY_TYPE_LABEL, MENU_STATUS, SNACK_TYPE_LABEL } from '../../../shared/type';
import { ClaudeText } from '../../../shared/ui';
import { DaySettingsDialog } from './DaySettingsDialog';
import { ItemCountDialog } from './ItemCountDialog';

interface DayCardProps {
    menu: MenuDetail;
    day: MenuDay;
    issues: MenuIssue[];
    candidates: ListedProduct[];
    isBusy: boolean;
}

/**
 * 日ごとのカード（T4-06・T4-07・T4-13）。上から、見出しの部分（日付・この日の金額・種別・人数・見出し）、
 * 商品（1つずつ枠に入れる。1人あたりの個数・パック数・金額）・理由・警告、「商品を足す」。3つの部分は罫線で分ける。
 * 金額・判定・警告はサーバーが計算したものを出す。「メーカーへの確認が必要」はどの画面にも出さない（D-93）。
 * カードには、使える商品の一覧から商品をドラッグして足せる。ほかの日のカードの商品をドラッグすると、この日へ動かす（FR-MENU-24）。
 */
export const DayCard = ({ menu, day, issues, candidates, isBusy }: DayCardProps) => {
    const addItem = useAddItem(menu.id);
    const moveItem = useMoveItem(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [adding, setAdding] = useState('');
    const [isSettingsOpen, setIsSettingsOpen] = useState(false);
    const notify = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });
    const dayIssues = issues.filter((issue) => issue.menuItemId === null);
    const isPending = isBusy || addItem.isPending || moveItem.isPending;
    // ほかのカード・一覧での保存の途中は、ドラッグで落とせなくする（古い row_version で送らないため。入力欄は止めない）
    const isEditing = useIsMenuEditing(menu.id);

    // 同じ日の商品と、この日にもう入っている商品は受け付けない（光らせない）
    const { isOver, handlers } = useMenuDropTarget(
        (payload) => (payload.kind === 'item' ? payload.fromDate !== day.date : true) && !day.items.some((item) => item.productId === payload.productId),
        (payload) => {
            const label = formatDateLabel(day.date);
            if (payload.kind === 'product') {
                addItem.mutate(
                    { update: { date: day.date, productId: payload.productId }, rowVersion: menu.rowVersion },
                    { onSuccess: () => enqueueSnackbar(`${label}に足しました`, { variant: 'success' }), onError: notify },
                );
            } else {
                moveItem.mutate(
                    { update: { date: day.date, itemId: payload.itemId }, rowVersion: menu.rowVersion },
                    { onSuccess: () => enqueueSnackbar(`${label}へ動かしました`, { variant: 'success' }), onError: notify },
                );
            }
        },
        isBusy || isEditing,
    );

    return (
        <Card
            component="section"
            aria-label={`${formatDateLabel(day.date)}の献立`}
            {...handlers}
            sx={{ display: 'flex', flexDirection: 'column', overflow: 'hidden', ...dropTargetSx(isOver) }}
        >
            {/* 見出しの部分（日付・この日の金額・種別・人数・見出し）。商品の部分とは背景（主色の薄い色）と罫線で分け、PC では動かさない */}
            <Box
                sx={{
                    px: 2,
                    py: 1.5,
                    flexShrink: 0,
                    bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.1),
                    borderBottom: 1,
                    borderColor: 'divider',
                }}
            >
                <Box sx={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 1 }}>
                    <Typography variant="h3" component="h3">
                        {formatDateLabel(day.date)}
                    </Typography>
                    <Typography sx={{ fontWeight: 700, fontSize: '1.125rem' }}>{formatYen(day.amount)}</Typography>
                </Box>
                <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 1 }}>
                    <Typography variant="caption" color="text.secondary">
                        {day.items.length}品
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                        1人あたり {formatPerChild(day.perChildAmount)}
                    </Typography>
                </Box>
                {/* 種別・人数・見出しは今の値だけを出し、「詳細設定」のダイアログで直す */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1 }}>
                    <Box sx={{ flex: 1, minWidth: 0 }}>
                        <Typography variant="body2">
                            {DAY_TYPE_LABEL[day.dayType]}・{day.childrenCount}人
                        </Typography>
                        {day.label && (
                            <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
                                {day.label}
                            </Typography>
                        )}
                    </Box>
                    <Button
                        size="small"
                        variant="outlined"
                        startIcon={<TuneIcon />}
                        disabled={isPending}
                        onClick={() => setIsSettingsOpen(true)}
                        aria-label={`${formatDateLabel(day.date)}の詳細設定`}
                        sx={{ flexShrink: 0, bgcolor: 'background.paper' }}
                    >
                        詳細設定
                    </Button>
                </Box>
            </Box>

            {/* 商品（1つずつ罫線の枠に入れる）・理由・この日の警告。PC ではここだけをスクロールする */}
            <Stack spacing={1} sx={{ p: 1.5, flex: 1, minHeight: 0, overflowY: { md: 'auto' }, '& > *': { flexShrink: 0 } }}>
                {day.items.map((item) => (
                    <ItemRow key={`${item.id}-${item.perChildCount}-${item.packCount}-${item.productId}`} menu={menu} day={day} item={item} issues={issues.filter((issue) => issue.menuItemId === item.id)} candidates={candidates} isBusy={isPending} />
                ))}
                {day.items.length === 0 && (
                    <Typography variant="body2" color="text.secondary">
                        商品はまだありません（使える商品からドラッグして足せます）
                    </Typography>
                )}
                {day.reason && (
                    <Box sx={{ p: 1, bgcolor: 'action.hover', borderRadius: 2 }}>
                        <Typography variant="caption" color="text.secondary">
                            理由
                        </Typography>
                        <ClaudeText text={day.reason} />
                    </Box>
                )}
                {dayIssues.map((issue) => (
                    <Typography key={issue.message} variant="body2" sx={{ color: 'warning.dark' }}>
                        ⚠ {issue.message}
                    </Typography>
                ))}
            </Stack>

            {/* 商品を足す（いちばん下に罫線で分けて置き、PC では動かさない） */}
            <Stack direction="row" spacing={1} sx={{ p: 1.5, flexShrink: 0, borderTop: 1, borderColor: 'divider' }}>
                <TextField select size="small" label="商品を足す" value={adding} onChange={(event) => setAdding(event.target.value)} sx={{ flex: 1, minWidth: 0 }} disabled={isPending}>
                    {candidates.map((candidate) => (
                        <MenuItem key={candidate.productId} value={candidate.productId}>
                            {candidate.name}（{candidate.price !== null ? formatYen(candidate.price) : '価格なし'}）{candidate.judgement.category === 'Warning' && (
                                <>
                                    {' '}
                                    <AllergenCategoryMark category="Warning" />
                                </>
                            )}
                        </MenuItem>
                    ))}
                </TextField>
                <Button
                    variant="outlined"
                    disabled={!adding || isPending}
                    onClick={() =>
                        addItem.mutate(
                            { update: { date: day.date, productId: adding }, rowVersion: menu.rowVersion },
                            { onSuccess: () => setAdding(''), onError: notify },
                        )
                    }
                >
                    足す
                </Button>
            </Stack>
            <DaySettingsDialog
                key={isSettingsOpen ? 'settings-open' : 'settings-closed'}
                menu={menu}
                day={day}
                isOpen={isSettingsOpen}
                onClose={() => setIsSettingsOpen(false)}
                onSuccess={() => {
                    setIsSettingsOpen(false);
                    enqueueSnackbar(`${formatDateLabel(day.date)}の詳細設定を保存しました`, { variant: 'success' });
                }}
            />
        </Card>
    );
};

interface ItemRowProps {
    menu: MenuDetail;
    day: MenuDay;
    item: MenuItemData;
    issues: MenuIssue[];
    candidates: ListedProduct[];
    isBusy: boolean;
}

const ItemRow = ({ menu, day, item, issues, candidates, isBusy }: ItemRowProps) => {
    const removeItem = useRemoveItem(menu.id);
    const replaceItem = useReplaceItem(menu.id);
    const acknowledge = useAcknowledge(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [isReplacing, setIsReplacing] = useState(false);
    const [isCountOpen, setIsCountOpen] = useState(false);
    const rowRef = useRef<HTMLDivElement>(null);
    const notify = (error: unknown) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' });
    const isPending = isBusy || removeItem.isPending || replaceItem.isPending;
    const isEditing = useIsMenuEditing(menu.id);
    // アレルゲンの警告は共通部品で出すので、ここでは重複・パック数などのほかの警告だけを出す
    const otherIssues = issues.filter((issue) => issue.kind !== 'AllergenWarning' && issue.kind !== 'AllergenBlocked');

    // 入力欄の文字を選べるように、行全体ではなく持ち手だけをつかめるようにする（ドラッグ中の絵は行全体）
    const handleDragStart = (event: DragEvent) => {
        startMenuDrag(event.dataTransfer, { kind: 'item', itemId: item.id, productId: item.productId, fromDate: day.date });
        if (rowRef.current) {
            event.dataTransfer.setDragImage(rowRef.current, 16, 16);
        }
    };

    return (
        <Paper ref={rowRef} variant="outlined" sx={{ p: 1.5 }}>
            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'flex-start' }}>
                <Box
                    draggable={!isPending && !isEditing}
                    onDragStart={handleDragStart}
                    onDragEnd={endMenuDrag}
                    aria-label={`${item.productName}をつかんで、ほかの日か使える商品へ動かす`}
                    title="ドラッグして、ほかの日へ動かす・使える商品へ戻して外す"
                    sx={{ display: { xs: 'none', lg: 'flex' }, alignItems: 'center', cursor: isPending || isEditing ? 'default' : 'grab', color: 'text.disabled', ml: -0.5, mt: 0.25 }}
                >
                    <DragIndicatorIcon fontSize="small" />
                </Box>
                <Typography sx={{ fontWeight: 600, flex: 1, minWidth: 0 }}>{item.productName}</Typography>
            </Stack>
            {(item.snackType || item.tags.length > 0) && (
                <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', flexWrap: 'wrap', mt: 0.5 }} useFlexGap>
                    {item.snackType && <Chip size="small" label={SNACK_TYPE_LABEL[item.snackType]} />}
                    {item.tags.map((tag) => (
                        <Chip key={`${tag.kind}-${tag.value}`} size="small" variant="outlined" label={tag.value} sx={{ height: 20, fontSize: 11 }} />
                    ))}
                </Stack>
            )}
            {item.judgement && item.judgement.category !== 'Safe' && (
                <Box sx={{ mt: 0.5 }}>
                    <AllergenJudgementLine judgement={item.judgement} isInMenu />
                    <Button component={RouterLink} to={allergenCheckPath(item.productId)} size="small">
                        パッケージで確認する
                    </Button>
                </Box>
            )}
            {item.needsRecheck && menu.status === MENU_STATUS.CONFIRMED && item.judgement && (
                <Stack direction="row" spacing={1} sx={{ mt: 0.5, alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                    <Chip size="small" color="warning" variant="outlined" label="確認し直し" />
                    <Button
                        size="small"
                        disabled={acknowledge.isPending || isBusy}
                        onClick={() =>
                            item.judgementFingerprint &&
                            acknowledge.mutate({ menuItemId: item.id, kind: item.judgement!.kind, fingerprint: item.judgementFingerprint }, { onError: notify })
                        }
                    >
                        ☐ {acknowledgementSentence(item.judgement)}
                    </Button>
                </Stack>
            )}
            {otherIssues.map((issue) => (
                <Typography key={issue.message} variant="body2" sx={{ mt: 0.5, color: issue.severity === 'Error' ? 'error.main' : 'warning.dark' }}>
                    ⚠ {issue.message}
                </Typography>
            ))}
            {/* 個数は今の値だけを出し、「個数設定」のダイアログで直す */}
            <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 1, mt: 1.5 }}>
                <Box sx={{ minWidth: 0 }}>
                    <Typography variant="body2">
                        1人あたり {item.perChildCount}個・{item.packCount}パック
                    </Typography>
                    {/* パック数の根拠（人数 × 1人あたり ÷ 入数を切り上げ。計算はサーバー） */}
                    {item.piecesPerPack !== null && (
                        <Typography variant="caption" color="text.secondary" component="p">
                            1パック {item.piecesPerPack}個入り
                        </Typography>
                    )}
                </Box>
                <Box sx={{ ml: 'auto', textAlign: 'right', flexShrink: 0 }}>
                    <Typography variant="caption" color="text.secondary" component="p">
                        {formatYen(item.unitPrice)} × {item.packCount}パック
                    </Typography>
                    <Typography sx={{ fontWeight: 700 }}>{formatYen(item.amount)}</Typography>
                </Box>
            </Box>
            {day.excludedFromAmount && (
                <Typography variant="caption" color="text.secondary" component="p">
                    金額に含まれていません
                </Typography>
            )}
            <Stack direction="row" sx={{ alignItems: 'center', mt: 0.5, mr: -1, mb: -1 }}>
                <Button
                    size="small"
                    startIcon={<EditOutlinedIcon />}
                    disabled={isPending}
                    onClick={() => setIsCountOpen(true)}
                    aria-label={`${item.productName}の個数設定`}
                    sx={{ ml: -0.5, mr: 'auto' }}
                >
                    個数設定
                </Button>
                <IconButton aria-label={`${item.productName}を入れ替える`} onClick={() => setIsReplacing(!isReplacing)} disabled={isPending}>
                    <SwapHorizIcon />
                </IconButton>
                <IconButton
                    aria-label={`${item.productName}を外す`}
                    disabled={isPending}
                    onClick={() => removeItem.mutate({ update: { itemId: item.id }, rowVersion: menu.rowVersion }, { onError: notify })}
                >
                    <DeleteOutlinedIcon />
                </IconButton>
            </Stack>
            {isReplacing && (
                <TextField
                    select
                    label="入れ替える商品"
                    value=""
                    fullWidth
                    sx={{ mt: 1 }}
                    onChange={(event) =>
                        replaceItem.mutate(
                            { update: { itemId: item.id, productId: event.target.value }, rowVersion: menu.rowVersion },
                            { onSuccess: () => setIsReplacing(false), onError: notify },
                        )
                    }
                >
                    {candidates
                        .filter((candidate) => candidate.productId !== item.productId)
                        .map((candidate) => (
                            <MenuItem key={candidate.productId} value={candidate.productId}>
                                {candidate.name}（{candidate.price !== null ? formatYen(candidate.price) : '価格なし'}）
                            </MenuItem>
                        ))}
                </TextField>
            )}
            <ItemCountDialog
                key={isCountOpen ? 'count-open' : 'count-closed'}
                menu={menu}
                day={day}
                item={item}
                isOpen={isCountOpen}
                onClose={() => setIsCountOpen(false)}
                onSuccess={() => {
                    setIsCountOpen(false);
                    enqueueSnackbar(`${item.productName}の個数を保存しました`, { variant: 'success' });
                }}
            />
        </Paper>
    );
};
