import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';
import type { SvgIconComponent } from '@mui/icons-material';
import ChatIcon from '@mui/icons-material/Chat';
import ChecklistIcon from '@mui/icons-material/Checklist';
import CodeIcon from '@mui/icons-material/Code';
import EditNoteIcon from '@mui/icons-material/EditNote';
import EventRepeatIcon from '@mui/icons-material/EventRepeat';
import FactCheckIcon from '@mui/icons-material/FactCheck';
import ForumIcon from '@mui/icons-material/Forum';
import HealthAndSafetyIcon from '@mui/icons-material/HealthAndSafety';
import HowToRegIcon from '@mui/icons-material/HowToReg';
import LightbulbIcon from '@mui/icons-material/Lightbulb';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import SavingsIcon from '@mui/icons-material/Savings';
import ScheduleIcon from '@mui/icons-material/Schedule';
import SearchIcon from '@mui/icons-material/Search';
import ShieldIcon from '@mui/icons-material/Shield';
import TrafficIcon from '@mui/icons-material/Traffic';
import { AllergenCategoryMark } from '../../../entities/allergen-judgement';
import { ROUTE } from '../../../shared/type';
import { AppIcon } from '../../../shared/ui';

/** ページの中の節の id（ナビのリンク先） */
const SECTION_ID = {
    HOW: 'how',
    FEATURES: 'features',
    SAFETY: 'safety',
    WHY: 'why',
} as const;

const NAV_LINKS = [
    { id: SECTION_ID.HOW, label: '使い方' },
    { id: SECTION_ID.FEATURES, label: 'できること' },
    { id: SECTION_ID.SAFETY, label: '安全への考え方' },
    { id: SECTION_ID.WHY, label: '作った理由' },
];

const PAINS: { icon: SvgIconComponent; text: string }[] = [
    { icon: ScheduleIcon, text: '毎週、チラシとにらめっこして1〜2時間かかる' },
    { icon: ChecklistIcon, text: '予算・人数・アレルギー・同じおやつが続かないか…条件が多い' },
    { icon: HealthAndSafetyIcon, text: 'アレルゲンの見落としが怖く、気が抜けない' },
];

const STEPS: { icon: SvgIconComponent; title: string; text: ReactNode }[] = [
    { icon: PhotoCameraIcon, title: '撮る', text: 'コープのチラシを撮影するか、画像・PDF を選びます。商品・価格・アレルゲンの表記を読み取ります。' },
    {
        icon: FactCheckIcon,
        title: '確かめる',
        text: (
            <>
                今週の商品を一覧で見渡します。アレルゲンの表示（<AllergenCategoryMark category="Blocked" /> ／ <AllergenCategoryMark category="Warning" /> ／{' '}
                <AllergenCategoryMark category="Safe" />）と理由が並びます。
            </>
        ),
    },
    { icon: EditNoteIcon, title: '伝える', text: '「金曜は誕生日会なので豪華に」など、週の要望を文章で伝えると、理由つきの献立案が届きます。' },
    { icon: ForumIcon, title: '直す', text: 'チャットで直したり、手で入れ替えたりして、確定します。予算に合わせる入れ替え案も出します。' },
];

const FEATURES: { icon: SvgIconComponent; title: string; detail?: string }[] = [
    { icon: LightbulbIcon, title: '理由つきの献立案', detail: '予算・アレルギー・要望をどう満たしたか' },
    { icon: ChatIcon, title: '文章で伝える週の要望', detail: '人数の変更・誕生日会など' },
    { icon: SavingsIcon, title: '週の予算に合わせる入れ替え案', detail: 'アプリが全通り計算' },
    { icon: EventRepeatIcon, title: 'しょっぱいもの・あまいものの重複の警告' },
];

