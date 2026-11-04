import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { captureScreen } from './helpers';

/** サイドバーのメニューを返す。スマホは ≡ で開いてから探す（PC は常に出ている） */
const openSideMenu = async (page: Page, isMobile: boolean) => {
    if (isMobile) {
        await page.getByRole('button', { name: 'メニューを開く' }).click();
    }
    const nav = page.getByRole('navigation', { name: 'メニュー', exact: true });
    await expect(nav).toBeVisible();
    // スマホのサイドバーは左からすべり込むので、開き終わる（左端が画面の端に着く）まで待ってから位置を測る
    await expect.poll(async () => Math.abs((await nav.boundingBox())?.x ?? -1000)).toBeLessThan(1);
    // ≡ を押したときのマウスの位置が項目に重なり、背景が変わらないように、マウスを画面の右下へ逃がす
    const viewport = page.viewportSize();
    if (viewport) {
        await page.mouse.move(viewport.width - 1, viewport.height - 1);
    }
    return nav;
};

test.describe('SC-02 ホームの枠（左サイドバー）', () => {
    test('SC-02 選ばれている項目を、幅いっぱいの薄い背景と左端の縦の線で示し、文字の位置はずれない', async ({ page }, testInfo) => {
        await page.goto('/products');
        const nav = await openSideMenu(page, testInfo.project.name === 'mobile');

        const selected = nav.getByRole('link', { name: '商品一覧', exact: true });
        const others = [nav.getByRole('link', { name: '献立一覧', exact: true }), nav.getByRole('link', { name: '設定', exact: true })];

        await expect(selected).toHaveAttribute('aria-current', 'page');
        await expect(selected).toHaveCSS('border-left-width', '4px');
        await expect(selected).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
        await expect(selected).not.toHaveCSS('border-left-color', 'rgba(0, 0, 0, 0)');

        const selectedLabel = await nav.getByText('商品一覧', { exact: true }).boundingBox();
        const navBox = await nav.boundingBox();
        const selectedBox = await selected.boundingBox();
        expect(selectedLabel).not.toBeNull();
        // 背景はサイドバーの幅いっぱい（左右に余白がない）
        expect(Math.abs((selectedBox?.x ?? 0) - (navBox?.x ?? -1))).toBeLessThan(1);
        expect(Math.abs((selectedBox?.width ?? 0) - (navBox?.width ?? -1))).toBeLessThan(1);

        for (const other of others) {
            await expect(other).not.toHaveAttribute('aria-current', 'page');
            await expect(other).toHaveCSS('border-left-width', '4px');
            await expect(other).toHaveCSS('border-left-color', 'rgba(0, 0, 0, 0)');
            await expect(other).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
        }
        const labels = [
            nav.getByText('献立一覧', { exact: true }),
            nav.getByText('設定', { exact: true }),
            // ログアウトの行（余白と角丸は残す）も、文字の位置はそろえる
            page.getByText('ログアウト', { exact: true }).filter({ visible: true }),
        ];
        for (const label of labels) {
            const box = await label.boundingBox();
            // 選ばれている項目と、文字の左端がそろっている
            expect(Math.abs((box?.x ?? 0) - (selectedLabel?.x ?? -1))).toBeLessThan(1);
        }

        await captureScreen(page, testInfo, 'sc-02-sidebar');
    });
});
