import { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { useSnackbar } from 'notistack';
import type { FlyerItem } from '../../../entities/flyer';
import {
    allergenChipsOf,
    checkPointsOf,
    FLYER_ROW_STATUS,
    FLYER_ROW_STATUS_COLOR,
    FLYER_ROW_STATUS_LABEL,
    flyerRowStatusOf,
    sameProductSummary,
} from '../model/flyerRows';
import { FlyerConfirmForm } from './FlyerConfirmForm';

interface FlyerReviewDialogProps {
    menuId: string;
    /** 読み取った商品（読み直すたびに新しくなる。確定したかどうかはここから見る） */
    items: FlyerItem[];
    /** 確かめる順（開いたときの未確定の商品。表と同じ、状態の順） */
    queue: string[];
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 未確定の商品を1件ずつ確かめて確定するダイアログ（読み取った商品の表の「未確定を1件ずつ確かめる」から開く）。
 * 決めるのは、ボタン（確定する）を押したときだけ（スワイプなどでうっかり確定しないように）。
 * 確定すると、次の未確定の商品へ進む。「あとで」で飛ばした商品は、最後に残りとして示す。
 */
export const FlyerReviewDialog = ({ menuId, items, queue, isOpen, onClose }: FlyerReviewDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const { enqueueSnackbar } = useSnackbar();
    const [currentId, setCurrentId] = useState<string | null>(queue[0] ?? null);

    const itemOf = (id: string) => items.find((item) => item.id === id);
    const isConfirmed = (id: string) => !!itemOf(id)?.confirmation;
    const current = currentId ? itemOf(currentId) : undefined;
    const index = currentId ? queue.indexOf(currentId) : -1;
    const confirmedCount = queue.filter(isConfirmed).length;
    const remainingCount = queue.length - confirmedCount;

    /** 今の商品より後ろの未確定の商品。なければ前から探す。どれもなければ null（終わり） */
    const nextUnconfirmed = (fromId: string) => {
        const from = queue.indexOf(fromId);
        const isOpenable = (id: string) => id !== fromId && !isConfirmed(id);
        return queue.slice(from + 1).find(isOpenable) ?? queue.slice(0, from).find(isOpenable) ?? null;
    };

    const handleConfirmed = (item: FlyerItem) => {
        enqueueSnackbar(`「${item.read.name}」を確定しました`, { variant: 'success' });
        setCurrentId(nextUnconfirmed(item.id));
    };

    // 「あとで」は、確定済みかどうかに関係なく次の商品へ（最後まで行ったら、残りを示す）
    const handleLater = () => setCurrentId(index >= 0 && index < queue.length - 1 ? queue[index + 1] : null);
    const handlePrevious = () => setCurrentId(index > 0 ? queue[index - 1] : currentId);
    const handleRestart = () => setCurrentId(queue.find((id) => !isConfirmed(id)) ?? null);

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} maxWidth="md" fullWidth aria-labelledby="flyer-review-title">
            <DialogTitle id="flyer-review-title" sx={{ pb: 1 }}>
                未確定の商品を1件ずつ確かめる
                <Typography variant="body2" color="text.secondary">
                    {current ? `${index + 1} / ${queue.length}件目` : '終わり'}（確定 {confirmedCount}件・残り {remainingCount}件）
                </Typography>
            </DialogTitle>
            <LinearProgress variant="determinate" value={queue.length > 0 ? (confirmedCount / queue.length) * 100 : 100} aria-label="確定した割合" />
            <DialogContent dividers>
                {current ? (
                    <Stack spacing={2}>
                        <ReviewItemSummary item={current} />
                        <FlyerConfirmForm key={current.id} menuId={menuId} item={current} closeLabel="表に戻る" onClose={onClose} onSuccess={() => handleConfirmed(current)} />
                    </Stack>
                ) : (
                    <Stack spacing={2} sx={{ alignItems: 'flex-start', py: 2 }}>
                        <Typography variant="h3" component="p">
                            {remainingCount === 0 ? 'すべて確定しました' : `「あとで」にした商品が ${remainingCount}件 残っています`}
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                            {remainingCount === 0 ? '表に戻って、確定した内容を見直せます。' : '確定していない商品は、今週の商品に入りません。'}
                        </Typography>
                        {remainingCount > 0 && (
                            <Button variant="contained" onClick={handleRestart}>
                                残りを確かめる
                            </Button>
                        )}
                    </Stack>
                )}
            </DialogContent>
            <DialogActions sx={{ justifyContent: 'space-between' }}>
                <Stack direction="row" spacing={1}>
                    <Button onClick={handlePrevious} disabled={!current || index <= 0}>
                        前へ
                    </Button>
                    <Button onClick={handleLater} disabled={!current}>
                        あとで（次へ）
                    </Button>
                </Stack>
                {!current && <Button onClick={onClose}>表に戻る</Button>}
            </DialogActions>
        </Dialog>
    );
};

interface ReviewItemSummaryProps {
    item: FlyerItem;
}

/** 1件ずつ確かめるときの、商品の見出し（表の1行と同じ内容を縦に並べる） */
const ReviewItemSummary = ({ item }: ReviewItemSummaryProps) => {
    const read = item.read;
    const status = flyerRowStatusOf(item);
    const summary = sameProductSummary(item);
    const reasons = Array.from(new Set(checkPointsOf(item).map((point) => point.short)));
    const labelText = read.allergenLabelText?.trim();
    return (
        <Box>
            <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <Chip size="small" color={FLYER_ROW_STATUS_COLOR[status]} variant={status === FLYER_ROW_STATUS.UNCONFIRMED ? 'outlined' : 'filled'} label={FLYER_ROW_STATUS_LABEL[status]} />
                {reasons.length > 0 && (
                    <Typography variant="body2" color="text.secondary">
                        {reasons.join('・')}
                    </Typography>
                )}
            </Stack>
            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mt: 1 }}>
                <Typography variant="h3" component="h3">
                    {read.name}
                </Typography>
                {item.isManual && <Chip size="small" label="手入力" />}
            </Stack>
            <Typography variant="body2" color="text.secondary">
                {[read.volume, read.priceIncludingTax ? `税込 ${read.priceIncludingTax}円` : '税込価格なし'].filter(Boolean).join('・')}
            </Typography>
            <Typography variant="body2" sx={{ mt: 0.5 }}>
                同じ商品か：{summary.title}
                {summary.detail ? `（${summary.detail}）` : ''}
            </Typography>
            <Stack direction="row" spacing={0.5} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mt: 0.5 }}>
                <Typography variant="body2">アレルゲン表記：{labelText ? `「${labelText}」` : ''}</Typography>
                {allergenChipsOf(item).map((chip) => (
                    <Chip key={chip.key} size="small" color={chip.color} variant={chip.variant} label={chip.label} sx={chip.isDashed ? { borderStyle: 'dashed' } : undefined} />
                ))}
            </Stack>
        </Box>
    );
};
