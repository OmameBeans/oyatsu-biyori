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
    useDiscardFlyerImport,
    useFinishFlyerImport,
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
import type { FlyerFinishResult, FlyerSession } from '../../../entities/flyer';
import { finishResultMessages } from '../model/flyerRows';
import type { PendingFlyerEdit } from '../model/flyerRows';
import { FlyerImagePicker } from './FlyerImagePicker';
import { FlyerItemGrid } from './FlyerItemGrid';
import { FlyerRemovedDialog } from './FlyerRemovedDialog';

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
 * SC-09 手順1 チラシ取り込み（T3-10）。撮影・ファイル → 読み取り（バックグラウンド。画面を閉じても続く）→ 結果の表で再入力を直し、
 * 「取り込みを終える」1回で取り込めるの商品をまとめて取り込む（新しい商品は登録し、登録済みの商品は上書きする。D-101）。
 * 取り込み済みなら飛ばせる。読み取りに失敗しても、選んだ画像は残して「もう一度試す」と手入力ができる。
 */
export const FlyerImportStep = ({ menuId, listedCount, onNext, onUnsavedInputChange, pickerInset }: FlyerImportStepProps) => {
    const { data: flyerImport, dataUpdatedAt: flyerImportUpdatedAt, errorUpdatedAt: flyerImportErrorAt } = useFlyerImport(menuId);
    const startReading = useStartFlyerReading(menuId);
    const discard = useDiscardFlyerImport(menuId);
    const refresh = useRefreshFlyerImport(menuId);
    const finish = useFinishFlyerImport(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [images, setImages] = useState<PreparedImage[]>([]);
    const [startedJobId, setStartedJobId] = useState<string | null>(null);
    const [manualName, setManualName] = useState<string | null>(null);
    const [isPreparingImages, setIsPreparingImages] = useState(false);
    // 表で開いている行のフォームで、直した内容をまだ送っていない状態（「取り込みを終える」の前に、反映するか捨てるかを聞く。09 の 5.1）
    const [pendingEdit, setPendingEdit] = useState<PendingFlyerEdit | null>(null);
    const [isPendingEditDialogOpen, setIsPendingEditDialogOpen] = useState(false);
    const [isApplyingPendingEdit, setIsApplyingPendingEdit] = useState(false);
    // 変えると、開いている行のフォームを作り直す（直した内容を捨てる）
    const [formResetKey, setFormResetKey] = useState(0);
    // 取り込みを終えた結果（一覧の上に出す。どの取り込みの結果かも持つ）
    const [finishResult, setFinishResult] = useState<{ sessionId: string; result: FlyerFinishResult } | null>(null);
    // 表の下に置くと画面全体がスクロールしてしまうので、外した掲載品と読み落としの指摘はダイアログで出す
    const [isRemovedOpen, setIsRemovedOpen] = useState(false);
    const [isMissedOpen, setIsMissedOpen] = useState(false);

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

    // 画像の準備中に送ると、準備できた分だけが送られるので、準備が終わるまで読み取らせない（#9）
    const canRead = images.length > 0 && !isPreparingImages && !startReading.isPending;

    const handleRead = () => {
        startReading.mutate(images, {
            onSuccess: (id) => setStartedJobId(id),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    // 確定前の読み取り結果がなければ、いつでも撮影の欄を出す（取り込み済みの週なら、取り込み直しになる）
    const showPicker = !isReading && !session;

    const showResult = !!session && !isReading;
    const shownFinishResult = session && finishResult?.sessionId === session.id ? finishResult.result : null;

    /** 取り込みを終える（どれを取り込むかはサーバーが分類し直して決める。使えない・再入力は取り込まない） */
    const runFinish = () => {
        if (!session) {
            return;
        }
        const sessionId = session.id;
        finish.mutate(undefined, {
            onSuccess: (result) => {
                setFinishResult({ sessionId, result });
                finishResultMessages(result).forEach(({ message, variant }) => enqueueSnackbar(message, { variant }));
                // 取り込んだ商品があれば、次の手順（今週の商品）へ進む。1件も取り込まなかったときは、結果を見られるようこの画面に残る
                if (result.createdCount + result.updatedCount > 0) {
                    onNext();
                }
            },
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    const handleFinishClick = () => {
        if (pendingEdit) {
            setIsPendingEditDialogOpen(true);
            return;
        }
        runFinish();
    };

    /** 直している途中の内容を反映してから終える（反映に失敗したら終えない） */
    const handleApplyAndFinish = async () => {
        if (!pendingEdit) {
            setIsPendingEditDialogOpen(false);
            runFinish();
            return;
        }
        setIsApplyingPendingEdit(true);
        try {
            await pendingEdit.apply();
            setIsPendingEditDialogOpen(false);
            runFinish();
        } catch (error) {
            enqueueSnackbar(`直した内容を反映できませんでした。取り込みは終えていません。${errorMessageOf(error)}`, { variant: 'error' });
        } finally {
            setIsApplyingPendingEdit(false);
        }
    };

    /** 直している途中の内容を捨ててから終える */
    const handleDiscardAndFinish = () => {
        setIsPendingEditDialogOpen(false);
        setPendingEdit(null);
        setFormResetKey((key) => key + 1);
        runFinish();
    };

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
                      ? // PC では表が残りの高さを埋め、表の中だけをスクロールする（表の上下は固定）。
                        // 表の最低の高さも入らないほど低い画面のときだけ、手順1の枠の中をスクロールする
                        { height: { md: '100%' }, overflowY: { md: 'auto' } }
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
                    この週はチラシを取り込み済みです（{listedCount}件）。取り込み直すときは、下でチラシを撮ってください（取り込みを終えたときに、前の取り込みと入れ替わります）。
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
                            <Button variant="contained" size="large" onClick={handleRead} disabled={!canRead} sx={{ px: 4 }}>
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
                        <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, flexWrap: { md: 'wrap' }, gap: 1.5, alignItems: { md: 'center' } }}>
                            <Box sx={{ flex: 1, minWidth: 0 }}>
                                <Typography variant="h3" component="h2">
                                    読み取った商品 {session.items.length}件
                                </Typography>
                                <Typography variant="body2" sx={{ color: session.notListed.total > 0 ? 'warning.dark' : 'success.main', fontWeight: 600 }}>
                                    {notListedText(session)}
                                </Typography>
                                <Typography variant="body2" color="text.secondary">
                                    {finishSummaryText(session)}
                                </Typography>
                            </Box>
                            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                                <Button variant="contained" onClick={handleFinishClick} disabled={finish.isPending || isApplyingPendingEdit}>
                                    {finish.isPending ? '取り込んでいます…' : `取り込みを終える（取り込める ${session.finish.readyCount}件）`}
                                </Button>
                                {!shownFinishResult && (
                                    <Button variant="outlined" onClick={onNext} disabled={listedCount === 0}>
                                        今週の商品へ
                                    </Button>
                                )}
                            </Stack>
                        </CardContent>
                    </Card>
                    {shownFinishResult && (
                        <Alert
                            severity="success"
                            sx={{ flexShrink: 0 }}
                            action={
                                <Button variant="contained" onClick={onNext} disabled={listedCount === 0} sx={{ whiteSpace: 'nowrap' }}>
                                    今週の商品へ
                                </Button>
                            }
                        >
                            取り込みを終えました（新しく登録 {shownFinishResult.createdCount}件・登録済みの商品を上書き {shownFinishResult.updatedCount}件）。
                            {shownFinishResult.notListedUnusableCount + shownFinishResult.notListedNeedsInputCount > 0 &&
                                ` 使えない ${shownFinishResult.notListedUnusableCount}件・再入力 ${shownFinishResult.notListedNeedsInputCount}件は今週の商品に入りません。`}
                        </Alert>
                    )}
                    {session.reviewFailed && (
                        <Alert severity="info" sx={{ flexShrink: 0 }}>
                            読み取りの検証ができませんでした。献立の確定のときに、チラシの表記と見比べてください。
                        </Alert>
                    )}
                    <FlyerItemGrid key={session.id} menuId={menuId} session={session} onPendingEditChange={setPendingEdit} formResetKey={formResetKey} />
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} useFlexGap sx={{ flexWrap: 'wrap', flexShrink: 0 }}>
                        {session.removedItems.length > 0 && (
                            <Button variant="outlined" onClick={() => setIsRemovedOpen(true)}>
                                外した掲載品（{session.removedItems.length}件）
                            </Button>
                        )}
                        {session.missedProducts.length > 0 && (
                            <Button variant="outlined" color="warning" onClick={() => setIsMissedOpen(true)}>
                                読み落としているかもしれない商品（{session.missedProducts.length}件）
                            </Button>
                        )}
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

            <Dialog open={isPendingEditDialogOpen} onClose={() => !isApplyingPendingEdit && setIsPendingEditDialogOpen(false)} maxWidth="xs" fullWidth>
                <DialogTitle>「{pendingEdit?.itemName ?? ''}」を直している途中です</DialogTitle>
                <DialogContent>
                    <Typography>直した内容をまだ送っていません。反映してから取り込みを終えるか、直した内容を捨てて終えるかを選んでください。</Typography>
                </DialogContent>
                <DialogActions sx={{ flexWrap: 'wrap', gap: 1, '& > :not(style) ~ :not(style)': { ml: 0 } }}>
                    <Button onClick={() => setIsPendingEditDialogOpen(false)} disabled={isApplyingPendingEdit}>
                        やめる
                    </Button>
                    <Button color="inherit" onClick={handleDiscardAndFinish} disabled={isApplyingPendingEdit}>
                        直した内容を捨てて終える
                    </Button>
                    <Button variant="contained" onClick={() => void handleApplyAndFinish()} disabled={isApplyingPendingEdit}>
                        直した内容を反映して終える
                    </Button>
                </DialogActions>
            </Dialog>
            {session && (
                <FlyerRemovedDialog
                    menuId={menuId}
                    items={session.removedItems}
                    isOpen={isRemovedOpen && session.removedItems.length > 0}
                    onClose={() => setIsRemovedOpen(false)}
                />
            )}
            <Dialog open={isMissedOpen && !!session && session.missedProducts.length > 0} onClose={() => setIsMissedOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>読み落としているかもしれない商品</DialogTitle>
                <DialogContent dividers>
                    {session?.missedProducts.map((missed) => (
                        <Stack key={missed.name} direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                            <Box sx={{ flex: 1 }}>
                                <Typography>{missed.name}</Typography>
                                <ClaudeText text={missed.reason} color="text.secondary" />
                            </Box>
                            <Button
                                onClick={() => {
                                    setIsMissedOpen(false);
                                    setManualName(missed.name);
                                }}
                            >
                                手で入力する
                            </Button>
                        </Stack>
                    ))}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setIsMissedOpen(false)}>閉じる</Button>
                </DialogActions>
            </Dialog>
            {manualName !== null && <ManualItemDialog menuId={menuId} initialName={manualName} onClose={() => setManualName(null)} />}
        </Stack>
    );
};

interface ManualItemDialogProps {
    menuId: string;
    initialName: string;
    onClose: () => void;
}

/** 手入力の商品（チラシと同じ扱いで、サーバーが分類してから一覧に並ぶ。取り込めるなら「取り込みを終える」でほかの商品と一緒に取り込む。U-36） */
const ManualItemDialog = ({ menuId, initialName, onClose }: ManualItemDialogProps) => {
    const addManual = useAddManualFlyerItem(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const [name, setName] = useState(initialName);
    const [maker, setMaker] = useState('');
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
                    <TextField
                        label="メーカー"
                        value={maker}
                        onChange={(event) => setMaker(event.target.value)}
                        helperText="チラシに書かれているメーカーを入れてください。空のままだと「再入力」になります"
                    />
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
                                maker: maker.trim() || null,
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
                                    enqueueSnackbar('一覧に足しました。取り込みを終えると、ほかの商品と一緒に取り込みます', { variant: 'success' });
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

/** 「今週の商品に入らない N件（使えない a件・再入力 b件）」（FR-FLYER-06b・09 の 5.1。件数はサーバーの値） */
const notListedText = (session: FlyerSession) => {
    const notListed = session.notListed;
    if (notListed.total === 0) {
        return session.counts.ready > 0 ? '取り込みを終えると、すべての商品が今週の商品に入ります' : session.counts.confirmed > 0 ? '取り込んだ商品は、すべて今週の商品に入ります' : '今週の商品に入る商品はまだありません';
    }
    return `今週の商品に入らない ${notListed.total}件（使えない ${notListed.unusable}件・再入力 ${notListed.needsInput}件）`;
};

/** 「取り込みを終える」で取り込む内訳（「新しい商品 a件・登録済み b件」。件数はサーバーの値） */
const finishSummaryText = (session: FlyerSession) => {
    const finish = session.finish;
    if (finish.readyCount === 0) {
        return session.counts.confirmed > 0 ? `取り込み済み ${session.counts.confirmed}件。取り込める商品はもうありません` : '取り込める商品はまだありません';
    }
    return `取り込みを終えると今週の商品に入る：新しい商品 ${finish.newCount}件・登録済み ${finish.existingCount}件`;
};
