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
import { matchSummaryOf } from '../model/flyerRows';
import { FlyerConfirmForm } from './FlyerConfirmForm';
import { FlyerAllergenChips, FlyerStatusCell } from './FlyerStatusChip';

interface FlyerReviewDialogProps {
    menuId: string;
    /** 読み取った商品（読み直すたびに新しくなる。確定したかどうかはここから見る） */
    items: FlyerItem[];
    /** 確かめる順（開いたときの再入力・要確認の商品。表と同じ並び） */
    queue: string[];
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 再入力・要確認の商品を1件ずつ確かめるダイアログ（読み取った商品の表の「未確定を1件ずつ確かめる」から開く。09 の 6）。
 * 決めるのは、ボタンを押したときだけ（スワイプなどでうっかり確定しないように）。
 * 確定するか外すと、次の商品へ進む。「あとで」で飛ばした商品は、最後に残りとして示す。
 * 直したあとに「使えない」になった商品は、確定できないので「あとで」で次へ進む。
 */
export const FlyerReviewDialog = ({ menuId, items, queue, isOpen, onClose }: FlyerReviewDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const [currentId, setCurrentId] = useState<string | null>(queue[0] ?? null);
    // フォームのボタンをフッターに出すための場所
    const [actionsEl, setActionsEl] = useState<HTMLElement | null>(null);

    const itemOf = (id: string) => items.find((item) => item.id === id);
    // 確定した・外した（一覧にない）商品は済み
    const isDone = (id: string) => {
        const item = itemOf(id);
        return !item || !!item.confirmation;
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

    // 「あとで」は、済んだかどうかに関係なく次の商品へ（最後まで行ったら、残りを示す）
    const handleLater = () => setCurrentId(index >= 0 && index < queue.length - 1 ? queue[index + 1] : null);
    const handlePrevious = () => setCurrentId(index > 0 ? queue[index - 1] : currentId);
    const handleRestart = () => setCurrentId(queue.find((id) => !isDone(id)) ?? null);

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} maxWidth="md" fullWidth aria-labelledby="flyer-review-title">
            <DialogTitle id="flyer-review-title" sx={{ pb: 1 }}>
                未確定の商品を1件ずつ確かめる
                <Typography variant="body2" color="text.secondary">
                    {current ? `${index + 1} / ${queue.length}件目` : '終わり'}（済み {doneCount}件・残り {remainingCount}件）
                </Typography>
            </DialogTitle>
            <LinearProgress variant="determinate" value={queue.length > 0 ? (doneCount / queue.length) * 100 : 100} aria-label="済んだ割合" />
            <DialogContent dividers>
                {current ? (
                    <Stack spacing={2}>
                        <ReviewItemSummary item={current} />
                        <FlyerConfirmForm
                            key={current.id}
                            menuId={menuId}
                            item={current}
                            closeLabel="表に戻る"
                            actionsContainer={actionsEl}
                            onClose={onClose}
                            onSuccess={() => handleDone(current)}
                            onRemoved={() => handleDone(current)}
                        />
                    </Stack>
                ) : (
                    <Stack spacing={2} sx={{ alignItems: 'flex-start', py: 2 }}>
                        <Typography variant="h3" component="p">
                            {remainingCount === 0 ? 'すべて確かめました' : `「あとで」にした商品が ${remainingCount}件 残っています`}
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

/** 1件ずつ確かめるときの、商品の見出し（表の1行と同じ内容を縦に並べる） */
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
