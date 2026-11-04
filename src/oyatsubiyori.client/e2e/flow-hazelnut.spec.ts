import path from 'node:path';
import { expect, test } from '@playwright/test';
import type { Browser, TestInfo } from '@playwright/test';
import { COLOR_MODE } from '../src/shared/type/colorMode';
import { PREFERENCE_KEY } from '../src/shared/type/preferenceKey';
import { captureScreen, captureScreenAs, expectReadable, openFlyerItemDetail } from './helpers';

const FLYER_IMAGE = path.resolve(process.cwd(), '../../samples/flyers/flyer-01.png');
const PACKAGE_IMAGE = path.resolve(process.cwd(), '../../samples/packages/package-8104-back.png');

/**
 * ブラウザの context を、playwright.config.ts のプロジェクト（pc・mobile）と同じ設定で作るための値を取り出す。
 * プロジェクトの use には context に渡せない値（trace など）も入っているので、context の設定だけを選ぶ。
 */
const contextOptionsOf = (testInfo: TestInfo, projectName: 'pc' | 'mobile') => {
    const project = testInfo.config.projects.find((p) => p.name === projectName);
    if (!project) throw new Error(`playwright.config.ts にプロジェクト ${projectName} がありません`);
    const { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, baseURL, ignoreHTTPSErrors, locale, timezoneId, storageState } = project.use;
    return { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch, baseURL, ignoreHTTPSErrors, locale, timezoneId, storageState };
};

/**
 * #4 ダーク（暗い背景）で、🔴 の大事な知らせが読めるかを PC とスマホで確かめて撮る。
 * 献立一覧の最上部の警告・商品詳細の「今日以降の献立に使えない商品が入っています」・確定のダイアログの 🔴 の理由を見る。
 * 読むだけで、データは変えない（確定のダイアログは開いて「やめる」で閉じる）。
 */
const checkBlockedNoticesInDark = async (browser: Browser, testInfo: TestInfo, urls: { product: string; menu: string }) => {
    for (const projectName of ['pc', 'mobile'] as const) {
        const context = await browser.newContext(contextOptionsOf(testInfo, projectName));
        await context.addInitScript(({ key, mode }) => window.localStorage.setItem(key, mode), { key: PREFERENCE_KEY.COLOR_MODE, mode: COLOR_MODE.DARK });
        const page = await context.newPage();
        try {
            // 献立一覧の最上部の 🔴 の警告（filled の Alert）
            await page.goto('/menus');
            await expectReadable(page.getByText('使えない商品が献立に入っています', { exact: false }));
            await expectReadable(page.getByText('原材料に『ヘーゼルナッツ』があります（ヘーゼルナッツ）', { exact: false }).first());
            await captureScreenAs(page, `dark-sc-10-blocked-alert-${projectName}`);

            // 商品詳細：🔴 の理由と、今日以降の献立に使えない商品が入っている知らせ（記録したあとのアレルゲン確認と同じ filled の Alert）
            await page.goto(urls.product);
            await expectReadable(page.getByText('原材料に『ヘーゼルナッツ』があります（ヘーゼルナッツ）').first());
            await expectReadable(page.getByText('今日以降の献立に、使えない商品が入っています', { exact: false }));
            await captureScreenAs(page, `dark-sc-07-product-blocked-${projectName}`);

            // 確定のダイアログ：🔴 の確定できない理由と、次にすること
            await page.goto(urls.menu);
            await page.getByRole('button', { name: /^確定/ }).click();
            const dialog = page.getByRole('dialog');
            // ダイアログの地に、MUI のダークの白の重ね（背景画像）がない（重ねると文字との比が計算と合わなくなる）
            await expect.poll(() => dialog.evaluate((el) => getComputedStyle(el).backgroundImage)).toBe('none');
            await expectReadable(dialog.getByText('次の問題があるため、確定できません'));
            await expectReadable(dialog.getByText('原材料に『ヘーゼルナッツ』があります（ヘーゼルナッツ）').first());
            await expectReadable(dialog.getByText('別の商品に替えてください', { exact: false }).first());
            await captureScreenAs(page, `dark-sc-09-confirm-dialog-blocked-${projectName}`);
            await dialog.getByRole('button', { name: 'やめる' }).click();
            await expect(dialog).toHaveCount(0);
        } finally {
            await context.close();
        }
    }
};

/**
 * T5-07・T7-01 ヘーゼルナッツの場面を通しで確かめる（08 §2.2・HK-DEMO-03）：
 * ナッツ類を避けている → チラシを取り込む（その商品は 🟡 要確認 で献立に入る）→ パッケージを撮る
 * → 強調と 🔴 使えません（原材料に『ヘーゼルナッツ』があります）→ 献立一覧の最上部と日ごとのカードに警告。
 */
