import { Fragment, useEffect, useRef, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormLabel from '@mui/material/FormLabel';
import Link from '@mui/material/Link';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { AllergenJudgementLine, BlockedInMenuNotice, KeywordHitNotice, manufacturerCheckText, NonPackageSourceNotice } from '../../../entities/allergen-judgement';
import { useKeywordHits, useRecordCheck, useStartExtraction } from '../../../entities/product';
import type { AllergenExtraction, CheckRecorded, CheckSource, KeywordRange, ProductDetail } from '../../../entities/product';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { formatDateLabel, releaseImages } from '../../../shared/lib';
import type { PreparedImage } from '../../../shared/lib';
import { JOB_STATE, menuPath, productPath } from '../../../shared/type';
import { ImagePicker, JobProgress } from '../../../shared/ui';

interface AllergenCheckFormProps {
    product: ProductDetail;
}

type Decision = 'contains' | 'notContains' | 'undecided';

const SOURCE_OPTIONS: { value: CheckSource; label: string }[] = [
    { value: 'Package', label: 'パッケージ' },
    { value: 'CoopSite', label: 'コープのサイト' },
    { value: 'Flyer', label: 'チラシ' },
    { value: 'Other', label: 'その他' },
];

/** 全文を、当たった言葉の範囲で区切って強調する（範囲はサーバーが元の全文の位置で返す） */
const HighlightedText = ({ text, ranges }: { text: string; ranges: KeywordRange[] }) => {
    // 重なった範囲はつなげてから強調する（「ヘーゼル」と「ヘーゼルナッツ」など）
    const merged: { start: number; end: number }[] = [];
    [...ranges]
        .sort((a, b) => a.start - b.start)
        .forEach((range) => {
            const last = merged[merged.length - 1];
            const end = range.start + range.length;
            if (last && range.start <= last.end) {
                last.end = Math.max(last.end, end);
            } else {
                merged.push({ start: range.start, end });
            }
        });
    const parts: { text: string; isHit: boolean }[] = [];
    let cursor = 0;
    merged.forEach((range) => {
        parts.push({ text: text.slice(cursor, range.start), isHit: false });
        parts.push({ text: text.slice(range.start, range.end), isHit: true });
        cursor = range.end;
    });
    parts.push({ text: text.slice(cursor), isHit: false });
    return (
        <Typography component="div" variant="body2" sx={{ whiteSpace: 'pre-wrap', p: 1.5, bgcolor: 'action.hover', borderRadius: 2 }} aria-label="強調した原材料">
            {parts.map((part, index) =>
                part.isHit ? (
                    <Box key={index} component="mark" sx={{ bgcolor: 'error.light', color: 'error.contrastText', px: 0.25, borderRadius: 0.5, fontWeight: 700 }}>
                        {part.text}
                    </Box>
                ) : (
                    <Fragment key={index}>{part.text}</Fragment>
                ),
            )}
        </Typography>
    );
};

/**
 * SC-08 アレルゲン確認（T5-04）。パッケージを撮る → AI が全文と「含む」の候補を読む → 職員が全文を直し、品目ごとに決めて記録する。
 * AI が「含む」としなかった品目は未確定のまま並べ、「残りをすべて含まないにする」を職員が押して決める（自動で確定しない。FR-PROD-06）。
 * パッケージで記録できるのは、全文があり、すべての品目を決めたときだけ（SR-12・SR-15）。判定・強調の範囲はサーバーで求める。
 */
export const AllergenCheckForm = ({ product }: AllergenCheckFormProps) => {
    const startExtraction = useStartExtraction(product.id);
    const record = useRecordCheck(product.id);
    const [images, setImages] = useState<PreparedImage[]>([]);
    // 画像の準備中に送ると、準備できた分だけが送られる（原材料が写った面が読まれない）ので、準備が終わるまで読み取らせない（#15）
    const [isPreparingImages, setIsPreparingImages] = useState(false);
    const [jobId, setJobId] = useState<string | null>(null);
    const { data: job, isError: isPollError } = useJobPolling<AllergenExtraction>(jobId);
    const [ocrText, setOcrText] = useState<string | null>(null);
    const [text, setText] = useState('');
    const [contamination, setContamination] = useState('');
    const [decisions, setDecisions] = useState<Record<string, Decision>>({});
    const [characterTags, setCharacterTags] = useState<string[]>([]);
    const [flavorTags, setFlavorTags] = useState<string[]>([]);
    const [source, setSource] = useState<CheckSource>('Package');
    const [result, setResult] = useState<CheckRecorded | null>(null);
    const [debouncedText, setDebouncedText] = useState('');
    const { data: hits = [] } = useKeywordHits(debouncedText);
    // AI が読み取った全文・注意書きの当たりも見せる（職員が全文から消しても 🔴 は消えないことを、その場で示す。SR-14）
    const { data: ocrHits = [] } = useKeywordHits(ocrText ?? '');
    const [debouncedContamination, setDebouncedContamination] = useState('');
    const { data: contaminationHits = [] } = useKeywordHits(debouncedContamination);
    const lostWords = ocrHits.filter((o) => !hits.some((h) => h.allergenId === o.allergenId));

    // 全文を打ち終えてから当たった言葉を調べる（打つたびにサーバーへ送らない）
    useEffect(() => {
        const timer = window.setTimeout(() => setDebouncedText(text), 400);
        return () => window.clearTimeout(timer);
    }, [text]);
    useEffect(() => {
        const timer = window.setTimeout(() => setDebouncedContamination(contamination), 400);
        return () => window.clearTimeout(timer);
    }, [contamination]);

    // 読み取りが終わったら、候補を入力欄に入れる（描画の中で1回だけ）
    const [appliedJobId, setAppliedJobId] = useState<string | null>(null);
    if (job && job.state === JOB_STATE.SUCCEEDED && job.result && appliedJobId !== job.id) {
        setAppliedJobId(job.id);
        setOcrText(job.result.rawIngredients);
        setText(job.result.rawIngredients);
        setContamination(job.result.contaminationNote ?? '');
        setDecisions(Object.fromEntries(job.result.containsAllergenIds.map((id) => [id, 'contains' as Decision])));
        setCharacterTags(job.result.characterTags);
        setFlavorTags(job.result.flavorTags);
    }

    // 画面を離れたら画像を破棄する（U-02）
    const imagesRef = useRef(images);
    useEffect(() => {
        imagesRef.current = images;
    }, [images]);
    useEffect(() => () => releaseImages(imagesRef.current), []);

    const isReading = !!jobId && !isJobFinished(job) && !isPollError;
    const undecided = product.checkTargets.filter((t) => (decisions[t.id] ?? 'undecided') === 'undecided');
    const hitAllergenIds = new Set(hits.map((h) => h.allergenId));
    const isPackage = source === 'Package';
    const canRecord = isPackage ? text.trim().length > 0 && undecided.length === 0 : true;
    const manufacturer = manufacturerCheckText(product.checkTargets.filter((t) => t.isAvoided && t.labelNotGuaranteed).map((t) => t.name));

    const handleRecord = () =>
        record.mutate(
            {
                source,
                sourceNote: null,
                rawIngredients: text.trim() || null,
                contaminationNote: contamination.trim() || null,
                items: Object.entries(decisions)
                    .filter(([, decision]) => decision !== 'undecided')
                    .map(([allergenId, decision]) => ({ allergenId, contains: decision === 'contains' })),
                characterTags,
                flavorTags,
            },
            { onSuccess: setResult },
        );

    if (result) {
        const isBlocked = result.judgement.category === 'Blocked';
        return (
            <Stack spacing={2}>
                <Card>
                    <CardContent>
                        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                            記録しました
                        </Typography>
                        <AllergenJudgementLine judgement={result.judgement} isInMenu={result.usages.length > 0} showManufacturerCheck />
                    </CardContent>
                </Card>
                {result.usages.length > 0 ? (
                    <Alert severity={isBlocked ? 'error' : 'info'} variant={isBlocked ? 'filled' : 'standard'}>
                        <Typography sx={{ fontWeight: 700 }}>
                            {isBlocked ? <BlockedInMenuNotice /> : 'この商品を使う今日以降の献立'}
                        </Typography>
                        {result.usages.map((usage) => (
                            <Box key={`${usage.menuId}-${usage.date}`}>
                                <Link component={RouterLink} to={menuPath(usage.menuId, 4)} color="inherit" sx={{ fontWeight: 600 }}>
                                    {formatDateLabel(usage.date)}の献立で使われています
                                </Link>
                            </Box>
                        ))}
                    </Alert>
                ) : (
                    <Alert severity="info">この商品を使う今日以降の献立はありません。</Alert>
                )}
                <Box>
                    <Button component={RouterLink} to={productPath(product.id)}>
                        商品の詳細へ戻る
                    </Button>
                </Box>
            </Stack>
        );
    }

    return (
        <Stack spacing={2}>
            <Card>
                <CardContent>
                    <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                        1. パッケージの原材料表示を撮る（4枚まで）
                    </Typography>
                    <ImagePicker images={images} onChange={setImages} maxImages={4} acceptPdf={false} isDisabled={isReading || startExtraction.isPending} onProcessingChange={setIsPreparingImages} />
                    <Stack direction="row" spacing={1} sx={{ mt: 1.5 }}>
                        <Button
                            variant="contained"
                            disabled={images.length === 0 || isPreparingImages || isReading || startExtraction.isPending}
                            onClick={() => startExtraction.mutate(images, { onSuccess: setJobId })}
                        >
                            {job?.state === JOB_STATE.FAILED ? 'もう一度読み取る' : '読み取る'}
                        </Button>
                    </Stack>
                    {isReading && <JobProgress stage={job?.stage} fallbackStage="原材料表示を読み取っています" />}
                    {startExtraction.isError && <Alert severity="error" sx={{ mt: 1 }}>{errorMessageOf(startExtraction.error)}</Alert>}
                    {job?.state === JOB_STATE.FAILED && (
                        <Alert severity="error" sx={{ mt: 1, whiteSpace: 'pre-wrap' }}>
                            {job.errorMessages.join('\n')}
                        </Alert>
                    )}
                    {isPollError && <Alert severity="warning" sx={{ mt: 1 }}>読み取りの状態を確かめられませんでした。原材料の全文を手で入れることもできます。</Alert>}
                </CardContent>
            </Card>

            <Card>
                <CardContent>
                    <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                        2. 原材料の全文を、写真と見比べて直す
                    </Typography>
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: images.length > 0 ? '1fr 1fr' : '1fr' }, gap: 2 }}>
                        {images.length > 0 && (
                            <Stack spacing={1}>
                                {images.map((image) => (
                                    <Box key={image.id} component="img" src={image.previewUrl} alt={image.label} sx={{ width: '100%', borderRadius: 2, border: 1, borderColor: 'divider' }} />
                                ))}
                            </Stack>
                        )}
                        <Stack spacing={1.5}>
                            <TextField
                                label="原材料の全文"
                                multiline
                                minRows={5}
                                value={text}
                                onChange={(event) => setText(event.target.value)}
                                helperText="読み取れなかったときや AI が使えないときは、パッケージを見て手で入れてください"
                            />
                            {/* 強調の範囲は、範囲を求めた全文（入力が落ち着いたあとの全文）に当てる */}
                            {debouncedText.trim() && <HighlightedText text={debouncedText} ranges={hits} />}
                            {hits.length > 0 && <Alert severity="error"><KeywordHitNotice words={hits} where="原材料" /></Alert>}
                            {lostWords.length > 0 && <Alert severity="error"><KeywordHitNotice words={lostWords} where="読み取った全文" /></Alert>}
                            <TextField label="同じ工場・コンタミの注意書き" value={contamination} onChange={(event) => setContamination(event.target.value)} />
                            {contaminationHits.length > 0 && <Alert severity="warning"><KeywordHitNotice words={contaminationHits} where="注意書き" /></Alert>}
                        </Stack>
                    </Box>
                </CardContent>
            </Card>

            <Card>
                <CardContent>
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' }, mb: 1 }}>
                        <Typography variant="h3" component="h2" sx={{ flex: 1 }}>
                            3. 品目ごとに「含む / 含まない」を決める
                        </Typography>
                        <Button
                            variant="outlined"
                            disabled={undecided.length === 0}
                            onClick={() => setDecisions({ ...decisions, ...Object.fromEntries(undecided.map((t) => [t.id, 'notContains' as Decision])) })}
                        >
                            残りをすべて含まないにする（{undecided.length}件）
                        </Button>
                    </Stack>
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                        AI が「含む」と読んだ品目だけを選んであります。ほかの品目は、パッケージを見て決めてください。
                    </Typography>
                    {manufacturer && (
                        <Typography variant="body2" sx={{ mb: 1 }}>
                            {manufacturer}
                        </Typography>
                    )}
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', lg: '1fr 1fr 1fr' }, gap: 1 }}>
                        {product.checkTargets.map((target) => {
                            const decision = decisions[target.id] ?? 'undecided';
                            return (
                                <Box key={target.id} sx={{ border: 1, borderColor: hitAllergenIds.has(target.id) ? 'error.main' : 'divider', borderRadius: 2, p: 1 }}>
                                    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', mb: 0.5 }}>
                                        <Typography sx={{ fontWeight: target.isAvoided ? 700 : 400, flex: 1 }}>{target.name}</Typography>
                                        {target.isAvoided && <Chip size="small" label="避ける品目" color="primary" variant="outlined" />}
                                    </Stack>
                                    <ToggleButtonGroup
                                        size="small"
                                        exclusive
                                        value={decision}
                                        onChange={(_, value: Decision | null) => value && setDecisions({ ...decisions, [target.id]: value })}
                                        aria-label={`${target.name}を含むか`}
                                    >
                                        <ToggleButton value="contains">含む</ToggleButton>
                                        <ToggleButton value="notContains">含まない</ToggleButton>
                                        <ToggleButton value="undecided">未確定</ToggleButton>
                                    </ToggleButtonGroup>
                                </Box>
                            );
                        })}
                    </Box>
                    {(characterTags.length > 0 || flavorTags.length > 0) && (
                        <Stack direction="row" spacing={0.5} sx={{ mt: 1.5, flexWrap: 'wrap', alignItems: 'center' }} useFlexGap>
                            <Typography variant="body2">タグの候補：</Typography>
                            {characterTags.map((tag) => (
                                <Chip key={`c-${tag}`} label={tag} size="small" onDelete={() => setCharacterTags(characterTags.filter((t) => t !== tag))} />
                            ))}
                            {flavorTags.map((tag) => (
                                <Chip key={`f-${tag}`} label={tag} size="small" variant="outlined" onDelete={() => setFlavorTags(flavorTags.filter((t) => t !== tag))} />
                            ))}
                        </Stack>
                    )}
                </CardContent>
            </Card>

            <Card>
                <CardContent>
                    <FormControl>
                        <FormLabel>4. 情報源</FormLabel>
                        <RadioGroup row value={source} onChange={(event) => setSource(event.target.value as CheckSource)}>
                            {SOURCE_OPTIONS.map((option) => (
                                <FormControlLabel key={option.value} value={option.value} control={<Radio />} label={option.label} />
                            ))}
                        </RadioGroup>
                    </FormControl>
                    {!isPackage && (
                        <Alert severity="warning" sx={{ mt: 1 }}>
                            <NonPackageSourceNotice />
                        </Alert>
                    )}
                    {isPackage && !canRecord && (
                        <Typography variant="body2" sx={{ mt: 1, color: 'warning.dark' }}>
                            {text.trim().length === 0 ? '原材料の全文を入れてください。' : ''}
                            {undecided.length > 0 ? `まだ決めていない品目が${undecided.length}件あります。` : ''}
                        </Typography>
                    )}
                    {record.isError && (
                        <Alert severity="error" sx={{ mt: 1, whiteSpace: 'pre-wrap' }}>
                            {errorMessageOf(record.error)}
                        </Alert>
                    )}
                    <Box sx={{ mt: 1.5 }}>
                        <Button variant="contained" size="large" disabled={!canRecord || isReading || record.isPending} onClick={handleRecord}>
                            {isPackage ? 'パッケージで確認して記録' : 'パッケージ以外の情報として記録'}
                        </Button>
                    </Box>
                </CardContent>
            </Card>
        </Stack>
    );
};
