import { Suspense, useEffect } from 'react';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import { useSnackbar } from 'notistack';
import { acknowledgementSentence, AllergenCategoryMark, AllergenJudgementLine, ISSUE_GUIDE, needsPackageCheck } from '../../../entities/allergen-judgement';
import { useAcknowledge, useMenu, useRefreshMenu } from '../../../entities/menu';
import type { MenuDetail, MenuItem } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatWeekLabel } from '../../../shared/lib';
import { allergenCheckPath, MENU_ITEM_ISSUE, MENU_STATUS, menuPath } from '../../../shared/type';

interface WeekIssuesDialogProps {
    isOpen: boolean;
    /** 開く献立。閉じているときは null */
    menuId: string | null;
    onClose: () => void;
}

/**
 * 週のバッジ（🔴 使えません・確認し直し・🟡 要確認）を押すと開く、商品の確認のダイアログ（SC-10・SC-11・SC-14）。
 * 商品ごとに「何を」「どのように」確認するかを示す。理由と次にすることは共通部品（AllergenJudgementLine）から出す。
 * 確認し直しは、このダイアログでチェックを付け直せる（献立は確定のまま。D-71）。要確認の確認の画面（SC-08）と、使えませんの献立（SC-09）は別のタブで開く
 * （一覧とダイアログを残したまま確かめられるように）。タブに戻ったら、その週の献立と一覧を読み直す。
 * 分け方はサーバーが商品ごとに返す確認の種類（issueKind。献立一覧の件数・日ごとのカードと同じ。D-90）をそのまま使う。説明の文は共通部品（ISSUE_GUIDE）から出す。
 */
export const WeekIssuesDialog = ({ isOpen, menuId, onClose }: WeekIssuesDialogProps) => {
    const theme = useTheme();
    const isNarrow = useMediaQuery(theme.breakpoints.down('sm'));
    return (
        <Dialog open={isOpen && menuId !== null} onClose={onClose} fullWidth maxWidth="sm" fullScreen={isNarrow} aria-labelledby="week-issues-title">
            {menuId && (
                <Suspense
                    fallback={
                        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
                            <CircularProgress />
                        </Box>
                    }
                >
                    <WeekIssuesContent menuId={menuId} onClose={onClose} />
                </Suspense>
            )}
        </Dialog>
    );
};

/** 別のタブで開くリンクのボタンに渡すもの（アイコンで新しいタブで開くことを示し、読み上げでも伝える） */
const newTabProps = (label: string) => ({
    component: RouterLink,
    target: '_blank',
    rel: 'noopener noreferrer',
    endIcon: <OpenInNewIcon fontSize="small" />,
    'aria-label': `${label}（新しいタブで開く）`,
});

interface EntryItem {
    date: string;
    item: MenuItem;
}

interface WeekIssuesContentProps {
    menuId: string;
    onClose: () => void;
}

