import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useUpdateItem } from '../../../entities/menu';
import type { ItemUpdate, MenuDay, MenuDetail, MenuItem } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';

interface ItemCountDialogProps {
    menu: MenuDetail;
    day: MenuDay;
    item: MenuItem;
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

/**
 * 献立の商品の個数（1人あたりの個数・買うパック数）のダイアログ。商品のカードの「個数設定」から開く（FR-MENU-08a）。
 * 1人あたりの個数を変えると、パック数はサーバーが式で求め直す。パック数も直したときは、その数で上書きする（両方を1回で送る）。
 * パック数の計算と金額はクライアントでしない。開くたびに今の値から始める（呼び出し側で key を替える）。
 */
export const ItemCountDialog = ({ menu, day, item, isOpen, onClose, onSuccess }: ItemCountDialogProps) => {
    const updateItem = useUpdateItem(menu.id);
    const [perChild, setPerChild] = useState(String(item.perChildCount));
    const [packs, setPacks] = useState(String(item.packCount));
    const [error, setError] = useState<string | null>(null);

    const update: ItemUpdate = { itemId: item.id };
    if (perChild !== String(item.perChildCount)) {
        update.perChildCount = Number(perChild);
    }
    if (packs !== String(item.packCount)) {
        update.packCount = Number(packs);
    }
    const hasChanges = update.perChildCount !== undefined || update.packCount !== undefined;

    const handleSave = () => {
        setError(null);
        updateItem.mutate({ update, rowVersion: menu.rowVersion }, { onSuccess, onError: (e) => setError(errorMessageOf(e)) });
    };

    return (
        <Dialog open={isOpen} onClose={updateItem.isPending ? undefined : onClose} maxWidth="xs" fullWidth>
            <DialogTitle>{item.productName}の個数</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2}>
                    {error && <Alert severity="error">{error}</Alert>}
                    <Typography variant="body2" color="text.secondary">
                        この日の人数 {day.childrenCount}人{item.piecesPerPack !== null ? `・1パック ${item.piecesPerPack}個入り` : ''}
                    </Typography>
                    <TextField
                        label="1人あたりの個数"
                        type="number"
                        value={perChild}
                        disabled={updateItem.isPending}
                        onChange={(event) => setPerChild(event.target.value)}
                        helperText="変えると、買うパック数を全員分に足りる数に計算し直します"
                        slotProps={{
                            htmlInput: { min: 1 },
                            input: { endAdornment: <InputAdornment position="end">個</InputAdornment> },
                        }}
                    />
                    <TextField
                        label="買うパック数"
                        type="number"
                        value={packs}
                        disabled={updateItem.isPending}
                        onChange={(event) => setPacks(event.target.value)}
                        helperText="予備を多めに買うときなどに直します（直したときはこの数にします）"
                        slotProps={{
                            htmlInput: { min: 1 },
                            input: { endAdornment: <InputAdornment position="end">パック</InputAdornment> },
                        }}
                    />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose} disabled={updateItem.isPending}>
                    キャンセル
                </Button>
                <Button variant="contained" onClick={handleSave} disabled={!hasChanges || updateItem.isPending}>
                    {updateItem.isPending ? '保存しています…' : '保存する'}
                </Button>
            </DialogActions>
        </Dialog>
    );
};
