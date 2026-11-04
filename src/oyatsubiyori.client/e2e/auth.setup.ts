import { expect, test as setup } from '@playwright/test';
import { E2E_USER } from './helpers';

/** 最初に1回ログインして、状態（Cookie）を使い回す */
setup('ログインして状態を保存する', async ({ page }) => {
    await page.goto('/login');
    await page.getByLabel('ユーザー名').fill(E2E_USER.userName);
    await page.getByLabel('パスワード').fill(E2E_USER.password);
    await page.getByRole('button', { name: 'ログイン' }).click();
    await expect(page).toHaveURL(/\/menus/);
    await page.context().storageState({ path: './e2e/.auth/state.json' });
});
