import { useEffect, useRef } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import FileUploadOutlinedIcon from '@mui/icons-material/FileUploadOutlined';
import CloseIcon from '@mui/icons-material/Close';
import type { PreparedImage } from '../../../shared/lib';
import { useImagePicker } from '../../../shared/ui';

interface FlyerImagePickerProps {
    images: PreparedImage[];
    onChange: (images: PreparedImage[]) => void;
    maxImages: number;
    isDisabled?: boolean;
    /** 選んだファイルを画像にしている途中か（PDF のページ分けなど）が変わったとき。まだ images に入っていないが、離れると消える */
    onProcessingChange?: (isProcessing: boolean) => void;
}

/** 撮り方・取り込み方の説明（ファイル・カメラ） */
const HOW_TO_NOTES = [
    { label: 'ファイル', text: 'PDFはページごとに取り込みます。いらないページは×で外せます。' },
    { label: 'カメラ', text: '小さい文字が読めるよう、1ページを半分ずつ近づけて撮ってください。' },
] as const;

/**
 * チラシの取り込み欄（SC-09 手順1）。ドラッグ＆ドロップの枠（中央にボタン）・撮り方の説明・追加したチラシの一覧。
 * 処理は ImagePicker と同じ（useImagePicker）。PC では親の残りの高さを埋め、追加したチラシの一覧の中だけをスクロールする。
 */
