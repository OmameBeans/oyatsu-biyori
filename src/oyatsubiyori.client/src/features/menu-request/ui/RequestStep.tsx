import { useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import { useSnackbar } from 'notistack';
import { useSaveRequest, useStartProposal } from '../../../entities/menu';
import type { MenuDetail } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatYen } from '../../../shared/lib';
import { SNACK_TYPE_LABEL } from '../../../shared/type';
import { ConfirmDialog } from '../../../shared/ui';

interface RequestStepProps {
    menu: MenuDetail;
    onStarted: () => void;
    onManual: () => void;
    /** 「いつもの条件」の「変更」を押したとき（ダイアログは呼び出し側のページが開く） */
    onEditConditions: () => void;
}

const EXAMPLE = '例：しょっぱいものとゼリー系を合わせて出したい／金曜は誕生日会なので豪華に／水曜は都民の日なので子どもが30人';

/** PC（md 以上）ではカードを縦いっぱいに広げ、中身が多いときはカードの中だけをスクロールする */
const cardSx = { height: { md: '100%' }, overflow: { md: 'auto' } } as const;

/**
 * SC-09 手順3 要望の入力（T4-03）。要望は入力欄を離れたとき・手順を移るとき・条件の変更を開く前に保存する。
 * 空欄でも「献立を作る」を押せる。「自分で組む（AI を使わない）」なら、日ごとの枠だけで手順4へ進む。
 */
export const RequestStep = ({ menu, onStarted, onManual, onEditConditions }: RequestStepProps) => {
    const [text, setText] = useState(menu.requestText ?? '');
    const [isRecreateOpen, setIsRecreateOpen] = useState(false);
    const saveRequest = useSaveRequest(menu.id);
    const startProposal = useStartProposal(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const hasItems = menu.days.some((day) => day.items.length > 0);

    // 保存の途中で「献立を作る」を押したら、保存が終わってから始める（同じ献立を同時に書き換えて衝突しないように）
    const savingRef = useRef<Promise<unknown> | null>(null);
    const save = () => {
        if (text !== (menu.requestText ?? '')) {
            savingRef.current = saveRequest.mutateAsync(text).catch(() => undefined);
        }
    };

    const start = async () => {
        await savingRef.current;
        startProposal.mutate(text, {
            onSuccess: () => onStarted(),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    return (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '3fr 2fr' }, gap: 2, height: { md: '100%' } }}>
            <Card sx={cardSx}>
                <CardContent>
                    <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                        今週の要望
                    </Typography>
                    <TextField
                        multiline
                        minRows={6}
                        fullWidth
                        value={text}
                        placeholder={EXAMPLE}
                        onChange={(event) => setText(event.target.value)}
                        onBlur={save}
                        slotProps={{ htmlInput: { 'aria-label': '今週の要望', maxLength: 2000 } }}
                    />
                    <Alert severity="info" sx={{ mt: 1.5 }}>
                        子どもの名前は書かないでください（献立の提案のため AI に送ります）。
                        <br />
                        要望の文章では、避ける品目（アレルギー）の条件は変えられません。
                    </Alert>
                    {startProposal.isError && <Alert severity="error" sx={{ mt: 1.5 }}>{errorMessageOf(startProposal.error)}</Alert>}
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mt: 2 }}>
                        <Button variant="contained" size="large" disabled={startProposal.isPending} onClick={() => (hasItems ? setIsRecreateOpen(true) : void start())}>
                            献立を作る
                        </Button>
                        <Button
                            onClick={() => {
                                save();
                                onManual();
                            }}
                        >
                            自分で組む（AI を使わない）
                        </Button>
                    </Stack>
                </CardContent>
            </Card>
            <Card sx={cardSx}>
                <CardContent>
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
                        <Typography variant="h3" component="h2">
                            いつもの条件
                        </Typography>
                        <Button
                            variant="outlined"
                            startIcon={<EditOutlinedIcon />}
                            aria-label="いつもの条件を変更"
                            onClick={() => {
                                save();
                                onEditConditions();
                            }}
                        >
                            変更
                        </Button>
                    </Stack>
                    <Stack spacing={1}>
                        <Typography>人数：{menu.defaultChildrenCount ?? '未設定'}人</Typography>
                        <Typography>週の予算：{formatYen(menu.weeklyBudget)}</Typography>
                        <Typography>避ける品目：{menu.avoidedAllergenNames.length === 0 ? 'なし' : menu.avoidedAllergenNames.join('・')}</Typography>
                        <Typography>
                            同じ商品を出さない期間：
                            {menu.duplicateRules.length === 0 ? '設定なし' : menu.duplicateRules.map((r) => `${SNACK_TYPE_LABEL[r.snackType]} ${r.days}日`).join('・')}
                        </Typography>
                    </Stack>
                </CardContent>
            </Card>
            <ConfirmDialog
                isOpen={isRecreateOpen}
                title="献立案を作り直しますか？"
                confirmLabel="作り直す"
                onClose={() => setIsRecreateOpen(false)}
                onConfirm={() => {
                    setIsRecreateOpen(false);
                    void start();
                }}
            >
                <Typography>今の献立案とチャットの履歴は消えます。職員が直した人数・種別・見出しは残します。</Typography>
            </ConfirmDialog>
        </Box>
    );
};
