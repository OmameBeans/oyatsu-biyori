import Typography from '@mui/material/Typography';
import type { TypographyProps } from '@mui/material/Typography';

interface ClaudeTextProps {
    text: string | null | undefined;
    variant?: TypographyProps['variant'];
    color?: TypographyProps['color'];
}

/**
 * Claude の文章（理由・説明・チャットの返答・照合の理由・検証の指摘）を、テキストとしてだけ表示する（NFR-SEC-11、T0-12）。
 * Markdown・HTML として描画しない（React の文字列はエスケープされる）。改行はそのまま見せる。
 */
export const ClaudeText = ({ text, variant = 'body2', color }: ClaudeTextProps) => {
    if (!text) {
        return null;
    }
    return (
        <Typography component="div" variant={variant} color={color} sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            {text}
        </Typography>
    );
};
