import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import CssBaseline from '@mui/material/CssBaseline';
import { ThemeProvider } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { SnackbarProvider } from 'notistack';
import { authKeys } from '../entities/auth';
import { AllergenCheckPage } from '../pages/allergen-check';
import { LandingPage } from '../pages/landing';
import { LoginPage } from '../pages/login';
import { MenuEditPage } from '../pages/menu-edit';
import { MenuHistoryPage, MenuListPage } from '../pages/menu-list';
import { ProductDetailPage } from '../pages/product-detail';
import { ProductListPage } from '../pages/product-list';
import { SettingsPage } from '../pages/settings';
import { ApiError, registerAuthHandlers } from '../shared/api';
import { AppearanceContext, readStoredColorMode, readStoredColorTheme, writeStoredColorMode, writeStoredColorTheme } from '../shared/lib';
import type { AppearanceValue } from '../shared/lib';
import { ROUTE } from '../shared/type';
import type { ColorMode, ColorTheme } from '../shared/type';
import { LoadingFallback, PageErrorBoundary } from '../shared/ui';
import { AppLayout } from './layout/AppLayout';
import { browserBarColorOf, COLOR_MODE_OPTIONS, colorThemeOptionsFor, createAppTheme, resolveColorMode } from './theme';

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
            { path: ROUTE.MENU_HISTORY, element: <MenuHistoryPage /> },
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
 * 色のテーマ（#2）と明るさ（#4）はこの端末のブラウザに保存し、最初の表示から保存したもので出す（ログイン前の画面も同じ）。
 * 明るさがシステムのときは、端末の設定（ライト・ダーク）に合わせ、端末の設定が変わればすぐ切り替える。
 */
export const App = () => {
    const [colorTheme, setColorThemeState] = useState<ColorTheme>(readStoredColorTheme);
    const [colorMode, setColorModeState] = useState<ColorMode>(readStoredColorMode);
    const prefersDark = useMediaQuery('(prefers-color-scheme: dark)', { noSsr: true });
    const setColorTheme = useCallback((next: ColorTheme) => {
        setColorThemeState(next);
        writeStoredColorTheme(next);
    }, []);
    const setColorMode = useCallback((next: ColorMode) => {
        setColorModeState(next);
        writeStoredColorMode(next);
    }, []);
    const mode = resolveColorMode(colorMode, prefersDark);
    const theme = useMemo(() => createAppTheme({ colorTheme, mode }), [colorTheme, mode]);
    const appearance = useMemo<AppearanceValue>(
        () => ({
            colorTheme,
            colorThemeOptions: colorThemeOptionsFor(mode),
            setColorTheme,
            colorMode,
            colorModeOptions: COLOR_MODE_OPTIONS,
            setColorMode,
        }),
        [colorTheme, colorMode, mode, setColorTheme, setColorMode],
    );

    // ブラウザの上の帯の色を、今のテーマに合わせる（ライトはテーマの色、ダークは背景の色）
    const browserBarColor = browserBarColorOf(theme);
    useEffect(() => {
        document.querySelector('meta[name="theme-color"]')?.setAttribute('content', browserBarColor);
    }, [browserBarColor]);
    // html の color-scheme を、選んだ明るさに合わせ続ける（読み込み中は index.html の最初のスクリプトが設定する。決める場所はこの2つだけ）
    useEffect(() => {
        document.documentElement.style.colorScheme = mode;
    }, [mode]);

    return (
        <AppearanceContext value={appearance}>
            <ThemeProvider theme={theme}>
                {/* color-scheme（スクロールバーなどブラウザが描く部分の明るさ）は、下の useEffect の html のインラインの style だけで決める。
                    読み込み中は index.html のスクリプトが同じ style を設定するので、CssBaseline の enableColorScheme は使わない（style の方が強く、効かないため） */}
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