test.describe.serial('T5-07 ヘーゼルナッツの場面', () => {
    test('HK-DEMO-03 取り込み直後は 🟡 で献立に入り、パッケージで 🔴 になり、献立一覧の最上部に出る', async ({ page, browser }, testInfo) => {
        // 同じデータで2回通すと前提（未確認のショコラ）が変わるため、PC だけで通す
        test.skip(testInfo.project.name === 'mobile', 'PC だけで通す');
        // ダークの確認（PC とスマホ）を足したぶん、時間を長めにとる
        test.setTimeout(180_000);
        // 1. 来週の献立でチラシを取り込み、「ことりのさくさくショコラ」を新しい商品として確定する
        await page.goto('/menus');
        await page.getByRole('region', { name: /^来週 / }).getByRole('button', { name: /献立を作る|開く/ }).click();
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await page.getByRole('button', { name: '読み取る' }).click();
        await expect(page.getByText('読み取った商品 20件')).toBeVisible({ timeout: 30_000 });
        const chocolate = await openFlyerItemDetail(page, 'ことりのさくさくショコラ');
        await chocolate.getByLabel('新しい商品として登録').check();
        await chocolate.getByRole('button', { name: '確定する' }).click();
        await expect(page.getByText('「ことりのさくさくショコラ」を確定しました')).toBeVisible();

        // 2. 今週の商品では 🟡 要確認（チラシの情報だけ）で、献立に入れられる
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const listed = page.getByRole('region', { name: 'ことりのさくさくショコラ' });
        await expect(listed.getByText('🟡 要確認')).toBeVisible();

        // 3. 手で水曜に入れる（自分で組む）
        await page.getByRole('tab', { name: '献立案', exact: true }).click();
        const wednesday = page.getByRole('region', { name: /（水）の献立$/ });
        await wednesday.getByLabel('商品を足す').click();
        await page.getByRole('option', { name: /ことりのさくさくショコラ/ }).click();
        await wednesday.getByRole('button', { name: '足す' }).click();
        await expect(wednesday.getByText('ことりのさくさくショコラ')).toBeVisible();

        // 4. カードからアレルゲン確認へ進み、パッケージを撮って読み取る
        await wednesday.getByRole('link', { name: 'パッケージで確認する' }).first().click();
        await expect(page).toHaveURL(/\/products\/[0-9a-f-]+\/check/);
        const productUrl = new URL(page.url()).pathname.replace(/\/check$/, '');
        await page.getByTestId('file-input').setInputFiles(PACKAGE_IMAGE);
        await page.getByRole('button', { name: '読み取る', exact: true }).click();
        await expect(page.getByLabel('原材料の全文')).toHaveValue(/ヘーゼルナッ\nツペースト/, { timeout: 30_000 });

        // 5. 改行をまたいでも、当たった言葉を強調し、「含まない」にしても 🔴 が消えないことを示す
        await expect(page.getByText(/^原材料に、避ける品目の言葉があります：.*ヘーゼルナッツ/)).toBeVisible();
        await expect(page.getByText(/^原材料に.*『含まない』にしても、🔴 使えません の表示は消えません/)).toBeVisible();

        // 職員が全文から言葉を消しても、AI が読み取った全文に言葉があることを示し続ける
        await page.getByLabel('原材料の全文').fill('小麦粉（国内製造）、砂糖、全粉乳（一部に小麦・乳成分・大豆を含む）');
        await expect(page.getByText(/^AI が読み取った全文に、避ける品目の言葉があります：.*ヘーゼルナッツ/)).toBeVisible();
        await expect(page.getByRole('button', { name: 'パッケージで確認して記録' })).toBeDisabled();
        await page.getByRole('button', { name: /残りをすべて含まないにする/ }).click();
        await captureScreen(page, testInfo, 'sc-08-allergen-check');
        await page.getByRole('button', { name: 'パッケージで確認して記録' }).click();

        // 6. 記録した結果は 🔴 使えません で、使っている献立が目立つ
        await expect(page.getByText('記録しました')).toBeVisible();
        await expect(page.getByText('原材料に『ヘーゼルナッツ』があります（ヘーゼルナッツ）').first()).toBeVisible();
        await expect(page.getByText('今日以降の献立に、使えない商品が入っています', { exact: false })).toBeVisible();
        await expect(page.getByRole('link', { name: /（水）の献立で使われています/ })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-08-recorded');

        // 7. 献立一覧の最上部に警告が出て、そこから献立を開ける
        await page.goto('/menus');
        await expect(page.getByText('使えない商品が献立に入っています', { exact: false })).toBeVisible();
        await expect(page.getByText('「ことりのさくさくショコラ」原材料に『ヘーゼルナッツ』があります（ヘーゼルナッツ）')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-10-blocked-alert');

        // 8. 日ごとのカードにも 🔴 と「別の商品に替えてください」が出る
        await page.getByRole('link', { name: /（水）の献立/ }).first().click();
        const card = page.getByRole('region', { name: /（水）の献立$/ });
        await expect(card.getByText('🔴 使えません')).toBeVisible();
        await expect(card.getByText('別の商品に替えてください', { exact: false })).toBeVisible();

        // #4 ダークでも、🔴 の知らせ（献立一覧・商品詳細・確定のダイアログ）が読める（PC とスマホ。データは変えない）
        // 日ごとのカードから開いた献立（手順4 の献立案。?step=4 を含む）
        const { pathname, search } = new URL(page.url());
        const menuUrl = `${pathname}${search}`;
        await checkBlockedNoticesInDark(browser, testInfo, { product: productUrl, menu: menuUrl });

        // 9. 外しておく（あとのテストの前提を変えないため）
        await card.getByRole('button', { name: 'ことりのさくさくショコラを外す' }).click();
        await expect(card.getByText('ことりのさくさくショコラ')).toHaveCount(0);
    });
});
