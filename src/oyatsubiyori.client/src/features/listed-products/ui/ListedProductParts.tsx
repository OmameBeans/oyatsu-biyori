import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useSetExcluded, useSetListedValues } from '../../../entities/menu';
import type { ListedProduct } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';

interface ExcludeSwitchProps {
    menuId: string;
    item: ListedProduct;
}

/** 「今週使う」（既定はオン）。切り替えるたびに保存する（FR-MENU-07a）。サーバーには「今週は使わない」として送る */
export const ExcludeSwitch = ({ menuId, item }: ExcludeSwitchProps) => {
    const setExcluded = useSetExcluded(menuId);
    const { enqueueSnackbar } = useSnackbar();
    return (
        <Switch
            checked={!item.isExcludedThisWeek}
            disabled={setExcluded.isPending}
            slotProps={{ input: { 'aria-label': '今週使う' } }}
            onChange={(event) =>
                setExcluded.mutate(
                    { productId: item.productId, excluded: !event.target.checked },
                    { onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }) },
                )
            }
        />
    );
};

interface MissingValuesFormProps {
    menuId: string;
    item: ListedProduct;
}

/** 税込価格・入数が入っていない商品に、その場で入れる欄 */
export const MissingValuesForm = ({ menuId, item }: MissingValuesFormProps) => {
    const setValues = useSetListedValues(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [price, setPrice] = useState(item.price?.toString() ?? '');
    const [pieces, setPieces] = useState(item.piecesPerPack?.toString() ?? '');

    const handleSave = () => {
        setValues.mutate(
            { productId: item.productId, price: item.price === null && price ? Number(price) : null, piecesPerPack: item.piecesPerPack === null && pieces ? Number(pieces) : null },
            {
                onSuccess: (note) => enqueueSnackbar(note ?? '保存しました', { variant: 'success' }),
                onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
            },
        );
    };

    return (
        // 表の中に置いたとき、文字の入力やスペースのキーを表の操作に取られないようにする
        <Alert severity="warning" sx={{ mt: 1 }} onKeyDown={(event) => event.stopPropagation()}>
            <Typography variant="body2" sx={{ mb: 1 }}>
                {item.price === null ? '税込価格' : ''}
                {item.price === null && item.piecesPerPack === null ? 'と' : ''}
                {item.piecesPerPack === null ? '入数' : ''}を入れてください（入れるまで献立に入れられません）
            </Typography>
            <Stack direction="row" spacing={1}>
                {item.price === null && <TextField label="税込価格（円）" type="number" size="small" value={price} onChange={(event) => setPrice(event.target.value)} sx={{ width: 140 }} />}
                {item.piecesPerPack === null && <TextField label="入数" type="number" size="small" value={pieces} onChange={(event) => setPieces(event.target.value)} sx={{ width: 100 }} />}
                <Button onClick={handleSave} disabled={setValues.isPending}>
                    保存
                </Button>
            </Stack>
        </Alert>
    );
};
