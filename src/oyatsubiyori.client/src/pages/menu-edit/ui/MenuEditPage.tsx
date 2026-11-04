import { useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import Box from '@mui/material/Box';
import Step from '@mui/material/Step';
import StepButton from '@mui/material/StepButton';
import Stepper from '@mui/material/Stepper';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import { useFlyerImport } from '../../../entities/flyer';
import { useMenu } from '../../../entities/menu';
import { FlyerImportStep } from '../../../features/flyer-import';
import { ListedProductsStep } from '../../../features/listed-products';
import { MenuBoard } from '../../../features/menu-board';
import { ChatPanel } from '../../../features/menu-chat';
import { RequestStep } from '../../../features/menu-request';
import { isJobFinished } from '../../../shared/api';
import { formatWeekLabel } from '../../../shared/lib';
import { PageErrorBoundary, PageLayout } from '../../../shared/ui';

const STEPS = ['チラシ取り込み', '今週の商品', '要望の入力', '献立案'] as const;

/**
 * SC-09 献立作成（T3-03）。4つの手順を1つの画面で進め、終わった手順に戻れる。途中でやめても下書きとして残る。
 * 再開時に開く手順：読み取り中・未確定の読み取り結果があれば手順1、献立案があれば手順4、取り込み済みなら手順2。
 */
export const MenuEditPage = () => {
    const { menuId = '' } = useParams();
    const [params, setParams] = useSearchParams();
    const { data: menu } = useMenu(menuId);
    const { data: flyerImport } = useFlyerImport(menuId);
    const [mobileTab, setMobileTab] = useState<'board' | 'chat'>('board');
    const [isBudgetFitRequested, setIsBudgetFitRequested] = useState(false);

    const hasItems = menu.days.some((day) => day.items.length > 0);
    const isReading = !!flyerImport.job && !isJobFinished(flyerImport.job);
    const hasUnconfirmed = (flyerImport.session?.unconfirmedCount ?? 0) > 0;
    const defaultStep = isReading || hasUnconfirmed || menu.listedCount === 0 ? 1 : hasItems || menu.requestText ? 4 : 2;
    const step = Number(params.get('step')) || defaultStep;
    const goStep = (next: number) => setParams({ step: String(next) }, { replace: false });

    return (
        <PageLayout
            title={`献立作成 ${formatWeekLabel(menu.weekStart)}`}
            header={
                <Stepper nonLinear activeStep={step - 1} alternativeLabel>
                    {STEPS.map((label, index) => (
                        <Step key={label} completed={index + 1 < step}>
                            <StepButton onClick={() => goStep(index + 1)} disabled={index > 0 && menu.listedCount === 0}>
                                {label}
                            </StepButton>
                        </Step>
                    ))}
                </Stepper>
            }
        >
            <PageErrorBoundary key={step}>
                {step === 1 && <FlyerImportStep menuId={menu.id} listedCount={menu.listedCount} onNext={() => goStep(2)} />}
                {step === 2 && <ListedProductsStep menuId={menu.id} onBackToFlyer={() => goStep(1)} onNext={() => goStep(3)} />}
                {step === 3 && <RequestStep menu={menu} onStarted={() => goStep(4)} onManual={() => goStep(4)} />}
                {step === 4 && (
                    <>
                        <Tabs value={mobileTab} onChange={(_, value) => setMobileTab(value)} sx={{ display: { md: 'none' }, mb: 1 }}>
                            <Tab value="board" label="献立" />
                            <Tab value="chat" label="チャット" />
                        </Tabs>
                        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0, 2fr) minmax(280px, 1fr)' }, gap: 2, height: { md: '100%' } }}>
                            <Box sx={{ display: { xs: mobileTab === 'board' ? 'block' : 'none', md: 'block' }, minWidth: 0, overflow: { md: 'auto' } }}>
                                <MenuBoard
                                    menu={menu}
                                    isBudgetFitRequested={isBudgetFitRequested}
                                    onBudgetFitClosed={() => setIsBudgetFitRequested(false)}
                                    onEditRequest={() => goStep(3)}
                                />
                            </Box>
                            <Box sx={{ display: { xs: mobileTab === 'chat' ? 'block' : 'none', md: 'block' }, minHeight: 0 }}>
                                <ChatPanel menu={menu} onBudgetFitRequested={() => setIsBudgetFitRequested(true)} />
                            </Box>
                        </Box>
                    </>
                )}
            </PageErrorBoundary>
        </PageLayout>
    );
};
