import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import { useMenuList } from '../../../entities/menu';
import type { MenuListRow } from '../../../entities/menu';
import { menuPath, ROUTE } from '../../../shared/type';
import { PageLayout } from '../../../shared/ui';
import { DeleteDraftDialog } from './DeleteDraftDialog';
import { PastMenuGrid } from './PastMenuGrid';
import { WeekIssuesDialog } from './WeekIssuesDialog';
import { WeekCard } from './WeekParts';

/**
 * SC-14 過去の献立。献立一覧（SC-10）の「過去の献立を見る」から開く。
 * 今週より前の週を新しい順に出す。PC は DataGrid（画面の高さいっぱい。表の中だけをスクロール）、スマホはカード。
 */
export const MenuHistoryPage = () => {
    const { data: list } = useMenuList();
    const navigate = useNavigate();
    const theme = useTheme();
    const isWide = useMediaQuery(theme.breakpoints.up('md'));
    const [deleting, setDeleting] = useState<MenuListRow | null>(null);
    const [issuesMenuId, setIssuesMenuId] = useState<string | null>(null);

    const pastRows = list.rows.filter((row) => row.weekStart < list.thisWeekStart);
    // 過去の週は献立のある週だけ（未作成の行は今週・来週・再来週だけに出る）
    const openRow = (row: MenuListRow) => row.menuId && navigate(menuPath(row.menuId));

    return (
        <PageLayout
            title="過去の献立"
            actions={
                <Button component={RouterLink} to={ROUTE.MENUS} startIcon={<ChevronLeftIcon />} sx={{ minHeight: 44 }}>
                    献立一覧へ戻る
                </Button>
            }
        >
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5, height: { md: '100%' } }}>
                <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                    今週より前の週の献立です（新しい順）。
                </Typography>
                {pastRows.length === 0 ? (
                    <Typography sx={{ color: 'text.secondary' }}>過去の献立はまだありません。</Typography>
                ) : isWide ? (
                    <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 320 }}>
                        <PastMenuGrid rows={pastRows} label="過去の献立" onOpen={openRow} onDelete={setDeleting} onShowIssues={(row) => setIssuesMenuId(row.menuId)} />
                    </Box>
                ) : (
                    <Stack spacing={1.5}>
                        {pastRows.map((row) => (
                            <WeekCard key={row.weekStart} row={row} isThisWeek={false} isPending={false} onOpen={() => openRow(row)} onDelete={() => setDeleting(row)} onShowIssues={() => setIssuesMenuId(row.menuId)} />
                        ))}
                    </Stack>
                )}
            </Box>
            <WeekIssuesDialog isOpen={issuesMenuId !== null} menuId={issuesMenuId} onClose={() => setIssuesMenuId(null)} />
            <DeleteDraftDialog
                isOpen={deleting !== null}
                menuId={deleting?.menuId ?? null}
                weekStart={deleting?.weekStart ?? null}
                onClose={() => setDeleting(null)}
                onSuccess={() => setDeleting(null)}
            />
        </PageLayout>
    );
};
