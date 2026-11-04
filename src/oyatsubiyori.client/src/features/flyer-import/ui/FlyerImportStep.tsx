import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Link from '@mui/material/Link';
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
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { releaseImages } from '../../../shared/lib';
import type { PreparedImage } from '../../../shared/lib';
import { JOB_STATE, SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { ClaudeText, JobProgress } from '../../../shared/ui';
import { FlyerImagePicker } from './FlyerImagePicker';
import { FlyerItemGrid } from './FlyerItemGrid';

interface FlyerImportStepProps {
    menuId: string;
    listedCount: number;
    onNext: () => void;
    /** 画面を離れると消える入力（何が消えるか。なければ null）が変わったとき。離れる前の確認に使う */
    onUnsavedInputChange?: (unsavedInput: string | null) => void;
    /**
     * PC（md 以上）で、撮影の欄と読み取り中の表示の左右を内側へ寄せる幅（px）。ページのステッパーの最初と最後のラベルの端に、
     * これらの端をそろえるために使う（見出しの行と同じ幅になる）
     */
    pickerInset?: { left: number; right: number } | null;
}

/**
 * SC-09 手順1 チラシ取り込み（T3-10）。撮影・ファイル → 読み取り（バックグラウンド。画面を閉じても続く）→ 結果の表で確定（行の下の詳細欄）。
 * 取り込み済みなら飛ばせる。読み取りに失敗しても、選んだ画像は残して「もう一度試す」と手入力ができる。
 */
export const FlyerImportStep = ({ menuId, listedCount, onNext, onUnsavedInputChange, pickerInset }: FlyerImportStepProps) => {
    const { data: flyerImport, dataUpdatedAt: flyerImportUpdatedAt, errorUpdatedAt: flyerImportErrorAt } = useFlyerImport(menuId);
    const startReading = useStartFlyerReading(menuId);
    const confirmSame = useConfirmSameFlyerItems(menuId);
    const discard = useDiscardFlyerImport(menuId);
    const refresh = useRefreshFlyerImport(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [images, setImages] = useState<PreparedImage[]>([]);
    const [startedJobId, setStartedJobId] = useState<string | null>(null);
    const [manualName, setManualName] = useState<string | null>(null);
    const [isPreparingImages, setIsPreparingImages] = useState(false);

    const runningJob = flyerImport.job && !isJobFinished(flyerImport.job) ? flyerImport.job.id : null;
    const jobId = startedJobId ?? runningJob;
    const { data: job, dataUpdatedAt: jobUpdatedAt, isError: isPollError, refetch } = useJobPolling<string>(jobId);
    // 読み取りが成功してから、取り込みの状態（読み取った商品）を読み直し終えるまでは、読み取り中のままにする
    // （読み直す前の古い状態には読み取った商品がないので、撮影の欄が一瞬出てしまう）。読み直しに失敗したときは待ち続けない
    const isAwaitingResult = job?.state === JOB_STATE.SUCCEEDED && Math.max(flyerImportUpdatedAt, flyerImportErrorAt) < jobUpdatedAt;
    const isReading = !!jobId && (!isJobFinished(job) || isAwaitingResult) && !isPollError;
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

    // 読み取りに送っていない画像（準備中を含む）があるかを、離れる前の確認のために親へ知らせる。
    // 送信中・読み取り中・状態を確かめられないときは、読み取りがサーバーで続くので数えない。この手順を離れたら、もうない
    const hasUnsentImages = (images.length > 0 || isPreparingImages) && !isReading && !startReading.isPending && !isPollError;
    const unsavedInput = hasUnsentImages ? 'まだ読み取っていないチラシの画像' : null;
    useEffect(() => {
        onUnsavedInputChange?.(unsavedInput);
    }, [unsavedInput, onUnsavedInputChange]);
    useEffect(() => () => onUnsavedInputChange?.(null), [onUnsavedInputChange]);

    const handleRead = () => {
        startReading.mutate(images, {
            onSuccess: (id) => setStartedJobId(id),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    // 確定前の読み取り結果がなければ、いつでも撮影の欄を出す（取り込み済みの週なら、取り込み直しになる）
    const showPicker = !isReading && !session;

    const showResult = !!session && !isReading;

    return (
        <Stack
            spacing={2}
            // 撮影の欄と読み取り中の表示は、PC ではステッパーの最初と最後のラベルの端に左右をそろえる。
            // 撮影の欄は残りの高さを埋める（ページ全体はスクロールさせず、追加したチラシの一覧の中だけをスクロールする）
            // 読み取った商品の表も、PC では残りの高さを埋めて表の中だけをスクロールする
            sx={
                showPicker || isReading
                    ? {
                          height: { md: showPicker ? '100%' : undefined },
                          pl: { md: `${pickerInset?.left ?? 0}px` },
                          pr: { md: `${pickerInset?.right ?? 0}px` },
                      }
                    : showResult
                      ? { height: { md: '100%' } }
                      : undefined
            }
        >
            {failedJob && !isReading && (
                <Alert severity="error" sx={{ whiteSpace: 'pre-wrap', flexShrink: 0 }}>
                    チラシの読み取りに失敗しました。{failedJob.errorMessages.join('\n')}
                </Alert>
            )}

            {!session && listedCount > 0 && !isReading && (
                <Alert
                    severity="success"
                    sx={{ flexShrink: 0 }}
                    action={
                        <Button variant="contained" onClick={onNext} sx={{ whiteSpace: 'nowrap' }}>
                            今週の商品へ
                        </Button>
                    }
                >
                    この週はチラシを取り込み済みです（{listedCount}件）。取り込み直すときは、下でチラシを撮ってください（最初に確定したときに、前の取り込みと入れ替わります）。
                </Alert>
            )}

            {showPicker && (
                <>
                    <Card component="section" aria-labelledby="flyer-import-heading" sx={{ display: 'flex', flexDirection: 'column', flex: { md: '1 1 auto' }, minHeight: { md: 400 } }}>
                        {/* 低い画面で中身が入りきらないときは、カードの中をスクロールする（Card は overflow: hidden なので、切れて見えなくならないように） */}
                        <CardContent sx={{ flex: { md: '1 1 auto' }, minHeight: 0, overflowY: { md: 'auto' }, display: 'flex', flexDirection: 'column', gap: 2, px: { sm: 3 }, '&:last-child': { pb: { xs: 2, sm: 2.5 } } }}>
                            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 0.5, sm: 2 }} sx={{ alignItems: { sm: 'baseline' }, justifyContent: 'space-between', flexShrink: 0 }}>
                                <Typography id="flyer-import-heading" variant="h3" component="h2">
                                    チラシを取り込む
                                </Typography>
                                <Typography variant="body2" color="text.secondary">
                                    画像やPDFを追加すると、載っている商品を読み取ります
                                </Typography>
                            </Stack>
                            <FlyerImagePicker images={images} onChange={setImages} maxImages={10} isDisabled={startReading.isPending} onProcessingChange={setIsPreparingImages} />
                        </CardContent>
                    </Card>

                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 1.5, sm: 2 }} useFlexGap sx={{ alignItems: { sm: 'center' }, justifyContent: 'space-between', flexWrap: 'wrap', flexShrink: 0 }}>
                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={{ xs: 1, sm: 1.5 }} sx={{ alignItems: { sm: 'center' } }}>
                            <Button variant="contained" size="large" onClick={handleRead} disabled={images.length === 0 || startReading.isPending} sx={{ px: 4 }}>
                                {failedJob ? 'もう一度試す' : '読み取る'}
                            </Button>
                            {images.length === 0 && (
                                <Typography variant="body2" color="text.secondary">
                                    チラシを追加すると読み取れます
                                </Typography>
                            )}
                        </Stack>
                        <Typography variant="body2">
                            チラシがない場合は{' '}
                            <Link component="button" type="button" onClick={() => setManualName('')} sx={{ fontWeight: 700, verticalAlign: 'baseline' }}>
                                商品を手で入力する →
                            </Link>
                        </Typography>
                    </Stack>
                </>
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

            {session && showResult && (
                <>
                    <Card sx={{ flexShrink: 0 }}>
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
                    {session.reviewFailed && (
                        <Alert severity="info" sx={{ flexShrink: 0 }}>
                            読み取りの検証ができませんでした。読み取った内容を、チラシと見比べて確かめてください。
                        </Alert>
                    )}
                    <FlyerItemGrid key={session.id} menuId={menuId} items={session.items} />
                    {session.missedProducts.length > 0 && (
                        <Card sx={{ flexShrink: 0 }}>
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
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ flexShrink: 0 }}>
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

            {manualName !== null && <ManualItemDialog menuId={menuId} initialName={manualName} onClose={() => setManualName(null)} />}
        </Stack>
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

