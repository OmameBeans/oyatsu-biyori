import type { PropsWithChildren } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import dayjs from 'dayjs';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonBase from '@mui/material/ButtonBase';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { AllergenCategoryIcon, CATEGORY_LABEL } from '../../../entities/allergen-judgement';
import { useMenuCalendar } from '../../../entities/menu';
import type { CalendarDay, CalendarItem, MenuListRow } from '../../../entities/menu';
import { formatDateLabel, formatWeekLabel, formatYen } from '../../../shared/lib';
import { DAY_TYPE, DAY_TYPE_LABEL, menuPath } from '../../../shared/type';
import { differenceText, hasIssues, TAG_SX, VISUALLY_HIDDEN_SX } from './weekDisplay';
import { IssueChips, WeekStatus } from './WeekParts';

const WEEKDAY_HEADERS = ['月', '火', '水', '木', '金', '土', '日'] as const;

interface MenuCalendarProps {
    /** YYYY-MM */
    month: string;
    /** 献立一覧の行（週の状態・金額・作れる週はこれで決める） */
    rows: MenuListRow[];
    thisWeekStart: string;
    isPending: boolean;
    /** 未作成の週の「献立を作る」 */
    onCreateWeek: (weekStart: string) => void;
    /** バッジを押したとき（商品の確認のダイアログを開く） */
    onShowIssues: (menuId: string) => void;
}

/**
 * SC-11 献立一覧（カレンダー。FR-LIST-03）。月を含む週（月曜〜日曜）を1行ずつ出す。
 * PC は左に週の状態、右に7日のマス。スマホは週ごとのカードに、献立のある日を縦に並べる。
 * 判定は API が返した区分をそのまま出す（商品が見つからないものは 🔴）。
 */
export const MenuCalendar = ({ month, rows, thisWeekStart, isPending, onCreateWeek, onShowIssues }: MenuCalendarProps) => {
    const { data: calendar } = useMenuCalendar(month);
    const rowByWeek = new Map(rows.map((row) => [row.weekStart, row]));
    const dayByDate = new Map(calendar.days.map((day) => [day.date, day]));
    const weekStarts: string[] = [];
    for (let week = dayjs(calendar.from); !week.isAfter(dayjs(calendar.to)); week = week.add(7, 'day')) {
        weekStarts.push(week.format('YYYY-MM-DD'));
    }
    const weeks = weekStarts.map((weekStart) => ({
        weekStart,
        row: rowByWeek.get(weekStart) ?? null,
        dates: WEEKDAY_HEADERS.map((_, offset) => dayjs(weekStart).add(offset, 'day').format('YYYY-MM-DD')),
    }));
    const isInMonth = (date: string) => date.startsWith(calendar.month);

    return (
        <>
            {/* PC：週の列＋7日のマス */}
            <Box
                role="table"
                aria-label={`${dayjs(calendar.month + '-01').format('YYYY年M月')}の献立`}
                sx={{
                    display: { xs: 'none', md: 'grid' },
                    gridTemplateColumns: '200px repeat(5, minmax(0, 1fr)) repeat(2, minmax(0, 0.6fr))',
                    border: 1,
                    borderColor: 'divider',
                    borderRadius: 1,
                    overflow: 'hidden',
                    bgcolor: 'background.paper',
                }}
            >
                <Box role="row" sx={{ display: 'contents' }}>
                    <HeaderCell>週</HeaderCell>
                    {WEEKDAY_HEADERS.map((label) => (
                        <HeaderCell key={label}>{label}</HeaderCell>
                    ))}
                </Box>
                {weeks.map(({ weekStart, row, dates }) => (
                    <Box key={weekStart} role="row" sx={{ display: 'contents' }}>
                        <Box
                            role="rowheader"
                            sx={{
                                p: 1.25,
                                borderTop: 1,
                                borderColor: 'divider',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: 0.75,
                                ...(weekStart === thisWeekStart
                                    ? {
                                          bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.06),
                                      }
                                    : {}),
                            }}
                        >
                            <WeekSummary weekStart={weekStart} row={row} isPending={isPending} onCreate={onCreateWeek} onShowIssues={onShowIssues} />
                        </Box>
                        {dates.map((date) => (
                            <DayCell key={date} date={date} day={dayByDate.get(date) ?? null} isToday={date === calendar.today} isInMonth={isInMonth(date)} />
                        ))}
                    </Box>
                ))}
            </Box>

            {/* スマホ：週ごとのカード */}
            <Stack spacing={1.5} sx={{ display: { md: 'none' } }}>
                {weeks.map(({ weekStart, row, dates }) => {
                    const days = dates.map((date) => dayByDate.get(date)).filter((day): day is CalendarDay => day !== undefined);
                    return (
                        <Card
                            key={weekStart}
                            component="section"
                            variant="outlined"
                            aria-label={formatWeekLabel(weekStart)}
                            sx={weekStart === thisWeekStart ? { borderWidth: 2, borderColor: 'primary.main' } : undefined}
                        >
                            <CardContent
                                sx={{
                                    display: 'flex',
                                    flexDirection: 'column',
                                    gap: 1,
                                    '&:last-child': { pb: 2 },
                                }}
                            >
                                <WeekSummary weekStart={weekStart} row={row} isPending={isPending} onCreate={onCreateWeek} onShowIssues={onShowIssues} />
                                {days.length > 0 && (
                                    <Stack spacing={1} sx={{ borderTop: 1, borderColor: 'divider', pt: 1 }}>
                                        {days.map((day) => (
                                            <Box key={day.date}>
                                                <Typography
                                                    variant="body2"
                                                    sx={{
                                                        fontWeight: 700,
                                                        color: day.date === calendar.today ? 'primary.main' : undefined,
                                                    }}
                                                >
                                                    {formatDateLabel(day.date)}
                                                    {day.date === calendar.today && '・今日'}
                                                </Typography>
                                                <DayContent day={day} />
                                            </Box>
                                        ))}
                                    </Stack>
                                )}
                            </CardContent>
                        </Card>
                    );
                })}
            </Stack>
        </>
    );
};

