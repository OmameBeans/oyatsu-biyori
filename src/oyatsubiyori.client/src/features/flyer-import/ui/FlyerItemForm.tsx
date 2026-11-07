import { useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import FormControl from '@mui/material/FormControl';
import FormLabel from '@mui/material/FormLabel';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { AllergenJudgementLine } from '../../../entities/allergen-judgement';
import { FLYER_ITEM_FIELD, FLYER_ITEM_STATUS, FLYER_REASON_CODE, useRemoveFlyerItem, useResolveFlyerTerm, useUpdateFlyerItem } from '../../../entities/flyer';
import type { FlyerItem, FlyerItemReason } from '../../../entities/flyer';
import { useAllergenMaster } from '../../../entities/settings';
import { errorMessageOf } from '../../../shared/api';
import { SNACK_TYPE, SNACK_TYPE_LABEL } from '../../../shared/type';
import type { SnackType } from '../../../shared/type';
import { ClaudeText, ConfirmDialog } from '../../../shared/ui';
import { FIELD_LABEL, isPiecesUnknown } from '../model/flyerRows';
import type { PendingFlyerEdit } from '../model/flyerRows';

interface FlyerItemFormProps {
    menuId: string;
    item: FlyerItem;
    onClose: () => void;
    /** 外したとき（取り込みの取り消しも） */
    onRemoved?: () => void;
    /** 「閉じる」のボタンの文言（1件ずつ直すダイアログでは「表に戻る」） */
    closeLabel?: string;
    /** ボタンを出す場所（ダイアログのフッター）。なければフォームの下に出す */
    actionsContainer?: HTMLElement | null;
    /** 直した内容をまだ送っていない状態が変わったとき（送っていなければ null。「取り込みを終える」の前に聞くために使う） */
    onPendingEditChange?: (pending: PendingFlyerEdit | null) => void;
}

/** 商品の基本の入力欄 */
type FieldKey = 'name' | 'maker' | 'volume' | 'orderNo' | 'priceIncludingTax' | 'priceExcludingTax' | 'piecesPerPack' | 'snackType';
/** 理由・注意を出す場所（入力欄のそば・アレルゲン表記・品目の分からない語・上の一覧） */
type ReasonTarget = FieldKey | 'allergenLabel' | 'terms' | 'top';

const FIELD_KEYS: FieldKey[] = ['name', 'maker', 'volume', 'orderNo', 'priceIncludingTax', 'priceExcludingTax', 'piecesPerPack', 'snackType'];

/** 読み取りの検証の指摘（Claude の項目名）から、出す欄を決める。「アレルゲン」をいちばん先に、「税抜」が「価格」より先に当たるように並べる */
const REVIEW_FIELD_WORDS: [string, ReasonTarget][] = [
    ['アレルゲン', 'allergenLabel'],
    ['入数', 'piecesPerPack'],
    ['内容量', 'volume'],
    ['税抜', 'priceExcludingTax'],
    ['税込', 'priceIncludingTax'],
    ['価格', 'priceIncludingTax'],
    ['商品名', 'name'],
    ['メーカー', 'maker'],
    ['注文番号', 'orderNo'],
    ['分類', 'snackType'],
];

/**
 * 理由・注意を出す場所（表示の振り分けだけ。分類はサーバーが決める）。
 * 使えないの理由は、表記の品目（直せる）以外は上の一覧に出す（職員の操作では外せないため。SR-10・09 の 3.6）
 */
const reasonTargetOf = (reason: FlyerItemReason): ReasonTarget => {
    if (reason.level === 'Unusable') {
        return reason.code === FLYER_REASON_CODE.AVOIDED_IN_LABEL ? 'allergenLabel' : 'top';
    }
    if (reason.code === FLYER_REASON_CODE.UNMAPPED_TERM) {
        return 'terms';
    }
    if (reason.code === FLYER_REASON_CODE.LABEL_ABSENT_BUT_MASTER_CONTAINS) {
        return 'allergenLabel';
    }
    if (reason.code === FLYER_REASON_CODE.REVIEW_NOTE) {
        return REVIEW_FIELD_WORDS.find(([word]) => reason.field?.includes(word))?.[1] ?? 'top';
    }
    if (reason.field === FLYER_ITEM_FIELD.ALLERGEN_LABEL) {
        return 'allergenLabel';
    }
    return FIELD_KEYS.find((key) => key === reason.field) ?? 'top';
};

const NONE = '__none__';

/** 理由のある欄の見た目（橙の太い枠。🔴 の赤とは分ける） */
const REASON_FIELD_SX = { '& .MuiOutlinedInput-notchedOutline': { borderColor: 'warning.main', borderWidth: 2 } } as const;

const toText = (value: number | null | undefined) => (value === null || value === undefined ? '' : value.toString());
const toNumber = (value: string): number | null => (value.trim() === '' ? null : Number(value));
const sameSet = (a: string[], b: string[]) => a.length === b.length && a.every((x) => b.includes(x));

interface ReasonLinesProps {
    reasons: FlyerItemReason[];
}

/** 理由・注意の文（読み取りの検証の指摘は Claude の文なので、テキストとしてだけ出す）。注意は目立ちすぎない色で出す */
const ReasonLines = ({ reasons }: ReasonLinesProps) => (
    <Stack spacing={0.5} sx={{ mt: 0.5 }}>
        {reasons.map((reason) => {
            const isNote = reason.level === 'Note';
            const color = reason.level === 'Unusable' ? 'error.main' : isNote ? 'text.secondary' : 'warning.dark';
            const mark = reason.level === 'Unusable' ? '● ' : isNote ? '注意：' : '⚠ ';
            return (
                <Box key={reason.key} sx={{ minWidth: 0, color }}>
                    {reason.code === FLYER_REASON_CODE.REVIEW_NOTE ? (
                        <ClaudeText text={`${mark}${reason.field ?? ''}：${reason.message}`} color="inherit" />
                    ) : (
                        <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                            {mark}
                            {reason.message}
                        </Typography>
                    )}
                </Box>
            );
        })}
    </Stack>
);

/**
 * 取り込みの1商品を確かめて直す（SC-09 手順1。読み取り結果の表の、行の下の詳細欄と「再入力を1件ずつ直す」に出す。D-101）。
 * 取り込みは一覧の上の「取り込みを終える」でまとめて行うので、ここには取り込みのボタンはない。することは分類（サーバーが決める）で変わる（09 の 5）。
 * - 再入力：理由の欄だけを開き、理由をそばに出す。直して「直した内容で分類し直す」
 * - 取り込める：何もしなくてよい。注意があれば出す（分類は変えない）
 * - 使えない：今週の商品に入らない。読み違いのときは表記の品目だけ直せる
 * - 確定済み（取り込み済み）：直すと取り込みがいったん取り消され、もう一度「取り込みを終える」で取り込み直す。「確定を取り消す」で外した掲載品に移す
 */
export const FlyerItemForm = ({ menuId, item, onClose, onRemoved, closeLabel = '閉じる', actionsContainer, onPendingEditChange }: FlyerItemFormProps) => {
    const { data: master } = useAllergenMaster();
    const updateItem = useUpdateFlyerItem(menuId);
    const resolveTerm = useResolveFlyerTerm(menuId);
    const removeItem = useRemoveFlyerItem(menuId);
    const { enqueueSnackbar } = useSnackbar();
    const read = item.read;
    const isConfirmed = item.status === FLYER_ITEM_STATUS.CONFIRMED;
    const isReady = item.status === FLYER_ITEM_STATUS.READY;
    const isUnusable = item.status === FLYER_ITEM_STATUS.UNUSABLE;
    const isNeedsInput = item.status === FLYER_ITEM_STATUS.NEEDS_INPUT;
    // 入数が分からない行は、表の行の入数の欄で入れる（入れ直した商品として扱うため、ここでは入数の欄を出さない）
    const isPiecesInRow = isPiecesUnknown(item);
    // 登録済みの商品を上書きするときは、おやつの分類・タグは商品一覧の値を使い、上書きしない（09 の 5.1）
    const usesMasterSnackType = !item.isNew;

    const [name, setName] = useState(read.name);
    const [maker, setMaker] = useState(read.maker ?? '');
    const [volume, setVolume] = useState(read.volume ?? '');
    const [orderNo, setOrderNo] = useState(read.orderNo ?? '');
    const [price, setPrice] = useState(toText(read.priceIncludingTax));
    const [priceExcluding, setPriceExcluding] = useState(toText(read.priceExcludingTax));
    const [pieces, setPieces] = useState(toText(read.piecesPerPackCandidate));
    // 表の行の入数の欄で入れたなど、サーバーの入数の候補がフォームの外で変わったら、フォームの値も合わせる
    // （古い空の値のまま「分類し直す」を押して、入れた入数を消さないように）
    const [syncedPieces, setSyncedPieces] = useState(read.piecesPerPackCandidate);
    if (read.piecesPerPackCandidate !== syncedPieces) {
        setSyncedPieces(read.piecesPerPackCandidate);
        setPieces(toText(read.piecesPerPackCandidate));
    }
    const [snackType, setSnackType] = useState<SnackType | ''>(read.snackType ?? '');
    const [labelIds, setLabelIds] = useState<string[]>(() => item.labelAllergens.map((a) => a.id));
    const [characterTags, setCharacterTags] = useState<string[]>(read.characterTags);
    const [addingAllergen, setAddingAllergen] = useState('');
    const [removingAllergen, setRemovingAllergen] = useState<string | null>(null);
    const [isOtherFieldsOpen, setIsOtherFieldsOpen] = useState(false);
    const [isCancelOpen, setIsCancelOpen] = useState(false);

    // 理由を場所ごとに分ける
    const reasonsAt = (target: ReasonTarget) => item.reasons.filter((reason) => reasonTargetOf(reason) === target);
    const fieldReasonKeys = FIELD_KEYS.filter((key) => reasonsAt(key).length > 0);
    // 開いて出す欄：理由のある欄（直したあとに理由が消えても、入力の途中で欄が閉じないよう、開いたときの欄は出し続ける）と、
    // 新しく登録するのに足りない欄（おやつの分類・入数）
    const [initialOpenKeys] = useState<FieldKey[]>(() => [
        ...fieldReasonKeys,
        ...(!usesMasterSnackType && !read.snackType ? (['snackType'] as const) : []),
        ...(item.piecesPerPack === null && !isPiecesInRow ? (['piecesPerPack'] as const) : []),
    ]);
    const openKeys = new Set<FieldKey>([...initialOpenKeys, ...fieldReasonKeys]);
    // 注意（分類は変えない）は、開いている欄かアレルゲン表記の欄に当たればそのそばに、ほかは上の一覧に出す。
    // 確認済みとの食い違いは目立つように上に出す
    const discrepancyNotes = item.notes.filter((note) => note.code === FLYER_REASON_CODE.MASTER_DISCREPANCY);
    const otherNotes = item.notes.filter((note) => note.code !== FLYER_REASON_CODE.MASTER_DISCREPANCY);
    const noteTargetOf = (note: FlyerItemReason): ReasonTarget => {
        const target = reasonTargetOf(note);
        return target === 'allergenLabel' || (target !== 'terms' && target !== 'top' && openKeys.has(target)) ? target : 'top';
    };
    const notesAt = (target: ReasonTarget) => otherNotes.filter((note) => noteTargetOf(note) === target);

    const allergenName = (id: string) => master.allergens.find((a) => a.id === id)?.name ?? id;
    const abbreviationTermsOf = (id: string) => Array.from(new Set(item.abbreviationHits.filter((hit) => hit.allergen.id === id).map((hit) => hit.term)));
    const conflictOf = (key: FieldKey) => item.duplicateConflicts.find((conflict) => conflict.field === key);

    const isLabelChanged = !sameSet(labelIds, item.labelAllergens.map((a) => a.id));
    const isTagsChanged = !sameSet(characterTags, read.characterTags);
    const isDirty =
        name.trim() !== read.name.trim() ||
        maker.trim() !== (read.maker ?? '').trim() ||
        volume.trim() !== (read.volume ?? '').trim() ||
        orderNo.trim() !== (read.orderNo ?? '').trim() ||
        toNumber(price) !== read.priceIncludingTax ||
        toNumber(priceExcluding) !== read.priceExcludingTax ||
        (!isPiecesInRow && toNumber(pieces) !== read.piecesPerPackCandidate) ||
        (snackType || null) !== read.snackType ||
        isLabelChanged ||
        isTagsChanged;

    const handleResolveTerm = (term: string, value: string) =>
        resolveTerm.mutate(
            { itemId: item.id, term, allergenIds: value === NONE ? [] : [value] },
            { onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }) },
        );

    /** 直した内容を送って分類し直す（失敗したら reject する。エラーはフォームの下にも出す） */
    const applyUpdate = async () => {
        const wasConfirmed = isConfirmed;
        await updateItem.mutateAsync({
            itemId: item.id,
            name: name.trim(),
            maker: maker.trim() || null,
            volume: volume.trim() || null,
            priceIncludingTax: toNumber(price),
            priceExcludingTax: toNumber(priceExcluding),
            orderNo: orderNo.trim() || null,
            piecesPerPack: isPiecesInRow ? read.piecesPerPackCandidate : toNumber(pieces),
            snackType: snackType || null,
            targetAgeLabel: read.targetAgeLabel,
            characterTags: isTagsChanged ? characterTags : null,
            labelAllergenIds: isLabelChanged ? labelIds : null,
        });
        enqueueSnackbar(
            wasConfirmed
                ? `「${name.trim() || read.name}」を直しました。今週の商品からいったん外れたので、「取り込みを終える」を押して取り込み直してください`
                : `「${name.trim() || read.name}」を直して、分類し直しました`,
            { variant: 'success' },
        );
    };

    const handleUpdate = () => {
        // エラーはフォームの下に出す（updateItem.isError）
        applyUpdate().catch(() => undefined);
    };

    // 直した内容をまだ送っていないことを親へ知らせる（「取り込みを終える」の前に、反映するか捨てるかを聞くため）。
    // 送る処理と知らせる先は描画のたびに変わるので、ref から呼ぶ（知らせ直しの繰り返しにならないように）
    const latestRef = useRef({ applyUpdate, onPendingEditChange });
    useEffect(() => {
        latestRef.current = { applyUpdate, onPendingEditChange };
    });
    const itemName = read.name;
    useEffect(() => {
        if (!isDirty) {
            return;
        }
        const notify = latestRef.current.onPendingEditChange;
        notify?.({ itemName, apply: () => latestRef.current.applyUpdate() });
        return () => notify?.(null);
    }, [isDirty, itemName]);

    const handleRemove = () =>
        removeItem.mutate(item.id, {
            onSuccess: () => {
                setIsCancelOpen(false);
                enqueueSnackbar(
                    isConfirmed
                        ? `「${read.name}」の取り込みを取り消し、外した掲載品に移しました。「戻す」で一覧に戻すと、もう一度取り込めます`
                        : `「${read.name}」を外しました。一覧の下の「外した掲載品」から戻せます`,
                    { variant: 'success' },
                );
                onRemoved?.();
            },
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    /** 入力欄と、そのそばの理由・注意・二重に読んだ値 */
    const fieldBlock = (key: FieldKey, node: ReactNode, setValue?: (value: string) => void) => {
        const reasons = [...reasonsAt(key), ...notesAt(key)];
        const conflict = conflictOf(key);
        return (
            <Box key={key} sx={{ display: 'grid', alignContent: 'start' }}>
                {node}
                {reasons.length > 0 && <ReasonLines reasons={reasons} />}
                {conflict && setValue && (
                    <Stack direction="row" spacing={0.5} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mt: 0.5 }}>
                        <Typography variant="caption" color="text.secondary">
                            読んだ値：
                        </Typography>
                        {conflict.values.map((value) => (
                            <Chip key={value} size="small" variant="outlined" label={value} onClick={() => setValue(value)} aria-label={`${FIELD_LABEL[key] ?? ''}を「${value}」にする`} />
                        ))}
                    </Stack>
                )}
            </Box>
        );
    };
    const fieldSx = (key: FieldKey) => (reasonsAt(key).length > 0 ? REASON_FIELD_SX : undefined);

    const fields: { key: FieldKey; label: string; summary: string; node: ReactNode }[] = [
        {
            key: 'name',
            label: '商品名',
            summary: name,
            node: fieldBlock('name', <TextField label="商品名" value={name} onChange={(event) => setName(event.target.value)} sx={fieldSx('name')} />, setName),
        },
        {
            key: 'maker',
            label: 'メーカー',
            summary: maker,
            node: fieldBlock(
                'maker',
                <TextField
                    label="メーカー"
                    value={maker}
                    onChange={(event) => setMaker(event.target.value)}
                    sx={fieldSx('maker')}
                    helperText="商品名とメーカーが同じ商品を、登録済みの商品と同じとみなします"
                />,
                setMaker,
            ),
        },
        {
            key: 'volume',
            label: '内容量',
            summary: volume,
            node: fieldBlock('volume', <TextField label="内容量" value={volume} onChange={(event) => setVolume(event.target.value)} sx={fieldSx('volume')} />, setVolume),
        },
        {
            key: 'orderNo',
            label: '注文番号',
            summary: orderNo,
            node: fieldBlock('orderNo', <TextField label="注文番号" value={orderNo} onChange={(event) => setOrderNo(event.target.value)} sx={fieldSx('orderNo')} />, setOrderNo),
        },
        {
            key: 'priceIncludingTax',
            label: '税込',
            summary: price ? `${price}円` : '',
            node: fieldBlock(
                'priceIncludingTax',
                <TextField label="税込価格（円）" type="number" value={price} onChange={(event) => setPrice(event.target.value)} sx={fieldSx('priceIncludingTax')} />,
                setPrice,
            ),
        },
        {
            key: 'priceExcludingTax',
            label: '税抜',
            summary: priceExcluding ? `${priceExcluding}円` : '',
            node: fieldBlock(
                'priceExcludingTax',
                <TextField label="税抜価格（円）" type="number" value={priceExcluding} onChange={(event) => setPriceExcluding(event.target.value)} sx={fieldSx('priceExcludingTax')} />,
                setPriceExcluding,
            ),
        },
        ...(isPiecesInRow
            ? []
            : [
                  {
                      key: 'piecesPerPack' as const,
                      label: '入数',
                      summary: pieces ? `${pieces}人分` : item.piecesPerPack !== null ? `${item.piecesPerPack}人分（商品一覧）` : '',
                      node: fieldBlock(
                          'piecesPerPack',
                          <TextField
                              label="入数（1つで何人分か）"
                              type="number"
                              value={pieces}
                              onChange={(event) => setPieces(event.target.value)}
                              sx={fieldSx('piecesPerPack')}
                              helperText={
                                  read.piecesPerPackReason
                                      ? `候補の理由：${read.piecesPerPackReason}`
                                      : item.matchedProduct?.piecesPerPack
                                        ? `空なら、商品一覧の入数（${item.matchedProduct.piecesPerPack}）を使います`
                                        : '1パックで何人分かを入れてください'
                              }
                          />,
                          setPieces,
                      ),
                  },
              ]),
        ...(usesMasterSnackType
            ? []
            : [
                  {
                      key: 'snackType' as const,
                      label: '分類',
                      summary: snackType ? SNACK_TYPE_LABEL[snackType] : '',
                      node: fieldBlock(
                          'snackType',
                          <FormControl size="small" sx={fieldSx('snackType')}>
                              <InputLabel id={`snack-${item.id}`}>おやつの分類</InputLabel>
                              <Select labelId={`snack-${item.id}`} label="おやつの分類" value={snackType} onChange={(event) => setSnackType(event.target.value as SnackType)}>
                                  {Object.values(SNACK_TYPE).map((type) => (
                                      <MenuItem key={type} value={type}>
                                          {SNACK_TYPE_LABEL[type]}
                                      </MenuItem>
                                  ))}
                              </Select>
                          </FormControl>,
                      ),
                  },
              ]),
    ];
    const fixFields = fields.filter((field) => openKeys.has(field.key));
    const otherFields = fields.filter((field) => !openKeys.has(field.key));
    const tagNames = [...characterTags, ...read.flavorTags];
    const otherSummary = [
        ...otherFields.map((field) => `${field.label} ${field.summary || 'なし'}`),
        ...(usesMasterSnackType ? [] : [`タグ ${tagNames.length > 0 ? tagNames.join('・') : 'なし'}`]),
    ].join('・');

    const topReasons = reasonsAt('top');
    const topNotes = notesAt('top');
    const allergenReasons = [...reasonsAt('allergenLabel'), ...notesAt('allergenLabel')];
    const termReasons = reasonsAt('terms');
    // 表記なしと読んだのに、商品一覧の記録で特定原材料を含む（記録の品目を表記の品目に足すボタンを出す。送るのは「分類し直す」）
    const masterContainsReason = item.reasons.find((reason) => reason.code === FLYER_REASON_CODE.LABEL_ABSENT_BUT_MASTER_CONTAINS);
    const masterAllergensToAdd = masterContainsReason?.allergens.filter((a) => !labelIds.includes(a.id)) ?? [];

    const actions = (
        <Stack direction="row" spacing={1} useFlexGap sx={{ justifyContent: 'flex-end', flexWrap: 'wrap' }}>
            {isConfirmed ? (
                <Button color="error" onClick={() => setIsCancelOpen(true)} disabled={removeItem.isPending}>
                    確定を取り消す
                </Button>
            ) : (
                <Button color="inherit" onClick={handleRemove} disabled={removeItem.isPending}>
                    外す
                </Button>
            )}
            <Box sx={{ flex: 1 }} />
            <Button onClick={onClose}>{closeLabel}</Button>
            <Button variant={isDirty ? 'contained' : 'outlined'} onClick={handleUpdate} disabled={!isDirty || updateItem.isPending}>
                直した内容で分類し直す
            </Button>
        </Stack>
    );

    return (
        <Box component="section" aria-label={`「${read.name}」を確かめる`}>
            <Stack spacing={2.5}>
                {/* 何をすればよいか（分類ごと） */}
                {isNeedsInput && (
                    <Alert severity="warning" aria-label="確かめること">
                        読み取りに足りないところがあります。理由の出ている欄をチラシと見比べて直し、「直した内容で分類し直す」を押してください。直すまで今週の商品に入りません。
                        {topReasons.length > 0 && <ReasonLines reasons={topReasons} />}
                    </Alert>
                )}
                {isUnusable && (
                    <Alert severity="error" aria-label="今週の商品に入らない理由">
                        この商品は今週の商品に入りません。何もしなくて構いません。
                        {topReasons.length > 0 && <ReasonLines reasons={topReasons} />}
                        {isPiecesInRow && <Typography variant="body2">個包装の数が分かれば、表の行の「入数」に入れて「分類し直す」を押してください。</Typography>}
                        <Typography variant="body2" sx={{ mt: 0.5 }}>
                            チラシの表記の読み違いのときは、下の「チラシのアレルゲン表記」の品目を直せます。説明文・商品名・商品一覧の記録で当たったものは、ここでは外せません（商品一覧のパッケージでの確認で直します）。
                        </Typography>
                    </Alert>
                )}
                {isReady && (
                    <Alert severity="info" icon={false}>
                        この商品は、取り込みを終えると今週の商品に入ります（{item.isNew ? '新しい商品として登録します' : '登録済みの商品の価格などを上書きします'}）。何もしなくて構いません。
                        {topReasons.length > 0 && <ReasonLines reasons={topReasons} />}
                    </Alert>
                )}
                {isConfirmed && item.confirmation && (
                    <Alert severity="success" icon={false}>
                        {item.confirmation.kind === 'CreatedNew'
                            ? `新しい商品「${item.confirmation.productName}」として取り込みました。`
                            : `登録済みの商品「${item.confirmation.productName}」を上書きして取り込みました。`}
                        {item.recordedDiscrepancyAllergens.length > 0 &&
                            ` チラシの表記がパッケージでの確認と食い違っていたため、チラシの表記を記録しました（${item.recordedDiscrepancyAllergens.map((a) => a.name).join('・')}）。`}
                        <Typography variant="body2" sx={{ mt: 0.5 }}>
                            直すと今週の商品からいったん外れ、もう一度「取り込みを終える」を押すと取り込み直します。
                        </Typography>
                    </Alert>
                )}
                {discrepancyNotes.map((note) => (
                    <Alert key={note.key} severity={item.discrepancies.some((d) => d.isAvoided) ? 'error' : 'warning'}>
                        {note.message}
                    </Alert>
                ))}
                {topNotes.length > 0 && (
                    <Box aria-label="注意">
                        <ReasonLines reasons={topNotes} />
                    </Box>
                )}

                {/* 一致した商品（名前＋メーカーが同じ登録済みの商品）と、商品一覧でのアレルゲンの表示 */}
                {item.matchedProduct && (
                    <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 1.5 }}>
                        <FormLabel sx={{ display: 'block', mb: 0.5 }}>{isConfirmed ? '取り込んだ商品' : '登録済みの同じ商品'}</FormLabel>
                        <Typography sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>{item.matchedProduct.name}</Typography>
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                            {[
                                item.matchedProduct.maker,
                                item.matchedProduct.volume ?? '内容量なし',
                                item.matchedProduct.piecesPerPack !== null ? `入数 ${item.matchedProduct.piecesPerPack}` : '入数なし',
                            ]
                                .filter(Boolean)
                                .join('・')}
                        </Typography>
                        <AllergenJudgementLine judgement={item.matchedProduct.judgement} />
                        {item.otherMatchCount > 0 && (
                            <Typography variant="body2" sx={{ mt: 1 }}>
                                ほかに同じ名前の商品が {item.otherMatchCount}件あります（最後にチラシに載った商品を使っています）。
                            </Typography>
                        )}
                    </Box>
                )}

                {fixFields.length > 0 && (
                    <Box>
                        <FormLabel sx={{ display: 'block', mb: 1.5 }}>{isNeedsInput ? '直してほしい欄（チラシと見比べてください）' : '確かめてほしい欄'}</FormLabel>
                        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>{fixFields.map((field) => field.node)}</Box>
                    </Box>
                )}
                {read.bulkNote && <Typography variant="body2">まとめ買い：{read.bulkNote}（価格は注文の単位あたり）</Typography>}

                <Box>
                    <FormLabel>チラシのアレルゲン表記</FormLabel>
                    <Typography sx={{ my: 0.5, overflowWrap: 'anywhere' }}>
                        {read.allergenLabelText?.trim() ? `「${read.allergenLabelText}」` : '表記なし（読み落としの可能性があります）'}
                    </Typography>
                    {item.isLabelAbsent && (
                        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                            チラシで表記が見つかれば、下の「品目を足す」で足してください。表記なしのまま取り込むと、献立の確定のときにチラシと見比べる確認が出ます。
                        </Typography>
                    )}
                    {allergenReasons.length > 0 && <ReasonLines reasons={allergenReasons} />}
                    {masterAllergensToAdd.length > 0 && (
                        <Button
                            size="small"
                            variant="outlined"
                            onClick={() => setLabelIds([...labelIds, ...masterAllergensToAdd.map((a) => a.id)])}
                            sx={{ mt: 1, alignSelf: 'flex-start' }}
                        >
                            商品一覧の記録の品目（{masterAllergensToAdd.map((a) => a.name).join('・')}）を表記に入れる
                        </Button>
                    )}
                    <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap', my: 1 }} useFlexGap>
                        {labelIds.length === 0 && (
                            <Typography variant="body2" color="text.secondary">
                                表記の品目なし
                            </Typography>
                        )}
                        {labelIds.map((id) => {
                            const terms = abbreviationTermsOf(id);
                            const label = terms.length > 0 ? `${terms.join('・')}→${allergenName(id)}` : allergenName(id);
                            return <Chip key={id} label={label} onDelete={() => setRemovingAllergen(id)} />;
                        })}
                    </Stack>
                    <Stack direction="row" spacing={1}>
                        <Select size="small" value={addingAllergen} displayEmpty onChange={(event) => setAddingAllergen(event.target.value)} sx={{ minWidth: 180, maxWidth: '100%' }} aria-label="品目を足す">
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
                        <Typography variant="body2" sx={{ mt: 1, color: 'warning.dark' }}>
                            チラシの説明文に、避ける品目の言葉があります：{item.descriptionHitWords.join('・')}（ここでは外せません）
                        </Typography>
                    )}
                    {/* 取り込みを終えると掲載品の写しに入り、献立の確定の確認で出す表記（Claude が読んだ文なので、テキストとしてだけ出す） */}
                    <Typography variant="body2" color="text.secondary" sx={{ mt: 1, overflowWrap: 'anywhere' }}>
                        献立の確定で見比べる表記：{item.listingLabelText}
                    </Typography>
                </Box>

                {item.unmappedTerms.length > 0 && (
                    <Box>
                        <FormLabel>品目の分からない表記の語（品目を選ぶか「対応する品目なし」を選んでください）</FormLabel>
                        {termReasons.length > 0 && <ReasonLines reasons={termReasons} />}
                        {item.unmappedTerms.map((term) => {
                            const resolved = item.resolvedTerms.find((r) => r.term === term);
                            const value = resolved ? (resolved.allergens[0]?.id ?? NONE) : '';
                            return (
                                <Stack key={term} direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1 }}>
                                    <Typography sx={{ minWidth: 80 }}>「{term}」</Typography>
                                    <Select
                                        size="small"
                                        value={value}
                                        displayEmpty
                                        disabled={isConfirmed || resolveTerm.isPending}
                                        onChange={(event) => event.target.value && handleResolveTerm(term, event.target.value)}
                                        sx={{ minWidth: 200, maxWidth: '100%' }}
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
                            );
                        })}
                    </Box>
                )}

                {otherFields.length > 0 && (
                    <Box>
                        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
                            <Typography variant="body2" color="text.secondary" sx={{ flex: 1, minWidth: 0, overflowWrap: 'anywhere' }}>
                                {fixFields.length > 0 ? 'ほかの項目：' : '読み取った内容：'}
                                {otherSummary}
                            </Typography>
                            <Button size="small" onClick={() => setIsOtherFieldsOpen(!isOtherFieldsOpen)} aria-expanded={isOtherFieldsOpen} sx={{ flexShrink: 0, alignSelf: 'flex-start' }}>
                                {isOtherFieldsOpen ? 'ほかの項目を閉じる' : 'ほかの項目も直す'}
                            </Button>
                        </Stack>
                        <Collapse in={isOtherFieldsOpen} unmountOnExit>
                            <Stack spacing={2} sx={{ mt: 1.5 }}>
                                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>{otherFields.map((field) => field.node)}</Box>
                                {!usesMasterSnackType && (
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
                                )}
                            </Stack>
                        </Collapse>
                    </Box>
                )}

                {isDirty && (
                    <Typography variant="body2" sx={{ color: 'warning.dark' }}>
                        直した内容は、まだ送っていません。「直した内容で分類し直す」を押すと反映します（直すと分類が変わることがあります）。
                        {isConfirmed && '送ると、この商品は今週の商品からいったん外れます。'}
                    </Typography>
                )}
                {updateItem.isError && (
                    <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>
                        {errorMessageOf(updateItem.error)}
                    </Alert>
                )}
                {actionsContainer === undefined && actions}
            </Stack>
            {/* ダイアログでは、スクロールしなくても押せるようにフッターに出す */}
            {actionsContainer && createPortal(actions, actionsContainer)}
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
                <Typography>
                    チラシと見比べて、この品目が表記に書かれていないことを確かめましたか？外したことは記録に残ります。外したあと「直した内容で分類し直す」を押すと反映します。
                </Typography>
            </ConfirmDialog>
            <ConfirmDialog
                isOpen={isCancelOpen}
                title={`「${read.name}」の取り込みを取り消しますか？`}
                confirmLabel="取り消す"
                isDanger
                isPending={removeItem.isPending}
                onClose={() => setIsCancelOpen(false)}
                onConfirm={handleRemove}
            >
                <Typography>
                    今週の商品から外し、一覧の下の「外した掲載品」に移します。新しく登録した商品は「使わない」にします。「戻す」で一覧に戻すと、もう一度取り込めます。
                </Typography>
            </ConfirmDialog>
        </Box>
    );
};
