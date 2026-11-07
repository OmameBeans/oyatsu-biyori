import { useState } from 'react';
import Alert from '@mui/material/Alert';
import Badge from '@mui/material/Badge';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import FactCheckOutlinedIcon from '@mui/icons-material/FactCheckOutlined';
import type { MenuDetail } from '../../../entities/menu';
import { formatDateLabel } from '../../../shared/lib';
import { DAY_TYPE_LABEL } from '../../../shared/type';

interface InterpretedRequestsButtonProps {
    menu: MenuDetail;
}

/**
 * 週の要望と「要望をこう理解しました」（FR-MENU-04）を開くボタン。AI チャットの見出しに置き、押すとダイアログで出す（見るだけ。
 * 要望を直すのはコマンドバーの「要望を直す」）。反映しなかった要望があるときは、ボタンに件数のバッジを付けて気づけるようにする。
 */
export const InterpretedRequestsButton = ({ menu }: InterpretedRequestsButtonProps) => {
    const [isOpen, setIsOpen] = useState(false);
    const interpreted = menu.interpretedRequests;
    const requestText = menu.requestText?.trim() ?? '';
    const rejectedCount = interpreted?.rejected.length ?? 0;
    const hasInterpreted = !!interpreted && (interpreted.days.length > 0 || interpreted.policies.length > 0);
    if (!requestText && !hasInterpreted && rejectedCount === 0) {
        return null;
    }
    const label = rejectedCount > 0 ? `週の要望とAIの理解（反映しなかった要望 ${rejectedCount}件）` : '週の要望とAIの理解';

    return (
        <>
            <Tooltip title={label}>
                <IconButton aria-label={label} onClick={() => setIsOpen(true)} size="small">
                    <Badge badgeContent={rejectedCount} color="warning">
                        <FactCheckOutlinedIcon fontSize="small" />
                    </Badge>
                </IconButton>
            </Tooltip>
            <Dialog open={isOpen} onClose={() => setIsOpen(false)} maxWidth="sm" fullWidth>
                <DialogTitle>週の要望とAIの理解</DialogTitle>
                <DialogContent dividers>
                    <Stack spacing={2}>
                        <Stack spacing={0.5}>
                            <Typography variant="h3" component="h3">
                                入力した要望
                            </Typography>
                            {requestText ? (
                                <Typography sx={{ whiteSpace: 'pre-wrap' }}>{requestText}</Typography>
                            ) : (
                                <Typography color="text.secondary">要望は入力していません</Typography>
                            )}
                        </Stack>
                        {hasInterpreted && (
                            <Stack spacing={0.5}>
                                <Typography variant="h3" component="h3">
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
                            </Stack>
                        )}
                        {interpreted && rejectedCount > 0 && (
                            <Alert severity="warning">
                                <Typography sx={{ fontWeight: 700 }}>反映しなかった要望</Typography>
                                {interpreted.rejected.map((r) => (
                                    <Typography key={r.text} variant="body2">
                                        ・{r.text}：{r.reason}
                                    </Typography>
                                ))}
                            </Alert>
                        )}
                        <Typography variant="caption" color="text.secondary">
                            人数・種別・見出しは、日ごとのカードの「詳細設定」で直せます。
                        </Typography>
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setIsOpen(false)}>閉じる</Button>
                </DialogActions>
            </Dialog>
        </>
    );
};
