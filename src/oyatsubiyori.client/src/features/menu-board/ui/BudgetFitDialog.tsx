import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { AllergenCategoryIcon } from '../../../entities/allergen-judgement';
import { useApplyBudgetFit, useStartBudgetFit } from '../../../entities/menu';
import type { BudgetFit, MenuDetail } from '../../../entities/menu';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { formatYen } from '../../../shared/lib';
import { JOB_STATE } from '../../../shared/type';
import { ClaudeText, JobProgress } from '../../../shared/ui';

interface BudgetFitDialogProps {
    menu: MenuDetail;
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 予算に合わせる（T4-09）。アプリが全通り試した案から、AI が要望に合うものを選んで説明する。
 * 反映するときは、サーバーが今の献立で探し直した案と照らし合わせる。
 */
export const BudgetFitDialog = ({ menu, isOpen, onClose }: BudgetFitDialogProps) => {
    const start = useStartBudgetFit(menu.id);
    const apply = useApplyBudgetFit(menu.id);
    const [jobId, setJobId] = useState<string | null>(null);
    const { data: job, isError: isPollError } = useJobPolling<BudgetFit>(jobId);
    // 開発時の StrictMode で effect が2回動いても、処理を2つ始めない
    const isStartedRef = useRef(false);

    useEffect(() => {
        if (isOpen && !isStartedRef.current) {
            isStartedRef.current = true;
            start.mutate(undefined, { onSuccess: setJobId });
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isOpen]);

    const handleClose = () => {
        setJobId(null);
        onClose();
    };

    const result = job?.state === JOB_STATE.SUCCEEDED ? job.result : null;

    return (
        <Dialog open={isOpen} onClose={handleClose} maxWidth="sm" fullWidth>
            <DialogTitle>予算に合わせる</DialogTitle>
            <DialogContent dividers>
                {(start.isPending || (jobId && !isJobFinished(job))) && <JobProgress stage={job?.stage} fallbackStage="予算に近づく組み合わせを探しています" />}
                {start.isError && <Alert severity="error">{errorMessageOf(start.error)}</Alert>}
                {isPollError && <Alert severity="warning">処理の状態を確かめられませんでした。閉じて、もう一度押してください。</Alert>}
                {job?.state === JOB_STATE.FAILED && <Alert severity="error">{job.errorMessages.join('\n')}</Alert>}
                {result && (
                    <Stack spacing={1.5}>
                        <Typography>
                            今の週の実績 {formatYen(result.currentTotal)} / 週の予算 {formatYen(result.budget)}
                        </Typography>
                        {result.note && <Alert severity="info">{result.note}</Alert>}
                        {result.options.map((option) => (
                            <Card key={`${option.kind}-${option.date}-${option.newProductId}-${option.newPackCount}`}>
                                <CardActionArea
                                    disabled={apply.isPending}
                                    onClick={() => apply.mutate(option, { onSuccess: handleClose })}
                                    aria-label={option.description}
                                >
                                    <CardContent>
                                        <Typography sx={{ fontWeight: 600 }}>{option.description}</Typography>
                                        {option.explanation && <ClaudeText text={option.explanation} color="text.secondary" />}
                                        {option.usesWarningProduct && (
                                            <Typography variant="caption" sx={{ color: 'warning.dark' }}>
                                                <AllergenCategoryIcon category="Warning" isInherit /> アレルゲンの確認が要る商品を使います
                                            </Typography>
                                        )}
                                    </CardContent>
                                </CardActionArea>
                            </Card>
                        ))}
                        {apply.isError && <Alert severity="error">{errorMessageOf(apply.error)}</Alert>}
                    </Stack>
                )}
            </DialogContent>
            <DialogActions>
                <Button onClick={handleClose}>閉じる</Button>
            </DialogActions>
        </Dialog>
    );
};
