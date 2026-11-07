import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Chip from '@mui/material/Chip';
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
import { useMarkLabelAbsentCompared } from '../../../entities/flyer';
import type { FlyerItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';

interface FlyerLabelAbsentCompareDialogProps {
    menuId: string;
    /** 見比べに並べる商品（サーバーが canMarkLabelAbsentCompared にした商品） */
    items: FlyerItem[];
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 「表記なし」と読んだ商品を、まとめてチラシと見比べるダイアログ（SC-09 手順1・FR-FLYER-04a・D-97）。
 * 商品名と表記の原文を全件並べ、職員がチラシで1件ずつ探して、見比べた商品に印を付ける（すべてに印を付ける近道は作らない）。
 * 印はサーバーの取り込みのメモリにだけ残り、まとめて確定に入れてよいかの判断にだけ使う（記録には使わない）。
 */
export const FlyerLabelAbsentCompareDialog = ({ menuId, items, isOpen, onClose }: FlyerLabelAbsentCompareDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const markCompared = useMarkLabelAbsentCompared(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [checkedIds, setCheckedIds] = useState<string[]>([]);
    const pending = items.filter((item) => !item.isLabelAbsentCompared);
    const compared = items.filter((item) => item.isLabelAbsentCompared);

    const handleToggle = (id: string) => setCheckedIds((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id]));

    const handleSubmit = () =>
        markCompared.mutate(checkedIds, {
            onSuccess: (count) => {
                enqueueSnackbar(`${count}件を「チラシで見比べた」にしました`, { variant: 'success' });
                setCheckedIds([]);
                onClose();
            },
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    const labelTextOf = (item: FlyerItem) => {
        const text = item.read.allergenLabelText?.trim();
        return text ? `表記の読み取り：「${text}」` : '表記の読み取り：なし';
    };

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth fullScreen={isMobile} aria-labelledby="label-absent-compare-title">
            <DialogTitle id="label-absent-compare-title">表記なしの商品をチラシと見比べる</DialogTitle>
            <DialogContent dividers>
                <Typography variant="body2" sx={{ mb: 1.5 }}>
                    アレルゲンの表記がないと読んだ商品です。読み落としのことがあるので、チラシで1件ずつ商品を探し、アレルゲンの表記がないことを確かめてから印を付けてください。表記があった商品は、印を付けずに表の行を開いて「品目を足す」で足してください。
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                    献立に使う前に、パッケージでの確認が必要なことは変わりません。
                </Typography>
                {pending.length === 0 && <Alert severity="success">すべて見比べ済みです。</Alert>}
                {pending.length > 0 && (
                    <List aria-label="まだ見比べていない商品" sx={{ border: 1, borderColor: 'divider', borderRadius: 1, py: 0 }}>
                        {pending.map((item, index) => {
                            const isChecked = checkedIds.includes(item.id);
                            const labelId = `label-absent-${item.id}`;
                            return (
                                <ListItem key={item.id} disablePadding divider={index < pending.length - 1}>
                                    <ListItemButton onClick={() => handleToggle(item.id)} sx={{ py: 1 }}>
                                        <ListItemIcon sx={{ minWidth: 44 }}>
                                            <Checkbox edge="start" checked={isChecked} tabIndex={-1} disableRipple slotProps={{ input: { 'aria-labelledby': labelId } }} />
                                        </ListItemIcon>
                                        <ListItemText
                                            id={labelId}
                                            primary={item.read.name}
                                            secondary={[item.read.volume, item.read.orderNo ? `注文番号 ${item.read.orderNo}` : null, labelTextOf(item)].filter(Boolean).join('・')}
                                            slotProps={{ primary: { sx: { fontWeight: 700 } } }}
                                        />
                                    </ListItemButton>
                                </ListItem>
                            );
                        })}
                    </List>
                )}
                {compared.length > 0 && (
                    <>
                        <Typography variant="subtitle2" component="h3" sx={{ mt: 2, mb: 0.5 }}>
                            見比べ済み {compared.length}件
                        </Typography>
                        <List aria-label="見比べ済みの商品" dense sx={{ border: 1, borderColor: 'divider', borderRadius: 1, py: 0 }}>
                            {compared.map((item, index) => (
                                <ListItem key={item.id} divider={index < compared.length - 1} secondaryAction={<Chip size="small" color="success" variant="outlined" label="見比べ済み" />}>
                                    <ListItemText primary={item.read.name} secondary={labelTextOf(item)} sx={{ pr: 10 }} />
                                </ListItem>
                            ))}
                        </List>
                    </>
                )}
            </DialogContent>
            <DialogActions sx={{ flexWrap: 'wrap', gap: 1 }}>
                <Button onClick={onClose}>閉じる</Button>
                <Button variant="contained" onClick={handleSubmit} disabled={checkedIds.length === 0 || markCompared.isPending}>
                    チラシで見比べました（{checkedIds.length}件）
                </Button>
            </DialogActions>
        </Dialog>
    );
};
