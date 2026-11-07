import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { useSnackbar } from 'notistack';
import { useConfirmUsableFlyerItems } from '../../../entities/flyer';
import type { FlyerItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';
import { confirmResultMessages } from '../model/flyerRows';

interface FlyerBulkUsableDialogProps {
    menuId: string;
    /** まとめて確定に並べる商品（サーバーの bulkUsable の商品。開いている行は除いて渡す） */
    items: FlyerItem[];
    /** 表で開いて直している途中の行（まとめから外すよう、サーバーにも伝える） */
    editingItemIds: string[];
    isOpen: boolean;
    onClose: () => void;
}

const detailOf = (item: FlyerItem) =>
    [
        item.read.priceIncludingTax !== null ? `税込 ${item.read.priceIncludingTax}円` : '税込価格なし',
        item.read.orderNo ? `注文番号 ${item.read.orderNo}` : '注文番号なし',
    ].join('・');

/**
 * 「使える」の商品をまとめて確定する前の確かめのダイアログ（SC-09 手順1・09 の 5.2）。
 * 商品名・一致した商品・税込価格・注文番号を並べ、既定ですべてに印を付ける（チラシと違う商品は印を外す）。
 * 確定すると、一致した商品の価格・注文番号を上書きする（おやつの分類・タグは変えない）。対象と条件はサーバーが確かめ直す
 */
export const FlyerBulkUsableDialog = ({ menuId, items, editingItemIds, isOpen, onClose }: FlyerBulkUsableDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const confirmUsable = useConfirmUsableFlyerItems(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [checkedIds, setCheckedIds] = useState<string[]>(() => items.map((item) => item.id));
    const sendIds = checkedIds.filter((id) => items.some((item) => item.id === id));

    const handleToggle = (id: string) => setCheckedIds((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));

    const handleSubmit = () =>
        confirmUsable.mutate(
            { itemIds: sendIds, editingItemIds },
            {
                onSuccess: (result) => {
                    confirmResultMessages(result, `使える商品 ${result.confirmedCount}件を確定しました`).forEach(({ message, variant }) =>
                        enqueueSnackbar(message, { variant }),
                    );
                    onClose();
                },
                onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
            },
        );

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth fullScreen={isMobile} aria-labelledby="bulk-usable-title">
            <DialogTitle id="bulk-usable-title">使える商品をまとめて確定</DialogTitle>
            <DialogContent dividers>
                <Typography variant="body2" sx={{ mb: 1.5 }}>
                    登録済みの商品と同じ名前・メーカーで、避ける品目が入っていない「使える」の商品です。チラシと見比べて、違う商品は印を外し、表の行を開いて1件ずつ確かめてください。
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                    確定すると、登録済みの商品の価格・注文番号をチラシの値にします。献立に使う前のアレルゲンの確かめ方は、今までどおり献立の画面で示します。
                </Typography>
                {items.length === 0 && <Alert severity="info">まとめて確定できる商品はありません。</Alert>}
                {items.length > 0 && (
                    <List aria-label="まとめて確定する商品" sx={{ border: 1, borderColor: 'divider', borderRadius: 1, py: 0 }}>
                        {items.map((item, index) => (
                            <ListItem key={item.id} disablePadding divider={index < items.length - 1}>
                                <ListItemButton onClick={() => handleToggle(item.id)} sx={{ py: 1 }}>
                                    <ListItemIcon sx={{ minWidth: 44 }}>
                                        <Checkbox
                                            edge="start"
                                            checked={checkedIds.includes(item.id)}
                                            tabIndex={-1}
                                            disableRipple
                                            slotProps={{ input: { 'aria-label': item.read.name } }}
                                        />
                                    </ListItemIcon>
                                    <ListItemText
                                        primary={item.read.name}
                                        secondary={
                                            <>
                                                <Typography component="span" variant="body2" sx={{ display: 'block', overflowWrap: 'anywhere' }}>
                                                    登録済みの商品：{item.matchedProduct?.name ?? '—'}
                                                    {item.otherMatchCount > 0 ? `（ほかに同じ名前の商品が ${item.otherMatchCount}件）` : ''}
                                                </Typography>
                                                <Typography component="span" variant="body2" sx={{ display: 'block' }}>
                                                    {detailOf(item)}
                                                </Typography>
                                            </>
                                        }
                                        slotProps={{ primary: { sx: { fontWeight: 700 } }, secondary: { component: 'span' } }}
                                    />
                                </ListItemButton>
                            </ListItem>
                        ))}
                    </List>
                )}
            </DialogContent>
            <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
                <Button onClick={onClose}>閉じる</Button>
                <Button variant="contained" onClick={handleSubmit} disabled={sendIds.length === 0 || confirmUsable.isPending}>
                    確定する（{sendIds.length}件）
                </Button>
            </DialogActions>
        </Dialog>
    );
};
