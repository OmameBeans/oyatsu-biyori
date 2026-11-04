import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

test.describe('SC-03・SC-04・SC-12 設定', () => {
    test('FR-COND-01 条件設定を保存できる（ログイン直後でも Antiforgery が通る）', async ({ page }, testInfo) => {
        await page.goto('/settings');
        const count = page.getByLabel('通常の人数');
        await expect(count).toHaveValue('50');
        await count.fill('52');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('条件設定を保存しました')).toBeVisible();
        await page.reload();
        await expect(page.getByLabel('通常の人数')).toHaveValue('52');
        await captureScreen(page, testInfo, 'sc-03-conditions');
        await page.getByLabel('通常の人数').fill('50');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('条件設定を保存しました')).toBeVisible();
    });

    test('FR-COND 受け入れ条件 人数が0だと保存できず、次にすることが出る', async ({ page }) => {
        await page.goto('/settings');
        await page.getByLabel('通常の人数').fill('0');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('通常の人数は1以上の整数で入れてください')).toBeVisible();
    });

    test('FR-AVOID-05 避ける品目を外すときは確認のダイアログが出る', async ({ page }, testInfo) => {
        await page.goto('/settings?tab=avoided');
        const hazelnut = page.getByRole('checkbox', { name: 'ヘーゼルナッツを避ける' });
        await expect(hazelnut).toBeChecked();
        await captureScreen(page, testInfo, 'sc-04-avoided');
        await hazelnut.click();
        await expect(page.getByText('この品目を避ける子がもういないことを確認しましたか？', { exact: false })).toBeVisible();
        await page.getByRole('button', { name: 'やめる' }).click();
        await expect(hazelnut).toBeChecked();
    });

    test('FR-AVOID-07 探す言葉を足して消せる（初期データの言葉は消せない）', async ({ page }) => {
        await page.goto('/settings?tab=avoided');
        await page.getByRole('button', { name: 'ヘーゼルナッツの探す言葉' }).click();
        const keyword = `ヘーゼルテスト${test.info().project.name}`;
        await page.getByLabel('探す言葉を足す').first().fill(keyword);
        await page.getByRole('button', { name: '足す' }).first().click();
        const chip = page.getByRole('button', { name: keyword });
        await expect(chip).toBeVisible();
        await chip.locator('svg').click();
        await page.getByRole('button', { name: '消す', exact: true }).click();
        await expect(chip).toHaveCount(0);
    });

    test('FR-AUTH-03a パスワードは12文字未満にできない', async ({ page }, testInfo) => {
        await page.goto('/settings?tab=password');
        await page.getByLabel('今のパスワード').fill('e2e-dummy-password-0001');
        await page.getByLabel('新しいパスワード（12文字以上）').fill('short');
        await page.getByLabel('新しいパスワード（確認）').fill('short');
        await page.getByRole('button', { name: 'パスワードを変更する' }).click();
        await expect(page.getByText('パスワードは12文字以上にしてください')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-12-password');
    });
});
