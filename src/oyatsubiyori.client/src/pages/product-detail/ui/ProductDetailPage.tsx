import { useState } from 'react';
import { Link as RouterLink, useParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import FormControlLabel from '@mui/material/FormControlLabel';
import Link from '@mui/material/Link';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { AllergenJudgementLine, BlockedInMenuNotice, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useProduct, useUpdateProduct } from '../../../entities/product';
import type { CheckHistory, ProductDetail } from '../../../entities/product';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatDateTime, formatYen } from '../../../shared/lib';
import { allergenCheckPath, menuPath, SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { PageLayout } from '../../../shared/ui';

const SOURCE_LABEL: Record<CheckHistory['source'], string> = {
    Package: 'パッケージ',
    CoopSite: 'コープのサイト',
    Flyer: 'チラシ',
    Other: 'その他',
};

/** ステータスの名前を画面に出すのは、この履歴だけ（03 §6.2） */
const STATUS_LABEL: Record<CheckHistory['status'], string> = {
    Confirmed: '確認済み',
    Reference: '参考',
};

const toNumber = (value: string): number | null => (value.trim() === '' ? null : Number(value));

/**
 * SC-07 商品詳細・編集（T6-02）。上部に今のアレルゲンの表示と理由・次にすること、基本情報の編集、確認の履歴、
 * 今日以降にこの商品を使う献立（🔴 なら目立たせる）、アレルゲン確認（SC-08）への入口。
 */
export const ProductDetailPage = () => {
    const { productId = '' } = useParams();
    const { data: product } = useProduct(productId);
    const isBlocked = product.judgement.category === 'Blocked';

    return (
        <PageLayout
            title={product.name}
            actions={
                <Button component={RouterLink} to={allergenCheckPath(product.id)} variant="contained">
                    パッケージで確認する
                </Button>
            }
            header={<AllergenJudgementLine judgement={product.judgement} isInMenu={product.usages.length > 0} />}
        >
            <Stack spacing={2}>
                {product.isAvoidedEmpty && <NoAvoidedAllergenNotice />}
                {product.usages.length > 0 && (
                    <Alert severity={isBlocked ? 'error' : 'info'} variant={isBlocked ? 'filled' : 'standard'}>
                        <Typography sx={{ fontWeight: 700 }}>
                            {isBlocked ? <BlockedInMenuNotice /> : '今日以降の献立で使っています'}
                        </Typography>
                        {product.usages.map((usage) => (
                            <Box key={`${usage.menuId}-${usage.date}`}>
                                <Link component={RouterLink} to={menuPath(usage.menuId, 4)} color="inherit">
                                    {formatDateLabel(usage.date)}の献立（{usage.status === 'Confirmed' ? '確定' : '下書き'}）
                                </Link>
                            </Box>
                        ))}
                    </Alert>
                )}
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '3fr 2fr' }, gap: 2 }}>
                    <ProductEditForm key={product.id + product.name + product.memo} product={product} />
                    <Card>
                        <CardContent>
                            <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                                アレルゲン確認の履歴
                            </Typography>
                            {product.checks.length === 0 && <Typography color="text.secondary">確認の記録はまだありません。</Typography>}
                            <Stack spacing={1.5}>
                                {product.checks.map((check) => (
                                    <Box key={check.id} sx={{ borderLeft: 3, borderColor: check.status === 'Confirmed' ? 'success.main' : 'warning.main', pl: 1.5 }}>
                                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                                            <Chip size="small" label={STATUS_LABEL[check.status]} color={check.status === 'Confirmed' ? 'success' : 'warning'} variant="outlined" />
                                            <Typography variant="body2">
                                                {formatDateTime(check.checkedAt)}・{check.checkedBy}・{SOURCE_LABEL[check.source]}
                                            </Typography>
                                        </Stack>
                                        <Typography variant="body2">含む品目：{check.containedAllergens.length === 0 ? 'なし' : check.containedAllergens.join('・')}</Typography>
                                        {check.sourceNote && (
                                            <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                                                {check.sourceNote}
                                            </Typography>
                                        )}
                                        {check.contaminationNote && <Typography variant="caption" sx={{ display: 'block' }}>注意書き：{check.contaminationNote}</Typography>}
                                        {check.rawIngredients && (
                                            <details>
                                                <summary>
                                                    <Typography variant="caption" component="span">
                                                        原材料の全文
                                                    </Typography>
                                                </summary>
                                                <Typography variant="caption" sx={{ display: 'block', whiteSpace: 'pre-wrap' }}>
                                                    {check.rawIngredients}
                                                </Typography>
                                            </details>
                                        )}
                                    </Box>
                                ))}
                            </Stack>
                        </CardContent>
                    </Card>
                </Box>
            </Stack>
        </PageLayout>
    );
};

interface ProductEditFormProps {
    product: ProductDetail;
}

