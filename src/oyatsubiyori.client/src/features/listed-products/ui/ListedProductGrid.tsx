import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { DataGrid } from '@mui/x-data-grid';
import type { GridColDef } from '@mui/x-data-grid';
import { jaJP } from '@mui/x-data-grid/locales';
import { AllergenJudgementLine } from '../../../entities/allergen-judgement';
import type { JudgementCategory } from '../../../entities/allergen-judgement';
import type { ListedProduct } from '../../../entities/menu';
import { formatYen } from '../../../shared/lib';
import { allergenCheckPath, SNACK_TYPE_LABEL } from '../../../shared/type';
import { isMissingValues } from '../model/missingValues';
import { ExcludeSwitch, MissingValuesForm } from './ListedProductParts';

/** 並べ替えの順（使えません → 要確認 → 使えます）。判定は変えず、API の区分を並べるだけ */
const CATEGORY_ORDER: Record<JudgementCategory, number> = { Blocked: 0, Warning: 1, Safe: 2 };

const NoneText = ({ label }: { label: string }) => (
    <Typography variant="body2" component="span" sx={{ color: 'text.secondary' }}>
        {label}
    </Typography>
);

interface ListedProductGridProps {
    menuId: string;
    items: ListedProduct[];
}

/** 今週の商品の表（PC。MUI X の DataGrid）。親の高さいっぱいに広がり、表の中だけをスクロールする */
export const ListedProductGrid = ({ menuId, items }: ListedProductGridProps) => {
    const columns: GridColDef<ListedProduct>[] = [
        {
            field: 'name',
            headerName: '商品名',
            minWidth: 240,
            flex: 1.6,
            renderCell: ({ row }) => (
                <Box sx={{ minWidth: 0, width: '100%' }}>
                    <Typography sx={{ fontWeight: 700 }}>{row.name}</Typography>
                    {(row.tags.length > 0 || row.isDisabled) && (
                        <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', mt: 0.5 }}>
                            {row.tags.map((tag) => (
                                <Chip key={`${tag.kind}-${tag.value}`} size="small" variant="outlined" label={tag.value} />
                            ))}
                            {row.isDisabled && <Chip size="small" variant="outlined" label="「使わない」にした商品" />}
                        </Stack>
                    )}
                    {isMissingValues(row) && <MissingValuesForm menuId={menuId} item={row} />}
                </Box>
            ),
        },
        {
            field: 'isExcludedThisWeek',
            headerName: '今週使う',
            width: 100,
            align: 'center',
            headerAlign: 'center',
            renderCell: ({ row }) => <ExcludeSwitch menuId={menuId} item={row} />,
        },
        {
            field: 'snackType',
            headerName: '分類',
            width: 130,
            valueGetter: (_value, row) => SNACK_TYPE_LABEL[row.snackType],
            renderCell: ({ value }) => <Chip size="small" label={value} />,
        },
        {
            field: 'price',
            headerName: '税込価格',
            type: 'number',
            width: 100,
            renderCell: ({ row }) => (row.price !== null ? formatYen(row.price) : <NoneText label="なし" />),
        },
        {
            field: 'piecesPerPack',
            headerName: '人分',
            type: 'number',
            width: 80,
            renderCell: ({ row }) => (row.piecesPerPack !== null ? `${row.piecesPerPack}人分` : <NoneText label="なし" />),
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
            minWidth: 340,
            flex: 3,
            valueGetter: (_value, row) => CATEGORY_ORDER[row.judgement.category],
            renderCell: ({ row }) => (
                <Box sx={{ minWidth: 0 }}>
                    <AllergenJudgementLine judgement={row.judgement} />
                    {row.judgement.category !== 'Safe' && (
                        <Button component={RouterLink} to={allergenCheckPath(row.productId)} size="small" sx={{ mt: 0.5 }}>
                            パッケージで確認する
                        </Button>
                    )}
                </Box>
            ),
        },
        {
            field: 'daysSinceServed',
            headerName: '前に出した日',
            type: 'number',
            width: 110,
            renderCell: ({ row }) =>
                row.daysSinceServed !== null ? (
                    <Typography variant="body2" component="span" sx={{ color: 'warning.dark' }}>
                        {row.daysSinceServed}日前
                    </Typography>
                ) : (
                    <NoneText label="—" />
                ),
        },
    ];

    return (
        <DataGrid
            aria-label="今週の商品"
            rows={items}
            columns={columns}
            getRowId={(row) => row.productId}
            getRowHeight={() => 'auto'}
            getRowClassName={({ row }) => (row.isExcludedThisWeek ? 'listed-excluded' : '')}
            localeText={jaJP.components.MuiDataGrid.defaultProps.localeText}
            // 無料版の DataGrid は1ページ100件まで。チラシ1枚分は普通それより少ないので、そのときはページ送りを出さない
            initialState={{ pagination: { paginationModel: { pageSize: 100 } } }}
            pageSizeOptions={[100]}
            hideFooter={items.length <= 100}
            disableColumnMenu
            disableRowSelectionOnClick
            // 全部の行を描いておき、ブラウザのページ内検索でも見つかるようにする
            disableVirtualization
            sx={{
                bgcolor: 'background.paper',
                '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 700 },
                '& .MuiDataGrid-cell': { display: 'flex', alignItems: 'center', py: 1 },
                '& .listed-excluded': { opacity: 0.6 },
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
