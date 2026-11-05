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

    test('SC-10 「今週とこれから」は今週が一番上で、その下に来週・再来週が並ぶ', async ({ page }) => {
        await page.goto('/menus');
        const upcoming = page.getByRole('region', { name: '今週とこれから' });
        const weeks = upcoming.getByRole('region');
        await expect(weeks.nth(0)).toHaveAccessibleName(/^今週 /);
        await expect(weeks.nth(1)).toHaveAccessibleName(/^来週 /);
        await expect(weeks.nth(2)).toHaveAccessibleName(/^再来週 /);
        await expect(upcoming.getByRole('button', { name: /^さらに先の週（.+〜）の献立を作る$/ })).toBeVisible();
    });

    test('SC-14 過去の週は献立一覧に出さず、「過去の献立を見る」から別の画面で開く', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: '過去の献立' })).toHaveCount(0);
        await page.getByRole('link', { name: /^過去の献立を見る（\d+週）$/ }).click();
        await expect(page).toHaveURL(/\/menus\/history$/);
        await expect(page.getByRole('heading', { level: 1, name: '過去の献立' })).toBeVisible();
    });

    test('FR-MENU-34a 確定済みの献立で判定が変わった商品に「確認し直し」のバッジが出る', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByText(/確認し直し \d+件/).first()).toBeVisible();
    });

    test('SC-10 今日以降の献立で使う、パッケージで確認する商品があれば、上部に件数つきの注意を出す', async ({ page }) => {
        await page.goto('/menus');
        await expect(page.getByText(/今日以降の献立で使う、パッケージで確認する商品が\s*\d+件\s*あります/)).toBeVisible();
        await expect(page.getByRole('link', { name: '商品を確認する' })).toBeVisible();
        // 「対応が必要なこと」のカード（そのほかの商品・未確定の献立の件数）は出さない
        await expect(page.getByText('未確定の献立', { exact: false })).toHaveCount(0);
    });

    test('FR-MENU-31 来週の「献立を作る」で下書きができ、手順1（チラシ取り込み）が開く', async ({ page }) => {
        await page.goto('/menus');
        const row = page.getByRole('heading', { level: 3 }).filter({ hasText: '（月）' });
        await expect(row.first()).toBeVisible();
        const nextWeekCard = page.getByRole('region', { name: /^来週 / });
        await nextWeekCard.getByRole('button', { name: /献立を作る|開く/ }).click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
        await expect(page.getByRole('tab', { name: 'チラシ取り込み', exact: true })).toBeVisible();
    });

    test('FR-LIST-00 先の週の下書きを削除して間が空いても、その週の行が出て献立を作れる', async ({ page }) => {
        await page.goto('/menus');
        const upcoming = page.getByRole('region', { name: '今週とこれから' });
        const further = upcoming.getByRole('button', { name: /^さらに先の週（.+〜）の献立を作る$/ });
        const weekCard = (date: string) => upcoming.getByRole('region', { name: new RegExp(`(^| )${date}（月）`) });

        // さらに先の週を2つ作る（1つ目を、あとで削除して間を空ける）
        const gapDate = (await further.textContent())?.match(/（(\d+\/\d+)〜）/)?.[1] ?? '';
        expect(gapDate).not.toBe('');
        await further.click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
        await page.goto('/menus');
        await expect(further).not.toContainText(`（${gapDate}〜）`);
        await further.click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);

        await page.goto('/menus');
        await weekCard(gapDate).getByRole('button', { name: /のメニュー$/ }).click();
        await page.getByRole('menuitem', { name: '下書きを削除' }).click();
        await page.getByRole('dialog').getByRole('button', { name: '削除する' }).click();

        const createButton = weekCard(gapDate).getByRole('button', { name: '献立を作る' });
        await expect(createButton).toBeVisible();
        await createButton.click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
        await expect(page.getByRole('tab', { name: 'チラシ取り込み', exact: true })).toBeVisible();
    });
});
