import path from 'node:path';
import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import { captureScreen } from './helpers';

const FLYER_IMAGE = path.resolve(process.cwd(), '../../samples/flyers/flyer-01.png');

/** 献立一覧から、指定した週（今週・来週・再来週）の献立を開く */
const openWeek = async (page: Page, label: '来週' | '再来週') => {
    await page.goto('/menus');
    const card = page.getByRole('region', { name: new RegExp('^' + label + ' ') });
    await card.getByRole('button', { name: /献立を作る|開く/ }).click();
    await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
    await expect(page.getByRole('heading', { level: 1, name: /^献立作成/ })).toBeVisible();
};

test.describe.serial('SC-09 献立作成', () => {
    test('FR-FLYER-01・05・06a チラシを読み取り、「同じ商品」をまとめて確定できる', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await page.getByText(/1ページを半分ずつ|取り込み済みです|読み取った商品/).first().waitFor();
        const reimport = page.getByRole('button', { name: 'チラシを取り込み直す' });
        if (await reimport.first().isVisible()) {
            await reimport.first().click();
        }
        await expect(page.getByText('1ページを半分ずつ近づけて撮ってください', { exact: false })).toBeVisible();
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await expect(page.getByRole('img', { name: 'flyer-01.png' })).toBeVisible();
        await page.getByRole('button', { name: '読み取る' }).click();
        await expect(page.getByText('読み取った商品 20件')).toBeVisible({ timeout: 30_000 });
        await expect(page.getByText(/確定していない \d+件は、今週の商品に入りません/)).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-flyer');

        await page.getByRole('button', { name: '「同じ商品」をまとめて確定' }).click();
        await expect(page.getByText(/「同じ商品」の \d+件を確定しました/)).toBeVisible();
        await expect(page.getByText('確定済み：登録済みの商品と同じ').first()).toBeVisible();
    });

    test('FR-FLYER-04a・SR-19 表記のない商品は「表記なし」と示し、新しい商品として確定すると今週の商品に 🟡 要確認 で入る', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const card = page.getByRole('region', { name: 'ことりのピピ ラムネ' });
        await expect(card.getByText('表記なし')).toBeVisible();
        await card.getByRole('button', { name: /確かめて確定|選び直す/ }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText('表記なし（読み落としの可能性があります', { exact: false })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-confirm-dialog');
        await dialog.getByLabel('新しい商品として登録').check();
        await dialog.getByRole('button', { name: '確定する' }).click();
        await expect(page.getByText('「ことりのピピ ラムネ」を確定しました')).toBeVisible();

        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const listed = page.getByRole('region', { name: 'ことりのピピ ラムネ' });
        await expect(listed.getByText('🟡 要確認')).toBeVisible();
        await expect(listed.getByText('チラシにアレルゲンの表記がありません')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step2-listed');
    });

    test('FR-MENU-07a 「今週は使わない」は切り替えるたびに保存される', async ({ page }) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const card = page.getByRole('region', { name: 'きらきら 星のラムネ' });
        const toggle = card.getByLabel('今週は使わない');
        const before = await toggle.isChecked();
        const saved = page.waitForResponse((response) => response.url().includes('/api/menus/setExcluded') && response.ok());
        await toggle.click();
        await saved;
        await page.reload();
        await expect(page.getByRole('region', { name: 'きらきら 星のラムネ' }).getByLabel('今週は使わない')).toBeChecked({ checked: !before });
    });

    test('SR-14 原材料に探す言葉がある商品は、今週の商品で 🔴 使えません と理由が出る', async ({ page }) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const card = page.getByRole('region', { name: 'ころころ ココナッツサブレ' });
        await expect(card.getByText('🔴 使えません')).toBeVisible();
        await expect(card.getByText(/『ココナッツ』があります/)).toBeVisible();
    });

    test('FR-MENU-01・02 要望を入れて「献立を作る」と、日ごとの献立案と理由が出る', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '要望の入力', exact: true }).click();
        await expect(page.getByText('子どもの名前は書かないでください', { exact: false })).toBeVisible();
        await expect(page.getByText('要望の文章では、避ける品目（アレルギー）の条件は変えられません', { exact: false })).toBeVisible();
        await page.getByLabel('今週の要望').fill('しょっぱいものとゼリー系を合わせて出したい');
        await captureScreen(page, testInfo, 'sc-09-step3-request');
        await page.getByRole('button', { name: '献立を作る' }).click();
        const recreate = page.getByRole('button', { name: '作り直す' });
        if (await recreate.isVisible({ timeout: 1000 }).catch(() => false)) {
            await recreate.click();
        }
        await expect(page.getByText('要望をこう理解しました')).toBeVisible({ timeout: 30_000 });
        await expect(page.getByText('しょっぱいものとゼリー系を合わせて出したい').first()).toBeVisible();
        await expect(page.getByText(/週の実績 [\d,]+円 \/ 週の予算/)).toBeVisible();
        await expect(page.getByText('理由').first()).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step4-board');
    });

    test('FR-MENU-28・30 予算に届かないときだけ「予算に合わせる」が出て、案を選ぶと反映される', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await expect(page.getByText(/週の実績 [\d,]+円 \/ 週の予算/)).toBeVisible();
        await expect(page.getByRole('button', { name: /^確定/ })).toBeEnabled();
        const fit = page.getByRole('button', { name: '予算に合わせる' });
        if (!(await fit.isVisible())) {
            test.skip(true, '今の献立は予算に届いているため、ボタンが出ない（D-67）');
        }
        await fit.click();
        const dialog = page.getByRole('dialog');
        const option = dialog.getByRole('button', { name: /週の実績/ }).first();
        await expect(option).toBeVisible({ timeout: 30_000 });
        await captureScreen(page, testInfo, 'sc-09-step4-budget-fit');
        await option.click();
        await expect(dialog).toBeHidden();
    });

    test('FR-MENU-24 パック数を変えると、金額をサーバーが計算し直す', async ({ page }) => {
        await openWeek(page, '再来週');
        const packInput = page.getByLabel(/のパック数$/).first();
        await expect(packInput).toBeVisible();
        const current = Number(await packInput.inputValue());
        const line = page.getByText(/円 × \d+パック = [\d,]+円/).first();
        const match = /([\d,]+)円 × (\d+)パック/.exec((await line.textContent()) ?? '');
        const unitPrice = Number((match?.[1] ?? '0').replace(/,/g, ''));
        await packInput.fill(String(current + 1));
        await packInput.blur();
        // 金額（単価 × パック数）はサーバーが計算し直した値が出る
        const expected = (unitPrice * (current + 1)).toLocaleString('ja-JP');
        await expect(page.getByText(`× ${current + 1}パック = ${expected}円`).first()).toBeVisible();
    });

    test('FR-MENU-21 チャットで依頼すると、組み直して返答が出る', async ({ page }) => {
        await openWeek(page, '再来週');
        const isMobile = test.info().project.name === 'mobile';
        if (isMobile) {
            await page.getByRole('tab', { name: 'チャット' }).click();
        }
        await expect(page.getByText('子どもの名前は書かないでください（AI に送ります）')).toBeVisible();
        await page.getByLabel('チャットの依頼').fill('水曜はせんべいにしてください');
        await page.getByRole('button', { name: '送る' }).click();
        await expect(page.getByText('ご依頼に合わせて、商品を組み直しました', { exact: false })).toBeVisible({ timeout: 30_000 });
    });

    test('FR-MENU-32・SR-04 確定のダイアログで 🟡 の商品にチェックを付けると確定できる', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await page.getByRole('button', { name: /^確定/ }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText('献立を確定する')).toBeVisible();
        const confirmButton = dialog.getByRole('button', { name: '確定する' });
        const checkboxes = dialog.getByRole('checkbox');
        await expect(dialog.getByText(/確定できます|要確認の商品|確定できません/).first()).toBeVisible();
        const count = await checkboxes.count();
        const unchecked = dialog.getByRole('checkbox', { checked: false, disabled: false });
        if ((await unchecked.count()) > 0) {
            await expect(confirmButton).toBeDisabled();
            await expect(dialog.getByText('届いたらパッケージで', { exact: false }).first()).toBeVisible();
        }
        await expect(dialog.getByText('メーカーへの確認が必要', { exact: false })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-confirm-dialog');
        for (let i = 0; i < count; i++) {
            const checkbox = checkboxes.nth(i);
            if (!(await checkbox.isDisabled())) {
                await checkbox.check();
            }
        }
        await confirmButton.click();
        await expect(page.getByText('献立を確定しました')).toBeVisible();
        await expect(page.getByText(/^確定済み（\d/)).toBeVisible();
    });
});
