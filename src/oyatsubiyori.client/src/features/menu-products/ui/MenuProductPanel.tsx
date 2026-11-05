import { useState } from 'react';
import type { MouseEvent } from 'react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import AddIcon from '@mui/icons-material/Add';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { useSnackbar } from 'notistack';
import { AllergenCategoryMark } from '../../../entities/allergen-judgement';
import {
    dropTargetSx,
    endMenuDrag,
    startMenuDrag,
    useAddItem,
    useIsMenuEditing,
    useIsMenuProposalRunning,
    useListedProducts,
    useMenuDropTarget,
    useRemoveItem,
} from '../../../entities/menu';
import type { ListedProduct, MenuDetail } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatYen } from '../../../shared/lib';
import { SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';

interface MenuProductPanelProps {
    menu: MenuDetail;
}

/**
 * SC-09 手順4 の左の列「使える商品」（FR-MENU-24）。献立に入れられる今週の商品（候補かどうかはサーバーが決める）を並べ、
 * 日のカードへドラッグすると足せる。献立の商品をここへドラッグして戻すと外せる。
 * ドラッグできない端末（スマホなど）のために、商品ごとの「＋」から日を選んで足すこともできる。
 */
export const MenuProductPanel = ({ menu }: MenuProductPanelProps) => {
    const { data: listed } = useListedProducts(menu.id);
    const removeItem = useRemoveItem(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [snackFilter, setSnackFilter] = useState<SnackType | 'all'>('all');
    // 献立案の作成・チャットの組み直しのあいだと、ほかの操作の保存の途中は、手で直させない（サーバーでも止める）
    const isRunning = useIsMenuProposalRunning(menu.id);
    const isEditing = useIsMenuEditing(menu.id);
    const isBusy = isRunning || isEditing;

    const candidates = listed.items.filter((item) => item.isCandidate);
    const shown = candidates.filter((item) => snackFilter === 'all' || item.snackType === snackFilter);

    // 献立の商品を戻したときだけ受け付けて外す（一覧の商品を一覧に落としても何もしないので、光らせない）
    const { isOver, handlers } = useMenuDropTarget(
        (payload) => payload.kind === 'item',
        (payload) => {
            if (payload.kind !== 'item') {
                return;
            }
            removeItem.mutate(
                { update: { itemId: payload.itemId }, rowVersion: menu.rowVersion },
                {
                    onSuccess: () => enqueueSnackbar('献立から外しました', { variant: 'success' }),
                    onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
                },
            );
        },
        isBusy,
    );

    return (
        <Paper
            component="section"
            aria-label="使える商品"
            variant="outlined"
            {...handlers}
            sx={{ display: 'flex', flexDirection: 'column', height: { md: '100%' }, borderRadius: 4, ...dropTargetSx(isOver) }}
        >
            <Box sx={{ p: 2, pb: 1 }}>
                <Typography variant="h3" component="h2">
                    使える商品 {candidates.length}件
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, display: { xs: 'none', lg: 'block' } }}>
                    日のカードへドラッグすると足せます（「＋」で日を選んでも足せます）。献立の商品をここへ戻すと外せます。
                </Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, display: { lg: 'none' } }}>
                    「＋」で日を選んで足せます。
                </Typography>
                <ToggleButtonGroup
                    size="small"
                    exclusive
                    value={snackFilter}
                    onChange={(_, value) => value && setSnackFilter(value)}
                    aria-label="分類で絞り込む"
                    sx={{ mt: 1, flexWrap: 'wrap' }}
                >
                    <ToggleButton value="all">すべて</ToggleButton>
                    {[SNACK_TYPE.SALTY, SNACK_TYPE.SWEET, SNACK_TYPE.SMOOTH].map((type) => (
                        <ToggleButton key={type} value={type}>
                            {SNACK_TYPE_LABEL[type]}
                        </ToggleButton>
                    ))}
                </ToggleButtonGroup>
            </Box>
            <Stack spacing={1} sx={{ p: 2, pt: 1, overflow: { md: 'auto' }, flex: 1, minHeight: 0 }}>
                {shown.map((product) => (
                    <ProductTile key={product.productId} menu={menu} product={product} isBusy={isBusy} />
                ))}
                {shown.length === 0 && (
                    <Typography variant="body2" color="text.secondary">
                        {candidates.length === 0 ? '献立に入れられる商品がありません。「今週の商品」で税込価格・入数を確かめてください。' : '当てはまる商品はありません。'}
                    </Typography>
                )}
            </Stack>
        </Paper>
    );
};

interface ProductTileProps {
    menu: MenuDetail;
    product: ListedProduct;
    isBusy: boolean;
}

const ProductTile = ({ menu, product, isBusy }: ProductTileProps) => {
    const addItem = useAddItem(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [anchor, setAnchor] = useState<HTMLElement | null>(null);

    const handleAdd = (date: string) => {
        setAnchor(null);
        addItem.mutate(
            { update: { date, productId: product.productId }, rowVersion: menu.rowVersion },
            {
                onSuccess: () => enqueueSnackbar(`${formatDateLabel(date)}に「${product.name}」を足しました`, { variant: 'success' }),
                onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
            },
        );
    };

    return (
        <Card
            aria-label={product.name}
            draggable={!isBusy}
            onDragStart={(event) => startMenuDrag(event.dataTransfer, { kind: 'product', productId: product.productId })}
            onDragEnd={endMenuDrag}
            sx={{ display: 'flex', alignItems: 'center', gap: 0.5, p: 1, pl: { xs: 1.5, lg: 0.5 }, cursor: { lg: isBusy ? 'default' : 'grab' }, flexShrink: 0 }}
        >
            <DragIndicatorIcon fontSize="small" sx={{ color: 'text.disabled', display: { xs: 'none', lg: 'block' } }} aria-hidden />
            <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography sx={{ fontWeight: 600, fontSize: 14 }}>{product.name}</Typography>
                <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                    <Chip size="small" label={SNACK_TYPE_LABEL[product.snackType]} sx={{ height: 20, fontSize: 11 }} />
                    <Typography variant="caption" color="text.secondary">
                        {product.price !== null ? formatYen(product.price) : '価格なし'}
                        {product.piecesPerPack !== null ? `・${product.piecesPerPack}個入り` : ''}
                    </Typography>
                    {product.judgement.category === 'Warning' && (
                        <Typography variant="caption" sx={{ color: 'warning.dark', fontWeight: 600 }}>
                            <AllergenCategoryMark category="Warning" isInherit />
                        </Typography>
                    )}
                </Stack>
                {product.daysSinceServed !== null && (
                    <Typography variant="caption" sx={{ color: 'warning.dark', display: 'block' }}>
                        {product.daysSinceServed}日前に出しました
                    </Typography>
                )}
            </Box>
            <IconButton
                aria-label={`${product.name}を日を選んで足す`}
                disabled={isBusy}
                onClick={(event: MouseEvent<HTMLElement>) => setAnchor(event.currentTarget)}
            >
                <AddIcon />
            </IconButton>
            <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}>
                {menu.days.map((day) => (
                    <MenuItem key={day.date} onClick={() => handleAdd(day.date)} disabled={day.items.some((item) => item.productId === product.productId)}>
                        {formatDateLabel(day.date)}に足す
                    </MenuItem>
                ))}
            </Menu>
        </Card>
    );
};
