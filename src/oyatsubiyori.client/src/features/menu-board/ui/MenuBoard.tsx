import { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { AllergenCategoryMark, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useLatestProposalJob, useListedProducts, useRefreshMenu, useStartProposal, useUpdateBudget } from '../../../entities/menu';
import type { MenuDetail, MenuProposalOutcome } from '../../../entities/menu';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { formatDateLabel, formatDateTime, formatYen } from '../../../shared/lib';
import { DAY_TYPE_LABEL, JOB_STATE, MENU_STATUS } from '../../../shared/type';
import { JobProgress } from '../../../shared/ui';
import { BudgetFitDialog } from './BudgetFitDialog';
import { ConfirmMenuDialog } from './ConfirmMenuDialog';
import { DayCard } from './DayCard';

interface MenuBoardProps {
    menu: MenuDetail;
    isBudgetFitRequested: boolean;
    onBudgetFitClosed: () => void;
    onEditRequest: () => void;
}

/**
 * SC-09 手順4 献立案（T4-05・T4-06・T4-07・T4-11）。上部に「要望をこう理解しました」・週の実績 / 週の予算・警告の種類分け、
 * その下に日ごとのカード。作成中は段階を示し、失敗したら要望を残したまま「もう一度作る」を出す。
 */
export const MenuBoard = ({ menu, isBudgetFitRequested, onBudgetFitClosed, onEditRequest }: MenuBoardProps) => {
    const { data: latest } = useLatestProposalJob(menu.id);
    const { data: listed } = useListedProducts(menu.id);
    const refresh = useRefreshMenu(menu.id);
    const startProposal = useStartProposal(menu.id);
    const updateBudget = useUpdateBudget(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const [startedJobId, setStartedJobId] = useState<string | null>(null);
    const runningId = latest.job && !isJobFinished(latest.job) ? latest.job.id : null;
    const { data: job, isError: isPollError, refetch } = useJobPolling<MenuProposalOutcome>(startedJobId ?? runningId);
    // チャットの組み直しが動いているあいだも、手で直させない（「もう一度作る」はチャットの失敗には出さない）
    const chatRunningId = latest.chatJob && !isJobFinished(latest.chatJob) ? latest.chatJob.id : null;
    const { data: chatJob } = useJobPolling<MenuProposalOutcome>(chatRunningId);
    const isChatRunning = !!chatRunningId && !isJobFinished(chatJob);
    const isRunning = (!!(startedJobId ?? runningId) && !isJobFinished(job)) || isChatRunning;
    const [isConfirmOpen, setIsConfirmOpen] = useState(false);
    const [isBudgetFitOpen, setIsBudgetFitOpen] = useState(false);
    const [budgetDraft, setBudgetDraft] = useState(String(menu.weeklyBudget));

    useEffect(() => {
        if (job && isJobFinished(job)) {
            void refresh();
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [job?.state]);

    const candidates = listed.items.filter((item) => item.isCandidate);
    const hasItems = menu.days.some((day) => day.items.length > 0);
    const failed = job?.state === JOB_STATE.FAILED ? job : latest.job?.state === JOB_STATE.FAILED && !startedJobId ? latest.job : null;
    const outcome = job?.state === JOB_STATE.SUCCEEDED ? job.result : null;
    const blocked = menu.issues.filter((i) => i.kind === 'AllergenBlocked');
    const warnings = menu.issues.filter((i) => i.kind === 'AllergenWarning');
    const duplicates = menu.issues.filter((i) => i.kind === 'Duplicate');
    const others = menu.issues.filter((i) => !['AllergenBlocked', 'AllergenWarning', 'Duplicate', 'OverBudget'].includes(i.kind));
    const interpreted = menu.interpretedRequests;

    const retry = () =>
        startProposal.mutate(menu.requestText ?? '', {
            onSuccess: setStartedJobId,
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    return (
        <Stack spacing={2}>
            {menu.avoidedAllergenNames.length === 0 && <NoAvoidedAllergenNotice />}
            {isRunning && (
                <Card>
                    <CardContent>
                        <JobProgress
                            stage={isChatRunning ? chatJob?.stage : job?.stage}
                            fallbackStage={isChatRunning ? 'ご依頼に合わせて、献立を組み直しています' : '要望を読み取って、献立を考えています'}
                            note="数十秒から数分かかることがあります。画面を閉じても続きます。"
                        />
                    </CardContent>
                </Card>
            )}
            {isPollError && (
                <Alert severity="warning" action={<Button color="inherit" onClick={() => void refetch()}>もう一度確かめる</Button>}>
                    処理の状態を確かめられませんでした。通信を確かめてください。
                </Alert>
            )}
            {failed && !isRunning && (
                <Alert
                    severity="error"
                    action={
                        <Button color="inherit" onClick={retry} disabled={startProposal.isPending}>
                            もう一度作る
                        </Button>
                    }
                    sx={{ whiteSpace: 'pre-wrap' }}
                >
                    {failed.errorMessages.join('\n')}
                </Alert>
            )}

            <Card>
                <CardContent sx={{ display: 'flex', flexDirection: { xs: 'column', md: 'row' }, gap: 2, alignItems: { md: 'center' } }}>
                    <Box sx={{ flex: 1 }}>
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
                            <Chip
                                color={menu.status === MENU_STATUS.CONFIRMED ? 'success' : 'primary'}
                                label={menu.status === MENU_STATUS.CONFIRMED ? `確定済み（${menu.confirmedAt ? formatDateTime(menu.confirmedAt) : ''}）` : '下書き'}
                            />
                            <Typography variant="h3" component="p" sx={{ color: menu.week.isOverBudget ? 'error.main' : 'text.primary' }}>
                                週の実績 {formatYen(menu.week.total)} / 週の予算
                            </Typography>
                            <TextField
                                type="number"
                                value={budgetDraft}
                                onChange={(event) => setBudgetDraft(event.target.value)}
                                onBlur={() =>
                                    budgetDraft !== String(menu.weeklyBudget) &&
                                    updateBudget.mutate(
                                        { budget: Number(budgetDraft), rowVersion: menu.rowVersion },
                                        { onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }) },
                                    )
                                }
                                sx={{ width: 120 }}
                                slotProps={{ htmlInput: { 'aria-label': 'この週の予算（円）' } }}
                            />
                            <Typography>円</Typography>
                        </Stack>
                        {menu.week.isOverBudget && (
                            <Typography sx={{ color: 'error.main', fontWeight: 600 }}>⚠ 週の予算を {formatYen(-menu.week.remaining)} 超えています</Typography>
                        )}
                        {menu.status === MENU_STATUS.CONFIRMED && (
                            <Typography variant="body2" color="text.secondary">
                                商品・個数・日の種類などを直すと下書きに戻ります。直したら、もう一度「確定」を押してください。
                            </Typography>
                        )}
                    </Box>
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                        {menu.week.isUnderBudget && hasItems && (
                            <Button variant="outlined" onClick={() => setIsBudgetFitOpen(true)} disabled={isRunning}>
                                予算に合わせる
                            </Button>
                        )}
                        <Button onClick={onEditRequest} disabled={isRunning}>
                            要望を直す
                        </Button>
                        <Button variant="contained" onClick={() => setIsConfirmOpen(true)} disabled={isRunning || !hasItems}>
                            {menu.status === MENU_STATUS.CONFIRMED ? '確定済み（もう一度確かめる）' : '確定'}
                        </Button>
                    </Stack>
                </CardContent>
            </Card>

            {interpreted && (interpreted.days.length > 0 || interpreted.policies.length > 0 || interpreted.rejected.length > 0) && (
                <Card>
                    <CardContent>
                        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                            要望をこう理解しました
                        </Typography>
                        <Stack component="ul" spacing={0.5} sx={{ pl: 2.5, m: 0 }}>
                            {interpreted.days.map((d) => (
                                <li key={d.date}>
                                    {formatDateLabel(d.date)}：
                                    {[d.childrenCount !== null ? `人数 ${d.childrenCount}人` : null, d.dayType ? DAY_TYPE_LABEL[d.dayType] : null, d.label ? `「${d.label}」` : null]
                                        .filter(Boolean)
                                        .join('・')}
                                </li>
                            ))}
                            {interpreted.policies.map((p) => (
                                <li key={p.text}>
                                    {p.days && p.days.length > 0 ? `${p.days.map(formatDateLabel).join('・')}：` : '毎日：'}
                                    {p.text}
                                </li>
                            ))}
                        </Stack>
                        {interpreted.rejected.length > 0 && (
                            <Alert severity="warning" sx={{ mt: 1 }}>
                                <Typography sx={{ fontWeight: 700 }}>反映しなかった要望</Typography>
                                {interpreted.rejected.map((r) => (
                                    <Typography key={r.text} variant="body2">
                                        ・{r.text}：{r.reason}
                                    </Typography>
                                ))}
                            </Alert>
                        )}
                        <Typography variant="caption" color="text.secondary">
                            人数・日の種類・見出しは、下の日ごとのカードで直せます。
                        </Typography>
                    </CardContent>
                </Card>
            )}

            {outcome && outcome.removedItems.length > 0 && (
                <Alert severity="warning">
                    <Typography sx={{ fontWeight: 700 }}>直しきれなかった問題（外した商品）</Typography>
                    {outcome.removedItems.map((line) => (
                        <Typography key={line} variant="body2">
                            ・{line}
                        </Typography>
                    ))}
                </Alert>
            )}
            {(blocked.length > 0 || warnings.length > 0 || duplicates.length > 0 || others.length > 0) && (
                <Card>
                    <CardContent>
                        <Typography variant="h3" component="h2" sx={{ mb: 1 }}>
                            確かめること
                        </Typography>
                        <Stack spacing={0.5}>
                            {blocked.length > 0 && (
                                <Typography sx={{ color: 'error.main', fontWeight: 700 }}>
                                    <AllergenCategoryMark category="Blocked" /> {blocked.length}件（別の商品に替えてください）
                                </Typography>
                            )}
                            {warnings.length > 0 && (
                                <Typography sx={{ color: 'warning.dark', fontWeight: 600 }}>
                                    <AllergenCategoryMark category="Warning" isInherit /> {warnings.length}件（確定のときに1件ずつ確かめます）
                                </Typography>
                            )}
                            {duplicates.length > 0 && <Typography>同じ商品を出す期間 {duplicates.length}件</Typography>}
                            {others.map((issue) => (
                                <Typography key={`${issue.kind}-${issue.date}-${issue.menuItemId}`} variant="body2">
                                    ⚠ {issue.date ? `${formatDateLabel(issue.date)} ` : ''}
                                    {issue.productName ? `「${issue.productName}」` : ''}
                                    {issue.message}
                                </Typography>
                            ))}
                        </Stack>
                    </CardContent>
                </Card>
            )}

            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', xl: '1fr 1fr' }, gap: 1.5 }}>
                {menu.days.map((day) => (
                    <DayCard
                        key={`${day.id}-${day.childrenCount}-${day.label ?? ''}`}
                        menu={menu}
                        day={day}
                        issues={menu.issues.filter((issue) => issue.date === day.date)}
                        candidates={candidates}
                        isBusy={isRunning}
                    />
                ))}
            </Box>

            <ConfirmMenuDialog
                key={isConfirmOpen ? 'confirm-open' : 'confirm-closed'}
                menu={menu}
                isOpen={isConfirmOpen}
                onClose={() => setIsConfirmOpen(false)}
                onSuccess={() => {
                    setIsConfirmOpen(false);
                    enqueueSnackbar('献立を確定しました', { variant: 'success' });
                }}
            />
            <BudgetFitDialog
                key={isBudgetFitOpen || isBudgetFitRequested ? 'fit-open' : 'fit-closed'}
                menu={menu}
                isOpen={isBudgetFitOpen || isBudgetFitRequested}
                onClose={() => {
                    setIsBudgetFitOpen(false);
                    onBudgetFitClosed();
                }}
            />
        </Stack>
    );
};