const WeekIssuesContent = ({ menuId, onClose }: WeekIssuesContentProps) => {
    const { data: menu } = useMenu(menuId);
    const refresh = useRefreshMenu(menuId);
    // 別のタブで確認してから戻ってきたら、その週の献立と一覧を読み直す（アプリ全体では、タブに戻ったときの読み直しを切っているため）
    useEffect(() => {
        const handleVisible = () => {
            if (document.visibilityState === 'visible') {
                void refresh();
            }
        };
        document.addEventListener('visibilitychange', handleVisible);
        return () => document.removeEventListener('visibilitychange', handleVisible);
    }, [refresh]);
    const entries: EntryItem[] = menu.days.flatMap((day) => day.items.map((item) => ({ date: day.date, item })));
    const isConfirmed = menu.status === MENU_STATUS.CONFIRMED;
    const blocked = entries.filter(({ item }) => item.issueKind === MENU_ITEM_ISSUE.BLOCKED);
    const recheck = entries.filter(({ item }) => item.issueKind === MENU_ITEM_ISSUE.RECHECK);
    const pending = entries.filter(({ item }) => item.issueKind === MENU_ITEM_ISSUE.WARNING);
    const isEmpty = blocked.length === 0 && recheck.length === 0 && pending.length === 0;

    return (
        <>
            <DialogTitle id="week-issues-title">{formatWeekLabel(menu.weekStart)}の商品の確認</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={3}>
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                        この週の献立で、確認が必要な商品です。商品ごとに、何をどのように確認するかを書いています。
                    </Typography>
                    {isEmpty && <Typography>確認が必要な商品はありません。</Typography>}
                    {blocked.length > 0 && (
                        <IssueSection title={<AllergenCategoryMark category="Blocked" />} count={blocked.length} description={ISSUE_GUIDE.BLOCKED}>
                            {blocked.map((entry) => (
                                <IssueItem key={entry.item.id} entry={entry}>
                                    {/* 情報がないため 🔴 の商品は、パッケージで確認すれば使えることがある（03 §6.3） */}
                                    {entry.item.judgement && needsPackageCheck(entry.item.judgement) && <PackageCheckLink item={entry.item} />}
                                    <Button
                                        {...newTabProps('献立を開いて替える')}
                                        to={menuPath(menuId, 4)}
                                        variant="outlined"
                                        size="small"
                                        sx={{ alignSelf: 'flex-start', fontWeight: 700 }}
                                    >
                                        献立を開いて替える
                                    </Button>
                                </IssueItem>
                            ))}
                        </IssueSection>
                    )}
                    {recheck.length > 0 && (
                        <IssueSection title="確認し直し" count={recheck.length} description={ISSUE_GUIDE.RECHECK}>
                            {recheck.map((entry) => (
                                <IssueItem key={entry.item.id} entry={entry}>
                                    <RecheckAction menu={menu} item={entry.item} />
                                </IssueItem>
                            ))}
                        </IssueSection>
                    )}
                    {pending.length > 0 && (
                        <IssueSection
                            title={<AllergenCategoryMark category="Warning" />}
                            count={pending.length}
                            description={isConfirmed ? ISSUE_GUIDE.WARNING_CONFIRMED : ISSUE_GUIDE.WARNING_DRAFT}
                        >
                            {pending.map((entry) => (
                                <IssueItem key={entry.item.id} entry={entry}>
                                    <PackageCheckLink item={entry.item} />
                                </IssueItem>
                            ))}
                        </IssueSection>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button {...newTabProps('献立を開く')} to={menuPath(menuId, 4)}>
                    献立を開く
                </Button>
                <Button onClick={onClose} variant="contained">
                    閉じる
                </Button>
            </DialogActions>
        </>
    );
};

interface IssueSectionProps {
    title: ReactNode;
    count: number;
    description: string;
    children: ReactNode;
}

/** 種類ごとのまとまり（見出し・件数・何をすればよいか） */
const IssueSection = ({ title, count, description, children }: IssueSectionProps) => (
    <Box component="section">
        <Typography variant="h3" component="h3" sx={{ fontSize: '1rem', mb: 0.5 }}>
            {title} {count}件
        </Typography>
        <Typography variant="body2" sx={{ mb: 1.5 }}>
            {description}
        </Typography>
        <Stack spacing={1.5} divider={<Divider flexItem />}>
            {children}
        </Stack>
    </Box>
);

interface IssueItemProps {
    entry: EntryItem;
    children: ReactNode;
}

/** 商品1件（日付・商品名・理由と次にすること・操作） */
const IssueItem = ({ entry, children }: IssueItemProps) => (
    <Stack spacing={0.75}>
        <Typography sx={{ fontWeight: 700 }}>
            {formatDateLabel(entry.date)} {entry.item.productName}
        </Typography>
        {entry.item.judgement ? (
            <AllergenJudgementLine judgement={entry.item.judgement} isInMenu showManufacturerCheck />
        ) : (
            <Typography variant="body2">{ISSUE_GUIDE.MISSING_PRODUCT}</Typography>
        )}
        {children}
    </Stack>
);

interface PackageCheckLinkProps {
    item: MenuItem;
}

/** 「パッケージで確認する」（アレルゲン確認の画面 SC-08 を別のタブで開く） */
const PackageCheckLink = ({ item }: PackageCheckLinkProps) => (
    <Button
        {...newTabProps(`「${item.productName}」をパッケージで確認する`)}
        to={allergenCheckPath(item.productId)}
        variant="outlined"
        size="small"
        sx={{ alignSelf: 'flex-start', fontWeight: 700 }}
    >
        パッケージで確認する
    </Button>
);

interface RecheckActionProps {
    menu: MenuDetail;
    item: MenuItem;
}

/** 確認し直しのチェック（日ごとのカードと同じ操作。画面で読んだ判定の種類と指紋を送り、変わっていたらサーバーが止める。SR-22） */
const RecheckAction = ({ menu, item }: RecheckActionProps) => {
    const acknowledge = useAcknowledge(menu.id);
    const { enqueueSnackbar } = useSnackbar();
    const { judgement, judgementFingerprint } = item;
    if (!judgement || !judgementFingerprint) return null;
    return (
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
            <Button
                variant="contained"
                color="warning"
                size="small"
                disabled={acknowledge.isPending}
                onClick={() =>
                    acknowledge.mutate(
                        { menuItemId: item.id, kind: judgement.kind, fingerprint: judgementFingerprint },
                        {
                            onSuccess: () => enqueueSnackbar(`「${item.productName}」を確認しました`, { variant: 'success' }),
                            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
                        },
                    )
                }
                sx={{ alignSelf: 'flex-start', fontWeight: 700, textAlign: 'left' }}
            >
                ☐ {acknowledgementSentence(judgement)}
            </Button>
            <Button {...newTabProps(`「${item.productName}」をパッケージで確認する`)} to={allergenCheckPath(item.productId)} size="small" sx={{ alignSelf: 'flex-start' }}>
                パッケージで確認する
            </Button>
        </Stack>
    );
};
