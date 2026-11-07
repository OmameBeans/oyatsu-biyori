import { useEffect, useRef, useState } from 'react';
import type { KeyboardEvent, ReactNode } from 'react';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutlineOutlined';
import DeleteSweepOutlinedIcon from '@mui/icons-material/DeleteSweepOutlined';
import KeyboardDoubleArrowRightIcon from '@mui/icons-material/KeyboardDoubleArrowRight';
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputBase from '@mui/material/InputBase';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import { keyframes } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { AiAnswerAllergenNotice } from '../../../entities/allergen-judgement';
import { useChat, useClearChat, useLatestProposalJob, useRefreshMenu } from '../../../entities/menu';
import type { MenuDetail, MenuProposalOutcome } from '../../../entities/menu';
import { errorMessageOf, isJobFinished, useJobPolling, useJobStream } from '../../../shared/api';
import { JOB_STATE } from '../../../shared/type';
import { ClaudeMarkdown, ClaudeText, ConfirmDialog } from '../../../shared/ui';

/** 文字の上を光がゆっくり流れる（処理中であることを、バーより控えめに示す） */
const shimmer = keyframes`
    from { background-position: 100% 0; }
    to { background-position: -100% 0; }
`;

/** 処理中の段階名（「ご依頼を読んでいます…」）。動きを減らす設定のときは、色を変えずに文字だけ出す */
const ThinkingText = ({ text }: { text: string }) => (
    <Typography
        role="status"
        aria-live="polite"
        variant="body2"
        sx={(theme) => ({
            display: 'inline-block',
            px: 0.5,
            py: 1,
            color: 'text.secondary',
            '@media (prefers-reduced-motion: no-preference)': {
                color: 'transparent',
                backgroundImage: `linear-gradient(90deg, ${theme.palette.text.secondary} 0%, ${theme.palette.text.secondary} 40%, ${theme.palette.text.primary} 50%, ${theme.palette.text.secondary} 60%, ${theme.palette.text.secondary} 100%)`,
                backgroundSize: '200% 100%',
                backgroundClip: 'text',
                WebkitBackgroundClip: 'text',
                animation: `${shimmer} 2s linear infinite`,
            },
        })}
    >
        {text}…
    </Typography>
);

interface ChatPanelProps {
    menu: MenuDetail;
    onBudgetFitRequested: () => void;
    /** 画面を離れると消える入力（送っていない依頼。なければ null）が変わったとき。離れる前の確認に使う */
    onUnsavedInputChange?: (unsavedInput: string | null) => void;
    /** 閉じて細い帯にしているか（広い PC だけ。閉じても入力中の文字・流れてくる返答は消さない） */
    isCollapsed?: boolean;
    /** 開閉を切り替える（渡さなければ、開閉のボタンを出さない。スマホはタブで切り替えるため） */
    onToggleCollapsed?: () => void;
    /** 見出しの「チャットを消す」の左に置くボタン（週の要望を開くボタンなど）。閉じた帯にも出す */
    headerActions?: ReactNode;
}

/**
 * チャットでの修正（T4-08）。依頼はバックグラウンドで組み直し、終わったら献立を読み直す。
 * 「予算に合わせて」の依頼なら、入れ替え案を出す（U-12）。失敗しても入力した依頼は消さない。
 * 質問には、献立を変えずに文章で答える。返答は届いた順に流し、安全な範囲の Markdown で出す（D-91）。
 */
