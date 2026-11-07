import { useEffect, useMemo, useRef, useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
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
import { AllergenJudgementLine } from '../../../entities/allergen-judgement';
import type { FlyerItem, FlyerSession } from '../../../entities/flyer';
import {
    filterItems,
    FLYER_ROW_TAB,
    FLYER_ROW_TABS,
    flyerTabCount,
    flyerTabLabel,
    isPiecesUnknown,
    isReviewTarget,
    labelAbsentCompareItemsOf,
    matchSummaryOf,
    nextOpenableItemId,
} from '../model/flyerRows';
import type { FlyerRowTab } from '../model/flyerRows';
import { FlyerConfirmForm } from './FlyerConfirmForm';
import { FlyerLabelAbsentCompareDialog } from './FlyerLabelAbsentCompareDialog';
import { FlyerPiecesInput } from './FlyerPiecesInput';
import { FlyerReviewDialog } from './FlyerReviewDialog';
import { FlyerAllergenChips, FlyerStatusCell } from './FlyerStatusChip';

interface FlyerItemGridProps {
    menuId: string;
    session: FlyerSession;
    /** 開いている（確定のフォームで直している途中の）行が変わったとき。まとめて確定・登録から外すために使う */
    onExpandedIdChange?: (id: string | null) => void;
}

/** 表の1行。商品の行と、開いている商品のすぐ下に差し込む詳細の行（全部の列をまたぐ） */
interface GridRow {
    id: string;
    item: FlyerItem;
    isDetail: boolean;
}

const detailRowId = (id: string) => `${id}__detail`;

/** 読み上げ用に見出しの文字を隠す（見た目には出さない） */
const VISUALLY_HIDDEN_SX = { position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)', whiteSpace: 'nowrap' } as const;

/** 表のふち（左右 1px ずつ） */
const GRID_BORDER_WIDTH = 2;
/** 商品の行のいちばん低い高さ（分類のチップと理由・商品名の2行が収まる） */
const MIN_ROW_HEIGHT = 64;

const NoRowsOverlay = () => (
    <Stack sx={{ height: '100%', minHeight: 96, alignItems: 'center', justifyContent: 'center' }}>
        <Typography color="text.secondary">この分類の商品はありません</Typography>
    </Stack>
);

interface ProductNameCellProps {
    item: FlyerItem;
}

/** 商品名と、内容量・税込価格・手入力の印 */
const ProductNameCell = ({ item }: ProductNameCellProps) => {
    const read = item.read;
    return (
        <Box sx={{ minWidth: 0 }}>
            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', minWidth: 0 }}>
                <Typography sx={{ fontWeight: 700, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={read.name}>
                    {read.name || '（商品名なし）'}
                </Typography>
                {item.isManual && <Chip size="small" label="手入力" sx={{ flexShrink: 0 }} />}
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                {[read.maker ?? 'メーカーなし', read.volume, read.priceIncludingTax !== null ? `税込 ${read.priceIncludingTax}円` : '税込価格なし'].filter(Boolean).join('・')}
            </Typography>
        </Box>
    );
};

interface MatchCellProps {
    menuId: string;
    item: FlyerItem;
}

/**
 * 一致した商品（名前＋メーカーが同じ登録済みの商品）と、商品一覧でのアレルゲンの表示（09 の 6）。
 * 2件以上一致したときは「ほかに同じ名前の商品が n 件」を添える。使えない（入数が分からない）の行には入数の欄を出す
 */
const MatchCell = ({ menuId, item }: MatchCellProps) => {
    const summary = matchSummaryOf(item);
    return (
        <Stack spacing={0.5} sx={{ minWidth: 0, width: '100%' }}>
            <Box sx={{ minWidth: 0 }}>
                <Typography variant="body2" sx={{ fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }} title={summary.title}>
                    {summary.title}
                </Typography>
                <Typography variant="caption" color="text.secondary" component="div" sx={{ overflowWrap: 'anywhere' }}>
                    {summary.detail}
                </Typography>
            </Box>
            {item.matchedProduct && !item.confirmation && <AllergenJudgementLine judgement={item.matchedProduct.judgement} showNextSteps={false} />}
            {isPiecesUnknown(item) && <FlyerPiecesInput menuId={menuId} item={item} />}
        </Stack>
    );
};

/**
 * SC-09 手順1 読み取った商品の表（MUI X の DataGrid。無料版）。
 * 取り込みの分類の順（再入力 → 要確認 → 使える → 使えない → 確定済み。サーバーが並べたまま）に並べ、タブで絞り込む。
 * 分類・理由・件数はサーバーの値をそのまま出す。並べ替え・列のメニュー・ページ送りは使わない。
 * 行の下の詳細欄（確定のフォーム）は、開いている行のすぐ後ろに全部の列をまたぐ行を差し込んで出す。
 * スマホでは、分類のチップ・商品名・アレルゲン表記を1つの列に縦に並べる（横にスクロールしなくても見えるように）。
 */
export const FlyerItemGrid = ({ menuId, session, onExpandedIdChange }: FlyerItemGridProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('md'));
    const apiRef = useGridApiRef();
    const items = session.items;

    const [tab, setTab] = useState<FlyerRowTab>(FLYER_ROW_TAB.ALL);
    const visibleItems = useMemo(() => filterItems(items, tab), [items, tab]);
    const [expandedId, setExpandedId] = useState<string | null>(null);
    // 開いている行を親に知らせる（フォームで直した内容を、まとめて確定・登録で捨てないように）。表が消えたら閉じた扱いにする
    useEffect(() => {
        onExpandedIdChange?.(expandedId);
    }, [expandedId, onExpandedIdChange]);
    useEffect(() => () => onExpandedIdChange?.(null), [onExpandedIdChange]);
    // 1件ずつ確かめるダイアログ。開いたときの再入力・要確認の商品を、表と同じ順で確かめる
    const reviewIds = items.filter(isReviewTarget).map((item) => item.id);
    const [reviewQueue, setReviewQueue] = useState<string[] | null>(null);
    // 表記なしの商品のまとめての見比べ（1件ずつ止めずに、まとめてチラシと見比べる。D-97）
    const labelAbsentItems = labelAbsentCompareItemsOf(items);
    const labelAbsentPendingCount = labelAbsentItems.filter((item) => !item.isLabelAbsentCompared).length;
    const [isLabelAbsentDialogOpen, setIsLabelAbsentDialogOpen] = useState(false);

    const gridRows = useMemo(
        () =>
            visibleItems.flatMap((item): GridRow[] =>
                item.id === expandedId
                    ? [
                          { id: item.id, item, isDetail: false },
                          { id: detailRowId(item.id), item, isDetail: true },
                      ]
                    : [{ id: item.id, item, isDetail: false }],
            ),
        [visibleItems, expandedId],
    );

    // 表の見えている幅。詳細欄は見えている幅に収めて左に留める
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
        const nextId = nextOpenableItemId(visibleItems, item.id);
        scrollRequestRef.current = nextId ? { openId: nextId, confirmedId: item.id } : null;
        setExpandedId(nextId);
    };

    const handleRemoved = () => {
        scrollRequestRef.current = null;
        setExpandedId(null);
    };

    const toggleColumn: GridColDef<GridRow> = {
        field: 'toggle',
        headerName: '詳細',
        minWidth: 52,
        width: 52,
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
                        <FlyerConfirmForm
                            key={row.item.id}
                            menuId={menuId}
                            item={row.item}
                            onClose={() => handleToggle(row.item.id)}
                            onSuccess={() => handleConfirmed(row.item)}
                            onRemoved={handleRemoved}
                        />
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
    };

    const desktopColumns: GridColDef<GridRow>[] = [
        {
            field: 'status',
            headerName: '取り込みの分類',
            minWidth: 160,
            width: 170,
            sortable: false,
            renderCell: ({ row }) => <FlyerStatusCell item={row.item} />,
        },
        {
            field: 'name',
            headerName: '商品名',
            minWidth: 200,
            flex: 1.2,
            sortable: false,
            renderCell: ({ row }) => <ProductNameCell item={row.item} />,
        },
        {
            field: 'allergens',
            headerName: 'アレルゲン表記',
            minWidth: 200,
            flex: 1,
            sortable: false,
            renderCell: ({ row }) => <FlyerAllergenChips item={row.item} />,
        },
        {
            field: 'match',
            headerName: '登録済みの同じ商品',
            minWidth: 240,
            flex: 1,
            sortable: false,
            renderCell: ({ row }) => <MatchCell menuId={menuId} item={row.item} />,
        },
    ];

    // スマホ：分類のチップ・商品名・アレルゲン表記・同じ商品を1つの列に縦に並べる
    const mobileColumns: GridColDef<GridRow>[] = [
        {
            field: 'summary',
            headerName: '取り込みの分類・商品',
            flex: 1,
            minWidth: 200,
            sortable: false,
            renderCell: ({ row }) => (
                <Stack spacing={0.75} sx={{ minWidth: 0, width: '100%' }}>
                    <FlyerStatusCell item={row.item} isSingleLine={false} />
                    <ProductNameCell item={row.item} />
                    <FlyerAllergenChips item={row.item} isSingleLine={false} />
                    <MatchCell menuId={menuId} item={row.item} />
                </Stack>
            ),
        },
    ];

    const columns: GridColDef<GridRow>[] = [toggleColumn, ...(isMobile ? mobileColumns : desktopColumns)];

    // 一覧の Stack は、中身（表の枠の最低の高さ）より小さく縮めない。縮めると、下の「外した掲載品」などが表に重なる
    return (
        <Stack spacing={1} sx={{ flex: { md: '1 0 auto' } }}>
            {labelAbsentItems.length > 0 && (
                <Paper
                    variant="outlined"
                    component="section"
                    aria-labelledby="label-absent-summary-heading"
                    sx={{ flexShrink: 0, px: 2, py: 1.5, display: 'flex', flexDirection: { xs: 'column', sm: 'row' }, gap: 1.5, alignItems: { sm: 'center' } }}
                >
                    <Stack sx={{ flex: 1, minWidth: 0 }}>
                        <Typography id="label-absent-summary-heading" sx={{ fontWeight: 700 }}>
                            アレルゲンの表記がないと読んだ {labelAbsentItems.length}件
                        </Typography>
                        <Typography variant="body2" color={labelAbsentPendingCount > 0 ? 'warning.dark' : 'text.secondary'}>
                            {labelAbsentPendingCount > 0 ? `まだチラシと見比べていない ${labelAbsentPendingCount}件は、まとめて確定に入りません` : 'すべてチラシと見比べました'}
                        </Typography>
                    </Stack>
                    <Button variant="outlined" onClick={() => setIsLabelAbsentDialogOpen(true)} sx={{ flexShrink: 0, minHeight: 44 }}>
                        表記なしの商品をチラシと見比べる
                    </Button>
                </Paper>
            )}
            {reviewIds.length > 0 && (
                <Button
                    variant="outlined"
                    size="large"
                    onClick={() => {
                        setExpandedId(null);
                        setReviewQueue(reviewIds);
                    }}
                    sx={{ alignSelf: { xs: 'stretch', sm: 'flex-start' }, flexShrink: 0 }}
                >
                    未確定を1件ずつ確かめる（再入力・要確認 {reviewIds.length}件）
                </Button>
            )}
            <Typography id="flyer-category-heading" variant="subtitle2" component="h3" sx={{ flexShrink: 0, fontWeight: 700, pt: 0.5 }}>
                取り込みの分類
                <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1, fontWeight: 400 }}>
                    （チラシの商品を今週の商品に入れられるか。献立のアレルゲンの表示とは別のものです）
                </Typography>
            </Typography>
            <Tabs
                value={tab}
                onChange={(_, value: FlyerRowTab) => handleTabChange(value)}
                variant="scrollable"
                allowScrollButtonsMobile
                aria-label="取り込みの分類で絞り込む"
                sx={{ flexShrink: 0, borderBottom: 1, borderColor: 'divider' }}
            >
                {FLYER_ROW_TABS.map((value) => (
                    <Tab key={value} value={value} label={`${flyerTabLabel(value)} ${flyerTabCount(session, value)}`} />
                ))}
            </Tabs>
            <Box ref={wrapperRef} sx={{ flex: { md: '1 1 auto' }, minHeight: { md: 320 }, minWidth: 0 }}>
                <DataGrid
                    apiRef={apiRef}
                    aria-label="読み取った商品"
                    rows={gridRows}
                    columns={columns}
                    // 行の高さは中身に合わせる（チップや一致した商品のアレルゲンの表示が折り返しても切れないように）
                    getRowHeight={() => 'auto'}
                    getEstimatedRowHeight={() => (isMobile ? 160 : 80)}
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
                        '& .MuiDataGrid-cell': { display: 'flex', alignItems: 'center', py: 1, lineHeight: 'normal', minHeight: MIN_ROW_HEIGHT },
                        '& .flyer-expanded-row, & .flyer-expanded-row:hover': { bgcolor: 'action.selected' },
                        '& .flyer-detail-row, & .flyer-detail-row:hover': { bgcolor: 'background.default', cursor: 'auto' },
                        '& .flyer-detail-cell': { display: 'block', p: 0, overflow: 'visible', whiteSpace: 'normal' },
                        '& .flyer-detail-cell:focus, & .flyer-detail-cell:focus-within': { outline: 'none' },
                    }}
                />
            </Box>
            {isLabelAbsentDialogOpen && <FlyerLabelAbsentCompareDialog menuId={menuId} items={labelAbsentItems} isOpen onClose={() => setIsLabelAbsentDialogOpen(false)} />}
            {reviewQueue && <FlyerReviewDialog menuId={menuId} items={items} queue={reviewQueue} isOpen onClose={() => setReviewQueue(null)} />}
        </Stack>
    );
};
