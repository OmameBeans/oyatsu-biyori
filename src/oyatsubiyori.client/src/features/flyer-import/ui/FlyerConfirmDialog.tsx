import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Select from '@mui/material/Select';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { FLYER_CHOICE, useConfirmFlyerItem } from '../../../entities/flyer';
import type { FlyerChoice, FlyerItem, FlyerMatchProduct } from '../../../entities/flyer';
import { useAllergenMaster } from '../../../entities/settings';
import { errorMessageOf } from '../../../shared/api';
import { SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { ClaudeText, ConfirmDialog } from '../../../shared/ui';
import { verdictLabel } from '../model/labels';

interface FlyerConfirmDialogProps {
    menuId: string;
    item: FlyerItem;
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

/**
 * 「同じ商品」として選べる候補。サーバーが決めた sameChoiceProductIds の商品だけ
 * （内容量違い・味違いを「同じ」にすると、別の商品の確認を引き継いでしまうため。SR-05）
 */
const sameCandidates = (item: FlyerItem): FlyerMatchProduct[] => {
    const all = [...item.sameProducts, ...item.volumeVariants, ...item.candidates];
    return all.filter(
        (product, index) => item.sameChoiceProductIds.includes(product.productId) && all.findIndex((p) => p.productId === product.productId) === index,
    );
};

const NONE = '__none__';
const toNumber = (value: string): number | null => (value.trim() === '' ? null : Number(value));

/**
 * 取り込みの1商品を確かめて確定する（T3-08・T3-10）。選択肢は「同じ商品」「味違い・内容量違いとして登録」「新しい商品」「取り込まない」。
 * 対応づけできなかったアレルゲン表記の語は、品目を選ぶか「対応する品目なし」を選ぶまで確定できない（サーバーでも止める）。
 */
export const FlyerConfirmDialog = ({ menuId, item, isOpen, onClose, onSuccess }: FlyerConfirmDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const { data: master } = useAllergenMaster();
    const confirm = useConfirmFlyerItem(menuId);
    const read = item.read;
    const candidates = sameCandidates(item);
    const variantNames = [...item.volumeVariants, ...item.candidates].filter((p) => !item.sameChoiceProductIds.includes(p.productId)).map((p) => p.name);

    const [choice, setChoice] = useState<FlyerChoice>(item.confirmation?.choice ?? item.suggestedChoice);
    const [productId, setProductId] = useState<string>(
        (item.confirmation?.choice === FLYER_CHOICE.SAME ? item.confirmation.productId : null) ?? item.suggestedProductId ?? candidates[0]?.productId ?? '',
    );
    const [name, setName] = useState(read.name);
    const [volume, setVolume] = useState(read.volume ?? '');
    const [maker, setMaker] = useState(read.maker ?? '');
    const [price, setPrice] = useState(read.priceIncludingTax?.toString() ?? '');
    const [priceExcluding, setPriceExcluding] = useState(read.priceExcludingTax?.toString() ?? '');
    const [orderNo, setOrderNo] = useState(read.orderNo ?? '');
    const [pieces, setPieces] = useState(read.piecesPerPackCandidate?.toString() ?? '');
    const [snackType, setSnackType] = useState<SnackType | ''>(read.snackType ?? '');
    const [labelIds, setLabelIds] = useState<string[]>(item.labelAllergens.map((a) => a.id));
    const [termMapping, setTermMapping] = useState<Record<string, string>>({});
    const [characterTags, setCharacterTags] = useState<string[]>(read.characterTags);
    const [treatAsFlyerError, setTreatAsFlyerError] = useState(false);
    const [addingAllergen, setAddingAllergen] = useState('');
    const [removingAllergen, setRemovingAllergen] = useState<string | null>(null);

    const unresolved = item.unmappedTerms.filter((term) => !termMapping[term]);
    const allergenName = (id: string) => master.allergens.find((a) => a.id === id)?.name ?? id;
    const isSkip = choice === FLYER_CHOICE.SKIP;
    // 選んだ「同じ商品」の候補との食い違い（候補ごとにサーバーが求めたもの）
    const selectedPlan = choice === FLYER_CHOICE.SAME ? item.plansByProduct[productId] : undefined;
    const discrepancies = selectedPlan?.discrepancies ?? [];
    const hasStaffChoice = selectedPlan?.referenceDecision === 'StaffChoice';

    const handleSubmit = () => {
        const mapped = Object.values(termMapping).filter((value) => value !== NONE);
        confirm.mutate(
            {
                itemId: item.id,
                choice,
                productId: choice === FLYER_CHOICE.SAME ? productId : null,
                name,
                maker: maker || null,
                volume: volume || null,
                piecesPerPack: toNumber(pieces),
                piecesEdited: toNumber(pieces) !== read.piecesPerPackCandidate,
                snackType: snackType || null,
                snackTypeEdited: snackType !== (read.snackType ?? ''),
                priceIncludingTax: toNumber(price),
                priceExcludingTax: toNumber(priceExcluding),
                orderNo: orderNo || null,
                targetAgeLabel: read.targetAgeLabel,
                characterTags,
                flavorTags: read.flavorTags,
                labelAllergenIds: Array.from(new Set([...labelIds, ...mapped])),
                resolvedTerms: Object.keys(termMapping),
                treatAsFlyerError: hasStaffChoice && treatAsFlyerError,
            },
            { onSuccess },
        );
    };

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} maxWidth="md" fullWidth>
            <DialogTitle>「{read.name}」を確かめる</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2.5}>
                    <FormControl>
                        <FormLabel>同じ商品かどうか</FormLabel>
                        <RadioGroup value={choice} onChange={(event) => setChoice(event.target.value as FlyerChoice)}>
                            <FormControlLabel value={FLYER_CHOICE.SAME} control={<Radio />} label="登録済みの商品と同じ" disabled={candidates.length === 0} />
                            {choice === FLYER_CHOICE.SAME && candidates.length > 0 && (
                                <Select value={productId} onChange={(event) => setProductId(event.target.value)} sx={{ ml: 4, mb: 1 }} size="small" aria-label="同じ商品">
                                    {candidates.map((p) => (
                                        <MenuItem key={p.productId} value={p.productId}>
                                            {p.name}（{p.volume ?? '内容量なし'}）{p.isDisabled ? '・使わない商品' : ''}
                                            {!p.makerCompared ? '・メーカー未確認' : ''}
                                        </MenuItem>
                                    ))}
                                </Select>
                            )}
                            <FormControlLabel value={FLYER_CHOICE.VARIANT} control={<Radio />} label="味違い・内容量違いとして登録（アレルゲンの確認は引き継ぎません）" />
                            {variantNames.length > 0 && (
                                <Typography variant="caption" color="text.secondary" sx={{ ml: 4 }}>
                                    似ている商品：{variantNames.join('・')}（同じ商品とは分からないため、別の商品として登録します）
                                </Typography>
                            )}
                            <FormControlLabel value={FLYER_CHOICE.NEW} control={<Radio />} label="新しい商品として登録" />
                            <FormControlLabel value={FLYER_CHOICE.SKIP} control={<Radio />} label="取り込まない（おやつに使わない）" />
                        </RadioGroup>
                        {item.judgement && (
                            <Alert severity="info" sx={{ mt: 1 }}>
                                AI の判定：{verdictLabel(item.judgement.verdict)}
                                {item.judgement.productName ? `（${item.judgement.productName}）` : ''}
                                <ClaudeText text={item.judgement.reason} />
                            </Alert>
                        )}
                    </FormControl>

                    {!isSkip && (
                        <>
                            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
                                <TextField label="商品名" value={name} onChange={(event) => setName(event.target.value)} disabled={choice === FLYER_CHOICE.SAME} />
                                <TextField label="メーカー" value={maker} onChange={(event) => setMaker(event.target.value)} disabled={choice === FLYER_CHOICE.SAME} />
                                <TextField label="内容量" value={volume} onChange={(event) => setVolume(event.target.value)} disabled={choice === FLYER_CHOICE.SAME} />
                                <TextField label="注文番号" value={orderNo} onChange={(event) => setOrderNo(event.target.value)} />
                                <TextField label="税込価格（円）" type="number" value={price} onChange={(event) => setPrice(event.target.value)} />
                                <TextField label="税抜価格（円）" type="number" value={priceExcluding} onChange={(event) => setPriceExcluding(event.target.value)} />
                                <TextField
                                    label="入数（1つで何人分か）"
                                    type="number"
                                    value={pieces}
                                    onChange={(event) => setPieces(event.target.value)}
                                    helperText={read.piecesPerPackReason ? `候補の理由：${read.piecesPerPackReason}` : '内容量から入数が分かりません。入れてください'}
                                />
                                <FormControl size="small">
                                    <InputLabel id={`snack-${item.id}`}>おやつの分類</InputLabel>
                                    <Select labelId={`snack-${item.id}`} label="おやつの分類" value={snackType} onChange={(event) => setSnackType(event.target.value as SnackType)}>
                                        {Object.values(SNACK_TYPE).map((type) => (
                                            <MenuItem key={type} value={type}>
                                                {SNACK_TYPE_LABEL[type]}
                                            </MenuItem>
                                        ))}
                                    </Select>
                                </FormControl>
                            </Box>
                            {read.bulkNote && <Typography variant="body2">まとめ買い：{read.bulkNote}（価格は注文の単位あたり）</Typography>}

                            <Box>
                                <FormLabel>チラシのアレルゲン表記</FormLabel>
                                <Typography sx={{ my: 0.5 }}>{read.allergenLabelText ? `「${read.allergenLabelText}」` : '表記なし（読み落としの可能性があります。チラシと見比べてください）'}</Typography>
                                <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', mb: 1 }} useFlexGap>
                                    {labelIds.map((id) => (
                                        <Chip key={id} label={allergenName(id)} onDelete={() => setRemovingAllergen(id)} />
                                    ))}
                                </Stack>
                                <Stack direction="row" spacing={1}>
                                    <Select size="small" value={addingAllergen} displayEmpty onChange={(event) => setAddingAllergen(event.target.value)} sx={{ minWidth: 200 }} aria-label="品目を足す">
                                        <MenuItem value="">品目を足す</MenuItem>
                                        {master.allergens
                                            .filter((a) => !labelIds.includes(a.id))
                                            .map((a) => (
                                                <MenuItem key={a.id} value={a.id}>
                                                    {a.name}
                                                </MenuItem>
                                            ))}
                                    </Select>
                                    <Button
                                        disabled={!addingAllergen}
                                        onClick={() => {
                                            setLabelIds([...labelIds, addingAllergen]);
                                            setAddingAllergen('');
                                        }}
                                    >
                                        足す
                                    </Button>
                                </Stack>
                                {item.descriptionHitWords.length > 0 && (
                                    <Alert severity="warning" sx={{ mt: 1 }}>
                                        商品名・説明文に、避ける品目の言葉があります：{item.descriptionHitWords.join('・')}（含むとして記録します）
                                    </Alert>
                                )}
                            </Box>

                            {item.unmappedTerms.length > 0 && (
                                <Box>
                                    <FormLabel>品目に当てはめられなかった表記の語（確定の前に選んでください）</FormLabel>
                                    {item.unmappedTerms.map((term) => (
                                        <Stack key={term} direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                                            <Typography sx={{ minWidth: 100 }}>「{term}」</Typography>
                                            <Select
                                                size="small"
                                                value={termMapping[term] ?? ''}
                                                displayEmpty
                                                onChange={(event) => setTermMapping({ ...termMapping, [term]: event.target.value })}
                                                sx={{ minWidth: 220 }}
                                                aria-label={`「${term}」の品目`}
                                            >
                                                <MenuItem value="">選んでください</MenuItem>
                                                <MenuItem value={NONE}>対応する品目なし</MenuItem>
                                                {master.allergens.map((a) => (
                                                    <MenuItem key={a.id} value={a.id}>
                                                        {a.name}
                                                    </MenuItem>
                                                ))}
                                            </Select>
                                        </Stack>
                                    ))}
                                </Box>
                            )}

                            {choice === FLYER_CHOICE.SAME && discrepancies.length > 0 && (
                                <Alert severity={discrepancies.some((d) => d.isAvoided) ? 'error' : 'warning'}>
                                    パッケージで確認した内容と、チラシの表記が食い違っています：{discrepancies.map((d) => d.allergen.name).join('・')}
                                    {discrepancies.some((d) => d.isAvoided)
                                        ? '。避ける品目のため、チラシの表記を記録します（パッケージで確認し直すまで献立に入れられません）'
                                        : ''}
                                    {hasStaffChoice && (
                                        <RadioGroup value={treatAsFlyerError ? 'error' : 'recheck'} onChange={(event) => setTreatAsFlyerError(event.target.value === 'error')}>
                                            <FormControlLabel value="recheck" control={<Radio size="small" />} label="確認し直す（チラシの表記を記録する）" />
                                            <FormControlLabel value="error" control={<Radio size="small" />} label="チラシの誤りとして扱う" />
                                        </RadioGroup>
                                    )}
                                </Alert>
                            )}

                            <Box>
                                <FormLabel>キャラクターのタグ</FormLabel>
                                <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', mt: 0.5 }} useFlexGap>
                                    {characterTags.length === 0 && <Typography variant="body2">なし</Typography>}
                                    {characterTags.map((tag) => (
                                        <Chip key={tag} label={tag} onDelete={() => setCharacterTags(characterTags.filter((t) => t !== tag))} />
                                    ))}
                                    {read.flavorTags.map((tag) => (
                                        <Chip key={tag} label={tag} variant="outlined" />
                                    ))}
                                </Stack>
                            </Box>
                        </>
                    )}
                    {confirm.isError && (
                        <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                            {errorMessageOf(confirm.error)}
                        </Alert>
                    )}
                </Stack>
            </DialogContent>
            <ConfirmDialog
                isOpen={removingAllergen !== null}
                title={`「${removingAllergen ? allergenName(removingAllergen) : ''}」を外しますか？`}
                confirmLabel="外す"
                isDanger
                onClose={() => setRemovingAllergen(null)}
                onConfirm={() => {
                    setLabelIds(labelIds.filter((x) => x !== removingAllergen));
                    setRemovingAllergen(null);
                }}
            >
                <Typography>チラシと見比べて、この品目が表記に書かれていないことを確かめましたか？外したことは記録に残ります。</Typography>
            </ConfirmDialog>
            <DialogActions>
                <Button onClick={onClose}>やめる</Button>
                <Button variant="contained" onClick={handleSubmit} disabled={confirm.isPending || (!isSkip && unresolved.length > 0) || (!isSkip && !name.trim())}>
                    確定する
                </Button>
            </DialogActions>
        </Dialog>
    );
};
