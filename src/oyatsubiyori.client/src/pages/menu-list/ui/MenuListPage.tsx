import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import { useSnackbar } from 'notistack';
import { CATEGORY_EMOJI, CATEGORY_LABEL, judgementText } from '../../../entities/allergen-judgement';
import { useCreateMenu, useDeleteMenu, useMenuList } from '../../../entities/menu';
import type { MenuListRow } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatWeekLabel, formatYen } from '../../../shared/lib';
import { MENU_STATUS, menuPath, ROUTE } from '../../../shared/type';
import { ConfirmDialog, PageLayout } from '../../../shared/ui';

/**
 * SC-10 献立一覧（ログイン直後の画面。T3-01・T4-12）。
 * 上部に対応が必要なこと（使えなくなった商品を使う献立・パッケージで確認する商品・未確定の献立）、その下に週ごとの行。
 */
export const MenuListPage = () => {
    const { data: list } = useMenuList();
    const createMenu = useCreateMenu();
    const deleteMenu = useDeleteMenu();
    const navigate = useNavigate();
    const { enqueueSnackbar } = useSnackbar();
    const [pickedDate, setPickedDate] = useState('');
    const [deleting, setDeleting] = useState<MenuListRow | null>(null);

    const openOrCreate = (weekStart: string) => {
        createMenu.mutate(weekStart, {
            onSuccess: (menuId) => navigate(menuPath(menuId)),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    return (
        <PageLayout
            title="献立一覧"
            header={
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
                    <TextField
                        type="date"
                        label="週を選んで献立を作る"
                        value={pickedDate}
                        onChange={(event) => setPickedDate(event.target.value)}
                        slotProps={{ inputLabel: { shrink: true } }}
                    />
                    <Button variant="outlined" disabled={!pickedDate || createMenu.isPending} onClick={() => openOrCreate(pickedDate)}>
                        この週を開く
                    </Button>
                </Stack>
            }
        >
            <Stack spacing={2}>
                {list.blockedUsages.length > 0 && (
                    <Alert severity="error" variant="filled">
                        <Typography sx={{ fontWeight: 700, mb: 0.5 }}>{CATEGORY_EMOJI.Blocked} 使えない商品が献立に入っています。別の商品に替えてください</Typography>
                        {list.blockedUsages.map((usage) => (
                            <Box key={`${usage.menuId}-${usage.date}-${usage.productName}`}>
                                <Link component={RouterLink} to={menuPath(usage.menuId, 4)} color="inherit" sx={{ fontWeight: 700 }}>
                                    {formatDateLabel(usage.date)}の献立
                                </Link>
                                ：「{usage.productName}」{usage.judgement ? judgementText(usage.judgement, true).reasons.join('／') : 'アレルゲンの判定ができませんでした'}
                            </Box>
                        ))}
                    </Alert>
                )}
                {!list.isReadyForMenu && (
                    <Alert severity="warning">
                        献立を作る前に、
                        <Link component={RouterLink} to={ROUTE.SETTINGS}>
                            設定の「条件設定」
                        </Link>
                        で通常の人数と既定の週の予算を入れてください。
                    </Alert>
                )}
                <Card>
                    <CardContent>
                        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                            対応が必要なこと
                        </Typography>
                        <Stack spacing={0.5}>
                            <Typography>
                                今日以降の献立で使う、パッケージで確認する商品：
                                <Link component={RouterLink} to={`${ROUTE.PRODUCTS}?filter=FuturePackage`} sx={{ fontWeight: 700 }}>
                                    {list.futurePackageCheckCount}件
                                </Link>
                            </Typography>
                            <Typography>
                                そのほかの、パッケージで確認する商品：
                                <Link component={RouterLink} to={`${ROUTE.PRODUCTS}?filter=OtherPackage`}>
                                    {list.otherPackageCheckCount}件
                                </Link>
                            </Typography>
                            <Typography>未確定の献立：{list.draftCount}件</Typography>
                        </Stack>
                    </CardContent>
                </Card>
                {list.rows.map((row) => (
                    <WeekRow
                        key={row.weekStart}
                        row={row}
                        isPending={createMenu.isPending}
                        onOpen={() => (row.menuId ? navigate(menuPath(row.menuId)) : openOrCreate(row.weekStart))}
                        onDelete={() => setDeleting(row)}
                    />
                ))}
            </Stack>
            <ConfirmDialog
                isOpen={deleting !== null}
                title={`${deleting ? formatWeekLabel(deleting.weekStart) : ''}の下書きを削除しますか？`}
                confirmLabel="削除する"
                isDanger
                isPending={deleteMenu.isPending}
                onClose={() => setDeleting(null)}
                onConfirm={() =>
                    deleting?.menuId &&
                    deleteMenu.mutate(deleting.menuId, {
                        onSuccess: () => {
                            enqueueSnackbar('下書きを削除しました', { variant: 'success' });
                            setDeleting(null);
                        },
                        onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
                    })
                }
            >
                <Typography>取り込んだ今週の商品・要望・献立案も消えます。商品の情報は残ります。</Typography>
            </ConfirmDialog>
        </PageLayout>
    );
};

interface WeekRowProps {
    row: MenuListRow;
    isPending: boolean;
    onOpen: () => void;
    onDelete: () => void;
}

const WeekRow = ({ row, isPending, onOpen, onDelete }: WeekRowProps) => {
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);
    const statusLabel = row.status === MENU_STATUS.CONFIRMED ? '確定' : row.status === MENU_STATUS.DRAFT ? '下書き' : '未作成';
    return (
        <Card component="section" aria-label={(row.relativeLabel ? row.relativeLabel + ' ' : '') + formatWeekLabel(row.weekStart)}>
            <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, gap: 1.5, alignItems: { sm: 'center' }, '&:last-child': { pb: 2 } }}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                        <Typography variant="h3" component="h2">
                            {formatWeekLabel(row.weekStart)}
                        </Typography>
                        {row.relativeLabel && <Chip size="small" label={row.relativeLabel} />}
                        <Chip size="small" label={statusLabel} color={row.status === MENU_STATUS.CONFIRMED ? 'success' : row.status === MENU_STATUS.DRAFT ? 'primary' : 'default'} variant="outlined" />
                    </Stack>
                    {row.menuId && (
                        <Stack direction="row" spacing={1} sx={{ mt: 1, alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                            <Typography variant="body2" sx={{ color: row.isOverBudget ? 'error.main' : 'text.secondary' }}>
                                週の実績 {formatYen(row.total ?? 0)} / 週の予算 {formatYen(row.budget ?? 0)}
                            </Typography>
                            {row.blockedCount > 0 && <Chip size="small" color="error" label={`${CATEGORY_EMOJI.Blocked} ${CATEGORY_LABEL.Blocked} ${row.blockedCount}件`} />}
                            {row.warningCount > 0 && <Chip size="small" color="warning" label={`${CATEGORY_EMOJI.Warning} ${CATEGORY_LABEL.Warning} ${row.warningCount}件`} />}
                            {row.recheckCount > 0 && <Chip size="small" color="warning" variant="outlined" label={`確認し直し ${row.recheckCount}件`} />}
                        </Stack>
                    )}
                </Box>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <Button variant={row.menuId ? 'outlined' : 'contained'} onClick={onOpen} disabled={isPending}>
                        {row.menuId ? '開く' : '献立を作る'}
                    </Button>
                    {row.status === MENU_STATUS.DRAFT && (
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
                    )}
                </Stack>
            </CardContent>
        </Card>
    );
};
