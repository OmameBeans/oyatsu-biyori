import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import LinearProgress from '@mui/material/LinearProgress';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import { useSnackbar } from 'notistack';
import { AllergenCategoryMark } from '../../../entities/allergen-judgement';
import { useSetOrdered } from '../../../entities/menu';
import type { MenuDetail, OrderListLine } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatDateLabel, formatWeekLabel, formatYen } from '../../../shared/lib';

interface OrderListDialogProps {
    menu: MenuDetail;
    isOpen: boolean;
    onClose: () => void;
}

/** 列の表示を細くする（スマホでは金額・使う日を出さない） */
const wideOnly = { display: { xs: 'none', sm: 'table-cell' } } as const;

/**
 * 注文リスト（FR-MENU-35・T9-02）。確定した献立の商品を、注文番号・商品名・必要なパック数でまとめて出す。
 * 行ごとに「注文した」をチェックでき（サーバーに保存。PC とスマホで同じものが見える）、ToDo として使える。
 * 集計と状態（注文した・数量が変わった・入れられない）はサーバー（Biz の OrderListBuilder）が決める。
 * 「コピー」でタブ区切りの文字にして、表計算や発注のメモに貼れるようにする。
 * 入れられない商品（SR-17）は、コピーの文字にも「【使えません】」を付け、「注文した」を付けさせず、残っている間は「すべて注文しました」にしない。
 */
