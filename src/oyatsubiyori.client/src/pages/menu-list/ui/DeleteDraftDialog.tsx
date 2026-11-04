import Typography from '@mui/material/Typography';
import { useSnackbar } from 'notistack';
import { useDeleteMenu } from '../../../entities/menu';
import { errorMessageOf } from '../../../shared/api';
import { formatWeekLabel } from '../../../shared/lib';
import { ConfirmDialog } from '../../../shared/ui';

interface DeleteDraftDialogProps {
    isOpen: boolean;
    /** 削除する下書きの献立（開いていないときは null） */
    menuId: string | null;
    weekStart: string | null;
    onClose: () => void;
    onSuccess: () => void;
}

/** 下書きの削除の確認（FR-MENU-31a） */
export const DeleteDraftDialog = ({ isOpen, menuId, weekStart, onClose, onSuccess }: DeleteDraftDialogProps) => {
    const deleteMenu = useDeleteMenu();
    const { enqueueSnackbar } = useSnackbar();
    return (
        <ConfirmDialog
            isOpen={isOpen}
            title={`${weekStart ? formatWeekLabel(weekStart) : ''}の下書きを削除しますか？`}
            confirmLabel="削除する"
            isDanger
            isPending={deleteMenu.isPending}
            onClose={onClose}
            onConfirm={() =>
                menuId &&
                deleteMenu.mutate(menuId, {
                    onSuccess: () => {
                        enqueueSnackbar('下書きを削除しました', { variant: 'success' });
                        onSuccess();
                    },
                    onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
                })
            }
        >
            <Typography>取り込んだ今週の商品・要望・献立案も消えます。商品の情報は残ります。</Typography>
        </ConfirmDialog>
    );
};
