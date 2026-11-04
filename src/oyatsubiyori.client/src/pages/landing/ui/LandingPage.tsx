import { Link as RouterLink } from 'react-router-dom';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { ROUTE } from '../../../shared/type';

const STEPS = [
    { title: '撮る', text: 'コープのチラシを撮影するか、画像・PDF を選びます。商品・価格・アレルゲンの表記を読み取ります。' },
    { title: '確かめる', text: '今週の商品を一覧で見渡します。アレルゲンの表示（🔴 / 🟡 / 🟢）と理由が並びます。' },
    { title: '伝える', text: '「金曜は誕生日会なので豪華に」など、週の要望を文章で伝えると、理由つきの献立案が届きます。' },
    { title: '直す', text: 'チャットで直したり、手で入れ替えたりして、確定します。予算に合わせる入れ替え案も出します。' },
];

const FEATURES = [
    '理由つきの献立案（予算・アレルギー・要望をどう満たしたか）',
    '文章で伝える週の要望（人数の変更・誕生日会など）',
    '週の予算に合わせる入れ替え案（アプリが全通り計算）',
    'しょっぱいもの・あまいものの重複の警告',
];

/**
 * SC-13 ランディング（ログイン不要・実データを出さない）。ボタンは常に「ログイン」で、API を呼ばない（U-31・D-69）。
 * 背景の色はテーマの色から作る（色のテーマ・明るさに合わせて変わる。#4）。
 */
export const LandingPage = () => (
    <Box sx={{ bgcolor: 'background.default', minHeight: '100vh' }}>
        <Box sx={{ background: (theme) => `linear-gradient(160deg, ${theme.palette.primary.light} 0%, ${theme.palette.background.default} 70%)`, py: { xs: 6, md: 10 } }}>
            <Container maxWidth="md">
                <Typography variant="h1" component="h1" sx={{ fontSize: { xs: '2rem', md: '2.6rem' }, mb: 2 }}>
                    🍪 おやつ日和
                </Typography>
                <Typography sx={{ fontSize: { xs: '1.1rem', md: '1.3rem' }, mb: 4 }}>
                    チラシを撮るだけで、1週間分のおやつ献立案。
                    <br />
                    学童保育の職員のための、おやつ献立アシスタントです。
                </Typography>
                <Button component={RouterLink} to={ROUTE.LOGIN} variant="contained" size="large" sx={{ px: 5 }}>
                    ログイン
                </Button>
            </Container>
        </Box>
        <Container maxWidth="md" sx={{ py: 6 }}>
            <Stack spacing={6}>
                <Box component="section">
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        こんな悩みはありませんか
                    </Typography>
                    <Stack component="ul" spacing={1} sx={{ pl: 3, m: 0 }}>
                        <li>毎週、チラシとにらめっこして1〜2時間かかる</li>
                        <li>予算・人数・アレルギー・同じおやつが続かないか…条件が多い</li>
                        <li>アレルゲンの見落としが怖く、気が抜けない</li>
                    </Stack>
                </Box>
                <Box component="section">
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        使い方
                    </Typography>
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2 }}>
                        {STEPS.map((step, index) => (
                            <Card key={step.title}>
                                <CardContent>
                                    <Typography variant="h3" component="h3" sx={{ mb: 1 }}>
                                        {index + 1}. {step.title}
                                    </Typography>
                                    <Typography variant="body2">{step.text}</Typography>
                                </CardContent>
                            </Card>
                        ))}
                    </Box>
                </Box>
                <Box component="section">
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        できること
                    </Typography>
                    <Stack component="ul" spacing={1} sx={{ pl: 3, m: 0 }}>
                        {FEATURES.map((feature) => (
                            <li key={feature}>{feature}</li>
                        ))}
                    </Stack>
                </Box>
                <Box component="section" sx={{ bgcolor: (theme) => theme.alpha(theme.palette.secondary.main, 0.12), borderRadius: 4, p: { xs: 2.5, md: 4 } }}>
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        安全への考え方
                    </Typography>
                    <Stack spacing={1.5}>
                        <Typography>
                            <strong>アプリが必ずチェックすること：</strong>施設の「避ける品目」を含む商品や、避ける品目があるのにアレルゲンの情報がない商品は、献立に入れられません。AI の答えではなく、アプリのルールで判定します。
                        </Typography>
                        <Typography>
                            <strong>職員が確定すること：</strong>アレルゲンの最終確認は、必ず職員がパッケージの原材料表示を見て行います。
                        </Typography>
                        <Typography>
                            <strong>3つの表示：</strong>🔴 使えません ／ 🟡 要確認 ／ 🟢 使えます。理由と、次にすることを必ず添えます。
                        </Typography>
                        <Typography>
                            <strong>29品目に入らない食材：</strong>ヘーゼルナッツなど、アレルゲン表示の対象外の食材も、原材料の全文から探します。
                        </Typography>
                    </Stack>
                </Box>
                <Box component="section">
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        AI の費用と、試用の結果
                    </Typography>
                    <Typography>
                        1週間分の献立づくりにかかる AI の費用と、現場での試用の結果は、測ってからここに載せます。AI の呼び出しには1日の上限を設け、使いすぎないようにしています。
                    </Typography>
                </Box>
                <Box component="section">
                    <Typography variant="h2" component="h2" sx={{ mb: 2 }}>
                        作った理由
                    </Typography>
                    <Typography>学童保育の現場で、おやつの献立づくりに毎週かかる時間と、アレルゲンの見落としへの不安を減らすために作りました。</Typography>
                </Box>
            </Stack>
        </Container>
        <Box component="footer" sx={{ borderTop: 1, borderColor: 'divider', py: 3, textAlign: 'center' }}>
            <Typography variant="body2" color="text.secondary">
                Built with Claude: Japan 2026 提出作品 ・{' '}
                <Link href="https://github.com/OmameBeans/oyatsu-biyori" target="_blank" rel="noopener noreferrer">
                    GitHub
                </Link>{' '}
                ・ 献立の提案に Claude（Anthropic）を使っています
            </Typography>
        </Box>
    </Box>
);
