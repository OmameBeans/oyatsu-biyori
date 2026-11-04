import { useState } from 'react';
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
import Typography from '@mui/material/Typography';
import MenuIcon from '@mui/icons-material/Menu';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import SettingsIcon from '@mui/icons-material/Settings';
import LogoutIcon from '@mui/icons-material/Logout';
import { useSnackbar } from 'notistack';
import { useAuthStatus, useLogout } from '../../entities/auth';
import { errorMessageOf } from '../../shared/api';
import { ROUTE } from '../../shared/type';
import { PageErrorBoundary, SafetyNotice } from '../../shared/ui';

const DRAWER_WIDTH = 220;

const NAV_ITEMS = [
    { to: '/menus', label: '献立一覧', icon: <CalendarMonthIcon /> },
    { to: '/products', label: '商品一覧', icon: <Inventory2Icon /> },
    { to: '/settings', label: '設定', icon: <SettingsIcon /> },
] as const;

/**
 * SC-02 ホームの枠（T0-14）：左サイドバー（献立一覧・商品一覧・設定・ログアウト）。スマホは ≡ で開く。
 * 「アレルゲンの最終確認は、必ずパッケージの原材料表示で行ってください」を常に表示する。
 */
export const AppLayout = () => {
    const { data: status } = useAuthStatus();
    const location = useLocation();
    const navigate = useNavigate();
    const logout = useLogout();
    const { enqueueSnackbar } = useSnackbar();
    const [isDrawerOpen, setIsDrawerOpen] = useState(false);

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

    const drawer = (
        <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
            <Toolbar sx={{ gap: 1 }}>
                <Typography component="span" sx={{ fontSize: 22 }} aria-hidden>
                    🍪
                </Typography>
                <Typography variant="h3" component="span">
                    おやつ日和
                </Typography>
            </Toolbar>
            <Divider />
            <List component="nav" aria-label="メニュー">
                {NAV_ITEMS.map((item) => (
                    <ListItemButton
                        key={item.to}
                        component={NavLink}
                        to={item.to}
                        onClick={() => setIsDrawerOpen(false)}
                        disabled={status.mustChangePassword && item.to !== '/settings'}
                        sx={{ mx: 1, borderRadius: 2, '&.active': { bgcolor: 'primary.light', color: 'primary.dark', fontWeight: 700 } }}
                    >
                        <ListItemIcon sx={{ minWidth: 36, color: 'inherit' }}>{item.icon}</ListItemIcon>
                        <ListItemText primary={item.label} />
                    </ListItemButton>
                ))}
            </List>
            <Box sx={{ flex: 1 }} />
            <Divider />
            <List>
                <ListItemButton onClick={handleLogout} disabled={logout.isPending} sx={{ mx: 1, borderRadius: 2 }}>
                    <ListItemIcon sx={{ minWidth: 36 }}>
                        <LogoutIcon />
                    </ListItemIcon>
                    <ListItemText primary="ログアウト" secondary={status.userName ?? undefined} />
                </ListItemButton>
            </List>
        </Box>
    );

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
            <Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }}>
                <Drawer
                    variant="temporary"
                    open={isDrawerOpen}
                    onClose={() => setIsDrawerOpen(false)}
                    ModalProps={{ keepMounted: true }}
                    sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
                >
                    {drawer}
                </Drawer>
                <Drawer variant="permanent" open sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, boxSizing: 'border-box' } }}>
                    {drawer}
                </Drawer>
            </Box>
            <Box component="main" sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', overflow: { md: 'hidden' } }}>
                <Toolbar sx={{ display: { md: 'none' } }} />
                <SafetyNotice />
                <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                    <PageErrorBoundary key={location.pathname}>
                        <Outlet />
                    </PageErrorBoundary>
                </Box>
            </Box>
        </Box>
    );
};
