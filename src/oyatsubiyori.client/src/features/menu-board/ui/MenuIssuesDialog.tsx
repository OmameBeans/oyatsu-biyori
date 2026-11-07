import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { AllergenCategoryMark, AllergenJudgementLine } from '../../../entities/allergen-judgement';
import type { MenuDetail, MenuIssue } from '../../../entities/menu';
import { formatDateLabel } from '../../../shared/lib';

interface MenuIssuesDialogProps {
    menu: MenuDetail;
    /** 出す問題（🔴 使えません・🟡 要確認・確定できない問題。区分は API の結果のまま） */
    issues: MenuIssue[];
    isOpen: boolean;
    onClose: () => void;
    /** 「パッケージで確認する」（画面を離れずに、アレルゲン確認のダイアログを開く。このダイアログは開いたまま） */
    onCheckAllergen: (productId: string) => void;
}

const isAllergenIssue = (issue: MenuIssue) => issue.kind === 'AllergenBlocked' || issue.kind === 'AllergenWarning';

/** 同じ日の中の並び（🔴 使えません → 確定できない問題 → 🟡 要確認） */
const issueOrder = (issue: MenuIssue) => (issue.kind === 'AllergenBlocked' ? 0 : issue.kind === 'AllergenWarning' ? 2 : 1);

/**
 * 「確かめること」のダイアログ（SR-04）。コマンドバーの件数を押すと開き、日ごとに、どの商品の何を確かめるかを並べる。
 * アレルゲンの文言は共通部品（AllergenJudgementLine）から出す。日ごとのカードと同じ判定（API の結果）を出すだけで、判定はしない。
 */
export const MenuIssuesDialog = ({ menu, issues, isOpen, onClose, onCheckAllergen }: MenuIssuesDialogProps) => {
    const itemOf = (issue: MenuIssue) =>
        issue.menuItemId ? menu.days.flatMap((day) => day.items).find((item) => item.id === issue.menuItemId) : undefined;
    // 日の順に並べる（日に結びつかないものは最後に「週全体」）
    const groups = [...new Set(issues.map((i) => i.date))]
        .sort((a, b) => (a ?? '￿').localeCompare(b ?? '￿'))
        .map((date) => ({ date, issues: issues.filter((i) => i.date === date).sort((a, b) => issueOrder(a) - issueOrder(b)) }));

    return (
        <Dialog open={isOpen} onClose={onClose} maxWidth="sm" fullWidth>
            <DialogTitle>確かめること</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2} divider={<Divider flexItem />}>
                    {groups.map((group) => (
                        <Stack key={group.date ?? 'week'} spacing={1}>
                            <Typography variant="h3" component="h3">
                                {group.date ? formatDateLabel(group.date) : '週全体'}
                            </Typography>
                            {group.issues.map((issue, index) => {
                                const item = itemOf(issue);
                                const productId = item?.productId ?? issue.productId;
                                return (
                                    <Box key={`${issue.kind}-${issue.menuItemId}-${index}`}>
                                        {issue.productName && <Typography sx={{ fontWeight: 600 }}>{issue.productName}</Typography>}
                                        {isAllergenIssue(issue) ? (
                                            <>
                                                {/* 判定の中身が読めないとき（取り直しの途中など）も、区分名とアイコンは API の kind から必ず出す */}
                                                {item?.judgement ? (
                                                    <AllergenJudgementLine judgement={item.judgement} isInMenu />
                                                ) : (
                                                    <Typography variant="body2" sx={{ color: issue.kind === 'AllergenBlocked' ? 'error.main' : 'warning.dark', fontWeight: 600 }}>
                                                        <AllergenCategoryMark category={issue.kind === 'AllergenBlocked' ? 'Blocked' : 'Warning'} isInherit />
                                                        {issue.message ? `：${issue.message}` : ''}
                                                    </Typography>
                                                )}
                                                {productId && (
                                                    <Button size="small" onClick={() => onCheckAllergen(productId)}>
                                                        パッケージで確認する
                                                    </Button>
                                                )}
                                            </>
                                        ) : (
                                            <Typography variant="body2" sx={{ color: issue.severity === 'Error' ? 'error.main' : 'warning.dark' }}>
                                                ⚠ {issue.message}
                                            </Typography>
                                        )}
                                    </Box>
                                );
                            })}
                        </Stack>
                    ))}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>閉じる</Button>
            </DialogActions>
        </Dialog>
    );
};
