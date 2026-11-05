import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import AddIcon from '@mui/icons-material/Add';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import { useSnackbar } from 'notistack';
import { AllergenCategoryIcon, judgementText } from '../../../entities/allergen-judgement';
import { useCreateMenu, useMenuList } from '../../../entities/menu';
import type { MenuListRow } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel } from '../../../shared/lib';
import { menuPath, ROUTE } from '../../../shared/type';
import { PageLayout } from '../../../shared/ui';
import { DeleteDraftDialog } from './DeleteDraftDialog';
import { WeekCard } from './WeekParts';

/**
 * SC-10 献立一覧（ログイン直後の画面。T3-01・T4-12）。
 * 上部に、必要なときだけ注意（使えなくなった商品を使う献立・条件設定がまだ・今日以降の献立で使う、パッケージで確認する商品）、
 * その下に「今週とこれから」（今週を目立たせ、古い順）。過去の週は「過去の献立」（SC-14）の画面で見る。
 * PC（md 以上）では、注意・見出し・「さらに先の週」・「過去の献立を見る」を固定し、週のカードの部分だけをスクロールする。
 */
export const MenuListPage = () => {
    const { data: list } = useMenuList();
    const createMenu = useCreateMenu();
    const navigate = useNavigate();
    const { enqueueSnackbar } = useSnackbar();
    const [deleting, setDeleting] = useState<MenuListRow | null>(null);

    const openOrCreate = (weekStart: string) => {
        createMenu.mutate(weekStart, {
            onSuccess: (menuId) => navigate(menuPath(menuId)),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    // サーバーは新しい週が上で返す。今週から先は古い順（今週が一番上）に並べ直す
    const upcomingRows = list.rows.filter((row) => row.weekStart >= list.thisWeekStart).sort((a, b) => a.weekStart.localeCompare(b.weekStart));
    const pastCount = list.rows.filter((row) => row.weekStart < list.thisWeekStart).length;
    const lastUpcoming = upcomingRows.at(-1)?.weekStart ?? list.thisWeekStart;
    const furtherWeekStart = dayjs(lastUpcoming).add(7, 'day').format('YYYY-MM-DD');
    const hasNotices = list.blockedUsages.length > 0 || !list.isReadyForMenu || list.futurePackageCheckCount > 0;

    return (
        <PageLayout title="献立一覧">
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3, height: { md: '100%' } }}>
                {hasNotices && (
                    <Stack spacing={2} sx={{ flexShrink: 0 }}>
                        {list.blockedUsages.length > 0 && (
                            <Alert severity="error" variant="filled">
                                <Typography sx={{ fontWeight: 700, mb: 0.5 }}><AllergenCategoryIcon category="Blocked" isInherit /> 使えない商品が献立に入っています。別の商品に替えてください</Typography>
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
                        {list.futurePackageCheckCount > 0 && (
                            // 確定のときに「届いたら確認する」とした商品を、届いたあとに見落とさないため（SC-10）。0件なら出さない
                            <Alert
                                severity="warning"
                                action={
                                    <Button component={RouterLink} to={`${ROUTE.PRODUCTS}?filter=FuturePackage`} color="inherit" sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>
                                        商品を確認する
                                    </Button>
                                }
                            >
                                今日以降の献立で使う、パッケージで確認する商品が
                                <Box component="strong" sx={{ mx: 0.5 }}>
                                    {list.futurePackageCheckCount}件
                                </Box>
                                あります。届いたら、パッケージの原材料表示を撮って確認してください。
                            </Alert>
                        )}
                    </Stack>
                )}

                <Box
                    component="section"
                    aria-labelledby="menu-list-upcoming"
                    sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, flex: { md: 1 }, minHeight: { md: 280 } }}
                >
                    <Typography id="menu-list-upcoming" variant="h2">
                        今週とこれから
                    </Typography>
                    {/* PC では週のカードの部分だけをスクロールする。カードは縮めない（縮むと中身が切れる）。注意が多いときは、上の section の最低の高さで潰れないようにする */}
                    <Stack spacing={1.5} sx={{ minHeight: { md: 0 }, overflowY: { md: 'auto' }, '& > *': { flexShrink: 0 } }}>
                        {upcomingRows.map((row) => (
                            <WeekCard
                                key={row.weekStart}
                                row={row}
                                isThisWeek={row.weekStart === list.thisWeekStart}
                                isPending={createMenu.isPending}
                                onOpen={() => (row.menuId ? navigate(menuPath(row.menuId)) : openOrCreate(row.weekStart))}
                                onDelete={() => setDeleting(row)}
                            />
                        ))}
                    </Stack>
                    <Button
                        variant="outlined"
                        fullWidth
                        startIcon={<AddIcon />}
                        disabled={createMenu.isPending}
                        onClick={() => openOrCreate(furtherWeekStart)}
                        sx={{ flexShrink: 0, minHeight: 56, borderRadius: 1, borderStyle: 'dashed', '&:hover': { borderStyle: 'dashed' }, fontWeight: 700 }}
                    >
                        さらに先の週（{dayjs(furtherWeekStart).format('M/D')}〜）の献立を作る
                    </Button>
                </Box>

                {pastCount > 0 && (
                    <Box sx={{ flexShrink: 0 }}>
                        <Button component={RouterLink} to={ROUTE.MENU_HISTORY} endIcon={<ChevronRightIcon />} sx={{ minHeight: 44, fontWeight: 700 }}>
                            過去の献立を見る（{pastCount}週）
                        </Button>
                    </Box>
                )}
            </Box>
            <DeleteDraftDialog
                isOpen={deleting !== null}
                menuId={deleting?.menuId ?? null}
                weekStart={deleting?.weekStart ?? null}
                onClose={() => setDeleting(null)}
                onSuccess={() => setDeleting(null)}
            />
        </PageLayout>
    );
};
