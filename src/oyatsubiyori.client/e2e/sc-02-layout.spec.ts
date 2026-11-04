import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { captureScreen, E2E_USER } from './helpers';

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

    test('SC-02 PC ではボタンでサイドバーを細い帯に閉じると本文が広がり、帯から画面を移れる。再読み込みしても閉じたままで、キーボードでも開ける', async ({
        page,
    }, testInfo) => {
        test.skip(testInfo.project.name !== 'pc', 'PC だけの動き（スマホは ≡ で開く）');
        await page.goto('/products');
        const nav = page.getByRole('navigation', { name: 'メニュー', exact: true });
        const main = page.getByRole('main');
        await expect(page.getByRole('heading', { level: 1, name: '商品一覧' })).toBeVisible();
        const openNavWidth = (await nav.boundingBox())?.width ?? 0;
        const openMainWidth = (await main.boundingBox())?.width ?? 0;

        const closeButton = page.getByRole('button', { name: 'メニューを閉じる' });
        await expect(closeButton).toHaveAttribute('aria-expanded', 'true');
        await closeButton.click();

        // 幅が動き終わるまで待つ。帯が細くなった分だけ、本文が広がる
        await expect.poll(async () => (await nav.boundingBox())?.width ?? 0).toBeLessThan(80);
        await expect.poll(async () => (await main.boundingBox())?.width ?? 0).toBeGreaterThan(openMainWidth + (openNavWidth - 80));
        await expect(page.getByRole('button', { name: 'メニューを開く' })).toHaveAttribute('aria-expanded', 'false');

        // 帯にはアイコンだけを出し、名前は aria-label で付く
        await expect(nav.getByText('商品一覧', { exact: true })).toHaveCount(0);
        for (const label of ['献立一覧', '商品一覧', '設定']) {
            await expect(nav.getByRole('link', { name: label, exact: true })).toBeVisible();
        }
        // 帯のログアウトは、ログイン中のアカウントも名前に入れる（文字のユーザー名が見えなくなるため）
        const logoutButton = page.getByRole('button', { name: `ログアウト（${E2E_USER.userName}）`, exact: true });
        await expect(logoutButton).toBeVisible();

        // アイコンが帯の幅に収まり、右端で切れていない（中身がボタンからはみ出さない）
        const stripBox = await nav.boundingBox();
        for (const item of [...['献立一覧', '商品一覧', '設定'].map((label) => nav.getByRole('link', { name: label, exact: true })), logoutButton]) {
            const box = await item.boundingBox();
            expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual((stripBox?.x ?? 0) + (stripBox?.width ?? 0) + 0.5);
            expect(await item.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
        }

        // 選ばれている項目は、帯でも幅いっぱいの薄い背景と左端の線で分かる
        const selected = nav.getByRole('link', { name: '商品一覧', exact: true });
        await expect(selected).toHaveAttribute('aria-current', 'page');
        await expect(selected).toHaveCSS('border-left-width', '4px');
        await expect(selected).not.toHaveCSS('border-left-color', 'rgba(0, 0, 0, 0)');
        await expect(selected).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
        const navBox = await nav.boundingBox();
        const selectedBox = await selected.boundingBox();
        expect(Math.abs((selectedBox?.width ?? 0) - (navBox?.width ?? -1))).toBeLessThan(1);

        // 閉じても、PC で画面全体は縦にスクロールしない（見出しは固定のまま）
        await expect(page.getByRole('heading', { level: 1, name: '商品一覧' })).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight)).toBe(true);

        // アイコンにマウスを乗せると、名前のツールチップが出る
        await nav.getByRole('link', { name: '設定', exact: true }).hover();
        await expect(page.getByRole('tooltip', { name: '設定' })).toBeVisible();
        await logoutButton.hover();
        await expect(page.getByRole('tooltip', { name: `ログアウト（${E2E_USER.userName}）` })).toBeVisible();
        const viewport = page.viewportSize();
        if (viewport) {
            await page.mouse.move(viewport.width - 1, viewport.height - 1);
        }
        await expect(page.getByRole('tooltip')).toHaveCount(0);
        await captureScreen(page, testInfo, 'sc-02-sidebar-collapsed');

        // 帯のアイコンから画面を移れる
        await nav.getByRole('link', { name: '献立一覧', exact: true }).click();
        await expect(page).toHaveURL(/\/menus/);
        await expect(nav.getByRole('link', { name: '献立一覧', exact: true })).toHaveAttribute('aria-current', 'page');

        // 再読み込みしても閉じたまま
        await page.reload();
        const openButton = page.getByRole('button', { name: 'メニューを開く' });
        await expect(openButton).toBeVisible();
        await expect(nav.getByText('献立一覧', { exact: true })).toHaveCount(0);

        // キーボードで開ける。開くと名前の文字が出て、本文の幅が元に戻る
        await openButton.focus();
        await page.keyboard.press('Enter');
        await expect(page.getByRole('button', { name: 'メニューを閉じる' })).toHaveAttribute('aria-expanded', 'true');
        await expect(nav.getByText('献立一覧', { exact: true })).toBeVisible();
        await expect.poll(async () => Math.abs(((await nav.boundingBox())?.width ?? 0) - openNavWidth)).toBeLessThan(1);
        // 開いたあとは、長い文字（ユーザー名など）も今までどおり折り返す
        await expect(nav.getByText('献立一覧', { exact: true })).toHaveCSS('white-space', 'normal');
        await expect(page.getByText(E2E_USER.userName, { exact: true }).filter({ visible: true })).toHaveCSS('white-space', 'normal');
    });

    test('SC-02 パスワードの変更が必要で押せない項目も、帯では名前のツールチップが出る', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name !== 'pc', 'PC だけの動き');
        // 画面の表示だけを確かめるため、ログインの状態の応答で「パスワードの変更が必要」にする（サーバーの状態は変えない）
        await page.route('**/api/auth/status', async (route) => {
            const response = await route.fetch();
            const json = await response.json();
            json.value.mustChangePassword = true;
            await route.fulfill({ response, json });
        });
        await page.addInitScript(() => window.localStorage.setItem('oyatsu-biyori.sidebar-collapsed', 'true'));
        await page.goto('/settings?tab=password');
        const nav = page.getByRole('navigation', { name: 'メニュー', exact: true });
        const disabledItem = nav.getByRole('link', { name: '献立一覧', exact: true });
        await expect(disabledItem).toHaveAttribute('aria-disabled', 'true');

        // 押せない項目はマウスの動きを受け取らないので、アイコンの位置にマウスを動かして確かめる
        const box = await disabledItem.boundingBox();
        expect(box).not.toBeNull();
        await page.mouse.move((box?.x ?? 0) + (box?.width ?? 0) / 2, (box?.y ?? 0) + (box?.height ?? 0) / 2);
        await expect(page.getByRole('tooltip', { name: '献立一覧' })).toBeVisible();
    });

    test('SC-02 スマホの ≡ のサイドバーは、PC で閉じた状態に左右されず、項目の名前を出す', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name !== 'mobile', 'スマホだけの動き');
        // PC で細い帯に閉じたあとの状態を作っておく
        await page.addInitScript(() => window.localStorage.setItem('oyatsu-biyori.sidebar-collapsed', 'true'));
        await page.goto('/products');
        const nav = await openSideMenu(page, true);
        for (const label of ['献立一覧', '商品一覧', '設定']) {
            await expect(nav.getByText(label, { exact: true })).toBeVisible();
        }
        await expect(page.getByText('ログアウト', { exact: true }).filter({ visible: true })).toBeVisible();
        // 長い文字（ユーザー名など）は今までどおり折り返し、横にはみ出さない
        await expect(nav.getByText('献立一覧', { exact: true })).toHaveCSS('white-space', 'normal');
        await expect(page.getByText(E2E_USER.userName, { exact: true }).filter({ visible: true })).toHaveCSS('white-space', 'normal');
        // スマホのサイドバーには、PC 用の開閉のボタンを出さない
        await expect(page.getByRole('button', { name: 'メニューを閉じる' })).toHaveCount(0);
    });

    test('SC-02 画面の幅をスマホと PC の間で変えても、サイドバーが開いたまま・本文が隠れたままにならない', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name !== 'pc', '幅を変えるのは PC のプロジェクトで確かめる');
        const pcViewport = page.viewportSize() ?? { width: 1280, height: 800 };
        await page.setViewportSize({ width: 600, height: pcViewport.height });
        await page.goto('/products');
        await openSideMenu(page, true);

        // PC の幅にすると、スマホのサイドバーは閉じ、本文が読める・押せる
        await page.setViewportSize(pcViewport);
        await expect(page.getByRole('heading', { level: 1, name: '商品一覧' })).toBeVisible();
        await expect(page.getByRole('button', { name: 'メニューを閉じる' })).toBeVisible();
        await page.getByRole('navigation', { name: 'メニュー', exact: true }).getByRole('link', { name: '設定', exact: true }).click();
        await expect(page).toHaveURL(/\/settings/);

        // スマホの幅に戻しても、サイドバーは開かない（≡ で開く）
        await page.setViewportSize({ width: 600, height: pcViewport.height });
        await expect(page.getByRole('navigation', { name: 'メニュー', exact: true })).toBeHidden();
        await expect(page.getByRole('button', { name: 'メニューを開く' })).toBeVisible();
    });

    test('SC-02 開閉の状態を保存できないブラウザでも、サイドバーは開いた状態で出て、開閉できる', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name !== 'pc', 'PC だけの動き');
        // ブラウザの設定で保存が使えないときのように、localStorage を読むと例外になるようにする
        await page.addInitScript(() => {
            Object.defineProperty(window, 'localStorage', {
                configurable: true,
                get: () => {
                    throw new DOMException('保存が使えません', 'SecurityError');
                },
            });
        });
        await page.goto('/products');
        const nav = page.getByRole('navigation', { name: 'メニュー', exact: true });
        await expect(nav.getByText('献立一覧', { exact: true })).toBeVisible();
        await page.getByRole('button', { name: 'メニューを閉じる' }).click();
        await expect(page.getByRole('button', { name: 'メニューを開く' })).toBeVisible();
        await expect(nav.getByText('献立一覧', { exact: true })).toHaveCount(0);
    });
});