const ProductEditForm = ({ product }: ProductEditFormProps) => {
    const update = useUpdateProduct(product.id);
    const { enqueueSnackbar } = useSnackbar();
    const [name, setName] = useState(product.name);
    const [maker, setMaker] = useState(product.maker ?? '');
    const [volume, setVolume] = useState(product.volume ?? '');
    const [pieces, setPieces] = useState(product.piecesPerPack?.toString() ?? '');
    const [perChild, setPerChild] = useState(String(product.defaultPerChildCount));
    const [price, setPrice] = useState(product.price?.toString() ?? '');
    const [jan, setJan] = useState(product.janCode ?? '');
    const [snackType, setSnackType] = useState<SnackType>(product.snackType);
    const [memo, setMemo] = useState(product.memo ?? '');
    const [isDisabled, setIsDisabled] = useState(product.isDisabled);
    const [characterTags, setCharacterTags] = useState(product.tags.filter((t) => t.kind === 'Character').map((t) => t.value));
    const [flavorTags, setFlavorTags] = useState(product.tags.filter((t) => t.kind === 'Flavor').map((t) => t.value));
    const [newCharacter, setNewCharacter] = useState('');

    const handleSave = () =>
        update.mutate(
            {
                name,
                maker: maker || null,
                volume: volume || null,
                piecesPerPack: toNumber(pieces),
                defaultPerChildCount: toNumber(perChild),
                price: toNumber(price),
                janCode: jan || null,
                snackType,
                memo: memo || null,
                isDisabled,
                characterTags,
                flavorTags,
            },
            { onSuccess: () => enqueueSnackbar('商品を保存しました', { variant: 'success' }) },
        );

    return (
        <Card>
            <CardContent>
                <Typography variant="h3" component="h2" sx={{ mb: 1.5 }}>
                    基本情報
                </Typography>
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
                    <TextField label="商品名" value={name} onChange={(event) => setName(event.target.value)} />
                    <TextField label="メーカー" value={maker} onChange={(event) => setMaker(event.target.value)} />
                    <TextField label="内容量" value={volume} onChange={(event) => setVolume(event.target.value)} />
                    <TextField select label="おやつの分類" value={snackType} onChange={(event) => setSnackType(event.target.value as SnackType)}>
                        {Object.values(SNACK_TYPE).map((type) => (
                            <MenuItem key={type} value={type}>
                                {SNACK_TYPE_LABEL[type]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField label="入数（1つで何人分か）" type="number" value={pieces} onChange={(event) => setPieces(event.target.value)} />
                    <TextField label="1人あたりの個数" type="number" value={perChild} onChange={(event) => setPerChild(event.target.value)} />
                    <TextField label="税込価格（円）" type="number" value={price} onChange={(event) => setPrice(event.target.value)} />
                    <TextField label="JAN コード" value={jan} onChange={(event) => setJan(event.target.value)} />
                </Box>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 1.5 }}>
                    税抜価格：{product.priceExcludingTax !== null ? formatYen(product.priceExcludingTax) : 'なし'}・注文番号：{product.orderNo ?? 'なし'}・対象年齢：
                    {product.targetAgeLabel ?? 'なし'}・最後に載った週：{product.listedWeekStart ? product.listedWeekStart.replace(/-/g, '/') : 'なし'}
                </Typography>
                <Box sx={{ mt: 2 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600, mb: 0.5 }}>
                        キャラクターのタグ
                    </Typography>
                    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', mb: 1 }} useFlexGap>
                        {characterTags.map((tag) => (
                            <Chip key={tag} label={tag} onDelete={() => setCharacterTags(characterTags.filter((t) => t !== tag))} />
                        ))}
                    </Stack>
                    <Stack direction="row" spacing={1}>
                        <TextField label="キャラクターを足す" value={newCharacter} onChange={(event) => setNewCharacter(event.target.value)} />
                        <Button
                            disabled={!newCharacter.trim()}
                            onClick={() => {
                                setCharacterTags([...new Set([...characterTags, newCharacter.trim()])]);
                                setNewCharacter('');
                            }}
                        >
                            足す
                        </Button>
                    </Stack>
                    <Typography variant="body2" sx={{ fontWeight: 600, mt: 1.5, mb: 0.5 }}>
                        味・種類のタグ
                    </Typography>
                    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap' }} useFlexGap>
                        {product.flavorVocabulary.map((word) => (
                            <Chip
                                key={word}
                                label={word}
                                color={flavorTags.includes(word) ? 'primary' : 'default'}
                                variant={flavorTags.includes(word) ? 'filled' : 'outlined'}
                                onClick={() => setFlavorTags(flavorTags.includes(word) ? flavorTags.filter((t) => t !== word) : [...flavorTags, word])}
                            />
                        ))}
                    </Stack>
                </Box>
                <TextField
                    label="メモ"
                    value={memo}
                    onChange={(event) => setMemo(event.target.value)}
                    multiline
                    minRows={2}
                    fullWidth
                    sx={{ mt: 2 }}
                    helperText="子どもの名前は書かないでください（献立の提案のため AI に送ります）"
                />
                <FormControlLabel control={<Switch checked={isDisabled} onChange={(event) => setIsDisabled(event.target.checked)} />} label="使わない（献立の候補から外す）" sx={{ mt: 1 }} />
                {update.isError && (
                    <Alert severity="error" sx={{ mt: 1, whiteSpace: 'pre-wrap' }}>
                        {errorMessageOf(update.error)}
                    </Alert>
                )}
                <Box sx={{ mt: 2 }}>
                    <Button variant="contained" onClick={handleSave} disabled={update.isPending || !name.trim()}>
                        保存する
                    </Button>
                </Box>
            </CardContent>
        </Card>
    );
};
