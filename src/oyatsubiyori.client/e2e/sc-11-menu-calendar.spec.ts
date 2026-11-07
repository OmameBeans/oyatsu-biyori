import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

test.describe('SC-11 献立一覧（カレンダー）', () => {
    test('FR-LIST-03 はじめて開く献立一覧はカレンダーで出る', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();
        await expect(page.getByRole('region', { name: '今週とこれから' })).toHaveCount(0);
    });

    test('FR-LIST-03 「カレンダー」に切り替えると、月のカレンダーが出て、表示と月がクエリ文字列に残る', async ({ page }, testInfo) => {
        await page.goto('/menus?view=list');
        await page.getByRole('button', { name: 'カレンダー' }).click();
        await expect(page).toHaveURL(/\/menus\?view=calendar&month=\d{4}-\d{2}$/);
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();
        // ダミーデータには今週の確定献立があるので、今週の行に「献立を開く」が出る
        await expect(page.getByRole('link', { name: /〜.+の献立を開く$/ }).first()).toBeVisible();
        // 「今週とこれから」「過去の献立を見る」は一覧のときだけ
        await expect(page.getByRole('region', { name: '今週とこれから' })).toHaveCount(0);
        await captureScreen(page, testInfo, 'sc-11-menu-calendar');

        // 再読み込みしてもカレンダーのまま
        await page.reload();
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();

        await page.getByRole('button', { name: '一覧' }).click();
        await expect(page).toHaveURL(/\/menus\?view=list$/);
        await expect(page.getByRole('region', { name: '今週とこれから' })).toBeVisible();
    });

    test('FR-LIST-03 最後に選んだ表示を覚えていて、クエリ文字列のない献立一覧を開くとその表示になる', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();
        await page.getByRole('button', { name: '一覧' }).click();
        await expect(page.getByRole('region', { name: '今週とこれから' })).toBeVisible();

        await page.goto('/menus');
        await expect(page.getByRole('region', { name: '今週とこれから' })).toBeVisible();
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toHaveCount(0);

        await page.getByRole('button', { name: 'カレンダー' }).click();
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();
    });

    test('FR-LIST-03 クエリ文字列の表示は、覚えている表示より優先する', async ({ page }) => {
        await page.goto('/menus?view=calendar');
        await page.getByRole('button', { name: '一覧' }).click();
        await page.goto('/menus?view=calendar&month=2026-11');
        await expect(page.getByRole('heading', { name: '2026年11月' })).toBeVisible();
        // 覚えている表示がカレンダーでも、クエリ文字列が一覧なら一覧
        await page.goto('/menus?view=list');
        await expect(page.getByRole('region', { name: '今週とこれから' })).toBeVisible();
        await page.goto('/menus?view=calendar&month=2026-11');
        await expect(page.getByRole('heading', { name: '2026年11月' })).toBeVisible();
    });

    test('FR-LIST-03 前の月・次の月で月を移り、URL の月も変わる', async ({ page }) => {
        await page.goto('/menus?view=calendar&month=2026-11');
        await expect(page.getByRole('heading', { name: '2026年11月' })).toBeVisible();
        await page.getByRole('button', { name: '次の月' }).click();
        await expect(page).toHaveURL(/month=2026-12$/);
        await expect(page.getByRole('heading', { name: '2026年12月' })).toBeVisible();
        await page.getByRole('button', { name: '前の月' }).click();
        // 月が替わるのを待ってから次を押す（続けて押すと、読み込み中の1回が消えることがある）
        await expect(page.getByRole('heading', { name: '2026年11月' })).toBeVisible();
        await page.getByRole('button', { name: '前の月' }).click();
        await expect(page).toHaveURL(/month=2026-10$/);
        await expect(page.getByRole('heading', { name: '2026年10月' })).toBeVisible();
    });

    test('FR-LIST-03 月の形が違えば今月を出す', async ({ page }) => {
        await page.goto('/menus?view=calendar&month=2026-13');
        await expect(page.getByRole('heading', { name: /^\d{4}年\d{1,2}月$/ })).toBeVisible();
        await expect(page.getByRole('heading', { name: '2026年13月' })).toHaveCount(0);
    });

    test('FR-LIST-03 献立のある日には商品と判定のアイコンが出て、押すとその献立が開く', async ({ page }, testInfo) => {
        // ダミーデータには今週の確定献立が必ずあり、今週は今月のカレンダーに必ず入る
        await page.goto('/menus?view=calendar');
        await expect(page.getByText(/^(使えません|要確認|使えます)：$/).first()).toBeAttached();
        if (testInfo.project.name === 'mobile') {
            // スマホは日のマスを出さず、週のカードに日を並べる。週の「献立を開く」で献立を開く
            await page.getByRole('link', { name: /〜.+の献立を開く$/ }).first().click();
            await expect(page).toHaveURL(/\/menus\/[^/?]+$/);
            return;
        }
        // 週の日付は押せない（週の献立は「献立を開く」から開く）
        await expect(page.getByRole('link', { name: /^\d+\/\d+〜$/ })).toHaveCount(0);
        await page.getByRole('link', { name: /^\d+\/\d+（.）の献立を開く$/ }).first().click();
        await expect(page).toHaveURL(/\/menus\/[^/?]+\?step=4$/);
    });
});
