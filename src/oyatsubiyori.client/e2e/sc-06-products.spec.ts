import path from 'node:path';
import { expect, test } from '@playwright/test';
import { captureScreen } from './helpers';

const PACKAGE_IMAGE = path.resolve(process.cwd(), '../../samples/packages/package-8104-back.png');

test.describe('SC-06・SC-07・SC-08 商品', () => {
    test('FR-PROD-01・03 一覧にアレルゲンの表示と理由が出て、🔴 で絞り込める', async ({ page }, testInfo) => {
        await page.goto('/products');
        await expect(page.getByRole('heading', { name: '商品一覧' })).toBeVisible();
        const blockedCard = page.getByRole('region', { name: 'ころころ ココナッツサブレ' });
        await expect(blockedCard.getByText('使えません', { exact: true })).toBeVisible();
        // 文字だけでなく、使えません のアイコンも出る（§6.1）
        await expect(blockedCard.getByTestId('allergen-icon-Blocked')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-06-products');

        await page.getByLabel('アレルゲンの表示').click();
        await page.getByRole('option', { name: '使えません', exact: true }).click();
        await expect(page).toHaveURL(/filter=Blocked/);
        await expect(page.getByRole('region', { name: 'しろくまの ヨーグルトゼリー' })).toHaveCount(0);
        await expect(page.getByRole('region', { name: 'ころころ ココナッツサブレ' })).toBeVisible();
    });

    test('FR-PROD-02・09 商品名で探せて、「使わない」商品は既定で隠れる', async ({ page }) => {
        await page.goto('/products');
        await expect(page.getByRole('region', { name: 'どんぐり きなこクッキー' })).toHaveCount(0);
        await page.getByLabel('「使わない」商品も出す').check();
        await expect(page.getByRole('region', { name: 'どんぐり きなこクッキー' })).toBeVisible();

        await page.getByLabel('商品名で探す').fill('ゼリー');
        await page.getByLabel('商品名で探す').press('Enter');
        await expect(page.getByRole('region', { name: 'しろくまの ヨーグルトゼリー' })).toBeVisible();
        await expect(page.getByRole('region', { name: 'ころころ ココナッツサブレ' })).toHaveCount(0);
    });

    test('SC-10 献立一覧の注意から、今日以降の献立で使うパッケージで確認する商品の一覧を開ける', async ({ page }) => {
        await page.goto('/menus');
        await page.getByRole('link', { name: '商品を確認する' }).click();
        await expect(page).toHaveURL(/filter=FuturePackage/);
        await expect(page.getByRole('heading', { level: 1, name: '商品一覧' })).toBeVisible();
        await expect(page.getByText('今日以降の献立で使う、パッケージで確認する商品')).toBeVisible();
        await expect(page.getByText(/^\d+件$/)).toBeVisible();
    });

    test('FR-PROD-07・SR-16 商品詳細に今の表示と、ステータスつきの確認の履歴が出る', async ({ page }, testInfo) => {
        await page.goto('/products');
        await page.getByRole('region', { name: 'うさぎのミミ いちごゼリー' }).getByRole('link').click();
        await expect(page.getByRole('heading', { name: 'うさぎのミミ いちごゼリー' })).toBeVisible();
        await expect(page.getByText(/前回の確認から\d+日たちました/)).toBeVisible();
        await expect(page.getByText('アレルゲン確認の履歴')).toBeVisible();
        await expect(page.getByText('確認済み', { exact: true }).first()).toBeVisible();
        await captureScreen(page, testInfo, 'sc-07-product-detail');
    });

    test('FR-PROD-04・08 基本情報とメモを保存できる（子どもの名前を書かない注意が出る）', async ({ page }) => {
        await page.goto('/products');
        await page.getByRole('region', { name: 'もぐもぐ おこめスナック' }).getByRole('link').click();
        await expect(page.getByText('子どもの名前は書かないでください（献立の提案のため AI に送ります）')).toBeVisible();
        const memo = `子どもに人気（${test.info().project.name}）`;
        await page.getByLabel('メモ').fill(memo);
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('商品を保存しました')).toBeVisible();
        await page.reload();
        await expect(page.getByLabel('メモ')).toHaveValue(memo);
    });

    test('SR-12・FR-PROD-06b 全文と品目がそろうまでパッケージでは記録できず、パッケージ以外なら 🟢 にならない', async ({ page }, testInfo) => {
        await page.goto('/products');
        await page.getByRole('region', { name: 'ぽかぽか さつまいもチップス' }).getByRole('link').click();
        await page.getByRole('link', { name: 'パッケージで確認する' }).click();
        const packageButton = page.getByRole('button', { name: 'パッケージで確認して記録' });
        await expect(packageButton).toBeDisabled();
        await page.getByLabel('原材料の全文').fill('さつまいも、植物油、砂糖');
        await expect(packageButton).toBeDisabled();
        await page.getByLabel('サイト', { exact: false }).check();
        await expect(page.getByText('パッケージ以外の情報では「使えます」にはなりません', { exact: false })).toBeVisible();
        // パッケージに戻すと、品目を決めるまで記録できない（SR-15）
        await page.getByLabel('パッケージ', { exact: true }).check();
        await expect(packageButton).toBeDisabled();
        await page.getByRole('button', { name: /残りをすべて含まないにする/ }).click();
        await expect(packageButton).toBeEnabled();
        await page.getByRole('group', { name: '卵を含むか' }).getByRole('button', { name: '未確定' }).click();
        await expect(packageButton).toBeDisabled();
        await page.getByLabel('サイト', { exact: false }).check();
        await captureScreen(page, testInfo, 'sc-08-other-source');
        await page.getByRole('button', { name: 'パッケージ以外の情報として記録' }).click();
        await expect(page.getByText('記録しました')).toBeVisible();
        await expect(page.getByText('要確認', { exact: true }).first()).toBeVisible();
    });

    test('FR-PROD-05 パッケージの画像を準備している間は「読み取る」を押せず、準備が終わると押せる', async ({ page }) => {
        // 画像の変換（createImageBitmap）を、holdImagePrep() から releaseImagePrep() までの間だけ止められるようにする
        await page.addInitScript(() => {
            const original = window.createImageBitmap.bind(window);
            let gate: Promise<void> = Promise.resolve();
            let release = () => {};
            Object.assign(window, {
                holdImagePrep: () => {
                    gate = new Promise<void>((resolve) => {
                        release = resolve;
                    });
                },
                releaseImagePrep: () => release(),
            });
            window.createImageBitmap = (async (...args: Parameters<typeof window.createImageBitmap>) => {
                await gate;
                return original(...args);
            }) as typeof window.createImageBitmap;
        });
        type ImagePrepControl = { holdImagePrep: () => void; releaseImagePrep: () => void };
        const thumbnails = page.getByRole('button', { name: /を外す$/ });

        await page.goto('/products');
        await page.getByRole('region', { name: 'ぽかぽか さつまいもチップス' }).getByRole('link').click();
        await page.getByRole('link', { name: 'パッケージで確認する' }).click();
        const readButton = page.getByRole('button', { name: '読み取る', exact: true });

        // 1枚目は準備し終えて、読み取れる状態にする
        await page.getByTestId('file-input').setInputFiles(PACKAGE_IMAGE);
        await expect(thumbnails).toHaveCount(1);
        await expect(readButton).toBeEnabled();

        // 2枚目を準備している間は、1枚目があっても読み取れない
        await page.evaluate(() => (window as unknown as ImagePrepControl).holdImagePrep());
        await page.getByTestId('file-input').setInputFiles(PACKAGE_IMAGE);
        await expect(page.getByText('画像を準備しています…')).toBeVisible();
        await expect(readButton).toBeDisabled();

        await page.evaluate(() => (window as unknown as ImagePrepControl).releaseImagePrep());
        await expect(page.getByText('画像を準備しています…')).toBeHidden();
        await expect(thumbnails).toHaveCount(2);
        await expect(readButton).toBeEnabled();
    });
});
