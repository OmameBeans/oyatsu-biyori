import { Suspense, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import dayjs from 'dayjs';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import AddIcon from '@mui/icons-material/Add';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import ViewListIcon from '@mui/icons-material/ViewList';
import { useSnackbar } from 'notistack';
import { AllergenCategoryIcon, judgementText } from '../../../entities/allergen-judgement';
import { useCreateMenu, useMenuList } from '../../../entities/menu';
import type { MenuListRow } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, readPreference, writePreference } from '../../../shared/lib';
import { menuPath, PREFERENCE_KEY, ROUTE } from '../../../shared/type';
import { PageLayout } from '../../../shared/ui';
import { DeleteDraftDialog } from './DeleteDraftDialog';
import { MenuCalendar } from './MenuCalendar';
import { WeekIssuesDialog } from './WeekIssuesDialog';
import { WeekCard } from './WeekParts';

/**
 * SC-10 献立一覧（ログイン直後の画面。T3-01・T4-12）。
 * 上部に、必要なときだけ注意（使えなくなった商品を使う献立・条件設定がまだ・今日以降の献立で使う、パッケージで確認する商品）、
 * その下に「今週とこれから」（今週を目立たせ、古い順）。過去の週は「過去の献立」（SC-14）の画面で見る。
 * PC（md 以上）では、注意・見出し・「さらに先の週」・「過去の献立を見る」を固定し、週のカードの部分だけをスクロールする。
 * 「カレンダー」に切り替えると、月のカレンダー（SC-11。FR-LIST-03）を出す。表示と月はクエリ文字列（?view=calendar&month=YYYY-MM）に持つ
 * （再読み込み・戻る・URL の共有で同じ表示になるように）。クエリ文字列に表示がなければ、この端末で最後に選んだ表示（localStorage）にする。
 */
const VIEW_LIST = 'list';
const VIEW_CALENDAR = 'calendar';
type MenuListView = typeof VIEW_LIST | typeof VIEW_CALENDAR;
const isMenuListView = (value: unknown): value is MenuListView => value === VIEW_LIST || value === VIEW_CALENDAR;
const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/;

