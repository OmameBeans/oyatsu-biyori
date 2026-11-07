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
import { useConfirmNewFlyerItems } from '../../../entities/flyer';
import type { FlyerItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';
import { SNACK_TYPE_LABEL } from '../../../shared/type';
import { allergenChipsOf, confirmResultMessages } from '../model/flyerRows';

interface FlyerBulkNewDialogProps {
    menuId: string;
    /** まとめて登録に並べる商品（サーバーの bulkNew の商品。開いている行は除いて渡す） */
    items: FlyerItem[];
    /** 表で開いて直している途中の行（まとめから外すよう、サーバーにも伝える） */
    editingItemIds: string[];
    isOpen: boolean;
    onClose: () => void;
}

/** 表記の原文と、補正した品目（麦→小麦。表記なしの行は、チラシで見比べ済みのものだけがここに来る） */
const labelSummaryOf = (item: FlyerItem) => {
    if (item.isLabelAbsent) {
        return 'アレルゲン表記：なし（チラシで見比べ済み）';
    }
    const text = item.read.allergenLabelText?.trim() ?? '';
    const names = allergenChipsOf(item)
        .filter((chip) => chip.key.startsWith('label-') || chip.key.startsWith('resolved-'))
        .map((chip) => chip.label)
        .join('・');
    return `アレルゲン表記：「${text}」→ ${names || '品目なし'}`;
};

const detailOf = (item: FlyerItem) =>
    [
        item.read.priceIncludingTax !== null ? `税込 ${item.read.priceIncludingTax}円` : null,
        item.read.volume,
        item.piecesPerPack !== null ? `入数 ${item.piecesPerPack}` : null,
        item.read.snackType ? SNACK_TYPE_LABEL[item.read.snackType] : null,
    ]
        .filter(Boolean)
        .join('・');

/**
 * 「要確認」の商品を、新しい商品としてまとめて登録するダイアログ（SC-09 手順1・09 の 5.2・D-98）。
 * 対象はサーバーが決め、登録のときにもサーバーが分類と条件を確かめ直す（外れた商品は登録しない）。
 * アレルゲンはチラシの表記で「参考」の記録にする（パッケージでの確認は献立の前に要る）。
 * 件数が多いので、既定ですべてに印を付け、チラシと違う商品の印を外せる。
 */
export const FlyerBulkNewDialog = ({ menuId, items, editingItemIds, isOpen, onClose }: FlyerBulkNewDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const confirmNew = useConfirmNewFlyerItems(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [checkedIds, setCheckedIds] = useState<string[]>(() => items.map((item) => item.id));
    // 開いている間に表が読み直されても、外れた商品の印は送らない（送ってもサーバーが確かめ直す）
    const sendIds = checkedIds.filter((id) => items.some((item) => item.id === id));

    const handleToggle = (id: string) => setCheckedIds((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));

    const handleSubmit = () =>
        confirmNew.mutate(
            { itemIds: sendIds, editingItemIds },
            {
                onSuccess: (result) => {
                    confirmResultMessages(result, `${result.confirmedCount}件を新しい商品として登録しました`).forEach(({ message, variant }) =>
                        enqueueSnackbar(message, { variant }),
                    );
                    onClose();
                },
                onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
            },
        );

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth fullScreen={isMobile} aria-labelledby="bulk-new-title">
            <DialogTitle id="bulk-new-title">新しい商品をまとめて登録</DialogTitle>
            <DialogContent dividers>
                <Typography variant="body2" sx={{ mb: 1.5 }}>
                    登録済みの商品に同じ名前・メーカーの商品がない「要確認」の商品です。チラシと見比べて、内容が違う商品は印を外し、表の行を開いて1件ずつ確かめてください。
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                    アレルゲンはチラシの表記として記録します。献立に使う前に、パッケージでの確認が必要なことは変わりません。
                </Typography>
                {items.length === 0 && <Alert severity="info">まとめて登録できる商品はありません。</Alert>}
                {items.length > 0 && (
                    <List aria-label="まとめて登録する商品" sx={{ border: 1, borderColor: 'divider', borderRadius: 1, py: 0 }}>
                        {items.map((item, index) => {
                            const isChecked = checkedIds.includes(item.id);
                            return (
                                <ListItem key={item.id} disablePadding divider={index < items.length - 1}>
                                    <ListItemButton onClick={() => handleToggle(item.id)} sx={{ py: 1 }}>
                                        <ListItemIcon sx={{ minWidth: 44 }}>
                                            <Checkbox edge="start" checked={isChecked} tabIndex={-1} disableRipple slotProps={{ input: { 'aria-label': item.read.name } }} />
                                        </ListItemIcon>
                                        <ListItemText
                                            primary={item.read.name}
                                            secondary={
                                                <>
                                                    <Typography component="span" variant="body2" sx={{ display: 'block' }}>
                                                        {detailOf(item)}
                                                    </Typography>
                                                    <Typography component="span" variant="body2" sx={{ display: 'block', overflowWrap: 'anywhere' }}>
                                                        {labelSummaryOf(item)}
                                                    </Typography>
                                                </>
                                            }
                                            slotProps={{ primary: { sx: { fontWeight: 700 } }, secondary: { component: 'span' } }}
                                        />
                                    </ListItemButton>
                                </ListItem>
                            );
                        })}
                    </List>
                )}
            </DialogContent>
            <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
                <Button onClick={onClose}>閉じる</Button>
                <Button variant="contained" onClick={handleSubmit} disabled={sendIds.length === 0 || confirmNew.isPending}>
                    登録する（{sendIds.length}件）
                </Button>
            </DialogActions>
        </Dialog>
    );
};
