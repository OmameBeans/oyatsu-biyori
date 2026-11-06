import { Suspense, useState } from 'react';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import type { MenuDetail } from '../../../entities/menu';
import { LoadingFallback, PageErrorBoundary } from '../../../shared/ui';
import { AvoidedForm } from './AvoidedForm';
import { ConditionsForm } from './ConditionsForm';
import { WeekBudgetForm } from './WeekBudgetForm';

type ConditionsTab = 'budget' | 'conditions' | 'avoided';

interface ConditionsDialogProps {
    menu: MenuDetail;
    isOpen: boolean;
    onClose: () => void;
}

/**
 * 献立作成の手順3から開く「いつもの条件」の変更（SC-03・SC-04 と同じ中身）。
 * タブは「この週の予算」（この献立だけ）・「いつもの条件」（人数・既定の予算・重複など）・「避ける品目」。
 * 保存した内容は献立を読み直して、手順3の表示にすぐ反映する（各フックの invalidate）。
 */
export const ConditionsDialog = ({ menu, isOpen, onClose }: ConditionsDialogProps) => {
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
    const [tab, setTab] = useState<ConditionsTab>('budget');

    return (
        <Dialog open={isOpen} onClose={onClose} fullScreen={isMobile} fullWidth maxWidth="md">
            <DialogTitle>いつもの条件を変える</DialogTitle>
            <Tabs value={tab} onChange={(_, value: ConditionsTab) => setTab(value)} variant="scrollable" allowScrollButtonsMobile sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
                <Tab value="budget" label="この週の予算" />
                <Tab value="conditions" label="人数・予算・重複" />
                <Tab value="avoided" label="避ける品目" />
            </Tabs>
            <DialogContent sx={{ pt: 3, minHeight: { sm: 480 } }}>
                <PageErrorBoundary key={tab}>
                    <Suspense fallback={<LoadingFallback />}>
                        {tab === 'budget' && <WeekBudgetForm menu={menu} />}
                        {tab === 'conditions' && <ConditionsForm />}
                        {tab === 'avoided' && <AvoidedForm />}
                    </Suspense>
                </PageErrorBoundary>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>閉じる</Button>
            </DialogActions>
        </Dialog>
    );
};
