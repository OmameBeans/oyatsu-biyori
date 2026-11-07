import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { AllergenCategoryMark, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useLatestProposalJob, useListedProducts, useRefreshMenu, useStartProposal, useUpdateBudget } from '../../../entities/menu';
import type { MenuDetail, MenuProposalOutcome } from '../../../entities/menu';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { formatDateLabel, formatDateTime, formatYen } from '../../../shared/lib';
import { JOB_STATE, MENU_STATUS } from '../../../shared/type';
import { JobProgress } from '../../../shared/ui';
import { BudgetFitDialog } from './BudgetFitDialog';
import { ConfirmMenuDialog } from './ConfirmMenuDialog';
import { DayCard } from './DayCard';
import { MenuIssuesDialog } from './MenuIssuesDialog';
import { OrderListDialog } from './OrderListDialog';

/** 問題のある日を「11/16（月）・11/20（金）」の形で並べる（同じ日は1回。日に結びつかないものは「週全体」） */
const issueDays = (issues: MenuDetail['issues']) =>
    [...new Set(issues.map((i) => i.date))]
        .sort((a, b) => (a ?? '').localeCompare(b ?? ''))
        .map((date) => (date ? formatDateLabel(date) : '週全体'))
        .join('・');

interface MenuBoardProps {
    menu: MenuDetail;
    isBudgetFitRequested: boolean;
    onBudgetFitClosed: () => void;
    onEditRequest: () => void;
    /**
     * 週の実績・予算と確定などのボタン（コマンドバー）を出す場所。渡さなければ献立の列の上に出す。
     * null（置き場所がまだできていない）のあいだは出さない（一度列の上に出てから動くのを避ける）
     */
    commandBarContainer?: HTMLElement | null;
    /** 「パッケージで確認する」（画面を離れずに、アレルゲン確認のダイアログを開く。ダイアログはページが持つ） */
    onCheckAllergen: (productId: string) => void;
}

/**
 * SC-09 手順4 献立案（T4-05・T4-06・T4-07・T4-11）。週の実績 / 週の予算・確かめることの件数と確定などのボタン（コマンドバー。
 * ページが用意した3列の上の場所に出す）、日に結びつかない警告、その下に日ごとのカード（横に並べる）。作成中は段階を示し、失敗したら要望を残したまま「もう一度作る」を出す。
 * 画面の上を広く使うため、「要望をこう理解しました」は AI チャットの見出しのボタン（InterpretedRequestsButton）から開く。
 */
