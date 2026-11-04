import { useSearchParams } from 'react-router-dom';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { useAuthStatus } from '../../../entities/auth';
import { PageErrorBoundary, PageLayout } from '../../../shared/ui';
import { AppearanceTab } from './AppearanceTab';
import { AvoidedTab } from './AvoidedTab';
import { ConditionsTab } from './ConditionsTab';
import { PasswordTab } from './PasswordTab';

const TABS = ['conditions', 'avoided', 'password', 'appearance'] as const;
type SettingsTab = (typeof TABS)[number];

/**
 * 設定（SC-03 条件設定・SC-04 避ける品目・SC-12 パスワード変更・見た目（#2）のタブ。T2-06）。タブは URL の ?tab= で持つ。
 */
export const SettingsPage = () => {
    const { data: status } = useAuthStatus();
    const [params, setParams] = useSearchParams();
    const requested = params.get('tab') as SettingsTab | null;
    const tab: SettingsTab = status.mustChangePassword ? 'password' : requested && TABS.includes(requested) ? requested : 'conditions';

    return (
        <PageLayout
            title="設定"
            header={
                <Tabs value={tab} onChange={(_, value: SettingsTab) => setParams({ tab: value }, { replace: true })} variant="scrollable" allowScrollButtonsMobile>
                    <Tab value="conditions" label="条件設定" disabled={status.mustChangePassword} />
                    <Tab value="avoided" label="避ける品目" disabled={status.mustChangePassword} />
                    <Tab value="password" label="パスワード変更" />
                    <Tab value="appearance" label="見た目" disabled={status.mustChangePassword} />
                </Tabs>
            }
        >
            <PageErrorBoundary key={tab}>
                {tab === 'conditions' && <ConditionsTab />}
                {tab === 'avoided' && <AvoidedTab />}
                {tab === 'password' && <PasswordTab />}
                {tab === 'appearance' && <AppearanceTab />}
            </PageErrorBoundary>
        </PageLayout>
    );
};