const HeaderCell = ({ children }: PropsWithChildren) => (
    <Box
        role="columnheader"
        sx={{
            px: 1.25,
            py: 0.75,
            bgcolor: 'action.hover',
            fontWeight: 700,
            fontSize: '0.875rem',
            textAlign: 'center',
        }}
    >
        {children}
    </Box>
);

interface WeekSummaryProps {
    weekStart: string;
    row: MenuListRow | null;
    isPending: boolean;
    onCreate: (weekStart: string) => void;
    onShowIssues: (menuId: string) => void;
}

/** 週の状態・実績と予算・商品の問題。献立のある週は「献立を開く」、未作成の週は「献立を作る」。献立一覧に行がない週（過去の未作成の週など）は日付だけ */
const WeekSummary = ({ weekStart, row, isPending, onCreate, onShowIssues }: WeekSummaryProps) => (
    <Stack spacing={0.75} sx={{ alignItems: 'flex-start' }}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', alignSelf: 'stretch' }}>
            {row?.relativeLabel && <Chip size="small" label={row.relativeLabel} color={row.relativeLabel === '今週' ? 'primary' : 'default'} sx={TAG_SX} />}
            <Typography variant="body2" sx={{ fontWeight: 700 }}>
                {dayjs(weekStart).format('M/D')}〜
            </Typography>
            {/* 献立のある週は、日付の横の「献立を開く」で献立（SC-09）を開く。未作成の週は下の「献立を作る」 */}
            {row?.menuId && (
                <Button
                    component={RouterLink}
                    to={menuPath(row.menuId)}
                    size="small"
                    variant="outlined"
                    aria-label={`${formatWeekLabel(weekStart)}の献立を開く`}
                    sx={{ minHeight: 32, py: 0, px: 1, fontWeight: 700, whiteSpace: 'nowrap' }}
                >
                    献立を開く
                </Button>
            )}
        </Stack>
        {row && <WeekStatus row={row} />}
        {row?.menuId && (
            // 予算を超えたら実績を赤にし、色だけに頼らないよう差額（サーバーが計算した「残り」から）も横に出す
            <Typography variant="body2">
                <Box component="span" sx={row.isOverBudget ? { color: 'error.main', fontWeight: 700 } : undefined}>
                    {formatYen(row.total ?? 0)}
                </Box>
                <Box component="span" sx={{ color: 'text.secondary' }}>
                    {' '}
                    / {formatYen(row.budget ?? 0)}
                </Box>
                {row.isOverBudget && (
                    <Box component="span" sx={{ color: 'error.main', fontWeight: 700, whiteSpace: 'nowrap', ml: 0.75 }}>
                        {differenceText(row.remaining ?? 0)}
                        <Box component="span" sx={VISUALLY_HIDDEN_SX}>
                            （予算オーバー）
                        </Box>
                    </Box>
                )}
            </Typography>
        )}
        {row?.menuId && hasIssues(row) && (
            <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                <IssueChips row={row} onClick={() => row.menuId && onShowIssues(row.menuId)} />
            </Stack>
        )}
        {row && !row.menuId && (
            <Button
                size="small"
                variant="outlined"
                disabled={isPending}
                onClick={() => onCreate(weekStart)}
                aria-label={`${formatWeekLabel(weekStart)}の献立を作る`}
                sx={{ minHeight: 36, fontWeight: 700 }}
            >
                献立を作る
            </Button>
        )}
    </Stack>
);