export const MenuBoard = ({ menu, isBudgetFitRequested, onBudgetFitClosed, onEditRequest, commandBarContainer, onCheckAllergen }: MenuBoardProps) => {
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
    const [isOrderListOpen, setIsOrderListOpen] = useState(false);
    const [budgetDraft, setBudgetDraft] = useState(String(menu.weeklyBudget));
    const [isIssuesOpen, setIsIssuesOpen] = useState(false);
    // 開いている商品のカード（既定は閉じる）。献立を直すと日ごとのカードが作り直されるので、ここで持つ
    const [expandedItemIds, setExpandedItemIds] = useState<ReadonlySet<string>>(() => new Set());
    const toggleItem = (itemId: string) =>
        setExpandedItemIds((prev) => {
            const next = new Set(prev);
            if (!next.delete(itemId)) {
                next.add(itemId);
            }
            return next;
        });

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
    // 日に結びつかない警告（日ごとのカードには出ないもの）
    const weekIssues = menu.issues.filter((i) => !i.date && i.kind !== 'OverBudget');
    // コマンドバーのまとめ。日ごとのカードは横に並び、画面の外の日の 🔴/🟡 に気づけないため、件数を出す（区分は API の結果のまま）
    const blocked = menu.issues.filter((i) => i.kind === 'AllergenBlocked');
    const warnings = menu.issues.filter((i) => i.kind === 'AllergenWarning');
    const otherBlocking = menu.issues.filter((i) => i.blocksConfirmation && i.kind !== 'AllergenBlocked');
    const issuesToCheck = [...blocked, ...warnings, ...otherBlocking];
    // 「確かめること」からダイアログで記録して0件になったら、開いている状態も戻す
    // （戻さないと、あとで新しい問題が出たときに、押していないのにダイアログが開く）
    if (isIssuesOpen && issuesToCheck.length === 0) {
        setIsIssuesOpen(false);
    }

    const retry = () =>
        startProposal.mutate(menu.requestText ?? '', {
            onSuccess: setStartedJobId,
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });

    // 週の実績・予算と「確定して保存」などのボタン。ページが場所（コマンドバーの領域）を用意していれば、そこへ出す
    const commandBar = (
        <Paper
            component="section"
            aria-label="献立の操作"
            variant="outlined"
            sx={{ px: 2, py: 1.25, display: 'flex', flexWrap: 'wrap', alignItems: 'center', columnGap: 2, rowGap: 1 }}
        >
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', flex: 1, minWidth: 0 }} useFlexGap>
                <Chip
                    color={menu.status === MENU_STATUS.CONFIRMED ? 'success' : 'primary'}
                    label={menu.status === MENU_STATUS.CONFIRMED ? `確定済み（${menu.confirmedAt ? formatDateTime(menu.confirmedAt) : ''}）` : '下書き'}
                />
                <Typography variant="h3" component="p" sx={{ color: menu.week.isOverBudget ? 'error.main' : 'text.primary' }}>
                    実績 {formatYen(menu.week.total)} / 予算
                </Typography>
                {/* 入力欄と「円」は離さない */}
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <TextField
                        type="number"
                        size="small"
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
                </Box>
                {/* 予算に合わせる入れ替え案は、予算の入力欄のすぐ横に置く */}
                {menu.week.isUnderBudget && hasItems && (
                    <Button variant="outlined" onClick={() => setIsBudgetFitOpen(true)} disabled={isRunning}>
                        予算に合わせる
                    </Button>
                )}
                {menu.week.isOverBudget && (
                    <Typography sx={{ color: 'error.main', fontWeight: 600 }}>⚠ 週の予算を {formatYen(-menu.week.remaining)} 超えています</Typography>
                )}
                {/* 確かめることの件数。日ごとのカードは横に並び、画面の外の日の 🔴/🟡 に気づけないため、区分名と件数を常に出す。
                    押すと、日ごとに何を確かめるかをダイアログで出す（区分は API の結果のまま） */}
                {issuesToCheck.length > 0 && (
                    <Box component="section" aria-label="確かめること" sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
                        {/* どの日かはツールチップで添える（describeChild：ボタンの名前は「使えません 1件」のまま、日は説明として読む） */}
                        {blocked.length > 0 && (
                            <Tooltip title={issueDays(blocked)} describeChild>
                                <Button variant="outlined" color="error" onClick={() => setIsIssuesOpen(true)} sx={{ fontWeight: 700 }}>
                                    <AllergenCategoryMark category="Blocked" isInherit /> {blocked.length}件
                                </Button>
                            </Tooltip>
                        )}
                        {warnings.length > 0 && (
                            <Tooltip title={issueDays(warnings)} describeChild>
                                <Button variant="outlined" color="warning" onClick={() => setIsIssuesOpen(true)} sx={{ color: 'warning.dark', fontWeight: 700 }}>
                                    <AllergenCategoryMark category="Warning" isInherit /> {warnings.length}件
                                </Button>
                            </Tooltip>
                        )}
                        {otherBlocking.length > 0 && (
                            <Tooltip title={issueDays(otherBlocking)} describeChild>
                                <Button variant="outlined" color="error" onClick={() => setIsIssuesOpen(true)} sx={{ fontWeight: 700 }}>
                                    ⚠ 確定できない問題 {otherBlocking.length}件
                                </Button>
                            </Tooltip>
                        )}
                    </Box>
                )}
            </Stack>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ width: { xs: '100%', sm: 'auto' } }}>
                {menu.status === MENU_STATUS.CONFIRMED && hasItems && (
                    <Button variant="outlined" onClick={() => setIsOrderListOpen(true)}>
                        注文リスト（{menu.orderList.orderedCount}/{menu.orderList.lines.length}）
                    </Button>
                )}
                <Button onClick={onEditRequest} disabled={isRunning}>
                    要望を直す
                </Button>
                <Button variant="contained" onClick={() => setIsConfirmOpen(true)} disabled={isRunning || !hasItems}>
                    確定して保存
                </Button>
            </Stack>
        </Paper>
    );

    return (
        // PC は列の高さいっぱいにし、日ごとのカード（menu-days）だけを伸び縮みさせる
        <Stack spacing={2} sx={{ height: { md: '100%' }, '& > :not(.menu-days)': { flexShrink: 0 } }}>
            {commandBarContainer === undefined ? commandBar : commandBarContainer && createPortal(commandBar, commandBarContainer)}
            {menu.avoidedAllergenNames.length === 0 && <NoAvoidedAllergenNotice />}
            {/* 進み具合は献立案の作成のときだけ出す（チャットの処理中は、チャットの欄に出す。手で直すボタンは押せないままにする） */}
            {isRunning && !isChatRunning && (
                <Card>
                    <CardContent>
                        <JobProgress
                            stage={job?.stage}
                            fallbackStage="要望を読み取って、献立を考えています"
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
            {/* アレルゲン・重複などの警告は日ごとのカードの商品に出すので、ここではまとめない。
                日に結びつかない警告（週の予算は上のコマンドバーに出す）だけをここに出す */}
            {weekIssues.length > 0 && (
                <Alert severity="warning">
                    {weekIssues.map((issue) => (
                        <Typography key={`${issue.kind}-${issue.menuItemId}-${issue.message}`} variant="body2">
                            {issue.productName ? `「${issue.productName}」` : ''}
                            {issue.message}
                        </Typography>
                    ))}
                </Alert>
            )}

            {/* 日ごとのカード。スマホは縦に、それより広い幅は横に並べ（1日の幅は固定）、はみ出したら横にスクロールする。
                PC（md 以上）はカードを列の高さいっぱいにし、カードの中で商品の部分だけをスクロールする */}
            <Box
                className="menu-days"
                sx={{
                    display: 'flex',
                    flexDirection: { xs: 'column', sm: 'row' },
                    alignItems: { sm: 'flex-start', md: 'stretch' },
                    gap: 1.5,
                    overflowX: { sm: 'auto' },
                    flex: { md: 1 },
                    minHeight: { md: 360 },
                    pb: { sm: 1 },
                    '& > section': { width: { sm: 340 }, flexShrink: 0 },
                }}
            >
                {menu.days.map((day) => (
                    <DayCard
                        key={`${day.id}-${day.childrenCount}-${day.label ?? ''}`}
                        menu={menu}
                        day={day}
                        issues={menu.issues.filter((issue) => issue.date === day.date)}
                        candidates={candidates}
                        isBusy={isRunning}
                        expandedItemIds={expandedItemIds}
                        onToggleItem={toggleItem}
                        onCheckAllergen={onCheckAllergen}
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
                    enqueueSnackbar('献立を保存しました（確定済み）', { variant: 'success' });
                }}
            />
            <MenuIssuesDialog
                menu={menu}
                issues={issuesToCheck}
                isOpen={isIssuesOpen && issuesToCheck.length > 0}
                onClose={() => setIsIssuesOpen(false)}
                onCheckAllergen={onCheckAllergen}
            />
            <OrderListDialog menu={menu} isOpen={isOrderListOpen} onClose={() => setIsOrderListOpen(false)} />
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
