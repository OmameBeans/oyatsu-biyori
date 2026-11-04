import { Suspense, useCallback, useMemo, useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider } from '@mui/material/styles';
import { SnackbarProvider } from 'notistack';
import { authKeys } from '../entities/auth';
import { AllergenCheckPage } from '../pages/allergen-check';
import { LandingPage } from '../pages/landing';
import { LoginPage } from '../pages/login';
import { MenuEditPage } from '../pages/menu-edit';
import { MenuListPage } from '../pages/menu-list';
import { ProductDetailPage } from '../pages/product-detail';
import { ProductListPage } from '../pages/product-list';
import { SettingsPage } from '../pages/settings';
import { ApiError, registerAuthHandlers } from '../shared/api';
import { AppearanceContext, readStoredColorTheme, writeStoredColorTheme } from '../shared/lib';
import type { AppearanceValue } from '../shared/lib';
import { ROUTE } from '../shared/type';
import type { ColorTheme } from '../shared/type';
import { LoadingFallback, PageErrorBoundary } from '../shared/ui';
import { AppLayout } from './layout/AppLayout';
import { COLOR_THEME_OPTIONS, createAppTheme } from './theme';

const queryClient = new QueryClient({
    defaultOptions: {
        queries: {
            // 通信のエラー（status 0）は1回だけ取り直す。サーバーが返したエラーは取り直さない
            retry: (count, error) => count < 1 && (!(error instanceof ApiError) || error.status === 0),
            refetchOnWindowFocus: false,
        },
    },
});

// ランディングはログイン後の枠の外に置く（API を呼ばない。U-31）
const router = createBrowserRouter([
    { path: ROUTE.LANDING, element: <LandingPage /> },
    {
        path: ROUTE.LOGIN,
        element: (
            <PageErrorBoundary>
                <LoginPage />
            </PageErrorBoundary>
        ),
    },
    {
        element: (
            <Suspense fallback={<LoadingFallback />}>
                <AppLayout />
            </Suspense>
        ),
        children: [
            { path: ROUTE.MENUS, element: <MenuListPage /> },
            { path: `${ROUTE.MENUS}/:menuId`, element: <MenuEditPage /> },
            { path: ROUTE.PRODUCTS, element: <ProductListPage /> },
            { path: `${ROUTE.PRODUCTS}/:productId`, element: <ProductDetailPage /> },
            { path: `${ROUTE.PRODUCTS}/:productId/check`, element: <AllergenCheckPage /> },
            { path: ROUTE.SETTINGS, element: <SettingsPage /> },
        ],
    },
    { path: '*', element: <Navigate to={ROUTE.LANDING} replace /> },
]);

// ログインが切れたらログイン画面へ（ランディングとログイン画面では移らない）。パスワードの変更が必要ならそのタブへ
registerAuthHandlers({
    onUnauthorized: () => {
        const path = window.location.pathname;
        queryClient.setQueryData(authKeys.status(), { isAuthenticated: false, userName: null, mustChangePassword: false });
        if (path !== ROUTE.LANDING && path !== ROUTE.LOGIN) {
            void router.navigate(ROUTE.LOGIN, { replace: true });
        }
    },
    onPasswordChangeRequired: () => {
        void queryClient.invalidateQueries({ queryKey: authKeys.status() });
        void router.navigate(ROUTE.PASSWORD_TAB, { replace: true });
    },
});

/**
 * アプリ全体（テーマ・サーバー状態・通知・ルーティング）。
 * 色のテーマ（#2）はこの端末のブラウザに保存し、最初の表示から保存した色で出す（ログイン前の画面も同じ）。
 */
export const App = () => {
    const [colorTheme, setColorThemeState] = useState<ColorTheme>(readStoredColorTheme);
    const setColorTheme = useCallback((next: ColorTheme) => {
        setColorThemeState(next);
        writeStoredColorTheme(next);
    }, []);
    const theme = useMemo(() => createAppTheme({ colorTheme }), [colorTheme]);
    const appearance = useMemo<AppearanceValue>(() => ({ colorTheme, colorThemeOptions: COLOR_THEME_OPTIONS, setColorTheme }), [colorTheme, setColorTheme]);

    return (
        <AppearanceContext value={appearance}>
            <ThemeProvider theme={theme}>
                <CssBaseline />
                <QueryClientProvider client={queryClient}>
                    <SnackbarProvider maxSnack={3} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }} autoHideDuration={4000}>
                        <RouterProvider router={router} />
                    </SnackbarProvider>
                </QueryClientProvider>
            </ThemeProvider>
        </AppearanceContext>
    );
};
