import { useEffect, useRef, useState } from 'react';
import type { RefObject } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Step from '@mui/material/Step';
import StepButton from '@mui/material/StepButton';
import { stepLabelClasses } from '@mui/material/StepLabel';
import Stepper from '@mui/material/Stepper';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import { useSnackbar } from 'notistack';
import { useFlyerImport } from '../../../entities/flyer';
import { useMenu } from '../../../entities/menu';
import { FlyerImportStep } from '../../../features/flyer-import';
import { ListedProductsStep } from '../../../features/listed-products';
import { MenuBoard } from '../../../features/menu-board';
import { ChatPanel } from '../../../features/menu-chat';
import { RequestStep } from '../../../features/menu-request';
import { isJobFinished } from '../../../shared/api';
import { formatWeekLabel } from '../../../shared/lib';
import { MENU_STATUS, ROUTE } from '../../../shared/type';
import { ConfirmDialog, PageErrorBoundary, PageLayout } from '../../../shared/ui';

const STEPS = ['チラシ取り込み', '今週の商品', '要望の入力', '献立案'] as const;

/** 文字が実際に描かれている範囲（ラベルの要素は幅いっぱいに広がるので、要素ではなく文字の範囲を測る） */
const textRectOf = (element: Element) => {
    const range = document.createRange();
    range.selectNodeContents(element);
    return range.getBoundingClientRect();
};

/**
 * ステッパーの最初と最後のラベルの、見た目の外側の端が、ステッパーの左右の端からどれだけ内側にあるか（px）。
 * 見出しの行をステッパーの見た目の幅にそろえるために使う。幅が変わったとき・手順が変わったとき（今の手順のラベルは太字になる）・
 * 文字のフォントを読み込み終えたときに測り直す。
 */
const useStepperLabelInset = (stepperRef: RefObject<HTMLDivElement | null>, step: number) => {
    const [inset, setInset] = useState<{ left: number; right: number } | null>(null);
    useEffect(() => {
        const root = stepperRef.current;
        if (!root) {
            return;
        }
        let isActive = true;
        const measure = () => {
            const labels = root.querySelectorAll(`.${stepLabelClasses.label}`);
            if (!isActive || labels.length === 0) {
                return;
            }
            const box = root.getBoundingClientRect();
            const left = Math.max(0, Math.round(textRectOf(labels[0]).left - box.left));
            const right = Math.max(0, Math.round(box.right - textRectOf(labels[labels.length - 1]).right));
            setInset((prev) => (prev && prev.left === left && prev.right === right ? prev : { left, right }));
        };
        // 見始めたときにも1回呼ばれる
        const observer = new ResizeObserver(measure);
        observer.observe(root);
        void document.fonts?.ready.then(measure);
        return () => {
            isActive = false;
            observer.disconnect();
        };
    }, [stepperRef, step]);
    return inset;
};

/**
 * SC-09 献立作成（T3-03）。4つの手順を1つの画面で進め、終わった手順に戻れる。途中でやめても下書きとして残る。
 * 再開時に開く手順：読み取り中・未確定の読み取り結果があれば手順1、献立案があれば手順4、取り込み済みなら手順2。
 * 「作成を中断して一覧へ」：献立は下書きとして残る（その旨を知らせる）。離れると消える入力（手順1の読み取っていない画像・
 * 手順3の保存していない予算の変更・手順4の送っていないチャットの依頼）があるときだけ、消えることを確かめる。
 */
export const MenuEditPage = () => {
    const { menuId = '' } = useParams();
    const [params, setParams] = useSearchParams();
    const { data: menu } = useMenu(menuId);
    const { data: flyerImport } = useFlyerImport(menuId);
    const [mobileTab, setMobileTab] = useState<'board' | 'chat'>('board');
    const [isBudgetFitRequested, setIsBudgetFitRequested] = useState(false);
    // 今の手順で、離れると消える入力（何が消えるか）。手順は1つずつしか出さないので、1つだけ持つ
    const [unsavedInput, setUnsavedInput] = useState<string | null>(null);
    const [isLeaveConfirmOpen, setIsLeaveConfirmOpen] = useState(false);
    const navigate = useNavigate();
    const { enqueueSnackbar } = useSnackbar();
    const stepperRef = useRef<HTMLDivElement>(null);

    const hasItems = menu.days.some((day) => day.items.length > 0);
    const isReading = !!flyerImport.job && !isJobFinished(flyerImport.job);
    const hasUnconfirmed = (flyerImport.session?.unconfirmedCount ?? 0) > 0;
    const defaultStep = isReading || hasUnconfirmed || menu.listedCount === 0 ? 1 : hasItems || menu.requestText ? 4 : 2;
    const step = Number(params.get('step')) || defaultStep;
    const goStep = (next: number) => setParams({ step: String(next) }, { replace: false });
    const titleInset = useStepperLabelInset(stepperRef, step);

    const isConfirmed = menu.status === MENU_STATUS.CONFIRMED;
    const leaveToList = () => {
        navigate(ROUTE.MENUS);
        enqueueSnackbar(isConfirmed ? '献立のここまでの内容は保存されています' : '献立は下書きとして残っています。献立一覧からいつでも続きを作れます', {
            variant: 'success',
        });
    };
    const handleLeave = () => (unsavedInput ? setIsLeaveConfirmOpen(true) : leaveToList());

    return (
        <PageLayout
            title={`献立作成 ${formatWeekLabel(menu.weekStart)}`}
            titleInset={titleInset}
            actions={
                <Button variant="outlined" color="inherit" startIcon={<CloseIcon />} onClick={handleLeave} sx={{ borderColor: 'divider' }}>
                    作成を中断して一覧へ
                </Button>
            }
            header={
                <Stepper ref={stepperRef} nonLinear activeStep={step - 1} alternativeLabel>
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
                {step === 1 && (
                    <FlyerImportStep
                        menuId={menu.id}
                        listedCount={menu.listedCount}
                        onNext={() => goStep(2)}
                        onUnsavedInputChange={setUnsavedInput}
                        pickerInset={titleInset}
                    />
                )}
                {step === 2 && <ListedProductsStep menuId={menu.id} onBackToFlyer={() => goStep(1)} onNext={() => goStep(3)} />}
                {step === 3 && <RequestStep menu={menu} onStarted={() => goStep(4)} onManual={() => goStep(4)} onUnsavedInputChange={setUnsavedInput} />}
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
                                <ChatPanel menu={menu} onBudgetFitRequested={() => setIsBudgetFitRequested(true)} onUnsavedInputChange={setUnsavedInput} />
                            </Box>
                        </Box>
                    </>
                )}
            </PageErrorBoundary>
            <ConfirmDialog
                isOpen={isLeaveConfirmOpen}
                title="作成を中断して一覧へ戻りますか？"
                confirmLabel="消して一覧へ"
                onClose={() => setIsLeaveConfirmOpen(false)}
                onConfirm={() => {
                    setIsLeaveConfirmOpen(false);
                    leaveToList();
                }}
            >
                <Typography>
                    {unsavedInput}は消えます。
                    {isConfirmed ? '献立のここまでの内容は保存されています。' : '献立はここまでの内容で下書きとして残り、献立一覧からいつでも続きを作れます。'}
                </Typography>
            </ConfirmDialog>
        </PageLayout>
    );
};
