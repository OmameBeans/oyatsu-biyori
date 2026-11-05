import Box from '@mui/material/Box';
import ReactMarkdown from 'react-markdown';
import type { Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';

interface ClaudeMarkdownProps {
    text: string | null | undefined;
}

/** 描画してよい要素（見出し・段落・太字・斜体・取り消し線・箇条書き・引用・表・コード・区切り線・改行）。これ以外は描画しない */
const ALLOWED_ELEMENTS = [
    'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'p', 'strong', 'em', 'del', 'ul', 'ol', 'li', 'blockquote',
    'table', 'thead', 'tbody', 'tr', 'th', 'td', 'code', 'pre', 'hr', 'br',
];

/** 見出しは本文より少し大きい程度にする（チャットの欄が狭いため） */
const heading = { fontSize: '1rem', fontWeight: 700, mt: 1.5, mb: 0.5 } as const;

const components: Components = {
    h1: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
    h2: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
    h3: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
    h4: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
    h5: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
    h6: ({ children }) => <Box component="p" sx={heading}>{children}</Box>,
};

/**
 * Claude のチャットの返答を、安全な範囲の Markdown として表示する（NFR-SEC-11・D-91）。
 * 生の HTML は描画せず（skipHtml）、リンク・画像は要素ごと描画しない（リンクの文字だけ残す。unwrapDisallowed）。
 * チャットの返答以外（理由・説明・照合の理由・検証の指摘）は、これまでどおり ClaudeText でテキストとして出す。
 */
export const ClaudeMarkdown = ({ text }: ClaudeMarkdownProps) => {
    if (!text) {
        return null;
    }
    return (
        <Box
            sx={{
                typography: 'body2',
                overflowWrap: 'anywhere',
                '& > :first-of-type': { mt: 0 },
                '& > :last-child': { mb: 0 },
                '& p': { my: 0.75 },
                '& ul, & ol': { my: 0.75, pl: 2.5 },
                '& li': { my: 0.25 },
                '& blockquote': { my: 0.75, mx: 0, pl: 1.5, borderLeft: 3, borderColor: 'divider', color: 'text.secondary' },
                '& code': { fontFamily: 'monospace', fontSize: '0.85em', bgcolor: 'action.hover', px: 0.5, borderRadius: 0.5 },
                '& pre': { my: 0.75, p: 1, bgcolor: 'action.hover', borderRadius: 1, overflowX: 'auto' },
                '& pre code': { bgcolor: 'transparent', p: 0 },
                '& table': { my: 0.75, borderCollapse: 'collapse', display: 'block', overflowX: 'auto', maxWidth: '100%' },
                '& th, & td': { border: 1, borderColor: 'divider', px: 1, py: 0.5, textAlign: 'left' },
                '& th': { bgcolor: 'action.hover', fontWeight: 700 },
                '& hr': { my: 1, border: 0, borderTop: 1, borderColor: 'divider' },
            }}
        >
            <ReactMarkdown
                remarkPlugins={[remarkGfm]}
                skipHtml
                allowedElements={ALLOWED_ELEMENTS}
                unwrapDisallowed
                components={components}
            >
                {text}
            </ReactMarkdown>
        </Box>
    );
};
