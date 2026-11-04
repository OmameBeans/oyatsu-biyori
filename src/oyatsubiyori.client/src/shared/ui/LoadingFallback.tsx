import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';

/** 読み込み中（Suspense の fallback） */
export const LoadingFallback = () => (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', py: 6, flex: 1 }}>
        <CircularProgress aria-label="読み込み中" />
    </Box>
);
