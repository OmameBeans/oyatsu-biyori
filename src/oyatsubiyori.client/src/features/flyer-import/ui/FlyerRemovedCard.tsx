import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useRestoreFlyerItem } from '../../../entities/flyer';
import type { FlyerRemovedItem } from '../../../entities/flyer';
import { errorMessageOf } from '../../../shared/api';
import { ClaudeText } from '../../../shared/ui';

interface FlyerRemovedCardProps {
    menuId: string;
    items: FlyerRemovedItem[];
}

/**
 * SC-09 手順1 外した掲載品（09 の 3.7）。食品以外として読み取りで外したものと、職員が「外す」で外したもの。
 * 黙って消さずに商品名と理由を並べ、「戻す」で一覧に戻せる（戻すとサーバーが分類し直す）。今週の商品には入らない
 */
export const FlyerRemovedCard = ({ menuId, items }: FlyerRemovedCardProps) => {
    const restore = useRestoreFlyerItem(menuId);
    const { enqueueSnackbar } = useSnackbar();

    const handleRestore = (item: FlyerRemovedItem) =>
        restore.mutate(item.id, {
            onSuccess: () => enqueueSnackbar(`「${item.name}」を一覧に戻しました`, { variant: 'success' }),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    return (
        <Card component="section" aria-labelledby="flyer-removed-heading" sx={{ flexShrink: 0 }}>
            <CardContent>
                <Typography id="flyer-removed-heading" variant="h3" component="h2" sx={{ mb: 0.5 }}>
                    外した掲載品（{items.length}件）
                </Typography>
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
                                        チラシの表記が、パッケージでの確認と食い違っています（{item.discrepancyAllergens.join('・')}）。ほかの商品を確定すると、商品一覧に「参考」の記録を足します。読み違いなら「戻す」で表記を直してください
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
            </CardContent>
        </Card>
    );
};
