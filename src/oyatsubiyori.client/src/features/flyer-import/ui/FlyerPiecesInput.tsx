import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useSnackbar } from 'notistack';
import { useEnterFlyerPieces } from '../../../entities/flyer';
import type { FlyerItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';

interface FlyerPiecesInputProps {
    menuId: string;
    item: FlyerItem;
}

/**
 * 使えない（入数が分からない）の行の入数の欄（FR-IMPORT-11）。入れると、読み取った内容のままサーバーが分類し直す
 * （取り込めるになれば、「取り込みを終える」で取り込む）。表のセルの中に置くので、キー・クリックは表に渡さない
 */
export const FlyerPiecesInput = ({ menuId, item }: FlyerPiecesInputProps) => {
    const enterPieces = useEnterFlyerPieces(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [pieces, setPieces] = useState('');
    // 入数は1以上の整数だけ（「1.5」などは送らない。範囲の確かめはサーバーでもする）
    const piecesValue = Number(pieces);
    const isPiecesValid = pieces.trim() !== '' && Number.isInteger(piecesValue) && piecesValue >= 1;

    const handleSubmit = () =>
        enterPieces.mutate(
            { itemId: item.id, piecesPerPack: piecesValue },
            {
                onSuccess: () => {
                    setPieces('');
                    enqueueSnackbar(`「${item.read.name}」の入数を入れて、分類し直しました`, { variant: 'success' });
                },
            },
        );

    return (
        <Box
            onClick={(event) => event.stopPropagation()}
            onMouseDown={(event) => event.stopPropagation()}
            onKeyDown={(event) => event.stopPropagation()}
            sx={{ minWidth: 0 }}
        >
            <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
                <TextField
                    size="small"
                    type="number"
                    label="入数"
                    value={pieces}
                    onChange={(event) => setPieces(event.target.value)}
                    slotProps={{ htmlInput: { min: 1, step: 1, 'aria-label': `「${item.read.name}」の入数` } }}
                    error={pieces.trim() !== '' && !isPiecesValid}
                    helperText={pieces.trim() !== '' && !isPiecesValid ? '1以上の整数' : undefined}
                    sx={{ width: 88, flexShrink: 0 }}
                />
                <Button variant="outlined" disabled={!isPiecesValid || enterPieces.isPending} onClick={handleSubmit} sx={{ minHeight: 40, whiteSpace: 'nowrap' }}>
                    分類し直す
                </Button>
            </Stack>
            {enterPieces.isError && (
                <Alert severity="error" sx={{ mt: 0.5 }}>
                    {errorMessageOf(enterPieces.error)}
                </Alert>
            )}
        </Box>
    );
};