const SAFETY: { icon: SvgIconComponent; title: string; text: ReactNode }[] = [
    {
        icon: ShieldIcon,
        title: 'アプリが必ずチェックすること',
        text: '施設の「避ける品目」を含む商品や、避ける品目があるのにアレルゲンの情報がない商品は、献立に入れられません。AI の答えではなく、アプリのルールで判定します。',
    },
    { icon: HowToRegIcon, title: '職員が確定すること', text: 'アレルゲンの最終確認は、必ず職員がパッケージの原材料表示を見て行います。' },
    {
        icon: TrafficIcon,
        title: '3つの表示',
        text: (
            <>
                <AllergenCategoryMark category="Blocked" /> ／ <AllergenCategoryMark category="Warning" /> ／ <AllergenCategoryMark category="Safe" />
                。理由と、次にすることを必ず添えます。
            </>
        ),
    },
    { icon: SearchIcon, title: '29品目に入らない食材', text: 'ヘーゼルナッツなど、アレルゲン表示の対象外の食材も、原材料の全文から探します。' },
];

/** 見出しの文字の色。ライトでは primary の濃い色、ダークでは primary の明るい色（どちらも背景と 4.5:1 以上。app/theme.ts） */
const HEADING_COLOR = 'primary.dark';

/** ナビの高さ（px）。節へ飛んだときに見出しがナビに隠れないよう、scrollMarginTop にも使う */
const NAV_HEIGHT = 64;

/** 節の見出し（中央寄せの大きな見出し） */
const SectionHeading = ({ children }: { children: ReactNode }) => (
    <Typography variant="h2" component="h2" sx={{ textAlign: 'center', fontSize: { xs: '1.45rem', md: '1.9rem' }, fontWeight: 800, color: HEADING_COLOR, mb: { xs: 3, md: 5 } }}>
        {children}
    </Typography>
);

/** 節（上下の余白と、ナビの下に隠れないための余白） */
const Section = ({ id, tinted = false, children }: { id?: string; tinted?: boolean; children: ReactNode }) => (
    <Box
        component="section"
        id={id}
        sx={{
            py: { xs: 7, md: 10 },
            scrollMarginTop: NAV_HEIGHT,
            bgcolor: tinted ? (theme) => theme.alpha(theme.palette.primary.main, 0.06) : undefined,
        }}
    >
        <Container maxWidth="lg">{children}</Container>
    </Box>
);

/** アイコンを入れる角丸の四角（悩みのカード）や丸（できること）。塗りの色は primary */
const IconBadge = ({ icon: Icon, round = false, filled = false }: { icon: SvgIconComponent; round?: boolean; filled?: boolean }) => (
    <Box
        aria-hidden
        sx={{
            flex: 'none',
            width: 52,
            height: 52,
            borderRadius: round ? '50%' : 4,
            display: 'grid',
            placeItems: 'center',
            bgcolor: filled ? 'primary.main' : 'background.paper',
            color: filled ? 'primary.contrastText' : 'primary.main',
            border: filled ? 'none' : 1,
            borderColor: 'divider',
        }}
    >
        <Icon sx={{ fontSize: 28 }} />
    </Box>
);

