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
import type { FlyerItem } from '../../../entities/flyer';
import { isReviewTarget, matchSummaryOf } from '../model/flyerRows';
import { FlyerItemForm } from './FlyerItemForm';
import { FlyerAllergenChips, FlyerStatusCell } from './FlyerStatusChip';

interface FlyerReviewDialogProps {
    menuId: string;
    /** 読み取った商品（読み直すたびに新しくなる。まだ再入力かどうかはここから見る） */
    items: FlyerItem[];
    /** 直す順（開いたときの再入力の商品。表と同じ並び） */
    queue: string[];
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 再入力の商品を1件ずつ直すダイアログ（読み取った商品の表の「再入力を1件ずつ直す」から開く。09 の 6。D-101）。
 * 直して分類し直し、再入力でなくなった（取り込める・使えないになった）か外すと、次の商品へ進む。
 * 「あとで」で飛ばした商品は、最後に残りとして示す。取り込みは、表の上の「取り込みを終える」でまとめて行う。
 */
export const FlyerReviewDialog = ({ menuId, items, queue, isOpen, onClose }: FlyerReviewDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const [currentId, setCurrentId] = useState<string | null>(queue[0] ?? null);
    // フォームのボタンをフッターに出すための場所
    const [actionsEl, setActionsEl] = useState<HTMLElement | null>(null);

    const itemOf = (id: string) => items.find((item) => item.id === id);
    // 再入力でなくなった・外した（一覧にない）商品は済み
    const isDone = (id: string) => {
        const item = itemOf(id);
        return !item || !isReviewTarget(item);
    };
    const current = currentId ? itemOf(currentId) : undefined;
    const index = currentId ? queue.indexOf(currentId) : -1;
    const doneCount = queue.filter(isDone).length;
    const remainingCount = queue.length - doneCount;

    /** 今の商品より後ろの済んでいない商品。なければ前から探す。どれもなければ null（終わり） */
    const nextPending = (fromId: string) => {
        const from = queue.indexOf(fromId);
        const isTarget = (id: string) => id !== fromId && !isDone(id);
        return queue.slice(from + 1).find(isTarget) ?? queue.slice(0, from).find(isTarget) ?? null;
    };

    const handleDone = (item: FlyerItem) => setCurrentId(nextPending(item.id));
    // 直して分類し直した結果、再入力でなくなったら次へ進む（描画の中で切り替える）。
    // 再入力のときに開いていた商品だけを見る（「前へ」で済んだ商品を見返すときは進めない）
    const [watchedId, setWatchedId] = useState<string | null>(null);
    if (current && isReviewTarget(current) && watchedId !== current.id) {
        setWatchedId(current.id);
    }
    if (current && !isReviewTarget(current) && watchedId === current.id) {
        setWatchedId(null);
        setCurrentId(nextPending(current.id));
    }

    // 「あとで」は、済んだかどうかに関係なく次の商品へ（最後まで行ったら、残りを示す）
    const handleLater = () => setCurrentId(index >= 0 && index < queue.length - 1 ? queue[index + 1] : null);
    const handlePrevious = () => setCurrentId(index > 0 ? queue[index - 1] : currentId);
    const handleRestart = () => setCurrentId(queue.find((id) => !isDone(id)) ?? null);

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} maxWidth="md" fullWidth aria-labelledby="flyer-review-title">
            <DialogTitle id="flyer-review-title" sx={{ pb: 1 }}>
                再入力を1件ずつ直す
                <Typography variant="body2" color="text.secondary">
                    {current ? `${index + 1} / ${queue.length}件目` : '終わり'}（済み {doneCount}件・残り {remainingCount}件）
                </Typography>
            </DialogTitle>
            <LinearProgress variant="determinate" value={queue.length > 0 ? (doneCount / queue.length) * 100 : 100} aria-label="済んだ割合" />
            <DialogContent dividers>
                {current ? (
                    <Stack spacing={2}>
                        <ReviewItemSummary item={current} />
                        <FlyerItemForm
                            key={current.id}
                            menuId={menuId}
                            item={current}
                            closeLabel="表に戻る"
                            actionsContainer={actionsEl}
                            onClose={onClose}
                            onRemoved={() => handleDone(current)}
                        />
                    </Stack>
                ) : (
                    <Stack spacing={2} sx={{ alignItems: 'flex-start', py: 2 }}>
                        <Typography variant="h3" component="p">
                            {remainingCount === 0 ? 'すべて直しました' : `「あとで」にした商品が ${remainingCount}件 残っています`}
                        </Typography>
                        <Typography variant="body2" color="text.secondary">
                            {remainingCount === 0
                                ? '表に戻って「取り込みを終える」を押すと、取り込めるの商品が今週の商品に入ります。'
                                : '再入力の商品は、直すまで今週の商品に入りません。'}
                        </Typography>
                        {remainingCount > 0 && (
                            <Button variant="contained" onClick={handleRestart}>
                                残りを直す
                            </Button>
                        )}
                    </Stack>
                )}
            </DialogContent>
            <DialogActions sx={{ justifyContent: 'space-between', flexWrap: 'wrap', gap: 1, '& > :not(style) ~ :not(style)': { ml: 0 } }}>
                <Stack direction="row" spacing={1}>
                    <Button onClick={handlePrevious} disabled={!current || index <= 0}>
                        前へ
                    </Button>
                    <Button onClick={handleLater} disabled={!current}>
                        あとで（次へ）
                    </Button>
                </Stack>
                <Box sx={{ ml: 'auto', flex: { xs: '1 1 100%', sm: '1 1 auto' } }}>
                    <Box ref={setActionsEl} />
                    {!current && <Button onClick={onClose}>表に戻る</Button>}
                </Box>
            </DialogActions>
        </Dialog>
    );
};

interface ReviewItemSummaryProps {
    item: FlyerItem;
}

/** 1件ずつ直すときの、商品の見出し（表の1行と同じ内容を縦に並べる） */
const ReviewItemSummary = ({ item }: ReviewItemSummaryProps) => {
    const read = item.read;
    const summary = matchSummaryOf(item);
    return (
        <Stack spacing={0.75}>
            <FlyerStatusCell item={item} isSingleLine={false} />
            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
                <Typography variant="h3" component="h3" sx={{ overflowWrap: 'anywhere' }}>
                    {read.name || '（商品名なし）'}
                </Typography>
                {item.isManual && <Chip size="small" label="手入力" />}
            </Stack>
            <Typography variant="body2" color="text.secondary">
                {[read.maker ?? 'メーカーなし', read.volume, read.priceIncludingTax !== null ? `税込 ${read.priceIncludingTax}円` : '税込価格なし'].filter(Boolean).join('・')}
            </Typography>
            <Typography variant="body2">
                登録済みの同じ商品：{summary.title}
                {summary.detail ? `（${summary.detail}）` : ''}
            </Typography>
            <FlyerAllergenChips item={item} isSingleLine={false} />
        </Stack>
    );
};