interface DayCellProps {
    date: string;
    day: CalendarDay | null;
    isToday: boolean;
    isInMonth: boolean;
}

/** PC のカレンダーの1日。献立のある日は、押すと献立（手順4）を開く */
const DayCell = ({ date, day, isToday, isInMonth }: DayCellProps) => {
    const dateMark = (
        <Box
            component="span"
            sx={{
                display: 'inline-flex',
                alignSelf: 'center',
                alignItems: 'center',
                justifyContent: 'center',
                minWidth: 26,
                height: 26,
                borderRadius: '13px',
                fontSize: '0.875rem',
                fontWeight: 700,
                color: isToday ? 'primary.contrastText' : isInMonth ? 'text.primary' : 'text.secondary',
                bgcolor: isToday ? 'primary.main' : undefined,
            }}
        >
            {dayjs(date).format(isInMonth ? 'D' : 'M/D')}
            {isToday && (
                <Box component="span" sx={VISUALLY_HIDDEN_SX}>
                    （今日）
                </Box>
            )}
        </Box>
    );
    const cellSx = {
        minHeight: 112,
        p: 0.75,
        borderTop: 1,
        borderLeft: 1,
        borderColor: 'divider',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'stretch',
        justifyContent: 'flex-start',
        textAlign: 'left',
        gap: 0.5,
        minWidth: 0,
    } as const;

    if (!day) {
        return (
            <Box role="cell" sx={cellSx}>
                {dateMark}
            </Box>
        );
    }
    return (
        <Box role="cell" sx={{ ...cellSx, p: 0 }}>
            <ButtonBase
                component={RouterLink}
                to={menuPath(day.menuId, 4)}
                aria-label={`${formatDateLabel(date)}の献立を開く`}
                sx={{
                    ...cellSx,
                    border: 0,
                    flex: 1,
                    width: '100%',
                    '&:hover': { bgcolor: 'action.selected' },
                }}
            >
                {dateMark}
                <DayContent day={day} />
            </ButtonBase>
        </Box>
    );
};

interface DayContentProps {
    day: CalendarDay;
}

/** 1日の中身（見出し・日の種類・商品と判定のアイコン） */
const DayContent = ({ day }: DayContentProps) => (
    <Stack spacing={0.25} sx={{ minWidth: 0, width: '100%' }}>
        {day.label && (
            <Typography
                variant="caption"
                sx={{
                    fontWeight: 700,
                    color: 'secondary.main',
                    overflowWrap: 'anywhere',
                }}
            >
                {day.label}
            </Typography>
        )}
        {day.dayType !== DAY_TYPE.NORMAL && (
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                {DAY_TYPE_LABEL[day.dayType]}
            </Typography>
        )}
        {day.items.length === 0 && day.dayType === DAY_TYPE.NORMAL && (
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                まだ商品がありません
            </Typography>
        )}
        {day.items.map((item, index) => (
            <ItemLine key={index} item={item} />
        ))}
    </Stack>
);

/** 商品1件。判定の区分は色だけでなくアイコンと読み上げの文言でも示す */
interface ItemLineProps {
    item: CalendarItem;
}

const ItemLine = ({ item }: ItemLineProps) => {
    const { category } = item;
    return (
        <Typography
            variant="caption"
            component="div"
            sx={{
                display: 'flex',
                gap: 0.5,
                alignItems: 'baseline',
                lineHeight: 1.4,
                minWidth: 0,
            }}
        >
            <Box component="span" sx={{ flexShrink: 0 }}>
                <AllergenCategoryIcon category={category} />
                <Box component="span" sx={VISUALLY_HIDDEN_SX}>
                    {CATEGORY_LABEL[category]}：
                </Box>
            </Box>
            <Box
                component="span"
                sx={{
                    overflowWrap: 'anywhere',
                    fontWeight: category === 'Safe' ? 400 : 700,
                }}
            >
                {item.productName}
            </Box>
        </Typography>
    );
};
