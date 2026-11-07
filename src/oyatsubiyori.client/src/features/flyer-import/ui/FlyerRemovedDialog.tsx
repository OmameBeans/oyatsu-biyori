import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useRestoreFlyerItem } from '../../../entities/flyer';
import type { FlyerRemovedItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';
import { ClaudeText } from '../../../shared/ui';

interface FlyerRemovedDialogProps {
    menuId: string;
    items: FlyerRemovedItem[];
    isOpen: boolean;
    onClose: () => void;
}

/**
 * SC-09 手順1 外した掲載品（09 の 3.7）。食品以外として読み取りで外したものと、職員が「外す」で外したもの。
 * 黙って消さずに商品名と理由を並べ、「戻す」で一覧に戻せる（戻すとサーバーが分類し直す）。今週の商品には入らない。
 * 表の中だけをスクロールさせるため、表の下には置かず、ボタンから開くダイアログにする
 */
export const FlyerRemovedDialog = ({ menuId, items, isOpen, onClose }: FlyerRemovedDialogProps) => {
    const restore = useRestoreFlyerItem(menuId);
    const { enqueueSnackbar } = useSnackbar();

    const handleRestore = (item: FlyerRemovedItem) =>
        restore.mutate(item.id, {
            onSuccess: () => {
                enqueueSnackbar(`「${item.name}」を一覧に戻しました`, { variant: 'success' });
                // 最後の1件を戻したら、空のダイアログを残さない
                if (items.length <= 1) {
                    onClose();
                }
            },
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth aria-labelledby="flyer-removed-heading">
            <DialogTitle id="flyer-removed-heading">外した掲載品（{items.length}件）</DialogTitle>
            <DialogContent dividers>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                    食品以外の掲載品と、「外す」で外した商品です。今週の商品には入りません。おやつに使うときは「戻す」を押してください。
                </Typography>
                <Stack divider={<Box sx={{ borderTop: 1, borderColor: 'divider' }} />}>
                    {items.map((item) => (
                        <Stack key={item.id} direction="row" spacing={1} sx={{ alignItems: 'center', py: 1 }}>
                            <Box sx={{ flex: 1, minWidth: 0 }}>
                                <Typography sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>{item.name || '（商品名なし）'}</Typography>
                                {(item.maker || item.volume) && (
                                    <Typography variant="body2" color="text.secondary">
                                        {[item.maker, item.volume].filter(Boolean).join('・')}
                                    </Typography>
                                )}
                                {/* 食品以外の理由は Claude の文なので、テキストとしてだけ出す */}
                                {item.isNonFood ? <ClaudeText text={item.reason} color="text.secondary" /> : <Typography variant="body2" color="text.secondary">{item.reason}</Typography>}
                                {item.discrepancyAllergens.length > 0 && (
                                    <Typography variant="body2" sx={{ color: 'warning.dark' }}>
                                        チラシの表記が、パッケージでの確認と食い違っています（{item.discrepancyAllergens.join('・')}）。取り込みを終えると、商品一覧に「参考」の記録を足します。読み違いなら「戻す」で表記を直してください
                                    </Typography>
                                )}
                            </Box>
                            <Button
                                onClick={() => handleRestore(item)}
                                disabled={restore.isPending}
                                aria-label={`「${item.name}」を戻す`}
                                sx={{ flexShrink: 0, minHeight: 44 }}
                            >
                                戻す
                            </Button>
                        </Stack>
                    ))}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>閉じる</Button>
            </DialogActions>
        </Dialog>
    );
};
