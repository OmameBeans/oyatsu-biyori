import { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useChat, useLatestProposalJob, useRefreshMenu } from '../../../entities/menu';
import type { MenuDetail, MenuProposalOutcome } from '../../../entities/menu';
import { errorMessageOf, isJobFinished, useJobPolling } from '../../../shared/api';
import { JOB_STATE } from '../../../shared/type';
import { ClaudeText, JobProgress } from '../../../shared/ui';

interface ChatPanelProps {
    menu: MenuDetail;
    onBudgetFitRequested: () => void;
    /** 画面を離れると消える入力（送っていない依頼。なければ null）が変わったとき。離れる前の確認に使う */
    onUnsavedInputChange?: (unsavedInput: string | null) => void;
}

/**
 * チャットでの修正（T4-08）。依頼はバックグラウンドで組み直し、終わったら献立を読み直す。
 * 「予算に合わせて」の依頼なら、入れ替え案を出す（U-12）。失敗しても入力した依頼は消さない。
 */
export const ChatPanel = ({ menu, onBudgetFitRequested, onUnsavedInputChange }: ChatPanelProps) => {
    const chat = useChat(menu.id);
    const refresh = useRefreshMenu(menu.id);
    const [message, setMessage] = useState('');
    const [jobId, setJobId] = useState<string | null>(null);
    const { data: latest } = useLatestProposalJob(menu.id);
    const latestRunning = [latest.job, latest.chatJob].find((j) => j && !isJobFinished(j) && j.id !== jobId);
    const otherRunningId = latestRunning ? latestRunning.id : null;
    const { data: job, isError: isPollError, refetch } = useJobPolling<MenuProposalOutcome>(jobId);
    const { data: otherJob } = useJobPolling<MenuProposalOutcome>(otherRunningId);
    const isOtherRunning = !!otherRunningId && !isJobFinished(otherJob);
    const isRunning = (!!jobId && !isJobFinished(job)) || isOtherRunning;

    // 送っていない依頼があるかを、離れる前の確認のために親へ知らせる（送ったあと・組み直しの途中は、サーバーで続くので数えない）
    const isOwnJobRunning = chat.isPending || (!!jobId && !isJobFinished(job) && !isPollError);
    const unsavedInput = message.trim() !== '' && !isOwnJobRunning ? '送っていないチャットの依頼' : null;
    useEffect(() => {
        onUnsavedInputChange?.(unsavedInput);
    }, [unsavedInput, onUnsavedInputChange]);
    useEffect(() => () => onUnsavedInputChange?.(null), [onUnsavedInputChange]);

    // 組み直しが終わったら、入力欄を空にする（描画の中で切り替える。失敗したら依頼は残す）
    const [handledJobId, setHandledJobId] = useState<string | null>(null);
    if (job && job.state === JOB_STATE.SUCCEEDED && handledJobId !== job.id) {
        setHandledJobId(job.id);
        setMessage('');
    }

    useEffect(() => {
        if (job && isJobFinished(job)) {
            void refresh();
            if (job.state === JOB_STATE.SUCCEEDED) {
                if (job.result?.isBudgetFitRequest) {
                    onBudgetFitRequested();
                }
            }
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [job?.state]);

    return (
        <Paper variant="outlined" sx={{ display: 'flex', flexDirection: 'column', height: { md: '100%' }, minHeight: { xs: 360 }, borderRadius: 4 }}>
            <Typography variant="h3" component="h2" sx={{ p: 2, pb: 1 }}>
                チャットで直す
            </Typography>
            <Box sx={{ flex: 1, overflow: 'auto', px: 2 }} role="log" aria-label="チャットの履歴">
                {menu.messages.length === 0 && (
                    <Typography variant="body2" color="text.secondary">
                        例：「水曜はゼリーをやめて、せんべいにしてください」「予算に合わせてください」
                    </Typography>
                )}
                <Stack spacing={1}>
                    {menu.messages.map((m) => (
                        <Box
                            key={`${m.createdAt}-${m.role}`}
                            sx={{
                                alignSelf: m.role === 'Staff' ? 'flex-end' : 'flex-start',
                                maxWidth: '90%',
                                bgcolor: m.role === 'Staff' ? 'primary.light' : 'action.hover',
                                borderRadius: 3,
                                px: 1.5,
                                py: 1,
                            }}
                        >
                            <ClaudeText text={m.content} />
                        </Box>
                    ))}
                </Stack>
                {isRunning && <JobProgress stage={job?.stage ?? otherJob?.stage} fallbackStage="献立を考えています" />}
                {isPollError && (
                    <Alert severity="warning" sx={{ my: 1 }} action={<Button color="inherit" onClick={() => void refetch()}>もう一度確かめる</Button>}>
                        処理の状態を確かめられませんでした。通信を確かめてください。
                    </Alert>
                )}
                {job?.state === JOB_STATE.FAILED && <Alert severity="error" sx={{ my: 1, whiteSpace: 'pre-wrap' }}>{job.errorMessages.join('\n')}</Alert>}
                {chat.isError && <Alert severity="error" sx={{ my: 1 }}>{errorMessageOf(chat.error)}</Alert>}
            </Box>
            <Box sx={{ p: 2, pt: 1 }}>
                <Typography variant="caption" color="text.secondary">
                    子どもの名前は書かないでください（AI に送ります）
                </Typography>
                <Stack direction="row" spacing={1} sx={{ mt: 0.5 }}>
                    <TextField
                        value={message}
                        onChange={(event) => setMessage(event.target.value)}
                        placeholder="直してほしいことを書く"
                        multiline
                        maxRows={4}
                        fullWidth
                        disabled={isRunning}
                        slotProps={{ htmlInput: { 'aria-label': 'チャットの依頼', maxLength: 1000 } }}
                    />
                    <Button
                        variant="contained"
                        disabled={!message.trim() || isRunning || chat.isPending}
                        onClick={() => chat.mutate(message, { onSuccess: setJobId })}
                    >
                        送る
                    </Button>
                </Stack>
            </Box>
        </Paper>
    );
};
