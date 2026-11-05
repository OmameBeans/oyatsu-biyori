import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import CloseIcon from '@mui/icons-material/Close';
import type { PreparedImage } from '../lib/imagePrep';
import { useImagePicker } from './useImagePicker';

interface ImagePickerProps {
    images: PreparedImage[];
    onChange: (images: PreparedImage[]) => void;
    maxImages: number;
    acceptPdf: boolean;
    isDisabled?: boolean;
    /** 画像を準備している途中か（変換など）が変わったとき。準備中は親の「読み取る」を押せなくするために使う */
    onProcessingChange?: (isProcessing: boolean) => void;
}

/**
 * 画像の取り込み（T0-13）：「カメラで撮る」「ファイルを選ぶ」の2つのボタン、PC ではドラッグ＆ドロップ、
 * サムネイルで外す。画像はこの画面のメモリにだけ持つ。処理は useImagePicker にある。
 */
export const ImagePicker = ({ images, onChange, maxImages, acceptPdf, isDisabled, onProcessingChange }: ImagePickerProps) => {
    const picker = useImagePicker({ images, onChange, maxImages, acceptPdf, isDisabled, onProcessingChange });

    return (
        <Box
            {...picker.dropZoneProps}
            sx={{
                border: '2px dashed',
                borderColor: picker.isDragOver ? 'primary.main' : 'divider',
                borderRadius: 3,
                p: 2,
                bgcolor: picker.isDragOver ? 'action.hover' : 'background.paper',
            }}
        >
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                <Button variant="contained" size="large" startIcon={<PhotoCameraIcon />} onClick={picker.openCamera} disabled={picker.isBusy}>
                    カメラで撮る
                </Button>
                <Button variant="outlined" size="large" startIcon={<FolderOpenIcon />} onClick={picker.openFile} disabled={picker.isBusy}>
                    ファイルを選ぶ
                </Button>
            </Stack>
            <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' }, mt: 1 }}>
                ここにファイルをドラッグ＆ドロップしても追加できます（{maxImages}枚まで・1ファイル20MBまで）
            </Typography>
            <input {...picker.cameraInputProps} />
            <input {...picker.fileInputProps} />
            {picker.isProcessing && (
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1.5 }}>
                    <CircularProgress size={18} />
                    <Typography variant="body2">画像を準備しています…</Typography>
                </Stack>
            )}
            {picker.errors.map((message) => (
                <Alert key={message} severity="warning" sx={{ mt: 1.5 }}>
                    {message}
                </Alert>
            ))}
            {images.length > 0 && (
                <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(110px, 1fr))', gap: 1, mt: 2 }}>
                    {images.map((image) => (
                        <Box key={image.id} sx={{ position: 'relative', borderRadius: 2, overflow: 'hidden', border: 1, borderColor: 'divider' }}>
                            <Box component="img" src={image.previewUrl} alt={image.label} sx={{ width: '100%', height: 120, objectFit: 'cover', display: 'block' }} />
                            <Typography variant="caption" sx={{ display: 'block', px: 0.5, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                {image.label}
                            </Typography>
                            <IconButton
                                size="small"
                                aria-label={`${image.label} を外す`}
                                onClick={() => picker.handleRemove(image.id)}
                                disabled={picker.isBusy}
                                sx={{ position: 'absolute', top: 2, right: 2, bgcolor: (theme) => theme.alpha(theme.palette.background.paper, 0.85) }}
                            >
                                <CloseIcon fontSize="small" />
                            </IconButton>
                        </Box>
                    ))}
                </Box>
            )}
        </Box>
    );
};
