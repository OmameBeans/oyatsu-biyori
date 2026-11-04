import { useRef, useState } from 'react';
import type { DragEvent } from 'react';
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
import { checkFile, isPdf, preparePdfPages, prepareImage, releaseImages, ImagePrepError } from '../lib/imagePrep';
import type { PreparedImage } from '../lib/imagePrep';

interface ImagePickerProps {
    images: PreparedImage[];
    onChange: (images: PreparedImage[]) => void;
    maxImages: number;
    acceptPdf: boolean;
    isDisabled?: boolean;
}

/**
 * 画像の取り込み（T0-13）：「カメラで撮る」「ファイルを選ぶ」の2つのボタン、PC ではドラッグ＆ドロップ、
 * サムネイルで外す。画像はこの画面のメモリにだけ持つ。
 */
export const ImagePicker = ({ images, onChange, maxImages, acceptPdf, isDisabled }: ImagePickerProps) => {
    const cameraRef = useRef<HTMLInputElement>(null);
    const fileRef = useRef<HTMLInputElement>(null);
    const [errors, setErrors] = useState<string[]>([]);
    const [isProcessing, setIsProcessing] = useState(false);
    const [isDragOver, setIsDragOver] = useState(false);

    const addFiles = async (files: File[]) => {
        if (files.length === 0) {
            return;
        }
        setIsProcessing(true);
        const messages: string[] = [];
        const added: PreparedImage[] = [];
        for (const file of files) {
            const problem = checkFile(file, acceptPdf);
            if (problem) {
                messages.push(problem);
                continue;
            }
            try {
                if (isPdf(file)) {
                    added.push(...(await preparePdfPages(file)));
                } else {
                    added.push(await prepareImage(file));
                }
            } catch (error) {
                messages.push(error instanceof ImagePrepError ? error.message : `「${file.name}」を読めませんでした。別のファイルで試してください。`);
            }
        }
        const room = Math.max(maxImages - images.length, 0);
        if (added.length > room) {
            messages.push(`1回に取り込めるのは${maxImages}枚までです。いらない画像を外してから、足してください。`);
            releaseImages(added.slice(room));
        }
        onChange([...images, ...added.slice(0, room)]);
        setErrors(messages);
        setIsProcessing(false);
    };

    const handleRemove = (id: string) => {
        releaseImages(images.filter((image) => image.id === id));
        onChange(images.filter((image) => image.id !== id));
    };

    const handleDrop = (event: DragEvent<HTMLDivElement>) => {
        event.preventDefault();
        setIsDragOver(false);
        if (!isDisabled && !isProcessing) {
            void addFiles(Array.from(event.dataTransfer.files));
        }
    };

    return (
        <Box
            onDragOver={(event) => {
                event.preventDefault();
                setIsDragOver(true);
            }}
            onDragLeave={() => setIsDragOver(false)}
            onDrop={handleDrop}
            sx={{
                border: '2px dashed',
                borderColor: isDragOver ? 'primary.main' : 'divider',
                borderRadius: 3,
                p: 2,
                bgcolor: isDragOver ? 'action.hover' : 'background.paper',
            }}
        >
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                <Button variant="contained" size="large" startIcon={<PhotoCameraIcon />} onClick={() => cameraRef.current?.click()} disabled={isDisabled || isProcessing}>
                    カメラで撮る
                </Button>
                <Button variant="outlined" size="large" startIcon={<FolderOpenIcon />} onClick={() => fileRef.current?.click()} disabled={isDisabled || isProcessing}>
                    ファイルを選ぶ
                </Button>
            </Stack>
            <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', md: 'block' }, mt: 1 }}>
                ここにファイルをドラッグ＆ドロップしても追加できます（{maxImages}枚まで・1ファイル20MBまで）
            </Typography>
            <input
                ref={cameraRef}
                type="file"
                accept="image/*"
                capture="environment"
                hidden
                data-testid="camera-input"
                onChange={(event) => {
                    void addFiles(Array.from(event.target.files ?? []));
                    event.target.value = '';
                }}
            />
            <input
                ref={fileRef}
                type="file"
                accept={acceptPdf ? 'image/*,application/pdf,.heic,.heif' : 'image/*,.heic,.heif'}
                multiple
                hidden
                data-testid="file-input"
                onChange={(event) => {
                    void addFiles(Array.from(event.target.files ?? []));
                    event.target.value = '';
                }}
            />
            {isProcessing && (
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 1.5 }}>
                    <CircularProgress size={18} />
                    <Typography variant="body2">画像を準備しています…</Typography>
                </Stack>
            )}
            {errors.map((message) => (
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
                                onClick={() => handleRemove(image.id)}
                                disabled={isDisabled || isProcessing}
                                sx={{ position: 'absolute', top: 2, right: 2, bgcolor: 'rgba(255,255,255,0.85)' }}
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