/** ヒーローの右に置く、献立案の画面の例（ダミーデータ。飾りなので文言は短く） */
const HeroShot = () => {
    const days = [
        { date: '11/9（月）', price: '2,438 円', items: ['うすしおポテトチップス', 'ぶどうゼリー'], why: '要望の「しょっぱいもの＋ゼリー系」に合わせました。' },
        { date: '11/13（金）誕生日会', price: '4,482 円', items: ['いちごミニ大福', 'オレンジジュース'], why: '誕生日会なので、季節のいちごの大福にしました。' },
    ];
    const bubbleSx = { borderRadius: 2, px: 1, py: 0.75, mt: 0.75, fontSize: '0.7rem', lineHeight: 1.6 } as const;
    return (
        <Paper
            role="img"
            aria-label="献立案の画面の例（ダミーデータ）"
            elevation={0}
            sx={{
                borderRadius: 4,
                overflow: 'hidden',
                boxShadow: (theme) => `0 24px 48px -12px ${theme.alpha(theme.palette.primary.main, 0.3)}`,
            }}
        >
            <Box sx={{ height: 28, bgcolor: 'primary.main', display: 'flex', alignItems: 'center', gap: 0.75, px: 1.5 }}>
                {[0, 1, 2].map((i) => (
                    <Box key={i} sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: (theme) => theme.alpha(theme.palette.primary.contrastText, 0.5) }} />
                ))}
            </Box>
            <Box sx={{ p: 1.75, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1.4fr 1fr' }, gap: 1.25, bgcolor: 'background.default' }}>
                <Box sx={{ display: 'grid', gap: 1 }}>
                    {days.map((day) => (
                        <Paper key={day.date} variant="outlined" sx={{ p: 1.25, borderRadius: 2 }}>
                            <Box sx={{ display: 'flex', justifyContent: 'space-between', fontWeight: 700, fontSize: '0.75rem' }}>
                                <span>{day.date}</span>
                                <span>{day.price}</span>
                            </Box>
                            {day.items.map((item) => (
                                <Box key={item} sx={{ fontSize: '0.72rem', mt: 0.5 }}>
                                    ・{item}
                                </Box>
                            ))}
                            <Box sx={{ display: 'flex', gap: 0.5, mt: 0.75, fontSize: '0.65rem', color: 'text.secondary', lineHeight: 1.6 }}>
                                <LightbulbIcon sx={{ fontSize: 13, color: 'primary.main', mt: '2px' }} />
                                {day.why}
                            </Box>
                        </Paper>
                    ))}
                </Box>
                <Paper variant="outlined" sx={{ p: 1.25, borderRadius: 2, display: { xs: 'none', sm: 'block' } }}>
                    <Box sx={{ fontWeight: 700, fontSize: '0.75rem' }}>献立の相談</Box>
                    <Box sx={{ ...bubbleSx, bgcolor: 'background.default' }}>献立案を作りました。週の予算まであと 1,104 円です。</Box>
                    <Box sx={{ ...bubbleSx, bgcolor: 'primary.main', color: 'primary.contrastText', ml: 2.5 }}>水曜を別のしょっぱいものにして</Box>
                    <Box sx={{ ...bubbleSx, bgcolor: 'background.default' }}>確認済みの商品から候補を出しました。</Box>
                </Paper>
            </Box>
        </Paper>
    );
};

/**
 * SC-13 ランディング（ログイン不要・実データを出さない）。ボタンは「ログイン」で、API を呼ばない（U-31・D-69）。
 * 見た目は mocks/landing.html に合わせる。色はテーマの色から作る（色のテーマ・明るさに合わせて変わる。#2・#4）。
 */
