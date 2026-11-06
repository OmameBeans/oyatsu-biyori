import { Suspense, useState } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import FormControlLabel from '@mui/material/FormControlLabel';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { AllergenCategoryMark, AllergenJudgementLine, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { PRODUCT_FILTER, useProductList } from '../../../entities/product';
import type { ProductFilter, ProductListItem } from '../../../entities/product';
import { productPath, SNACK_TYPE_LABEL } from '../../../shared/type';
import { LoadingFallback, PageLayout } from '../../../shared/ui';
import { ProductGrid } from './ProductGrid';

const FILTER_LABEL: Record<ProductFilter, ReactNode> = {
    All: 'すべて',
    Blocked: <AllergenCategoryMark category="Blocked" />,
    Warning: <AllergenCategoryMark category="Warning" />,
    Safe: <AllergenCategoryMark category="Safe" />,
    Package: 'パッケージで確認する商品',
    OtherPackage: 'そのほかの、パッケージで確認する商品',
    FuturePackage: '今日以降の献立で使う、パッケージで確認する商品',
};

const toFilter = (value: string | null): ProductFilter =>
    value && (Object.values(PRODUCT_FILTER) as string[]).includes(value) ? (value as ProductFilter) : PRODUCT_FILTER.ALL;

/**
 * SC-06 商品一覧（T6-01）。検索（正規化名の部分一致）・アレルゲンの表示での絞り込みは、サーバーで行う。
 * 「使わない」商品は既定で隠す。絞り込みは URL の ?filter= で持つ（献立一覧の件数から開くため）。
 * PC は DataGrid（並べ替えできる表）、スマホはカード。
 */
export const ProductListPage = () => {
    const [params, setParams] = useSearchParams();
    const filter = toFilter(params.get('filter'));
    const [query, setQuery] = useState(params.get('q') ?? '');
    const [includeDisabled, setIncludeDisabled] = useState(false);

    const update = (next: Record<string, string>) => {
        const merged = new URLSearchParams(params);
        Object.entries(next).forEach(([key, value]) => (value ? merged.set(key, value) : merged.delete(key)));
        setParams(merged, { replace: true });
    };

    return (
        <PageLayout
            title="商品一覧"
            header={
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} sx={{ alignItems: { md: 'center' } }}>
                    <TextField
                        label="商品名で探す"
                        value={query}
                        onChange={(event) => setQuery(event.target.value)}
                        onBlur={() => update({ q: query })}
                        onKeyDown={(event) => event.key === 'Enter' && update({ q: query })}
                        sx={{ minWidth: 220 }}
                    />
                    <TextField select label="アレルゲンの表示" value={filter} onChange={(event) => update({ filter: event.target.value })} sx={{ minWidth: 260 }}>
                        {Object.values(PRODUCT_FILTER).map((value) => (
                            <MenuItem key={value} value={value}>
                                {FILTER_LABEL[value]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <FormControlLabel control={<Switch checked={includeDisabled} onChange={(event) => setIncludeDisabled(event.target.checked)} />} label="「使わない」商品も出す" />
                </Stack>
            }
        >
            <Suspense fallback={<LoadingFallback />}>
                <ProductCards query={params.get('q') ?? ''} filter={filter} includeDisabled={includeDisabled} />
            </Suspense>
        </PageLayout>
    );
};

interface ProductCardsProps {
    query: string;
    filter: ProductFilter;
    includeDisabled: boolean;
}

const ProductCards = ({ query, filter, includeDisabled }: ProductCardsProps) => {
    const { data } = useProductList(query, filter, includeDisabled);
    const theme = useTheme();
    const isWide = useMediaQuery(theme.breakpoints.up('md'));
    return (
        <Stack spacing={1.5} sx={{ height: { md: '100%' } }}>
            {data.isAvoidedEmpty && <NoAvoidedAllergenNotice />}
            <Typography variant="body2" color="text.secondary">
                {data.items.length}件
            </Typography>
            {isWide ? (
                // PC は表（画面の高さいっぱい。表の中だけをスクロール）
                <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 320 }}>
                    <ProductGrid items={data.items} />
                </Box>
            ) : (
                <ProductCardList items={data.items} />
            )}
        </Stack>
    );
};

interface ProductCardListProps {
    items: ProductListItem[];
}

/** スマホはカード */
const ProductCardList = ({ items }: ProductCardListProps) => (
    <>
        <Box sx={{ display: 'grid', gridTemplateColumns: '1fr', gap: 1.5 }}>
            {items.map((item) => (
                <Card key={item.id} component="section" aria-label={item.name} sx={{ opacity: item.isDisabled ? 0.65 : 1 }}>
                    <CardActionArea component={RouterLink} to={productPath(item.id)}>
                        <CardContent>
                            <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                                <Typography sx={{ fontWeight: 700 }}>{item.name}</Typography>
                                <Chip size="small" label={SNACK_TYPE_LABEL[item.snackType]} />
                                {item.isDisabled && <Chip size="small" variant="outlined" label="使わない" />}
                                {item.isUsedInFutureMenus && <Chip size="small" color="primary" variant="outlined" label="今日以降の献立で使う" />}
                            </Stack>
                            <Typography variant="body2" color="text.secondary">
                                {[item.maker, item.volume].filter(Boolean).join('・')}
                                {item.lastCheckedDate ? `・最終確認 ${item.lastCheckedDate.replace(/-/g, '/')}` : '・確認の記録なし'}
                            </Typography>
                            <Box sx={{ mt: 1 }}>
                                <AllergenJudgementLine judgement={item.judgement} />
                            </Box>
                        </CardContent>
                    </CardActionArea>
                </Card>
            ))}
        </Box>
        {items.length === 0 && <Typography color="text.secondary">当てはまる商品はありません。</Typography>}
    </>
);