export const OrderListDialog = ({ menu, isOpen, onClose }: OrderListDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const { enqueueSnackbar } = useSnackbar();
    const setOrdered = useSetOrdered(menu.id);
    const { lines, totalPackCount, totalAmount, missingOrderNoCount, orderedCount, quantityChangedCount, blockedCount } = menu.orderList;
    const isAllOrdered = lines.length > 0 && blockedCount === 0 && orderedCount === lines.length;

    const copy = async () => {
        const text = ['注文番号\t商品名\t数量', ...lines.map((l) => `${l.orderNo ?? ''}\t${l.isBlocked ? '【使えません】' : ''}${l.productName}\t${l.packCount}`)].join('\n');
        try {
            await navigator.clipboard.writeText(text);
            enqueueSnackbar('注文リストをコピーしました', { variant: 'success' });
        } catch {
            enqueueSnackbar('コピーできませんでした。表を選んでコピーしてください', { variant: 'error' });
        }
    };

    // 「数量が変わった」の行をチェックすると、今の数量で注文したとして記録し直す
    const handleToggle = (line: OrderListLine) =>
        setOrdered.mutate(
            // 入れられない行は、外す操作だけにする（数量が変わったあとに 🔴 になった行でも、前のチェックを外せるように）
            { productId: line.productId, orderNo: line.orderNo, isOrdered: !line.isBlocked && line.state !== 'Ordered' },
            { onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }) },
        );

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} fullWidth maxWidth="md">
            <DialogTitle>注文リスト（{formatWeekLabel(menu.weekStart)}）</DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2}>
                    {lines.length > 0 && (
                        <Stack spacing={0.5}>
                            <Typography sx={{ fontWeight: 700, color: isAllOrdered ? 'success.main' : 'text.primary' }}>
                                {isAllOrdered ? `すべて注文しました（${lines.length}品）` : `注文済み ${orderedCount} / ${lines.length}品`}
                            </Typography>
                            <LinearProgress
                                variant="determinate"
                                value={(orderedCount / lines.length) * 100}
                                color={isAllOrdered ? 'success' : 'primary'}
                                aria-label="注文済みの割合"
                            />
                        </Stack>
                    )}
                    {blockedCount > 0 && (
                        <Alert severity="error">
                            <AllergenCategoryMark category="Blocked" isInherit /> の商品が {blockedCount} 件あります（表の赤い行）。注文せず、献立で別の商品に替えてください。
                        </Alert>
                    )}
                    {quantityChangedCount > 0 && (
                        <Alert severity="warning">
                            注文したあとで数量が変わった商品が {quantityChangedCount} 件あります。注文を直したら、もう一度チェックしてください。
                        </Alert>
                    )}
                    {missingOrderNoCount > 0 && (
                        <Alert severity="warning">注文番号のない商品が {missingOrderNoCount} 件あります。チラシで番号を確かめてください。</Alert>
                    )}
                    {lines.length === 0 ? (
                        <Typography>注文する商品がありません（通常の日に商品が入っていません）。</Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small" aria-label="注文リスト">
                                <TableHead>
                                    <TableRow>
                                        <TableCell padding="checkbox" sx={{ whiteSpace: 'nowrap', px: 1 }}>
                                            注文済み
                                        </TableCell>
                                        <TableCell>注文番号</TableCell>
                                        <TableCell>商品名</TableCell>
                                        <TableCell align="right">数量</TableCell>
                                        <TableCell align="right" sx={wideOnly}>
                                            金額
                                        </TableCell>
                                        <TableCell sx={wideOnly}>使う日</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {lines.map((line) => {
                                        const isBlocked = line.isBlocked;
                                        const isOrdered = line.state === 'Ordered';
                                        const isChanged = line.state === 'QuantityChanged';
                                        // 注文した行は薄くして、残りが目に入るようにする
                                        const doneSx = isOrdered ? { color: 'text.disabled', textDecoration: 'line-through' } : undefined;
                                        return (
                                            <TableRow
                                                key={`${line.productId}-${line.orderNo ?? ''}`}
                                                hover
                                                sx={isBlocked ? { bgcolor: (t) => t.alpha(t.palette.error.main, 0.08) } : undefined}
                                            >
                                                <TableCell padding="checkbox">
                                                    <Checkbox
                                                        checked={isOrdered}
                                                        indeterminate={isChanged}
                                                        color={isChanged ? 'warning' : 'primary'}
                                                        onChange={() => handleToggle(line)}
                                                        // 入れられない商品には付けさせない（前に付けたチェックは外せる）
                                                        disabled={setOrdered.isPending || (isBlocked && line.state === 'NotOrdered')}
                                                        slotProps={{ input: { 'aria-label': `${line.productName}を注文した` } }}
                                                    />
                                                </TableCell>
                                                <TableCell sx={{ fontWeight: 700, whiteSpace: 'nowrap', ...doneSx }}>{line.orderNo ?? 'なし'}</TableCell>
                                                <TableCell sx={doneSx}>
                                                    {line.productName}
                                                    {isBlocked && (
                                                        <Typography component="span" sx={{ ml: 1, fontWeight: 700 }}>
                                                            <AllergenCategoryMark category="Blocked" />
                                                        </Typography>
                                                    )}
                                                    {isChanged && (
                                                        <Typography variant="body2" sx={{ color: 'warning.dark', fontWeight: 700 }}>
                                                            数量が変わりました（注文したときは {line.orderedPackCount}パック）
                                                        </Typography>
                                                    )}
                                                </TableCell>
                                                <TableCell align="right" sx={{ whiteSpace: 'nowrap', ...doneSx }}>
                                                    {line.packCount}パック
                                                </TableCell>
                                                <TableCell align="right" sx={{ ...wideOnly, whiteSpace: 'nowrap', ...doneSx }}>
                                                    {formatYen(line.amount)}
                                                </TableCell>
                                                <TableCell sx={{ ...wideOnly, ...doneSx }}>{line.dates.map(formatDateLabel).join('・')}</TableCell>
                                            </TableRow>
                                        );
                                    })}
                                    <TableRow>
                                        <TableCell padding="checkbox" />
                                        <TableCell colSpan={2} sx={{ fontWeight: 700 }}>
                                            合計（{lines.length}品）
                                        </TableCell>
                                        <TableCell align="right" sx={{ fontWeight: 700, whiteSpace: 'nowrap' }}>
                                            {totalPackCount}パック
                                        </TableCell>
                                        <TableCell align="right" sx={{ ...wideOnly, fontWeight: 700, whiteSpace: 'nowrap' }}>
                                            {formatYen(totalAmount)}
                                        </TableCell>
                                        <TableCell sx={wideOnly} />
                                    </TableRow>
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                    <Typography variant="body2" color="text.secondary">
                        通常の日の商品だけをまとめています（おやつなしの日の商品は入れていません）。注文番号は、チラシを取り込んで献立に入れた時点のものです。チェックは保存され、PC とスマホで同じものが見えます。
                    </Typography>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={() => void copy()} disabled={lines.length === 0}>
                    コピー
                </Button>
                <Button variant="contained" onClick={onClose}>
                    閉じる
                </Button>
            </DialogActions>
        </Dialog>
    );
};
