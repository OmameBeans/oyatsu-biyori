import { useEffect, useMemo, useRef, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { DataGrid, useGridApiRef } from '@mui/x-data-grid';
import type { GridColDef } from '@mui/x-data-grid';
import { jaJP } from '@mui/x-data-grid/locales';
import { useSnackbar } from 'notistack';
import type { FlyerItem } from '../../../entities/flyer';
import {
    allergenChipsOf,
    checkPointsOf,
    FLYER_ROW_STATUS,
    FLYER_ROW_STATUS_COLOR,
    FLYER_ROW_STATUS_LABEL,
    FLYER_ROW_TAB,
    filterRows,
    nextUnconfirmedRowId,
    sameProductSummary,
    toSortedRows,
} from '../model/flyerRows';
import type { AllergenChip, FlyerItemRow, FlyerRowTab } from '../model/flyerRows';
import { FlyerConfirmForm } from './FlyerConfirmForm';
import { FlyerReviewDialog } from './FlyerReviewDialog';

interface FlyerItemGridProps {
    menuId: string;
    items: FlyerItem[];
}

/** 表の1行。商品の行と、開いている商品のすぐ下に差し込む詳細の行（全部の列をまたぐ） */
interface GridRow extends FlyerItemRow {
    isDetail: boolean;
}

const detailRowId = (id: string) => `${id}__detail`;

const TABS: FlyerRowTab[] = [FLYER_ROW_TAB.ALL, FLYER_ROW_TAB.WARNING, FLYER_ROW_TAB.NEEDS_CHECK, FLYER_ROW_TAB.UNCONFIRMED, FLYER_ROW_TAB.CONFIRMED];
const tabLabel = (tab: FlyerRowTab) => (tab === FLYER_ROW_TAB.ALL ? 'すべて' : FLYER_ROW_STATUS_LABEL[tab]);

/** 読み上げ用に見出しの文字を隠す（見た目には出さない） */
const VISUALLY_HIDDEN_SX = { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap' } as const;

/** 表のふち（左右 1px ずつ） */
const GRID_BORDER_WIDTH = 2;

/** アレルゲン表記の列の、いちばん狭いときの幅（この幅で折り返しても収まる高さを、行の高さにする） */
const ALLERGEN_COLUMN_MIN_WIDTH = 200;
/** セルの左右の余白（DataGrid の既定。10px ずつ） */
const CELL_PADDING_X = 20;
/** セルの上下の余白（py: 1） */
const CELL_PADDING_Y = 16;
/** 小さいチップの高さと、チップどうしの間（spacing 0.5） */
const CHIP_HEIGHT = 24;
const CHIP_GAP = 4;
/** 表記の原文の行の高さ（body2 の1行） */
const TEXT_LINE_HEIGHT = 20;
/** 商品名・同じ商品か（2行）が収まる高さ */
const MIN_ROW_HEIGHT = 64;

/** 小さいチップの幅の見積もり（13px の文字と左右の余白・枠。和文は1文字ほぼ 13px なので、多めに見る） */
const estimateChipWidth = (label: string) => 13 * label.length + 22;

/** チップを、列のいちばん狭い幅で左から詰めたときの行数 */
const countChipLines = (chips: AllergenChip[]) => {
    const available = ALLERGEN_COLUMN_MIN_WIDTH - CELL_PADDING_X;
    let lines = 0;
    let used = 0;
    for (const chip of chips) {
        const width = Math.min(estimateChipWidth(chip.label), available);
        if (lines === 0 || used + CHIP_GAP + width > available) {
            lines++;
            used = width;
        } else {
            used += CHIP_GAP + width;
        }
    }
    return lines;
};

/** アレルゲン表記の列（原文の1行とチップ）が収まる高さ */
const allergenCellHeight = (item: FlyerItem) => {
    const lines = countChipLines(allergenChipsOf(item));
    const textHeight = item.read.allergenLabelText?.trim() ? TEXT_LINE_HEIGHT : 0;
    const chipsHeight = lines > 0 ? lines * CHIP_HEIGHT + (lines - 1) * CHIP_GAP + (textHeight ? CHIP_GAP : 0) : 0;
    return CELL_PADDING_Y + textHeight + chipsHeight;
};

const NoRowsOverlay = () => (
    <Stack sx={{ height: '100%', minHeight: 96, alignItems: 'center', justifyContent: 'center' }}>
        <Typography color="text.secondary">この条件に当てはまる商品はありません</Typography>
    </Stack>
);

/**
 * SC-09 手順1 読み取った商品の表（MUI X の DataGrid。無料版）。
 * 状態の順（警告あり → 要確認 → 未確定 → 確定済み）に並べ、タブで絞り込む。並べ替え・列のメニュー・ページ送りは使わない。
 * 行の下の詳細欄（確定のフォーム）は、開いている行のすぐ後ろに全部の列をまたぐ行を差し込んで出す。
 * 初めはどの行も閉じておき、確定したら次の未確定の行を開く。
 */
export const FlyerItemGrid = ({ menuId, items }: FlyerItemGridProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('md'));
    const apiRef = useGridApiRef();
    const { enqueueSnackbar } = useSnackbar();

    const sortedRows = useMemo(() => toSortedRows(items), [items]);
    const [tab, setTab] = useState<FlyerRowTab>(FLYER_ROW_TAB.ALL);
    const visibleRows = useMemo(() => filterRows(sortedRows, tab), [sortedRows, tab]);
    const [expandedId, setExpandedId] = useState<string | null>(null);
    // 1件ずつ確かめるダイアログ。開いたときの未確定の商品を、表と同じ状態の順で確かめる
    const unconfirmedIds = sortedRows.filter((row) => row.status !== FLYER_ROW_STATUS.CONFIRMED).map((row) => row.id);
    const [reviewQueue, setReviewQueue] = useState<string[] | null>(null);

    const gridRows = useMemo(
        () => visibleRows.flatMap((row): GridRow[] => (row.id === expandedId ? [{ ...row, isDetail: false }, { ...row, id: detailRowId(row.id), isDetail: true }] : [{ ...row, isDetail: false }])),
        [visibleRows, expandedId],
    );

    // 商品の行の高さは、すべての行でそろえる。アレルゲンのチップがいちばん多い行が、列のいちばん狭い幅で折り返しても収まる高さにする
    // （タブを切り替えても変わらないように、絞り込む前のすべての商品で求める）
    const productRowHeight = useMemo(() => Math.max(MIN_ROW_HEIGHT, ...items.map(allergenCellHeight)), [items]);

    // 表の見えている幅。スマホで表を横にスクロールしても、詳細欄は見えている幅に収めて左に留める
    const wrapperRef = useRef<HTMLDivElement>(null);
    const [viewportWidth, setViewportWidth] = useState<number | null>(null);
    useEffect(() => {
        const wrapper = wrapperRef.current;
        if (!wrapper) {
            return;
        }
        const observer = new ResizeObserver(([entry]) => setViewportWidth(entry.contentRect.width - GRID_BORDER_WIDTH));
        observer.observe(wrapper);
        return () => observer.disconnect();
    }, []);

    // 確定したあとに開いた行を、見える位置までスクロールする。確定した行は、読み直したあと下へ移って並びが変わるので、
    // 読み直しが届くまでは並びが変わるたびに合わせ直し、届いたら止める（そのあとの読み直しでは動かさない）。DataGrid が行を描き直してから動かす
    const scrollRequestRef = useRef<{ openId: string; confirmedId: string } | null>(null);
    useEffect(() => {
        const request = scrollRequestRef.current;
        if (!request) {
            return;
        }
        const frame = requestAnimationFrame(() => apiRef.current?.getRowElement(request.openId)?.scrollIntoView({ block: 'start' }));
        if (items.find((item) => item.id === request.confirmedId)?.confirmation) {
            scrollRequestRef.current = null;
        }
        return () => cancelAnimationFrame(frame);
    }, [gridRows, items, apiRef]);

    const handleTabChange = (next: FlyerRowTab) => {
        scrollRequestRef.current = null;
        setTab(next);
        setExpandedId(null);
    };

    const handleToggle = (id: string) => {
        scrollRequestRef.current = null;
        setExpandedId((current) => (current === id ? null : id));
    };

    const handleConfirmed = (item: FlyerItem) => {
        enqueueSnackbar(`「${item.read.name}」を確定しました`, { variant: 'success' });
        const nextId = nextUnconfirmedRowId(visibleRows, item.id);
        scrollRequestRef.current = nextId ? { openId: nextId, confirmedId: item.id } : null;
        setExpandedId(nextId);
    };

    const columns: GridColDef<GridRow>[] = [
        {
            field: 'toggle',
            headerName: '詳細',
            minWidth: 56,
            width: 56,
            sortable: false,
            align: 'center',
            renderHeader: () => (
                <Box component="span" sx={VISUALLY_HIDDEN_SX}>
                    詳細
                </Box>
            ),
            // 詳細の行は、この列から全部の列をまたぐ
            colSpan: (_value, row) => (row.isDetail ? columns.length : undefined),
            renderCell: ({ row, hasFocus }) => {
                if (row.isDetail) {
                    return (
                        // 矢印キーで詳細の行に来たときは、この枠にフォーカスを置く（DataGrid は枠の中の最初の tabindex="0" を選ぶ）。
                        // 枠そのものでのキーは DataGrid に渡して行の移動に使い、中の入力でのキー・クリックは DataGrid に渡さない
                        // （渡すと、矢印・Space・Enter をセルの移動に取られ、クリックでセレクトのメニューからフォーカスを奪われる）
                        <Box
                            role="group"
                            aria-label={`「${row.item.read.name}」の詳細`}
                            tabIndex={hasFocus ? 0 : -1}
                            onKeyDown={(event) => event.target !== event.currentTarget && event.stopPropagation()}
                            onMouseDown={(event) => event.stopPropagation()}
                            onMouseUp={(event) => event.stopPropagation()}
                            sx={{
                                position: 'sticky',
                                left: 0,
                                width: viewportWidth ? `min(100%, ${viewportWidth}px)` : '100%',
                                p: { xs: 2, sm: 3 },
                                cursor: 'auto',
                                textAlign: 'left',
                                '&:focus-visible': { outline: 2, outlineColor: 'primary.main', outlineOffset: -2 },
                            }}
                        >
                            <FlyerConfirmForm menuId={menuId} item={row.item} onClose={() => handleToggle(row.item.id)} onSuccess={() => handleConfirmed(row.item)} />
                        </Box>
                    );
                }
                const isExpanded = row.id === expandedId;
                return (
                    <IconButton
                        aria-expanded={isExpanded}
                        aria-label={`「${row.item.read.name}」の詳細を${isExpanded ? '閉じる' : '開く'}`}
                        onClick={(event) => {
                            // 行のクリックでも開閉するので、二重に切り替えない
                            event.stopPropagation();
                            handleToggle(row.id);
                        }}
                        onKeyDown={(event) => event.key === ' ' && event.stopPropagation()}
                        sx={{ width: 44, height: 44 }}
                    >
                        {isExpanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
                    </IconButton>
                );
            },
        },
        {
            field: 'status',
            headerName: '状態',
            minWidth: 150,
            width: 150,
            sortable: false,
            renderCell: ({ row }) => {
                // 「警告あり」「要確認」は、何を確かめるのかを短く添える（詳しくは行を開いた先の「確かめること」）
                // 確定したあとも、指摘は消さずに出す（選び直すときの手がかり）
                const reasons = Array.from(new Set(checkPointsOf(row.item).map((point) => point.short)));
                return (
                    <Stack sx={{ minWidth: 0, alignItems: 'flex-start' }}>
                        <Chip
                            size="small"
                            color={FLYER_ROW_STATUS_COLOR[row.status]}
                            variant={row.status === FLYER_ROW_STATUS.UNCONFIRMED ? 'outlined' : 'filled'}
                            label={FLYER_ROW_STATUS_LABEL[row.status]}
                        />
                        {reasons.length > 0 && (
                            <Typography variant="caption" color="text.secondary" component="div" sx={{ mt: 0.25, maxWidth: '100%', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={reasons.join('・')}>
                                {reasons.join('・')}
                            </Typography>
                        )}
                    </Stack>
                );
            },
        },
        {
            field: 'name',
            headerName: '商品名',
            minWidth: 200,
            flex: 1.2,
            sortable: false,
            renderCell: ({ row }) => {
                const read = row.item.read;
                return (
                    <Box sx={{ minWidth: 0 }}>
                        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                            <Typography sx={{ fontWeight: 700, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={read.name}>
                                {read.name}
                            </Typography>
                            {row.item.isManual && <Chip size="small" label="手入力" />}
                        </Stack>
                        <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                            {[read.volume, read.priceIncludingTax ? `税込 ${read.priceIncludingTax}円` : '税込価格なし'].filter(Boolean).join('・')}
                        </Typography>
                    </Box>
                );
            },
        },
        {
            field: 'allergens',
            headerName: 'アレルゲン表記',
            minWidth: ALLERGEN_COLUMN_MIN_WIDTH,
            flex: 1,
            sortable: false,
            renderCell: ({ row }) => {
                const labelText = row.item.read.allergenLabelText?.trim();
                return (
                    <Box sx={{ minWidth: 0 }}>
                        {/* 読み取った表記の原文（チラシと見比べられるように。FR-FLYER-04a） */}
                        {labelText && (
                            <Typography variant="body2" sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={labelText}>
                                「{labelText}」
                            </Typography>
                        )}
                        <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', mt: labelText ? 0.5 : 0 }}>
                            {allergenChipsOf(row.item).map((chip) => (
                                // 「表記なし」は読み落としのこともある。点線の枠で、ほかの品目と見分ける
                                <Chip key={chip.key} size="small" color={chip.color} variant={chip.variant} label={chip.label} title={chip.label} sx={chip.isDashed ? { borderStyle: 'dashed' } : undefined} />
                            ))}
                        </Stack>
                    </Box>
                );
            },
        },
        {
            field: 'same',
            headerName: '同じ商品か',
            minWidth: 240,
            width: 240,
            sortable: false,
            renderCell: ({ row }) => {
                const summary = sameProductSummary(row.item);
                return (
                    <Box sx={{ minWidth: 0 }}>
                        <Typography variant="body2" sx={{ fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={summary.title}>
                            {summary.title}
                        </Typography>
                        <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={summary.detail}>
                            {summary.detail || '—'}
                        </Typography>
                    </Box>
                );
            },
        },
    ];

    return (
        <Stack spacing={1} sx={{ flex: { md: '1 1 auto' }, minHeight: { md: 0 } }}>
            {unconfirmedIds.length > 0 && (
                <Button
                    variant="contained"
                    size="large"
                    onClick={() => {
                        setExpandedId(null);
                        setReviewQueue(unconfirmedIds);
                    }}
                    sx={{ alignSelf: { xs: 'stretch', sm: 'flex-start' }, flexShrink: 0 }}
                >
                    未確定を1件ずつ確かめる（{unconfirmedIds.length}件）
                </Button>
            )}
            <Tabs
                value={tab}
                onChange={(_, value: FlyerRowTab) => handleTabChange(value)}
                variant="scrollable"
                allowScrollButtonsMobile
                aria-label="状態で絞り込む"
                sx={{ flexShrink: 0, borderBottom: 1, borderColor: 'divider' }}
            >
                {TABS.map((value) => (
                    <Tab key={value} value={value} label={`${tabLabel(value)} ${filterRows(sortedRows, value).length}`} />
                ))}
            </Tabs>
            <Box ref={wrapperRef} sx={{ flex: { md: '1 1 auto' }, minHeight: { md: 320 }, minWidth: 0 }}>
                <DataGrid
                    apiRef={apiRef}
                    aria-label="読み取った商品"
                    rows={gridRows}
                    columns={columns}
                    // 商品の行はそろえた高さ、詳細の行は中身に合わせる
                    getRowHeight={({ model }) => (model.isDetail ? 'auto' : productRowHeight)}
                    getEstimatedRowHeight={() => productRowHeight}
                    getRowClassName={({ row }) => (row.isDetail ? 'flyer-detail-row' : row.id === expandedId ? 'flyer-expanded-row' : '')}
                    getCellClassName={({ row }) => (row.isDetail ? 'flyer-detail-cell' : '')}
                    onRowClick={({ row }) => !row.isDetail && handleToggle(row.id)}
                    localeText={jaJP.components.MuiDataGrid.defaultProps.localeText}
                    slots={{ noRowsOverlay: NoRowsOverlay }}
                    autoHeight={isMobile}
                    // 件数は数件〜数十件。詳細欄の入力を保つため、見えていない行も描く
                    disableVirtualization
                    disableColumnMenu
                    disableColumnSorting
                    disableRowSelectionOnClick
                    hideFooter
                    sx={{
                        bgcolor: 'background.paper',
                        '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 700 },
                        '& .MuiDataGrid-row': { cursor: 'pointer', scrollMarginTop: 72 },
                        // DataGrid はセルに行の高さぶんの line-height を付ける。チップなどの行が広がらないように戻す
                        '& .MuiDataGrid-cell': { display: 'flex', alignItems: 'center', py: 1, lineHeight: 'normal' },
                        '& .flyer-expanded-row, & .flyer-expanded-row:hover': { bgcolor: 'action.selected' },
                        '& .flyer-detail-row, & .flyer-detail-row:hover': { bgcolor: 'background.default', cursor: 'auto' },
                        '& .flyer-detail-cell': { display: 'block', p: 0, overflow: 'visible', whiteSpace: 'normal' },
                        '& .flyer-detail-cell:focus, & .flyer-detail-cell:focus-within': { outline: 'none' },
                    }}
                />
            </Box>
            {reviewQueue && <FlyerReviewDialog menuId={menuId} items={items} queue={reviewQueue} isOpen onClose={() => setReviewQueue(null)} />}
        </Stack>
    );
};
