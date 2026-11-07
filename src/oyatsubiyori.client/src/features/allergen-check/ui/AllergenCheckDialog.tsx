import { Suspense } from 'react';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import useMediaQuery from '@mui/material/useMediaQuery';
import type { Theme } from '@mui/material/styles';
import CloseIcon from '@mui/icons-material/Close';
import { AllergenJudgementLine, NoAvoidedAllergenNotice } from '../../../entities/allergen-judgement';
import { useProduct } from '../../../entities/product';
import { PageErrorBoundary } from '../../../shared/ui';
import { AllergenCheckForm } from './AllergenCheckForm';

interface AllergenCheckDialogProps {
    /** 確かめる商品（null のあいだは閉じている） */
    productId: string | null;
    onClose: () => void;
}

/**
 * アレルゲン確認（SC-08）をダイアログで開く。献立案の画面から、画面を離れずにパッケージで確かめて記録するために使う。
 * 中身は SC-08 の画面と同じ（いまの表示・避ける品目がないときの注意・確認の記録の手順）。判定はサーバーの結果を出すだけ。
 * 記録すると、献立を含むすべての取得し直しが走る（useRecordCheck）ので、閉じたときには献立の判定も新しくなっている。
 * 撮った画像や入力は、閉じると消える（D-41）。誤って消さないように、外側を押しても閉じない（閉じるボタンか Esc で閉じる）。
 */
export const AllergenCheckDialog = ({ productId, onClose }: AllergenCheckDialogProps) => {
    const isMobile = useMediaQuery((theme: Theme) => theme.breakpoints.down('sm'));
    return (
        <Dialog
            open={productId !== null}
            onClose={(_, reason) => reason !== 'backdropClick' && onClose()}
            fullScreen={isMobile}
            fullWidth
            maxWidth="lg"
            aria-labelledby="allergen-check-dialog-title"
        >
            {/* 読み込みに失敗したときも閉じられるように、閉じるボタンは中身の外に置く */}
            <IconButton aria-label="閉じる" onClick={onClose} sx={{ position: 'absolute', right: 8, top: 8, zIndex: 1 }}>
                <CloseIcon />
            </IconButton>
            {productId !== null && (
                <PageErrorBoundary>
                    <Suspense
                        fallback={
                            <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
                                <CircularProgress />
                            </Box>
                        }
                    >
                        <AllergenCheckDialogBody productId={productId} onClose={onClose} />
                    </Suspense>
                </PageErrorBoundary>
            )}
        </Dialog>
    );
};

const AllergenCheckDialogBody = ({ productId, onClose }: { productId: string; onClose: () => void }) => {
    const { data: product } = useProduct(productId);
    return (
        <>
            <DialogTitle id="allergen-check-dialog-title" sx={{ pr: 7 }}>
                アレルゲン確認：{product.name}
            </DialogTitle>
            <DialogContent dividers>
                <Stack spacing={2}>
                    <AllergenJudgementLine judgement={product.judgement} isInMenu={product.usages.length > 0} />
                    {product.isAvoidedEmpty && <NoAvoidedAllergenNotice />}
                    <AllergenCheckForm product={product} onDone={onClose} />
                </Stack>
            </DialogContent>
        </>
    );
};
