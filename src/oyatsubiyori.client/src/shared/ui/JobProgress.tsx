import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';

interface JobProgressProps {
    stage: string | null | undefined;
    fallbackStage: string;
    note?: string;
}

/** Claude の処理の進捗（段階の名前を文言で示す。T0-11） */
export const JobProgress = ({ stage, fallbackStage, note }: JobProgressProps) => (
    <Box role="status" aria-live="polite" sx={{ py: 2 }}>
        <Typography variant="body1" sx={{ mb: 1, fontWeight: 500 }}>
            {stage || fallbackStage}…
        </Typography>
        <LinearProgress />
        {note && (
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                {note}
            </Typography>
        )}
    </Box>
);
