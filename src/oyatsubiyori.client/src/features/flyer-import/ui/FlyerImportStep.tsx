import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import {
    useAddManualFlyerItem,
    useConfirmSameFlyerItems,
    useDiscardFlyerImport,
    useFlyerImport,
    useRefreshFlyerImport,
    useStartFlyerReading,
} from '../../../entities/flyer';
import type { FlyerItem } from '../../../entities/flyer';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { releaseImages } from '../../../shared/lib';
import type { PreparedImage } from '../../../shared/lib';
import { JOB_STATE, SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { ClaudeText, ImagePicker, JobProgress } from '../../../shared/ui';
import { verdictLabel } from '../model/labels';
import { FlyerConfirmDialog } from './FlyerConfirmDialog';

interface FlyerImportStepProps {
    menuId: string;
    listedCount: number;
    onNext: () => void;
}

const CHOICE_LABEL: Record<string, string> = {
    Same: '登録済みの商品と同じ',
    Variant: '味違い・内容量違いとして登録',
    New: '新しい商品として登録',
    Skip: '取り込まない',
};

/**
 * SC-09 手順1 チラシ取り込み（T3-10）。撮影・ファイル → 読み取り（バックグラウンド。画面を閉じても続く）→ 結果の一覧で確定。
 * 取り込み済みなら飛ばせる。読み取りに失敗しても、選んだ画像は残して「もう一度試す」と手入力ができる。
 */
export const FlyerImportStep = ({ menuId, listedCount, onNext }: FlyerImportStepProps) => {
    const { data: flyerImport } = useFlyerImport(menuId);
    const startReading = useStartFlyerReading(menuId);
    const confirmSame = useConfirmSameFlyerItems(menuId);
    const discard = useDiscardFlyerImport(menuId);
    const refresh = useRefreshFlyerImport(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [images, setImages] = useState<PreparedImage[]>([]);
    const [startedJobId, setStartedJobId] = useState<string | null>(null);
    const [editing, setEditing] = useState<FlyerItem | null>(null);
    const [manualName, setManualName] = useState<string | null>(null);

    const runningJob = flyerImport.job && !isJobFinished(flyerImport.job) ? flyerImport.job.id : null;
    const jobId = startedJobId ?? runningJob;
    const { data: job, isError: isPollError, refetch } = useJobPolling<string>(jobId);
    const isReading = !!jobId && !isJobFinished(job) && !isPollError;
    // 失敗した読み取りは、画面を開き直しても示す（最新の処理が失敗していれば）
    const failedJob = job?.state === JOB_STATE.FAILED ? job : !startedJobId && flyerImport.job?.state === JOB_STATE.FAILED ? flyerImport.job : null;
    const session = flyerImport.session;

    // 読み取りが終わったら、選んでいた画像はもう使わない（画面の状態は描画の中で切り替える）
    const [handledJobId, setHandledJobId] = useState<string | null>(null);
    if (job && job.state === JOB_STATE.SUCCEEDED && handledJobId !== job.id) {
        setHandledJobId(job.id);
        setImages([]);
    }

    // 画面を離れたとき・読み取りが終わったときに、画像を破棄する（U-02）
    const imagesRef = useRef(images);
    useEffect(() => {
        if (job && isJobFinished(job)) {
            refresh();
            if (job.state === JOB_STATE.SUCCEEDED) {
                releaseImages(imagesRef.current);
            }
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [job?.state]);
    useEffect(() => {
        imagesRef.current = images;
    }, [images]);
    useEffect(() => () => releaseImages(imagesRef.current), []);

    const handleRead = () => {
        startReading.mutate(images, {
            onSuccess: (id) => setStartedJobId(id),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    // 確定前の読み取り結果がなければ、いつでも撮影の欄を出す（取り込み済みの週なら、取り込み直しになる）
    const showPicker = !isReading && !session;

    return (
        <Stack spacing={2}>
            {failedJob && !isReading && (
                <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                    チラシの読み取りに失敗しました。{failedJob.errorMessages.join('\n')}
                </Alert>
            )}

            {!session && listedCount > 0 && !isReading && (
                <Alert
                    severity="success"
                    action={
                        <Button variant="contained" onClick={onNext}>
                            今週の商品へ
                        </Button>
                    }
                >
                    この週はチラシを取り込み済みです（{listedCount}件）。取り込み直すときは、下でチラシを撮ってください（最初に確定したときに、前の取り込みと入れ替わります）。
                </Alert>
            )}

            {showPicker && (
                <Card>
                    <CardContent>
                        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                            チラシを取り込む
                        </Typography>
                        <Typography variant="body2" sx={{ mb: 2 }}>
                            小さい文字が読めるよう、1ページを半分ずつ近づけて撮ってください。PDF はページごとに取り込みます（いらないページは × で外せます）。
                        </Typography>
                        <ImagePicker images={images} onChange={setImages} maxImages={10} acceptPdf isDisabled={startReading.isPending} />

                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mt: 2 }}>
                            <Button variant="contained" size="large" onClick={handleRead} disabled={images.length === 0 || startReading.isPending}>
                                {failedJob ? 'もう一度試す' : '読み取る'}
                            </Button>
                            <Button onClick={() => setManualName('')}>手で入力する</Button>
                        </Stack>
                    </CardContent>
                </Card>
            )}

            {isPollError && (
                <Alert severity="warning" action={<Button color="inherit" onClick={() => void refetch()}>もう一度確かめる</Button>}>
                    読み取りの状態を確かめられませんでした。通信を確かめてください。
                </Alert>
            )}

            {isReading && (
                <Card>
                    <CardContent>
                        <JobProgress stage={job?.stage} fallbackStage="チラシを読み取っています" note="画面を閉じても読み取りは続きます。あとで戻ると結果が出ます。" />
                    </CardContent>
                </Card>
            )}

            {session && !isReading && (
                <>
                    <Card>
                        <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 1.5, alignItems: { md: 'center' } }}>
                            <Box sx={{ flex: 1 }}>
                                <Typography variant="h3" component="h2">
                                    読み取った商品 {session.items.length}件
                                </Typography>
                                <Typography variant="body2" sx={{ color: session.unconfirmedCount > 0 ? 'warning.dark' : 'success.main', fontWeight: 600 }}>
                                    {session.unconfirmedCount > 0 ? `確定していない ${session.unconfirmedCount}件は、今週の商品に入りません` : 'すべて確定しました'}
                                </Typography>
                            </Box>
                            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                                <Button
                                    variant="outlined"
                                    disabled={confirmSame.isPending || !session.items.some((i) => i.canBulkConfirm && !i.confirmation)}
                                    onClick={() =>
                                        confirmSame.mutate(undefined, {
                                            onSuccess: (count) => enqueueSnackbar(`「同じ商品」の ${count}件を確定しました`, { variant: 'success' }),
                                            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
                                        })
                                    }
                                >
                                    「同じ商品」をまとめて確定
                                </Button>
                                <Button variant="contained" onClick={onNext} disabled={listedCount === 0}>
                                    今週の商品へ
                                </Button>
                            </Stack>
                        </CardContent>
                    </Card>
                    {session.reviewFailed && <Alert severity="info">読み取りの検証ができませんでした。読み取った内容を、チラシと見比べて確かめてください。</Alert>}
                    <Stack spacing={1.5}>
                        {session.items.map((item) => (
                            <FlyerItemCard key={item.id} item={item} onEdit={() => setEditing(item)} />
                        ))}
                    </Stack>
                    {session.missedProducts.length > 0 && (
                        <Card>
                            <CardContent>
                                <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                                    読み落としているかもしれない商品
                                </Typography>
                                {session.missedProducts.map((missed) => (
                                    <Stack key={missed.name} direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                                        <Box sx={{ flex: 1 }}>
                                            <Typography>{missed.name}</Typography>
                                            <ClaudeText text={missed.reason} color="text.secondary" />
                                        </Box>
                                        <Button onClick={() => setManualName(missed.name)}>手で入力する</Button>
                                    </Stack>
                                ))}
                            </CardContent>
                        </Card>
                    )}
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                        <Button onClick={() => setManualName('')}>商品を手で入力する</Button>
                        <Button
                            color="inherit"
                            onClick={() => discard.mutate()}
                        >
                            チラシを取り込み直す
                        </Button>
                    </Stack>
                </>
            )}

            {editing && (
                <FlyerConfirmDialog
                    key={editing.id}
                    menuId={menuId}
                    item={editing}
                    isOpen
                    onClose={() => setEditing(null)}
                    onSuccess={() => {
                        enqueueSnackbar(`「${editing.read.name}」を確定しました`, { variant: 'success' });
                        setEditing(null);
                    }}
                />
            )}
            {manualName !== null && <ManualItemDialog menuId={menuId} initialName={manualName} onClose={() => setManualName(null)} />}
        </Stack>
    );
};

interface FlyerItemCardProps {
    item: FlyerItem;
    onEdit: () => void;
}

/** 取り込みの1商品（同じ商品かどうか・アレルゲン表記・指摘・確定済みの印） */
const FlyerItemCard = ({ item, onEdit }: FlyerItemCardProps) => {
    const read = item.read;
    const same = item.sameProducts[0];
    return (
        <Card component="section" aria-label={read.name} sx={{ borderColor: item.confirmation ? 'success.light' : undefined }}>
            <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 1.5, '&:last-child': { pb: 2 } }}>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                        <Typography sx={{ fontWeight: 700 }}>{read.name}</Typography>
                        {item.isManual && <Chip size="small" label="手入力" />}
                        {item.confirmation && <Chip size="small" color="success" label={`確定済み：${CHOICE_LABEL[item.confirmation.choice]}`} />}
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                        {[read.volume, read.priceIncludingTax ? `税込 ${read.priceIncludingTax}円` : '税込価格なし', read.orderNo ? `注文番号 ${read.orderNo}` : null, read.snackType ? SNACK_TYPE_LABEL[read.snackType] : null]
                            .filter(Boolean)
                            .join('・')}
                    </Typography>
                    <Typography variant="body2" sx={{ mt: 0.5 }}>
                        アレルゲン表記：{read.allergenLabelText ? `「${read.allergenLabelText}」` : <strong>表記なし</strong>}
                        {item.unmappedTerms.length > 0 && <Typography component="span" color="error" variant="body2">（品目に当てはめられない語：{item.unmappedTerms.join('・')}）</Typography>}
                    </Typography>
                    <Typography variant="body2" sx={{ mt: 0.5 }}>
                        同じ商品かどうか：
                        {same
                            ? `登録済みの「${same.name}」と同じ${same.makerCompared ? '' : '（メーカー未確認）'}`
                            : item.volumeVariants[0]
                              ? `「${item.volumeVariants[0].name}」の内容量違い`
                              : item.judgement
                                ? `AI の判定：${verdictLabel(item.judgement.verdict)}${item.judgement.productName ? `（${item.judgement.productName}）` : ''}`
                                : item.candidates.length > 0
                                  ? `似ている商品があります（${item.candidates.map((c) => c.name).join('・')}）`
                                  : '新しい商品'}
                    </Typography>
                    {item.judgement && <ClaudeText text={item.judgement.reason} color="text.secondary" variant="caption" />}
                    {item.descriptionHitWords.length > 0 && (
                        <Alert severity="warning" sx={{ mt: 1, py: 0 }}>
                            商品名・説明文に避ける品目の言葉があります：{item.descriptionHitWords.join('・')}
                        </Alert>
                    )}
                    {item.discrepancies.length > 0 && (
                        <Alert severity={item.discrepancies.some((d) => d.isAvoided) ? 'error' : 'warning'} sx={{ mt: 1, py: 0 }}>
                            パッケージでの確認と食い違い：{item.discrepancies.map((d) => d.allergen.name).join('・')}
                        </Alert>
                    )}
                    {item.issues.map((issue) => (
                        <Alert key={`${issue.source}-${issue.field}-${issue.message}`} severity="warning" icon={false} sx={{ mt: 1, py: 0 }}>
                            ⚠ {issue.field}：<ClaudeText text={issue.message} />
                        </Alert>
                    ))}
                </Box>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                    <Button variant={item.confirmation ? 'text' : 'contained'} onClick={onEdit}>
                        {item.confirmation ? '選び直す' : '確かめて確定'}
                    </Button>
                </Box>
            </CardContent>
        </Card>
    );
};

interface ManualItemDialogProps {
    menuId: string;
    initialName: string;
    onClose: () => void;
}

/** 手入力の商品（チラシと同じ扱いで、同じ商品かどうかを調べてから一覧に並ぶ。U-36） */
const ManualItemDialog = ({ menuId, initialName, onClose }: ManualItemDialogProps) => {
    const addManual = useAddManualFlyerItem(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [name, setName] = useState(initialName);
    const [volume, setVolume] = useState('');
    const [price, setPrice] = useState('');
    const [pieces, setPieces] = useState('');
    const [orderNo, setOrderNo] = useState('');
    const [label, setLabel] = useState('');
    const [snackType, setSnackType] = useState<SnackType>(SNACK_TYPE.SWEET);
    const toNumber = (value: string) => (value.trim() === '' ? null : Number(value));

    return (
        <Dialog open onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>商品を手で入力する</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2}>
                    <TextField label="商品名" value={name} onChange={(event) => setName(event.target.value)} required />
                    <TextField label="内容量" value={volume} onChange={(event) => setVolume(event.target.value)} />
                    <TextField label="税込価格（円）" type="number" value={price} onChange={(event) => setPrice(event.target.value)} />
                    <TextField label="入数（1つで何人分か）" type="number" value={pieces} onChange={(event) => setPieces(event.target.value)} />
                    <TextField label="注文番号" value={orderNo} onChange={(event) => setOrderNo(event.target.value)} />
                    <TextField
                        label="アレルゲン表記（例：乳・小麦）"
                        value={label}
                        onChange={(event) => setLabel(event.target.value)}
                        helperText="チラシに書かれているとおりに入れてください。空なら「表記なし」として扱います"
                    />
                    <TextField select label="おやつの分類" value={snackType} onChange={(event) => setSnackType(event.target.value as SnackType)}>
                        {Object.values(SNACK_TYPE).map((type) => (
                            <MenuItem key={type} value={type}>
                                {SNACK_TYPE_LABEL[type]}
                            </MenuItem>
                        ))}
                    </TextField>
                    {addManual.isError && <Alert severity="error">{errorMessageOf(addManual.error)}</Alert>}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>やめる</Button>
                <Button
                    variant="contained"
                    disabled={!name.trim() || addManual.isPending}
                    onClick={() =>
                        addManual.mutate(
                            {
                                name,
                                maker: null,
                                volume: volume || null,
                                priceIncludingTax: toNumber(price),
                                priceExcludingTax: null,
                                orderNo: orderNo || null,
                                piecesPerPack: toNumber(pieces),
                                snackType,
                                allergenLabelText: label || null,
                            },
                            {
                                onSuccess: () => {
                                    enqueueSnackbar('一覧に足しました。確かめて確定してください', { variant: 'success' });
                                    onClose();
                                },
                            },
                        )
                    }
                >
                    一覧に足す
                </Button>
            </DialogActions>
        </Dialog>
    );
};