export const LandingPage = () => (
    <Box sx={{ bgcolor: 'background.paper', minHeight: '100vh', color: 'text.primary' }}>
        {/* ナビ（上に固定） */}
        <Box
            component="header"
            sx={{
                position: 'sticky',
                top: 0,
                zIndex: 30,
                bgcolor: (theme) => theme.alpha(theme.palette.background.paper, 0.92),
                backdropFilter: 'blur(8px)',
                borderBottom: 1,
                borderColor: 'divider',
            }}
        >
            <Container maxWidth="lg" sx={{ display: 'flex', alignItems: 'center', gap: 2, height: NAV_HEIGHT }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, fontSize: '1.25rem', fontWeight: 800, color: HEADING_COLOR, mr: 'auto' }}>
                    <AppIcon size={36} />
                    おやつ日和
                </Box>
                <Box component="nav" aria-label="ページ内のリンク" sx={{ display: { xs: 'none', md: 'flex' }, gap: 0.5 }}>
                    {NAV_LINKS.map((link) => (
                        <Link key={link.id} href={`#${link.id}`} underline="none" color="text.secondary" sx={{ fontSize: '0.875rem', px: 1.5, py: 1, borderRadius: 1.5 }}>
                            {link.label}
                        </Link>
                    ))}
                </Box>
                <Button component={RouterLink} to={ROUTE.LOGIN} variant="contained" sx={{ borderRadius: 999, px: 3 }}>
                    ログイン
                </Button>
            </Container>
        </Box>

        <main>
            {/* ファーストビュー */}
            <Box
                sx={{
                    py: { xs: 5, md: 9 },
                    overflow: 'hidden',
                    background: (theme) =>
                        `radial-gradient(1200px 500px at 85% -10%, ${theme.palette.primary.light} 0%, ${theme.alpha(theme.palette.primary.light, 0)} 60%), linear-gradient(180deg, ${theme.alpha(theme.palette.primary.light, 0.7)} 0%, ${theme.palette.background.paper} 100%)`,
                }}
            >
                <Container maxWidth="lg" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1.05fr' }, gap: { xs: 4, md: 6 }, alignItems: 'center' }}>
                    <Box>
                        <Typography variant="h1" component="h1" sx={{ fontSize: { xs: '1.9rem', md: '2.6rem' }, fontWeight: 800, lineHeight: 1.4, color: HEADING_COLOR }}>
                            チラシを撮るだけで、
                            <br />
                            <Box component="span" sx={{ color: 'primary.main' }}>
                                1週間分のおやつ献立案
                            </Box>
                            。
                        </Typography>
                        <Typography sx={{ fontSize: { xs: '1rem', md: '1.1rem' }, color: 'text.secondary', lineHeight: 1.9, mt: 2.5, mb: 3.5 }}>
                            学童保育の職員のための、おやつ献立アシスタントです。
                        </Typography>
                        <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
                            <Button href={`#${SECTION_ID.HOW}`} variant="outlined" size="large" startIcon={<PlayCircleIcon />} sx={{ borderRadius: 999, px: 3 }}>
                                使い方を見る
                            </Button>
                        </Box>
                        <Typography variant="caption" component="p" color="text.secondary" sx={{ mt: 1.75 }}>
                            ※ 画面の画像はダミーデータです。
                        </Typography>
                    </Box>
                    <HeroShot />
                </Container>
            </Box>

            {/* 悩み */}
            <Section>
                <SectionHeading>こんな悩みはありませんか</SectionHeading>
                <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, 1fr)' }, gap: 2.5 }}>
                    {PAINS.map((pain) => (
                        <Box
                            component="li"
                            key={pain.text}
                            sx={{ p: { xs: 2.5, md: 3.5 }, borderRadius: 5, bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.05), border: 1, borderColor: 'divider' }}
                        >
                            <IconBadge icon={pain.icon} />
                            <Typography sx={{ mt: 2, fontWeight: 700, fontSize: '1.05rem', lineHeight: 1.7 }}>{pain.text}</Typography>
                        </Box>
                    ))}
                </Box>
            </Section>

            {/* 使い方 */}
            <Section id={SECTION_ID.HOW} tinted>
                <SectionHeading>使い方</SectionHeading>
                <Box component="ol" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: 'repeat(4, 1fr)' }, gap: { xs: 3.5, md: 2.5 } }}>
                    {STEPS.map((step, index) => (
                        <Paper component="li" key={step.title} variant="outlined" sx={{ position: 'relative', borderRadius: 5, p: 2.5, overflow: 'visible' }}>
                            <Box
                                aria-hidden
                                sx={{
                                    position: 'absolute',
                                    top: -14,
                                    left: 20,
                                    width: 32,
                                    height: 32,
                                    borderRadius: '50%',
                                    bgcolor: 'primary.main',
                                    color: 'primary.contrastText',
                                    display: 'grid',
                                    placeItems: 'center',
                                    fontWeight: 800,
                                }}
                            >
                                {index + 1}
                            </Box>
                            <Box
                                aria-hidden
                                sx={{
                                    height: 120,
                                    borderRadius: 3,
                                    my: 1.5,
                                    display: 'grid',
                                    placeItems: 'center',
                                    color: 'primary.main',
                                    background: (theme) => `linear-gradient(135deg, ${theme.palette.primary.light}, ${theme.alpha(theme.palette.primary.light, 0.35)})`,
                                }}
                            >
                                <step.icon sx={{ fontSize: 48 }} />
                            </Box>
                            <Typography variant="h3" component="h3" sx={{ fontSize: '1.25rem', fontWeight: 800, color: HEADING_COLOR }}>
                                {step.title}
                            </Typography>
                            <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.75, lineHeight: 1.8 }}>
                                {step.text}
                            </Typography>
                        </Paper>
                    ))}
                </Box>
            </Section>

            {/* できること */}
            <Section id={SECTION_ID.FEATURES}>
                <SectionHeading>できること</SectionHeading>
                <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5 }}>
                    {FEATURES.map((feature) => (
                        <Paper component="li" key={feature.title} variant="outlined" sx={{ display: 'flex', gap: 2, alignItems: 'center', p: 3, borderRadius: 5 }}>
                            <IconBadge icon={feature.icon} round filled />
                            <Box>
                                <Typography sx={{ fontWeight: 700, fontSize: '1.05rem' }}>{feature.title}</Typography>
                                {feature.detail && (
                                    <Typography variant="body2" sx={{ color: 'text.secondary', mt: 0.5 }}>
                                        （{feature.detail}）
                                    </Typography>
                                )}
                            </Box>
                        </Paper>
                    ))}
                </Box>
            </Section>

            {/* 安全への考え方 */}
            <Section id={SECTION_ID.SAFETY} tinted>
                <SectionHeading>安全への考え方</SectionHeading>
                <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2.5 }}>
                    {SAFETY.map((item) => (
                        <Paper key={item.title} variant="outlined" sx={{ p: 3, borderRadius: 4 }}>
                            <Typography variant="h3" component="h3" sx={{ display: 'flex', alignItems: 'center', gap: 1, fontSize: '1.05rem', fontWeight: 700, mb: 1.5 }}>
                                <item.icon aria-hidden sx={{ color: 'primary.main' }} />
                                {item.title}
                            </Typography>
                            <Typography sx={{ fontSize: '0.9rem', lineHeight: 1.9 }}>{item.text}</Typography>
                        </Paper>
                    ))}
                </Box>
            </Section>

            {/* AI の費用と、試用の結果 */}
            <Section>
                <SectionHeading>AI の費用と、試用の結果</SectionHeading>
                <Box sx={{ maxWidth: 760, mx: 'auto', borderRadius: 5, p: { xs: 3, md: 4.5 }, bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.05), lineHeight: 2.1 }}>
                    1週間分の献立づくりにかかる AI の費用と、現場での試用の結果は、測ってからここに載せます。AI の呼び出しには1日の上限を設け、使いすぎないようにしています。
                </Box>
            </Section>

            {/* 作った理由 */}
            <Section id={SECTION_ID.WHY}>
                <SectionHeading>作った理由</SectionHeading>
                <Box sx={{ maxWidth: 760, mx: 'auto', borderRadius: 5, p: { xs: 3, md: 4.5 }, bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.05), lineHeight: 2.1 }}>
                    学童保育の現場で、おやつの献立づくりに毎週かかる時間と、アレルゲンの見落としへの不安を減らすために作りました。
                </Box>
            </Section>
        </main>

        <Box component="footer" sx={{ bgcolor: (theme) => theme.alpha(theme.palette.primary.main, 0.1), borderTop: 1, borderColor: 'divider', py: 5 }}>
            <Container maxWidth="lg" sx={{ display: 'flex', gap: { xs: 1.5, md: 3 }, alignItems: { xs: 'flex-start', md: 'center' }, flexDirection: { xs: 'column', md: 'row' }, fontSize: '0.85rem', color: 'text.secondary' }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, fontWeight: 700, fontSize: '1rem', color: HEADING_COLOR }}>
                    <AppIcon size={24} />
                    おやつ日和
                </Box>
                <span>Built with Claude: Japan 2026 提出作品</span>
                <Link href="https://github.com/OmameBeans/oyatsu-biyori" target="_blank" rel="noopener noreferrer" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.5 }}>
                    <CodeIcon sx={{ fontSize: 16 }} aria-hidden />
                    GitHub
                </Link>
                <Box sx={{ ml: { md: 'auto' } }}>献立の提案に Claude（Anthropic）を使っています</Box>
            </Container>
        </Box>
    </Box>
);
