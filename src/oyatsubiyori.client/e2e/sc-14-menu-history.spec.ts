import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

test.describe('SC-14 過去の献立', () => {
    test('FR-LIST-00 今週より前の週を、週の実績と週の予算・商品の問題つきで出す', async ({ page }, testInfo) => {
        await page.goto('/menus/history');
        await expect(page.getByRole('heading', { level: 1, name: '過去の献立' })).toBeVisible();
        // スマホの上部のバーにも「メニューを開く」があるので、本文（main）の中で探す
        const main = page.getByRole('main');
        await expect(main.getByText(/\d+円/).first()).toBeVisible();
        await expect(main.getByRole('button', { name: /開く$/ }).first()).toBeVisible();
        // 今週・来週・再来週は出さない
        await expect(main.getByText('今週', { exact: true })).toHaveCount(0);
        await captureScreen(page, testInfo, 'sc-14-menu-history');
    });

    test('SC-14 「開く」で、その週の献立を開ける', async ({ page }) => {
        await page.goto('/menus/history');
        await page.getByRole('main').getByRole('button', { name: /開く$/ }).first().click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+$/);
        await expect(page.getByRole('heading', { level: 1, name: /^献立作成/ })).toBeVisible();
    });

    test('SC-14 「献立一覧へ戻る」で献立一覧に戻れる', async ({ page }) => {
        await page.goto('/menus/history');
        await page.getByRole('link', { name: '献立一覧へ戻る' }).click();
        await expect(page).toHaveURL(/\/menus$/);
        await expect(page.getByRole('heading', { level: 1, name: '献立一覧' })).toBeVisible();
    });
});