export const ChatPanel = ({ menu, onBudgetFitRequested, onUnsavedInputChange, isCollapsed = false, onToggleCollapsed, headerActions }: ChatPanelProps) => {
    const chat = useChat(menu.id);
    const clearChat = useClearChat(menu.id);
    const [isClearOpen, setIsClearOpen] = useState(false);
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

    // 送ったら入力欄を空にして履歴に出す。失敗したら、送った文章を入力欄に戻す（描画の中で切り替える。NFR-UX-04）
    const [sentText, setSentText] = useState('');
    const [handledJobId, setHandledJobId] = useState<string | null>(null);
    if (job && job.state === JOB_STATE.FAILED && handledJobId !== job.id) {
        setHandledJobId(job.id);
        setMessage((current) => current || sentText);
    }

    // 返答は届いた順に流す（D-91）。保存された返答を読み込むまでは、流れてきた文章を出しておく
    const activeJob = jobId ? job : otherJob;
    const stream = useJobStream(jobId ?? otherRunningId);
    const hasSavedReply = !!activeJob && menu.messages.some((m) => m.role === 'Claude' && Date.parse(m.createdAt) >= Date.parse(activeJob.createdAt));
    const showStream = stream.text.trim() !== '' && activeJob?.state !== JOB_STATE.FAILED && !hasSavedReply;

    // 新しいメッセージ・流れてきた文章が見えるように、いちばん下までスクロールする
    const logRef = useRef<HTMLDivElement>(null);
    useEffect(() => {
        const log = logRef.current;
        if (log) {
            log.scrollTop = log.scrollHeight;
        }
    }, [menu.messages.length, stream.text, isRunning]);

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

    const canSend = !!message.trim() && !isRunning && !chat.isPending;
    const handleSend = () => {
        if (!canSend) return;
        const text = message;
        chat.mutate(text, {
            onSuccess: (id) => {
                setJobId(id);
                setSentText(text);
                setMessage('');
                // 送った依頼を、すぐ履歴に出す（サーバーは受け付けたときに保存している）
                void refresh();
            },
        });
    };
    // Enter は改行（日本語の変換の確定と混ざらないように）。Ctrl / ⌘ + Enter で送る
    const handleKeyDown = (event: KeyboardEvent<HTMLInputElement | HTMLTextAreaElement>) => {
        if (event.key === 'Enter' && (event.ctrlKey || event.metaKey) && !event.nativeEvent.isComposing) {
            event.preventDefault();
            handleSend();
        }
    };

    if (isCollapsed) {
        // 閉じているときは細い帯だけ出す（組み直し・返答の途中なら、くるくるで知らせる）
        return (
            <Paper variant="outlined" sx={{ height: '100%', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1, py: 1 }}>
                <Tooltip title="AIチャットを開く" placement="left">
                    <IconButton aria-label="AIチャットを開く" aria-expanded={false} onClick={onToggleCollapsed} color="primary">
                        <SmartToyOutlinedIcon />
                    </IconButton>
                </Tooltip>
                <Typography variant="body2" color="text.secondary" sx={{ writingMode: 'vertical-rl', letterSpacing: '0.1em', userSelect: 'none' }}>
                    AIチャット
                </Typography>
                {isRunning && <CircularProgress size={18} aria-label="AI が考えています" />}
                {headerActions}
            </Paper>
        );
    }

    return (
        <Paper variant="outlined" sx={{ display: 'flex', flexDirection: 'column', height: { md: '100%' }, minHeight: { xs: 360 } }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', p: 2, pb: 1 }}>
                <SmartToyOutlinedIcon color="primary" aria-hidden />
                <Typography variant="h3" component="h2" sx={{ flex: 1 }}>
                    AIチャット
                </Typography>
                {headerActions}
                <Tooltip title="チャットを消す">
                    {/* 押せないときもツールチップを出すため span で包む */}
                    <span>
                        <IconButton
                            aria-label="チャットを消す"
                            onClick={() => setIsClearOpen(true)}
                            disabled={menu.messages.length === 0 || isRunning || chat.isPending || clearChat.isPending}
                            size="small"
                        >
                            <DeleteSweepOutlinedIcon fontSize="small" />
                        </IconButton>
                    </span>
                </Tooltip>
                {onToggleCollapsed && (
                    <Tooltip title="AIチャットを閉じる">
                        <IconButton aria-label="AIチャットを閉じる" aria-expanded onClick={onToggleCollapsed} size="small" edge="end">
                            <KeyboardDoubleArrowRightIcon fontSize="small" />
                        </IconButton>
                    </Tooltip>
                )}
            </Stack>
            <Box ref={logRef} sx={{ flex: 1, overflow: 'auto', px: 2 }} role="log" aria-label="チャットの履歴">
                {menu.messages.length === 0 && !showStream && !isRunning && (
                    <Stack spacing={1} sx={{ alignItems: 'center', justifyContent: 'center', textAlign: 'center', height: '100%', minHeight: 160, color: 'text.secondary' }}>
                        <ChatBubbleOutlineIcon sx={{ fontSize: 40 }} aria-hidden />
                        <Typography variant="body2">何かお手伝いできることはありますか？</Typography>
                    </Stack>
                )}
                <Stack spacing={1.5}>
                    {menu.messages.map((m) =>
                        m.role === 'Staff' ? (
                            <Box
                                key={`${m.createdAt}-${m.role}`}
                                sx={{ alignSelf: 'flex-end', maxWidth: '85%', bgcolor: 'primary.light', borderRadius: 2, px: 1.5, py: 1 }}
                            >
                                <ClaudeText text={m.content} />
                            </Box>
                        ) : (
                            // Claude の返答は吹き出しにせず、横幅いっぱいに Markdown で出す
                            <Box key={`${m.createdAt}-${m.role}`} sx={{ px: 0.5 }}>
                                <ClaudeMarkdown text={m.content} />
                            </Box>
                        ),
                    )}
                    {showStream && (
                        <Box sx={{ px: 0.5 }} aria-busy={isRunning}>
                            <ClaudeMarkdown text={stream.text} />
                        </Box>
                    )}
                    {/* AI の返答はアレルゲンの判定ではないことを、いちばん新しい返答（流している途中を含む）の下に1回だけ出す（D-91・SR-18） */}
                    {(showStream || (!isRunning && menu.messages.at(-1)?.role === 'Claude')) && (
                        <Typography variant="caption" color="text.secondary" sx={{ px: 0.5 }}>
                            {AiAnswerAllergenNotice()}
                        </Typography>
                    )}
                </Stack>
                {isRunning && <ThinkingText text={stream.stage ?? activeJob?.stage ?? 'ご依頼を読んでいます'} />}
                {isPollError && (
                    <Alert severity="warning" sx={{ my: 1 }} action={<Button color="inherit" onClick={() => void refetch()}>もう一度確かめる</Button>}>
                        処理の状態を確かめられませんでした。通信を確かめてください。
                    </Alert>
                )}
                {job?.state === JOB_STATE.FAILED && <Alert severity="error" sx={{ my: 1, whiteSpace: 'pre-wrap' }}>{job.errorMessages.join('\n')}</Alert>}
                {chat.isError && <Alert severity="error" sx={{ my: 1 }}>{errorMessageOf(chat.error)}</Alert>}
            </Box>
            {/* 入力欄：枠の中に、文章と、下の段に注意書きと送るボタンを並べる */}
            <Box sx={{ p: 1.5, pt: 1 }}>
                <Box
                    sx={{
                        border: 1,
                        borderColor: 'divider',
                        borderRadius: 2,
                        bgcolor: isRunning ? 'action.hover' : 'background.paper',
                        transition: 'border-color 0.15s',
                        '&:hover': { borderColor: isRunning ? 'divider' : 'text.primary' },
                        '&:focus-within': { borderColor: 'primary.main', boxShadow: (theme) => `0 0 0 1px ${theme.palette.primary.main}` },
                    }}
                >
                    <InputBase
                        value={message}
                        onChange={(event) => setMessage(event.target.value)}
                        onKeyDown={handleKeyDown}
                        placeholder="AIとチャット"
                        multiline
                        minRows={2}
                        maxRows={6}
                        fullWidth
                        disabled={isRunning}
                        sx={{ px: 1.5, pt: 1.25, pb: 0.5, fontSize: '0.95rem' }}
                        slotProps={{ input: { 'aria-label': 'チャットの依頼', maxLength: 1000 } }}
                    />
                    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 1, pb: 0.75 }}>
                        <Typography variant="caption" color="text.secondary" sx={{ flex: 1, pl: 0.5, lineHeight: 1.4 }}>
                            子どもの名前は書かないでください（AI に送ります）
                        </Typography>
                        <Tooltip title="送る（Ctrl + Enter）">
                            <span>
                                <IconButton
                                    aria-label="送る"
                                    color="primary"
                                    size="small"
                                    disabled={!canSend}
                                    onClick={handleSend}
                                    sx={{
                                        bgcolor: canSend ? 'primary.main' : 'action.disabledBackground',
                                        color: 'primary.contrastText',
                                        '&:hover': { bgcolor: 'primary.dark' },
                                        '&.Mui-disabled': { color: 'action.disabled' },
                                    }}
                                >
                                    <ArrowUpwardIcon fontSize="small" />
                                </IconButton>
                            </span>
                        </Tooltip>
                    </Stack>
                </Box>
            </Box>
            <ConfirmDialog
                isOpen={isClearOpen}
                title="チャットを消しますか？"
                confirmLabel="消す"
                isDanger
                isPending={clearChat.isPending}
                onClose={() => setIsClearOpen(false)}
                onConfirm={() =>
                    clearChat.mutate(undefined, {
                        onSuccess: () => {
                            setIsClearOpen(false);
                            // 前の処理の返答を、流れてきた文章として出し直さないように
                            setJobId(null);
                            setSentText('');
                        },
                    })
                }
            >
                <Typography>これまでのやりとりを消します。献立と週の要望はそのまま残ります。</Typography>
                {clearChat.isError && <Alert severity="error" sx={{ mt: 2 }}>{errorMessageOf(clearChat.error)}</Alert>}
            </ConfirmDialog>
        </Paper>
    );
};