export const MenuListPage = () => {
    const { data: list } = useMenuList();
    const createMenu = useCreateMenu();
    const navigate = useNavigate();
    const { enqueueSnackbar } = useSnackbar();
    const [deleting, setDeleting] = useState<MenuListRow | null>(null);
    const [issuesMenuId, setIssuesMenuId] = useState<string | null>(null);
    const [searchParams, setSearchParams] = useSearchParams();
    const viewParam = searchParams.get('view');
    const isCalendar = (isMenuListView(viewParam) ? viewParam : readPreference(PREFERENCE_KEY.MENU_LIST_VIEW, isMenuListView, VIEW_LIST)) === VIEW_CALENDAR;
    const monthParam = searchParams.get('month');
    // 形が違う月は今月にする（API でエラーにしない）
    const month = monthParam && MONTH_PATTERN.test(monthParam) ? monthParam : dayjs().format('YYYY-MM');
    // 一覧・カレンダーを選んだら、この端末に覚えておく（月の移動では書かない）
    const showCalendar = (nextMonth: string | null) => {
        writePreference(PREFERENCE_KEY.MENU_LIST_VIEW, nextMonth === null ? VIEW_LIST : VIEW_CALENDAR);
        setSearchParams(nextMonth === null ? { view: VIEW_LIST } : { view: VIEW_CALENDAR, month: nextMonth }, { replace: nextMonth !== null && isCalendar });
    };
    const moveMonth = (offset: number) =>
        showCalendar(
            dayjs(month + '-01')
                .add(offset, 'month')
                .format('YYYY-MM'),
        );

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
        <PageLayout
            title="献立一覧"
            actions={
                <ToggleButtonGroup
                    exclusive
                    size="small"
                    value={isCalendar ? VIEW_CALENDAR : VIEW_LIST}
                    onChange={(_, value: string | null) => value !== null && showCalendar(value === VIEW_CALENDAR ? month : null)}
                    aria-label="表示の切り替え"
                >
                    <ToggleButton value={VIEW_LIST} sx={{ px: 2, gap: 0.75, fontWeight: 700, minHeight: 40 }}>
                        <ViewListIcon fontSize="small" />
                        一覧
                    </ToggleButton>
                    <ToggleButton value={VIEW_CALENDAR} sx={{ px: 2, gap: 0.75, fontWeight: 700, minHeight: 40 }}>
                        <CalendarMonthIcon fontSize="small" />
                        カレンダー
                    </ToggleButton>
                </ToggleButtonGroup>
            }
        >
            <Box
                sx={{
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 3,
                    height: { md: '100%' },
                }}
            >
                {hasNotices && (
                    <Stack spacing={2} sx={{ flexShrink: 0 }}>
                        {list.blockedUsages.length > 0 && (
                            <Alert severity="error" variant="filled">
                                <Typography sx={{ fontWeight: 700, mb: 0.5 }}>
                                    <AllergenCategoryIcon category="Blocked" isInherit /> 使えない商品が献立に入っています。別の商品に替えてください
                                </Typography>
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

                {isCalendar ? (
                    <Box
                        component="section"
                        aria-labelledby="menu-list-calendar"
                        sx={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 1.5,
                            flex: { md: 1 },
                            minHeight: { md: 280 },
                        }}
                    >
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexShrink: 0 }}>
                            <IconButton aria-label="前の月" onClick={() => moveMonth(-1)}>
                                <ChevronLeftIcon />
                            </IconButton>
                            <Typography id="menu-list-calendar" variant="h2" sx={{ minWidth: '7em', textAlign: 'center' }}>
                                {dayjs(month + '-01').format('YYYY年M月')}
                            </Typography>
                            <IconButton aria-label="次の月" onClick={() => moveMonth(1)}>
                                <ChevronRightIcon />
                            </IconButton>
                            <Button variant="outlined" size="small" onClick={() => showCalendar(dayjs().format('YYYY-MM'))} sx={{ minHeight: 36, fontWeight: 700 }}>
                                今月
                            </Button>
                        </Stack>
                        <Box sx={{ minHeight: { md: 0 }, overflowY: { md: 'auto' } }}>
                            <Suspense
                                fallback={
                                    <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                                        <CircularProgress />
                                    </Box>
                                }
                            >
                                <MenuCalendar
                                    month={month}
                                    rows={list.rows}
                                    thisWeekStart={list.thisWeekStart}
                                    isPending={createMenu.isPending}
                                    onCreateWeek={openOrCreate}
                                    onShowIssues={setIssuesMenuId}
                                />
                            </Suspense>
                        </Box>
                    </Box>
                ) : (
                    <Box
                        component="section"
                        aria-labelledby="menu-list-upcoming"
                        sx={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 1.5,
                            flex: { md: 1 },
                            minHeight: { md: 280 },
                        }}
                    >
                        <Typography id="menu-list-upcoming" variant="h2">
                            今週とこれから
                        </Typography>
                        {/* PC では週のカードの部分だけをスクロールする。カードは縮めない（縮むと中身が切れる）。注意が多いときは、上の section の最低の高さで潰れないようにする */}
                        <Stack
                            spacing={1.5}
                            sx={{
                                minHeight: { md: 0 },
                                overflowY: { md: 'auto' },
                                '& > *': { flexShrink: 0 },
                            }}
                        >
                            {upcomingRows.map((row) => (
                                <WeekCard
                                    key={row.weekStart}
                                    row={row}
                                    isThisWeek={row.weekStart === list.thisWeekStart}
                                    isPending={createMenu.isPending}
                                    onOpen={() => (row.menuId ? navigate(menuPath(row.menuId)) : openOrCreate(row.weekStart))}
                                    onDelete={() => setDeleting(row)}
                                    onShowIssues={() => setIssuesMenuId(row.menuId)}
                                />
                            ))}
                        </Stack>
                        <Button
                            variant="outlined"
                            fullWidth
                            startIcon={<AddIcon />}
                            disabled={createMenu.isPending}
                            onClick={() => openOrCreate(furtherWeekStart)}
                            sx={{
                                flexShrink: 0,
                                minHeight: 56,
                                borderRadius: 1,
                                borderStyle: 'dashed',
                                '&:hover': { borderStyle: 'dashed' },
                                fontWeight: 700,
                            }}
                        >
                            さらに先の週（{dayjs(furtherWeekStart).format('M/D')}
                            〜）の献立を作る
                        </Button>
                    </Box>
                )}

                {!isCalendar && pastCount > 0 && (
                    <Box sx={{ flexShrink: 0 }}>
                        <Button component={RouterLink} to={ROUTE.MENU_HISTORY} endIcon={<ChevronRightIcon />} sx={{ minHeight: 44, fontWeight: 700 }}>
                            過去の献立を見る（{pastCount}週）
                        </Button>
                    </Box>
                )}
            </Box>
            <WeekIssuesDialog isOpen={issuesMenuId !== null} menuId={issuesMenuId} onClose={() => setIssuesMenuId(null)} />
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
