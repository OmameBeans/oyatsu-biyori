import { expect, test } from '@playwright/test';
import { captureScreen, E2E_USER } from './helpers';

test.describe('SC-01 ログイン', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('FR-AUTH-02 ログインしていないと、ログイン後の画面はログイン画面へ移る', async ({ page }) => {
        await page.goto('/menus');
        await expect(page).toHaveURL(/\/login/);
        await expect(page.getByRole('button', { name: 'ログイン' })).toBeVisible();
    });

    test('NFR-SEC-10 ログインしていないと、API は 401 を返す', async ({ request }) => {
        const response = await request.get('/api/settings/get');
        expect(response.status()).toBe(401);
    });

    test('FR-LP-01 ランディングはログインせずに見られ、ボタンは「ログイン」', async ({ page }, testInfo) => {
        await page.goto('/');
        await expect(page.getByRole('heading', { name: '安全への考え方' })).toBeVisible();
        await expect(page.getByRole('link', { name: 'ログイン' })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-13-landing');
    });

    test('FR-AUTH-01 パスワードが違うとログインできず、理由が出る', async ({ page }, testInfo) => {
        await page.goto('/login');
        await page.getByLabel('ユーザー名').fill(E2E_USER.userName);
        await page.getByLabel('パスワード').fill('wrong-password-xxxx');
        await page.getByRole('button', { name: 'ログイン' }).click();
        await expect(page.getByText('ユーザー名かパスワードが違います')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-01-login');
    });

    test('FR-AUTH-01 正しいパスワードでログインすると献立一覧へ移る', async ({ page }) => {
        await page.goto('/login');
        await page.getByLabel('ユーザー名').fill(E2E_USER.userName);
        await page.getByLabel('パスワード').fill(E2E_USER.password);
        await page.getByRole('button', { name: 'ログイン' }).click();
        await expect(page).toHaveURL(/\/menus/);
    });
});
