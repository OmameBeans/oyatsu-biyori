import type { PropsWithChildren, ReactNode } from 'react';
import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

interface PageLayoutProps {
    title: string;
    actions?: ReactNode;
    header?: ReactNode;
    /**
     * PC（md 以上）で、見出しの行の左右を内側へ寄せる幅（px）。header の中の見た目の端（ステッパーの最初と最後のラベルの端など）に
     * 見出しの行をそろえるときに使う
     */
    titleInset?: { left: number; right: number } | null;
}

/**
 * ページの枠。PC（md 以上）では見出し・操作を固定し、中身だけをスクロールする。スマホではページ全体をスクロールする（05 §4）。
 */
export const PageLayout = ({ title, actions, header, titleInset, children }: PropsWithChildren<PageLayoutProps>) => (
    <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
        <Box sx={{ px: { xs: 2, md: 3 }, pt: 2, pb: 1.5, borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
            <Stack
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1}
                sx={{ alignItems: { sm: 'center' }, justifyContent: 'space-between', pl: { md: `${titleInset?.left ?? 0}px` }, pr: { md: `${titleInset?.right ?? 0}px` } }}
            >
                <Typography variant="h1" component="h1">
                    {title}
                </Typography>
                {actions && (
                    <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                        {actions}
                    </Stack>
                )}
            </Stack>
            {header && <Box sx={{ mt: 1.5 }}>{header}</Box>}
        </Box>
        <Box sx={{ flex: 1, minHeight: 0, overflow: { md: 'auto' }, px: { xs: 2, md: 3 }, py: 2 }}>{children}</Box>
    </Box>
);
