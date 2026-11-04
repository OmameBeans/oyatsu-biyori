import type { PropsWithChildren } from 'react';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';

interface ConfirmDialogProps {
    isOpen: boolean;
    title: string;
    confirmLabel: string;
    isDanger?: boolean;
    isPending?: boolean;
    onClose: () => void;
    onConfirm: () => void;
}

/** 確認のダイアログ（削除・外すなど） */
export const ConfirmDialog = ({ isOpen, title, confirmLabel, isDanger, isPending, onClose, onConfirm, children }: PropsWithChildren<ConfirmDialogProps>) => (
    <Dialog open={isOpen} onClose={onClose} maxWidth="xs" fullWidth>
        <DialogTitle>{title}</DialogTitle>
        <DialogContent>{children}</DialogContent>
        <DialogActions>
            <Button onClick={onClose}>やめる</Button>
            <Button variant="contained" color={isDanger ? 'error' : 'primary'} onClick={onConfirm} disabled={isPending}>
                {confirmLabel}
            </Button>
        </DialogActions>
    </Dialog>
);
