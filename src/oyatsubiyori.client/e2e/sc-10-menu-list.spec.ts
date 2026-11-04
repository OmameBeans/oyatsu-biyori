import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

test.describe('SC-10 献立一覧', () => {
    test('FR-LIST-00 今週・来週・再来週の行が、未作成でも出る', async ({ page }, testInfo) => {
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: '献立一覧' })).toBeVisible();
        await expect(page.getByText('今週', { exact: true })).toBeVisible();
        await expect(page.getByText('来週', { exact: true })).toBeVisible();
        await expect(page.getByText('再来週', { exact: true })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-10-menu-list');
    });

    test('FR-MENU-34a 確定済みの献立で判定が変わった商品に「確認し直し」のバッジが出る', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByText(/確認し直し \d+件/).first()).toBeVisible();
    });

    test('SC-10 上部に「今日以降の献立で使う、パッケージで確認する商品」を最初に出す', async ({ page }) => {
        await page.goto('/menus');
        const attention = page.getByText('今日以降の献立で使う、パッケージで確認する商品', { exact: false });
        await expect(attention).toBeVisible();
        await expect(page.getByText('未確定の献立', { exact: false })).toBeVisible();
    });

    test('FR-MENU-31 来週の「献立を作る」で下書きができ、手順1（チラシ取り込み）が開く', async ({ page }) => {
        await page.goto('/menus');
        const row = page.getByRole('heading', { level: 2 }).filter({ hasText: '（月）' });
        await expect(row.first()).toBeVisible();
        const nextWeekCard = page.getByRole('region', { name: /^来週 / });
        await nextWeekCard.getByRole('button', { name: /献立を作る|開く/ }).click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
        await expect(page.getByRole('tab', { name: 'チラシ取り込み', exact: true })).toBeVisible();
    });
});
