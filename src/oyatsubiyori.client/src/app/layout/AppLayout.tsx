import { Fragment, useState } from 'react';
import type { ReactElement } from 'react';
import { Navigate, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import type { Theme } from '@mui/material/styles';
import MenuIcon from '@mui/icons-material/Menu';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import SettingsIcon from '@mui/icons-material/Settings';
import LogoutIcon from '@mui/icons-material/Logout';
import { useSnackbar } from 'notistack';
import { useAuthStatus, useLogout } from '../../entities/auth';
import { errorMessageOf } from '../../shared/api';
import { readPreference, writePreference } from '../../shared/lib';
import { PREFERENCE_KEY, ROUTE } from '../../shared/type';
import { PageErrorBoundary } from '../../shared/ui';

const DRAWER_WIDTH = 220;
/**
 * PC で閉じたときの細い帯の幅。中身はいちばん広い行で 60px（画面の項目：左の線 4px・内側 16px・アイコン 24px・内側 16px。
 * ログアウト：余白 8px・内側 12px・アイコン 24px・内側 8px・余白 8px）。アイコンの左端は開いたときと同じ 20px。
 * これに paper の右の枠線 1px と余裕を足して 64px にする（中身に使えるのは 63px）
 */
const MINI_DRAWER_WIDTH = 64;
/** MUI の Drawer（permanent）の paper の右の枠線の幅。paper は border-box なので、中身の幅はこの分だけ狭い */
const DRAWER_PAPER_BORDER_WIDTH = 1;
/** 開閉の状態として保存する値（"true"：閉じている / "false"：開いている） */
const isCollapsedValue = (value: unknown): value is 'true' | 'false' => value === 'true' || value === 'false';

/** 保存した開閉の状態を読む。保存がない・読めない（ブラウザの設定で使えないなど）ときは、開いた状態で始める */
const readIsCollapsed = (): boolean => readPreference(PREFERENCE_KEY.SIDEBAR_COLLAPSED, isCollapsedValue, 'false') === 'true';

/** 開閉の状態を保存する。保存できなくても、開閉はこの画面の中では効く（再読み込みすると開いた状態に戻る） */
const writeIsCollapsed = (isCollapsed: boolean) => writePreference(PREFERENCE_KEY.SIDEBAR_COLLAPSED, String(isCollapsed));

/**
 * 帯のときだけ、項目に名前のツールチップを付ける。押せない項目（マウスの動きを受け取らない）でも出るように span で包む。
 * 名前は中のボタンの aria-label だけで付け、ツールチップは見た目だけにする（describeChild で span に名前を付けない）。
 */
const withMiniTooltip = (isMini: boolean, title: string, item: ReactElement) =>
    isMini ? (
        <Tooltip title={title} placement="right" describeChild>
            <Box component="span" sx={{ display: 'block' }}>
                {item}
            </Box>
        </Tooltip>
    ) : (
        item
    );

/** PC のサイドバーの幅を変えるときの動き */
const widthTransition = (theme: Theme) =>
    theme.transitions.create('width', { easing: theme.transitions.easing.sharp, duration: theme.transitions.duration.enteringScreen });

const NAV_ITEMS = [
    { to: '/menus', label: '献立一覧', icon: <CalendarMonthIcon /> },
    { to: '/products', label: '商品一覧', icon: <Inventory2Icon /> },
    { to: '/settings', label: '設定', icon: <SettingsIcon /> },
] as const;

/**
 * サイドバーの項目の見た目。選ばれている項目は、幅いっぱいの薄い背景・左端の縦の線・テーマの色の文字で示す。
 * 選ばれていない項目にも同じ幅の透明な線を付け、文字の位置がずれないようにする。
 */
const NAV_ITEM_SX = {
    borderLeft: 4,
    borderLeftColor: 'transparent',
    '&.active': {
        bgcolor: 'primary.light',
        // 線は文字と同じ濃い色にし、薄い背景の上でも見分けられるようにする
        borderLeftColor: 'primary.dark',
        color: 'primary.dark',
        '& .MuiListItemText-primary': { fontWeight: 700 },
    },
    // 選ばれている項目は背景がフォーカスの色に置き換わるので、キーボードで選んだときは枠線で示す
    '&.Mui-focusVisible': { outline: (theme: Theme) => `2px solid ${theme.palette.primary.dark}`, outlineOffset: -2 },
} as const;

/**
 * SC-02 ホームの枠（T0-14）：左サイドバー（献立一覧・商品一覧・設定・ログアウト）。スマホは ≡ で開く。
 * PC は上部のボタンで開閉でき、閉じるとアイコンだけの細い帯になる（開閉の状態はブラウザに保存する）。
 */
export const AppLayout = () => {
    const { data: status } = useAuthStatus();
    const location = useLocation();
    const navigate = useNavigate();
    const logout = useLogout();
    const { enqueueSnackbar } = useSnackbar();
    // スマホの ≡ で開く一時的なサイドバー
    const [isDrawerOpen, setIsDrawerOpen] = useState(false);
    // PC のサイドバーを細い帯に閉じているか（スマホのサイドバーには効かない）
    const [isCollapsed, setIsCollapsed] = useState(readIsCollapsed);
    const isDesktop = useMediaQuery((theme: Theme) => theme.breakpoints.up('md'));
    // PC の幅になったら、スマホのサイドバーを閉じる。開いたままだと、見えないサイドバーが本文を隠し、フォーカスを閉じ込める
    const [wasDesktop, setWasDesktop] = useState(isDesktop);
    if (isDesktop !== wasDesktop) {
        setWasDesktop(isDesktop);
        if (isDesktop) {
            setIsDrawerOpen(false);
        }
    }

    if (!status.isAuthenticated) {
        return <Navigate to={ROUTE.LOGIN} replace />;
    }
    const isPasswordTab = location.pathname === '/settings' && new URLSearchParams(location.search).get('tab') === 'password';
    if (status.mustChangePassword && !isPasswordTab) {
        return <Navigate to={ROUTE.PASSWORD_TAB} replace />;
    }

    const handleLogout = () => {
        logout.mutate(undefined, {
            onSuccess: () => navigate(ROUTE.LOGIN, { replace: true }),
            onError: (error) => enqueueSnackbar(errorMessageOf(error), { variant: 'error' }),
        });
    };

    const handleToggleCollapsed = () => {
        const next = !isCollapsed;
        setIsCollapsed(next);
        writeIsCollapsed(next);
    };

    // 帯のログアウトは文字を出さないので、ログイン中のアカウントは名前とツールチップで示す
    const miniLogoutLabel = status.userName ? `ログアウト（${status.userName}）` : 'ログアウト';

    /**
     * サイドバーの中身。isMini のときはアイコンだけの細い帯にし、名前は aria-label とツールチップで示す。
     * 選ばれている項目は、細い帯でも同じ見た目（幅いっぱいの薄い背景・左端の線・濃い色のアイコン）で示す。
     * hasToggle（開閉のボタン）は PC のサイドバーだけ（スマホは ≡ で開き、外を押すと閉じる）。
     */
    const renderDrawer = (isMini: boolean, hasToggle: boolean) => (
        <Box
            sx={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                // PC で開くときは、中身を開いたあとの幅で先に並べる。幅が動いている間は paper の外を隠すので、文字が折り返さない。
                // 開いたあとは、長い文字も今までどおり折り返す
                width: hasToggle && !isMini ? DRAWER_WIDTH - DRAWER_PAPER_BORDER_WIDTH : undefined,
            }}
        >
            <Toolbar disableGutters={isMini} sx={{ gap: 1, justifyContent: isMini ? 'center' : undefined }}>
                {!isMini && (
                    <>
                        <Typography component="span" sx={{ fontSize: 22 }} aria-hidden>
                            🍪
                        </Typography>
                        <Typography variant="h3" component="span">
                            おやつ日和
                        </Typography>
                    </>
                )}
                {hasToggle && (
                    <IconButton
                        edge={isMini ? false : 'end'}
                        aria-label={isMini ? 'メニューを開く' : 'メニューを閉じる'}
                        aria-expanded={!isMini}
                        onClick={handleToggleCollapsed}
                        sx={{ ml: isMini ? 0 : 'auto' }}
                    >
                        {isMini ? <MenuIcon /> : <ChevronLeftIcon />}
                    </IconButton>
                )}
            </Toolbar>
            <Divider />
            <List component="nav" aria-label="メニュー">
                {NAV_ITEMS.map((item) => (
                    <Fragment key={item.to}>
                        {withMiniTooltip(
                            isMini,
                            item.label,
                            <ListItemButton
                                component={NavLink}
                                to={item.to}
                                onClick={() => setIsDrawerOpen(false)}
                                disabled={status.mustChangePassword && item.to !== '/settings'}
                                aria-label={isMini ? item.label : undefined}
                                sx={NAV_ITEM_SX}
                            >
                                <ListItemIcon sx={{ minWidth: isMini ? 0 : 36, color: 'inherit' }}>{item.icon}</ListItemIcon>
                                {!isMini && <ListItemText primary={item.label} />}
                            </ListItemButton>,
                        )}
                    </Fragment>
                ))}
            </List>
            <Box sx={{ flex: 1 }} />
            <Divider />
            <List>
                {/* 左の余白 8px と内側 12px で、上の項目（線 4px と内側 16px）と文字・アイコンの位置をそろえる */}
                {withMiniTooltip(
                    isMini,
                    miniLogoutLabel,
                    <ListItemButton
                        onClick={handleLogout}
                        disabled={logout.isPending}
                        aria-label={isMini ? miniLogoutLabel : undefined}
                        sx={{ mx: 1, pl: 1.5, pr: isMini ? 1 : undefined, borderRadius: 2 }}
                    >
                        <ListItemIcon sx={{ minWidth: isMini ? 0 : 36 }}>
                            <LogoutIcon />
                        </ListItemIcon>
                        {!isMini && <ListItemText primary="ログアウト" secondary={status.userName ?? undefined} />}
                    </ListItemButton>,
                )}
            </List>
        </Box>
    );
    const pcDrawerWidth = isCollapsed ? MINI_DRAWER_WIDTH : DRAWER_WIDTH;

    return (
        <Box sx={{ display: 'flex', minHeight: '100vh', height: { md: '100vh' } }}>
            <AppBar position="fixed" color="inherit" elevation={0} sx={{ display: { md: 'none' }, borderBottom: 1, borderColor: 'divider' }}>
                <Toolbar>
                    <IconButton edge="start" aria-label="メニューを開く" onClick={() => setIsDrawerOpen(true)} sx={{ mr: 1 }}>
                        <MenuIcon />
                    </IconButton>
                    <Typography variant="h3" component="span">
                        🍪 おやつ日和
                    </Typography>
                </Toolbar>
            </AppBar>
            <Box component="nav" sx={{ width: { md: pcDrawerWidth }, flexShrink: { md: 0 }, transition: widthTransition }}>
                <Drawer
                    variant="temporary"
                    open={isDrawerOpen}
                    onClose={() => setIsDrawerOpen(false)}
                    ModalProps={{ keepMounted: true }}
                    sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
                >
                    {renderDrawer(false, false)}
                </Drawer>
                <Drawer
                    variant="permanent"
                    open
                    sx={{
                        display: { xs: 'none', md: 'block' },
                        '& .MuiDrawer-paper': { width: pcDrawerWidth, boxSizing: 'border-box', overflowX: 'hidden', transition: widthTransition },
                    }}
                >
                    {renderDrawer(isCollapsed, true)}
                </Drawer>
            </Box>
            <Box component="main" sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', overflow: { md: 'hidden' } }}>
                <Toolbar sx={{ display: { md: 'none' } }} />
                <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                    <PageErrorBoundary key={location.pathname}>
                        <Outlet />
                    </PageErrorBoundary>
                </Box>
            </Box>
        </Box>
    );
};