export const FlyerImagePicker = ({ images, onChange, maxImages, isDisabled, onProcessingChange }: FlyerImagePickerProps) => {
    const picker = useImagePicker({ images, onChange, maxImages, acceptPdf: true, isDisabled });
    const { isProcessing } = picker;
    useEffect(() => {
        onProcessingChange?.(isProcessing);
    }, [isProcessing, onProcessingChange]);
    // 欄が外れたら、準備中の印を戻す（外れたあとに準備が終わった画像は useImagePicker が解放し、親には足さない。#9）。
    // 外れるときだけ知らせるよう、最新のコールバックは ref で持つ
    const onProcessingChangeRef = useRef(onProcessingChange);
    useEffect(() => {
        onProcessingChangeRef.current = onProcessingChange;
    }, [onProcessingChange]);
    useEffect(() => () => onProcessingChangeRef.current?.(false), []);

    return (
        // ドロップは欄全体で受ける（枠の外の「追加したチラシ」欄に落としても、ブラウザがファイルを開いて画面を離れないように）
        <Box {...picker.dropZoneProps} sx={{ display: 'flex', flexDirection: 'column', gap: 2, flex: { md: '1 1 auto' }, minHeight: 0 }}>
            <Box
                sx={{
                    flexShrink: 0,
                    border: '2px dashed',
                    borderColor: (theme) => (picker.isDragOver ? theme.palette.primary.main : theme.alpha(theme.palette.primary.main, 0.4)),
                    borderRadius: 3,
                    bgcolor: (theme) => theme.alpha(theme.palette.primary.main, picker.isDragOver ? 0.1 : 0.04),
                    px: 3,
                    py: 2,
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    textAlign: 'center',
                    gap: 1,
                }}
            >
                <FileUploadOutlinedIcon color="primary" sx={{ fontSize: 36 }} aria-hidden />
                {/* スマホではドラッグ＆ドロップできないので、案内は PC だけ */}
                <Typography sx={{ fontWeight: 700, display: { xs: 'none', md: 'block' } }}>ここにチラシをドラッグ＆ドロップ</Typography>
                <Typography variant="body2" color="text.secondary">
                    画像・PDF / {maxImages}枚まで / 1ファイル20MBまで
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ pt: 0.5, alignSelf: { xs: 'stretch', sm: 'center' } }}>
                    <Button variant="outlined" size="large" startIcon={<FolderOpenIcon />} onClick={picker.openFile} disabled={picker.isBusy} sx={{ bgcolor: 'background.paper' }}>
                        ファイルを選ぶ
                    </Button>
                    <Button
                        variant="outlined"
                        color="inherit"
                        size="large"
                        startIcon={<PhotoCameraIcon />}
                        onClick={picker.openCamera}
                        disabled={picker.isBusy}
                        sx={{ bgcolor: 'background.paper', borderColor: 'divider' }}
                    >
                        カメラで撮る
                    </Button>
                </Stack>
                <input {...picker.fileInputProps} />
                <input {...picker.cameraInputProps} />
            </Box>

            <Box sx={{ flexShrink: 0, display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 1.25 }}>
                {HOW_TO_NOTES.map((note) => (
                    <Typography key={note.label} variant="body2" color="text.secondary" sx={{ bgcolor: 'action.hover', borderRadius: 2, px: 1.75, py: 1.25, lineHeight: 1.6 }}>
                        <Box component="strong" sx={{ color: 'text.primary' }}>
                            {note.label}：
                        </Box>
                        {note.text}
                    </Typography>
                ))}
            </Box>

            {picker.isProcessing && (
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexShrink: 0 }}>
                    <CircularProgress size={18} />
                    <Typography variant="body2">画像を準備しています…</Typography>
                </Stack>
            )}
            {picker.errors.map((message) => (
                <Alert key={message} severity="warning" sx={{ flexShrink: 0 }}>
                    {message}
                </Alert>
            ))}

            {/* PC では残りの高さを埋める。サムネイル1段（ファイル名まで）は残し、それより狭いときはカードの中をスクロールする */}
            <Box component="section" aria-labelledby="flyer-added-heading" sx={{ flex: { md: '1 1 auto' }, minHeight: { md: 168 }, display: 'flex', flexDirection: 'column', gap: 1 }}>
                <Typography id="flyer-added-heading" variant="subtitle2" component="h3" sx={{ fontWeight: 700, flexShrink: 0 }}>
                    追加したチラシ
                    <Box component="span" sx={{ fontWeight: 500, color: 'text.secondary', ml: 0.5 }}>
                        （{images.length} / {maxImages}枚）
                    </Box>
                </Typography>
                {images.length === 0 ? (
                    <Box
                        sx={{
                            flex: { md: '1 1 auto' },
                            minHeight: 56,
                            border: 1,
                            borderStyle: 'dashed',
                            borderColor: 'divider',
                            borderRadius: 2,
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                        }}
                    >
                        <Typography variant="body2" color="text.secondary">
                            まだ追加されていません
                        </Typography>
                    </Box>
                ) : (
                    <Box
                        component="ul"
                        aria-label="追加したチラシの一覧"
                        sx={{
                            flex: { md: '1 1 auto' },
                            minHeight: 0,
                            overflowY: { md: 'auto' },
                            listStyle: 'none',
                            m: 0,
                            p: 0,
                            display: 'grid',
                            gridTemplateColumns: 'repeat(auto-fill, minmax(104px, 1fr))',
                            alignContent: 'start',
                            gap: 1.5,
                        }}
                    >
                        {images.map((image) => (
                            <Box component="li" key={image.id} sx={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                                <Box sx={{ position: 'relative', borderRadius: 2, overflow: 'hidden', border: 1, borderColor: 'divider', bgcolor: 'action.hover' }}>
                                    <Box component="img" src={image.previewUrl} alt={image.label} sx={{ width: '100%', height: 112, objectFit: 'cover', display: 'block' }} />
                                    <IconButton
                                        size="small"
                                        aria-label={`${image.label} を外す`}
                                        onClick={() => picker.handleRemove(image.id)}
                                        disabled={picker.isBusy}
                                        sx={{
                                            position: 'absolute',
                                            top: 4,
                                            right: 4,
                                            border: 1,
                                            borderColor: 'divider',
                                            bgcolor: (theme) => theme.alpha(theme.palette.background.paper, 0.9),
                                            '&:hover': { bgcolor: 'background.paper' },
                                        }}
                                    >
                                        <CloseIcon fontSize="small" />
                                    </IconButton>
                                </Box>
                                <Typography variant="caption" title={image.label} sx={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                                    {image.label}
                                </Typography>
                            </Box>
                        ))}
                    </Box>
                )}
            </Box>
        </Box>
    );
};
