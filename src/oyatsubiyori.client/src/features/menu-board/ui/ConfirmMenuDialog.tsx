import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { acknowledgementSentence, AllergenCategoryMark, AllergenJudgementLine, manufacturerCheckText } from '../../../entities/allergen-judgement';
import { useConfirmMenu, useMenuConfirmation } from '../../../entities/menu';
import type { MenuDetail } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel } from '../../../shared/lib';

interface ConfirmMenuDialogProps {
    menu: MenuDetail;
    isOpen: boolean;
    onClose: () => void;
    onSuccess: () => void;
}

/**
 * 確定のダイアログ（T4-11。FR-MENU-32・SR-04）。止めるエラーがあれば一覧を示し「確定する」を押せない。
 * 🟡 の商品は1件ずつ、理由と次にすることを含む1文にチェックを付ける。末尾に「メーカーへの確認が必要」の1行。
 * 判定は開くたびにサーバーで求め直し、確定のときにももう一度確かめる。
 */
export const ConfirmMenuDialog = ({ menu, isOpen, onClose, onSuccess }: ConfirmMenuDialogProps) => {
    const { data: confirmation, isLoading, error } = useMenuConfirmation(menu.id, isOpen);
    const confirm = useConfirmMenu(menu.id);
    // チェックは「商品と、そのとき読んだ判定の中身（指紋）」の組で持つ。判定が変わったら、チェックは外れた扱いになる（SR-22）
    const [checked, setChecked] = useState<string[]>([]);
    const checkKey = (a: { menuItemId: string; fingerprint: string }) => `${a.menuItemId}:${a.fingerprint}`;
    const pending = confirmation?.acknowledgements.filter((a) => !a.isAcknowledged) ?? [];
    const allChecked = pending.every((a) => checked.includes(checkKey(a)));
    const canConfirm = confirmation !== undefined && confirmation.blockingIssues.length === 0 && allChecked;
    const manufacturer = confirmation ? manufacturerCheckText(confirmation.manufacturerCheckAllergens) : null;

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>献立を確定する</DialogTitle>
            <DialogContent dividers>
                {isLoading && <CircularProgress aria-label="読み込み中" />}
                {error && <Alert severity="error">{errorMessageOf(error)}</Alert>}
                {confirmation && (
                    <Stack spacing={2}>
                        {confirmation.blockingIssues.length > 0 && (
                            <Alert severity="error">
                                <Typography sx={{ fontWeight: 700 }}>次の問題があるため、確定できません</Typography>
                                {confirmation.blockingIssues.map((issue, index) => (
                                    <Stack key={`${issue.date}-${issue.productName}-${index}`} sx={{ mt: 0.5 }}>
                                        <Typography variant="body2" sx={{ fontWeight: 600 }}>
                                            ・{issue.date ? formatDateLabel(issue.date) : '週全体'}
                                            {issue.productName ? `「${issue.productName}」` : ''}
                                            {issue.message ? `：${issue.message}` : ''}
                                        </Typography>
                                        {issue.judgement && <AllergenJudgementLine judgement={issue.judgement} isInMenu />}
                                    </Stack>
                                ))}
                            </Alert>
                        )}
                        {confirmation.acknowledgements.length > 0 && (
                            <Stack>
                                <Typography sx={{ fontWeight: 700, mb: 0.5 }}>
                                    <AllergenCategoryMark category="Warning" />の商品（1件ずつ確かめてチェックしてください）
                                </Typography>
                                {confirmation.acknowledgements.map((a) => (
                                    <FormControlLabel
                                        key={a.menuItemId}
                                        control={
                                            <Checkbox
                                                checked={a.isAcknowledged || checked.includes(checkKey(a))}
                                                disabled={a.isAcknowledged}
                                                onChange={(event) =>
                                                    setChecked(event.target.checked ? [...checked, checkKey(a)] : checked.filter((key) => key !== checkKey(a)))
                                                }
                                            />
                                        }
                                        label={`${a.productName}（${formatDateLabel(a.date)}）：${acknowledgementSentence(a.judgement)}`}
                                    />
                                ))}
                            </Stack>
                        )}
                        {confirmation.displayWarnings.length > 0 && (
                            <Alert severity="warning">
                                {confirmation.displayWarnings.map((line) => (
                                    <Typography key={line} variant="body2">
                                        ・{line}
                                    </Typography>
                                ))}
                            </Alert>
                        )}
                        {confirmation.blockingIssues.length === 0 && confirmation.acknowledgements.length === 0 && <Typography>確定できます。</Typography>}
                        <Typography variant="body2" color="text.secondary">
                            発注のときに、コープのカートのアレルゲンの絞り込みでも、もう一度確かめると安心です。
                        </Typography>
                        {manufacturer && <Typography variant="body2">{manufacturer}</Typography>}
                        {confirm.isError && <Alert severity="error" sx={{ whiteSpace: 'pre-wrap' }}>{errorMessageOf(confirm.error)}</Alert>}
                    </Stack>
                )}
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>やめる</Button>
                <Button
                    variant="contained"
                    disabled={!canConfirm || confirm.isPending}
                    onClick={() =>
                        confirm.mutate(
                            {
                                acknowledgements: pending
                                    .filter((a) => checked.includes(checkKey(a)))
                                    .map((a) => ({ menuItemId: a.menuItemId, kind: a.judgement.kind, fingerprint: a.fingerprint })),
                                rowVersion: menu.rowVersion,
                            },
                            {
                                onSuccess,
                                // 拒まれたら（判定が変わったなど）、チェックをすべて外して読み直してもらう
                                onError: () => setChecked([]),
                            },
                        )
                    }
                >
                    確定する
                </Button>
            </DialogActions>
        </Dialog>
    );
};
