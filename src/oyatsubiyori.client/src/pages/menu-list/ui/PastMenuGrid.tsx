import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { DataGrid } from '@mui/x-data-grid';
import type { GridColDef } from '@mui/x-data-grid';
import { jaJP } from '@mui/x-data-grid/locales';
import type { MenuListRow } from '../../../entities/menu';
import { formatWeekLabel, formatYen } from '../../../shared/lib';
import { differenceText, hasIssues, VISUALLY_HIDDEN_SX } from './weekDisplay';
import { DraftMenu, IssueChips, WeekStatus } from './WeekParts';

interface PastMenuGridProps {
    rows: MenuListRow[];
    /** 表の名前（読み上げ用） */
    label: string;
    onOpen: (row: MenuListRow) => void;
    onDelete: (row: MenuListRow) => void;
}

/** 過去の献立の表（PC。MUI X の DataGrid）。親の高さいっぱいに広がり、表の中だけをスクロールする */
export const PastMenuGrid = ({ rows, label, onOpen, onDelete }: PastMenuGridProps) => {
    const columns: GridColDef<MenuListRow>[] = [
        {
            field: 'weekStart',
            headerName: '週',
            minWidth: 200,
            flex: 1.2,
            renderCell: ({ row }) => (
                <Typography component="span" sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>
                    {formatWeekLabel(row.weekStart)}
                </Typography>
            ),
        },
        {
            field: 'status',
            headerName: '状態',
            width: 110,
            renderCell: ({ row }) => <WeekStatus row={row} />,
        },
        {
            field: 'total',
            headerName: '週の実績 / 週の予算',
            minWidth: 170,
            flex: 1,
            align: 'right',
            headerAlign: 'right',
            renderCell: ({ row }) =>
                row.menuId ? (
                    <Box component="span" sx={{ whiteSpace: 'nowrap' }}>
                        {formatYen(row.total ?? 0)}{' '}
                        <Box component="span" sx={{ color: 'text.secondary' }}>
                            / {formatYen(row.budget ?? 0)}
                        </Box>
                    </Box>
                ) : (
                    '—'
                ),
        },
        {
            field: 'remaining',
            headerName: '差額',
            width: 110,
            align: 'right',
            headerAlign: 'right',
            renderCell: ({ row }) => (
                <Box component="span" sx={{ whiteSpace: 'nowrap', ...(row.isOverBudget ? { color: 'error.main', fontWeight: 700 } : { color: 'text.secondary' }) }}>
                    {row.remaining === null ? '—' : differenceText(row.remaining)}
                </Box>
            ),
        },
        {
            field: 'issues',
            headerName: '商品の問題',
            minWidth: 220,
            flex: 1.5,
            sortable: false,
            renderCell: ({ row }) =>
                hasIssues(row) ? (
                    <Stack direction="row" spacing={0.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
                        <IssueChips row={row} />
                    </Stack>
                ) : (
                    <Typography variant="body2" component="span" sx={{ color: 'text.secondary' }}>
                        なし
                    </Typography>
                ),
        },
        {
            field: 'actions',
            headerName: '操作',
            width: 130,
            sortable: false,
            align: 'right',
            renderHeader: () => (
                <Box component="span" sx={VISUALLY_HIDDEN_SX}>
                    操作
                </Box>
            ),
            renderCell: ({ row }) => (
                // DataGrid は Space をセルの移動に使い、ボタンが押せなくなる。セルの中のボタンでは Space を DataGrid に渡さない
                <Stack direction="row" sx={{ alignItems: 'center' }} onKeyDown={(event) => event.key === ' ' && event.stopPropagation()}>
                    <Button onClick={() => onOpen(row)} aria-label={`${formatWeekLabel(row.weekStart)}を開く`} sx={{ minHeight: 44, fontWeight: 700 }}>
                        開く
                    </Button>
                    <DraftMenu row={row} onDelete={() => onDelete(row)} />
                </Stack>
            ),
        },
    ];

    return (
        <DataGrid
            aria-label={label}
            rows={rows}
            columns={columns}
            getRowId={(row) => row.weekStart}
            getRowHeight={() => 'auto'}
            getRowClassName={({ row }) => (row.blockedCount > 0 ? 'menu-list-blocked' : '')}
            localeText={jaJP.components.MuiDataGrid.defaultProps.localeText}
            initialState={{ pagination: { paginationModel: { pageSize: 50 } } }}
            pageSizeOptions={[25, 50, 100]}
            disableColumnMenu
            disableRowSelectionOnClick
            sx={{
                bgcolor: 'background.paper',
                '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 700 },
                '& .MuiDataGrid-cell': { display: 'flex', alignItems: 'center', py: 1 },
                // 🔴 の商品がある週は行に色を付ける（バッジの文言もある。色だけに頼らない）
                '& .menu-list-blocked': { bgcolor: (theme) => theme.alpha(theme.palette.error.main, 0.06) },
                '& .menu-list-blocked:hover': { bgcolor: (theme) => theme.alpha(theme.palette.error.main, 0.1) },
            }}
        />
    );
};
