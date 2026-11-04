import path from 'node:path';
import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

const FLYER_IMAGE = path.resolve(process.cwd(), '../../samples/flyers/flyer-01.png');
const PACKAGE_IMAGE = path.resolve(process.cwd(), '../../samples/packages/package-8104-back.png');

/**
 * T5-07・T7-01 ヘーゼルナッツの場面を通しで確かめる（08 §2.2・HK-DEMO-03）：
 * ナッツ類を避けている → チラシを取り込む（その商品は 🟡 要確認 で献立に入る）→ パッケージを撮る
 * → 強調と 🔴 使えません（原材料に『ヘーゼルナッツ』があります）→ 献立一覧の最上部と日ごとのカードに警告。
 */
test.describe.serial('T5-07 ヘーゼルナッツの場面', () => {
    test('HK-DEMO-03 取り込み直後は 🟡 で献立に入り、パッケージで 🔴 になり、献立一覧の最上部に出る', async ({ page }, testInfo) => {
        // 同じデータで2回通すと前提（未確認のショコラ）が変わるため、PC だけで通す
        test.skip(testInfo.project.name === 'mobile', 'PC だけで通す');
        // 1. 来週の献立でチラシを取り込み、「ことりのさくさくショコラ」を新しい商品として確定する
        await page.goto('/menus');
        await page.getByRole('region', { name: /^来週 / }).getByRole('button', { name: /献立を作る|開く/ }).click();
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await page.getByRole('button', { name: '読み取る' }).click();
        await expect(page.getByText('読み取った商品 20件')).toBeVisible({ timeout: 30_000 });
        const chocolate = page.getByRole('region', { name: 'ことりのさくさくショコラ' });
        await chocolate.getByRole('button', { name: '確かめて確定' }).click();
        await page.getByRole('dialog').getByLabel('新しい商品として登録').check();
        await page.getByRole('dialog').getByRole('button', { name: '確定する' }).click();
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

        // 9. 外しておく（あとのテストの前提を変えないため）
        await card.getByRole('button', { name: 'ことりのさくさくショコラを外す' }).click();
        await expect(card.getByText('ことりのさくさくショコラ')).toHaveCount(0);
    });
});
