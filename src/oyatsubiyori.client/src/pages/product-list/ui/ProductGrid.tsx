import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { DataGrid } from '@mui/x-data-grid';
import type { GridColDef } from '@mui/x-data-grid';
import { jaJP } from '@mui/x-data-grid/locales';
import { AllergenJudgementLine } from '../../../entities/allergen-judgement';
import type { JudgementCategory } from '../../../entities/allergen-judgement';
import type { ProductListItem } from '../../../entities/product';
import { productPath, SNACK_TYPE_LABEL } from '../../../shared/type';

/** 並べ替えの順（使えません → 要確認 → 使えます）。判定は変えず、API の区分を並べるだけ */
const CATEGORY_ORDER: Record<JudgementCategory, number> = { Blocked: 0, Warning: 1, Safe: 2 };

const formatDate = (value: string | null) => (value ? value.replace(/-/g, '/') : null);

interface ProductGridProps {
    items: ProductListItem[];
}

/** 商品一覧の表（PC。MUI X の DataGrid）。親の高さいっぱいに広がり、表の中だけをスクロールする */
export const ProductGrid = ({ items }: ProductGridProps) => {
    const navigate = useNavigate();
    const columns: GridColDef<ProductListItem>[] = [
        {
            field: 'name',
            headerName: '商品名',
            minWidth: 220,
            flex: 1.4,
            renderCell: ({ row }) => (
                <Stack spacing={0.5} sx={{ minWidth: 0 }}>
                    <Link component={RouterLink} to={productPath(row.id)} onClick={(event) => event.stopPropagation()} underline="hover" sx={{ fontWeight: 700, color: 'text.primary' }}>
                        {row.name}
                    </Link>
                    {(row.isDisabled || row.isUsedInFutureMenus) && (
                        <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
                            {row.isDisabled && <Chip size="small" variant="outlined" label="使わない" />}
                            {row.isUsedInFutureMenus && <Chip size="small" color="primary" variant="outlined" label="今日以降の献立で使う" />}
                        </Stack>
                    )}
                </Stack>
            ),
        },
        {
            field: 'snackType',
            headerName: '種類',
            width: 130,
            valueGetter: (_value, row) => SNACK_TYPE_LABEL[row.snackType],
            renderCell: ({ value }) => <Chip size="small" label={value} />,
        },
        {
            field: 'maker',
            headerName: 'メーカー',
            minWidth: 120,
            flex: 0.6,
            valueGetter: (_value, row) => row.maker ?? '',
        },
        {
            field: 'volume',
            headerName: '内容量',
            minWidth: 110,
            flex: 0.5,
            valueGetter: (_value, row) => row.volume ?? '',
        },
        {
            field: 'judgement',
            headerName: 'アレルゲンの表示',
            minWidth: 360,
            flex: 3,
            valueGetter: (_value, row) => CATEGORY_ORDER[row.judgement.category],
            renderCell: ({ row }) => <AllergenJudgementLine judgement={row.judgement} />,
        },
        {
            field: 'lastCheckedDate',
            headerName: '最終確認',
            width: 120,
            renderCell: ({ row }) =>
                formatDate(row.lastCheckedDate) ?? (
                    <Typography variant="body2" component="span" sx={{ color: 'text.secondary' }}>
                        記録なし
                    </Typography>
                ),
        },
    ];

    return (
        <DataGrid
            aria-label="商品一覧"
            rows={items}
            columns={columns}
            getRowHeight={() => 'auto'}
            getRowClassName={({ row }) => (row.isDisabled ? 'product-disabled' : '')}
            onRowClick={({ row }) => navigate(productPath(row.id))}
            localeText={jaJP.components.MuiDataGrid.defaultProps.localeText}
            initialState={{ pagination: { paginationModel: { pageSize: 100 } } }}
            pageSizeOptions={[25, 50, 100]}
            disableColumnMenu
            disableRowSelectionOnClick
            // 商品は数百件まで。全部の行を描いておき、ブラウザのページ内検索でも見つかるようにする
            disableVirtualization
            sx={{
                bgcolor: 'background.paper',
                '& .MuiDataGrid-row': { cursor: 'pointer' },
                '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 700 },
                '& .MuiDataGrid-cell': { display: 'flex', alignItems: 'center', py: 1 },
                '& .product-disabled': { opacity: 0.65 },
            }}
            slots={{
                noRowsOverlay: () => (
                    <Box sx={{ display: 'flex', height: '100%', alignItems: 'center', justifyContent: 'center' }}>
                        <Typography sx={{ color: 'text.secondary' }}>当てはまる商品はありません。</Typography>
                    </Box>
                ),
            }}
        />
    );
};
