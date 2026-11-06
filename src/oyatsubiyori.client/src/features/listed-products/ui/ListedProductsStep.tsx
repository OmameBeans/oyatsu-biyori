import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { AllergenCategoryMark, AllergenJudgementLine, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useListedProducts } from '../../../entities/menu';
import type { ListedProduct } from '../../../entities/menu';
import { formatYen } from '../../../shared/lib';
import { allergenCheckPath, JUDGEMENT_CATEGORY, SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { ListedProductGrid } from './ListedProductGrid';
import { isMissingValues } from '../model/missingValues';
import { ExcludeSwitch, MissingValuesForm } from './ListedProductParts';

interface ListedProductsStepProps {
    menuId: string;
    onBackToFlyer: () => void;
    onNext: () => void;
}

type AllergenFilter = 'all' | 'warning' | 'blocked';

/**
 * SC-09 手順2 今週の商品（T4-02）。Claude に献立を作らせる前に、候補とアレルゲンの表示を見渡す。
 * 判定・候補かどうかはサーバーが決める。「今週使う」は切り替えるたびに保存する。
 */
export const ListedProductsStep = ({ menuId, onBackToFlyer, onNext }: ListedProductsStepProps) => {
    const { data } = useListedProducts(menuId);
    const [allergenFilter, setAllergenFilter] = useState<AllergenFilter>('all');
    const [snackFilter, setSnackFilter] = useState<SnackType | 'all'>('all');
    const theme = useTheme();
    const isWide = useMediaQuery(theme.breakpoints.up('md'));

    const items = data.items.filter(
        (item) =>
            (allergenFilter === 'all' ||
                (allergenFilter === 'warning' && item.judgement.category === JUDGEMENT_CATEGORY.WARNING) ||
                (allergenFilter === 'blocked' && item.judgement.category === JUDGEMENT_CATEGORY.BLOCKED)) &&
            (snackFilter === 'all' || item.snackType === snackFilter),
    );

    return (
        // PC は見出し・絞り込みを固定し、表の中だけをスクロールする
        <Stack spacing={2} sx={{ height: { md: '100%' }, '& > *': { flexShrink: { md: 0 } } }}>
            {data.isAvoidedEmpty && <NoAvoidedAllergenNotice />}
            <Card>
                <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 1.5, alignItems: { md: 'center' } }}>
                    <Box sx={{ flex: 1 }}>
                        <Typography variant="h3" component="h2">
                            献立に入れられる商品 {data.candidateCount}件（うち <AllergenCategoryMark category="Warning" /> {data.candidateWarningCount}件）／{' '}
                            <AllergenCategoryMark category="Blocked" /> {data.blockedCount}件
                        </Typography>
                        {data.excludedCount > 0 && (
                            <Typography variant="body2" color="text.secondary">
                                今週使わない商品：{data.excludedCount}件
                            </Typography>
                        )}
                    </Box>
                    <Stack direction="row" spacing={1}>
                        <Button onClick={onBackToFlyer}>チラシを取り込み直す</Button>
                        <Button variant="contained" onClick={onNext}>
                            要望の入力へ
                        </Button>
                    </Stack>
                </CardContent>
            </Card>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                <ToggleButtonGroup size="small" exclusive value={allergenFilter} onChange={(_, value) => value && setAllergenFilter(value)} aria-label="アレルゲンの表示で絞り込む">
                    <ToggleButton value="all">すべて</ToggleButton>
                    <ToggleButton value="warning">
                        <AllergenCategoryMark category="Warning" />
                    </ToggleButton>
                    <ToggleButton value="blocked">
                        <AllergenCategoryMark category="Blocked" />
                    </ToggleButton>
                </ToggleButtonGroup>
                <ToggleButtonGroup size="small" exclusive value={snackFilter} onChange={(_, value) => value && setSnackFilter(value)} aria-label="分類で絞り込む">
                    <ToggleButton value="all">分類すべて</ToggleButton>
                    {[SNACK_TYPE.SALTY, SNACK_TYPE.SWEET, SNACK_TYPE.SMOOTH, SNACK_TYPE.NOT_SNACK].map((type) => (
                        <ToggleButton key={type} value={type}>
                            {SNACK_TYPE_LABEL[type]}
                        </ToggleButton>
                    ))}
                </ToggleButtonGroup>
            </Stack>
            {isWide ? (
                <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, flexShrink: 1, minHeight: 320 }}>
                    <ListedProductGrid menuId={menuId} items={items} />
                </Box>
            ) : (
                <>
                    <Stack spacing={1.5}>
                        {items.map((item) => (
                            <ListedProductCard key={item.productId} menuId={menuId} item={item} />
                        ))}
                    </Stack>
                    {items.length === 0 && <Typography color="text.secondary">当てはまる商品はありません。</Typography>}
                </>
            )}
        </Stack>
    );
};

interface ListedProductCardProps {
    menuId: string;
    item: ListedProduct;
}

/** スマホはカード */
const ListedProductCard = ({ menuId, item }: ListedProductCardProps) => (
    <Card component="section" aria-label={item.name} sx={{ opacity: item.isExcludedThisWeek ? 0.6 : 1, borderColor: item.judgement.category === JUDGEMENT_CATEGORY.BLOCKED ? 'error.light' : undefined }}>
        <CardContent sx={{ '&:last-child': { pb: 2 } }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 700 }}>{item.name}</Typography>
                    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', alignItems: 'center', mt: 0.5 }} useFlexGap>
                        <Chip size="small" label={SNACK_TYPE_LABEL[item.snackType]} />
                        {item.tags.map((tag) => (
                            <Chip key={`${tag.kind}-${tag.value}`} size="small" variant="outlined" label={tag.value} />
                        ))}
                        <Typography variant="body2" color="text.secondary">
                            {item.price !== null ? formatYen(item.price) : '税込価格なし'}・{item.piecesPerPack !== null ? `${item.piecesPerPack}人分` : '入数なし'}
                            {item.volume ? `・${item.volume}` : ''}
                        </Typography>
                    </Stack>
                </Box>
                <FormControlLabel
                    control={<ExcludeSwitch menuId={menuId} item={item} />}
                    label="今週使う"
                    sx={{ mr: 0, '& .MuiFormControlLabel-label': { fontSize: 13 } }}
                />
            </Stack>
            <Box sx={{ mt: 1 }}>
                <AllergenJudgementLine judgement={item.judgement} />
                {item.judgement.category !== 'Safe' && (
                    <Button component={RouterLink} to={allergenCheckPath(item.productId)} size="small" sx={{ mt: 0.5 }}>
                        パッケージで確認する
                    </Button>
                )}
            </Box>
            {item.daysSinceServed !== null && (
                <Typography variant="body2" sx={{ mt: 0.5, color: 'warning.dark' }}>
                    {item.daysSinceServed}日前に出しました
                </Typography>
            )}
            {item.isDisabled && (
                <Typography variant="body2" color="text.secondary">
                    「使わない」にした商品です
                </Typography>
            )}
            {isMissingValues(item) && <MissingValuesForm menuId={menuId} item={item} />}
        </CardContent>
    </Card>
);
