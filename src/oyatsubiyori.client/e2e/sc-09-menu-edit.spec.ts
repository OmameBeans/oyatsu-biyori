import { readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { captureScreen, captureScreenAs, listedProductItem, openFlyerItemDetail } from './helpers';

const FLYER_IMAGE = path.resolve(process.cwd(), '../../samples/flyers/flyer-01.png');
const FLYER_PDF = path.resolve(process.cwd(), '../../samples/flyers/flyer-01.pdf');

/** 文字列を、正規表現の中でそのままの文字として使えるようにする */
const escapeRegExp = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** 献立一覧から、指定した週（今週・来週・再来週）の献立を開く */
const openWeek = async (page: Page, label: '来週' | '再来週') => {
    await page.goto('/menus?view=list');
    const card = page.getByRole('region', { name: new RegExp('^' + label + ' ') });
    await card.getByRole('button', { name: /献立を作る|開く/ }).click();
    await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
    await expect(page.getByRole('heading', { level: 1, name: /^献立作成/ })).toBeVisible();
};

/** 手順1（チラシ取り込み）の撮影の欄を開く。再来週はこのファイルのテストでは読み取らないので、撮影の欄が出る */
const openFlyerPicker = async (page: Page) => {
    await openWeek(page, '再来週');
    await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
    await expect(page.getByRole('region', { name: 'チラシを取り込む' })).toBeVisible();
};

/**
 * ドラッグ＆ドロップをイベントで送る。Playwright の dragTo は、落とす先がスクロールしないと見えない位置にあると
 * ドラッグを始められないため（実際のブラウザは端に近づけるとスクロールする）、同じ DataTransfer で
 * dragstart → dragover → drop を順に送る（落としたあと元の要素は動くか消えるので、dragend は送らない）。
 */
const dragByEvents = async (page: Page, source: Locator, target: Locator) => {
    const dataTransfer = await page.evaluateHandle(() => new DataTransfer());
    await source.dispatchEvent('dragstart', { dataTransfer });
    await target.dispatchEvent('dragenter', { dataTransfer });
    await target.dispatchEvent('dragover', { dataTransfer });
    await target.dispatchEvent('drop', { dataTransfer });
};

/** 要素の外側にある、縦にスクロールしている枠（ページ全体を含む）の数 */
const countScrollingAncestors = (locator: Locator) =>
    locator.evaluate((el) => {
        let count = 0;
        for (let node = el.parentElement; node; node = node.parentElement) {
            const isPage = node === document.documentElement;
            const canScroll = isPage || /auto|scroll/.test(getComputedStyle(node).overflowY);
            if (canScroll && node.scrollHeight > node.clientHeight + 1) {
                count++;
            }
        }
        return count;
    });

/** 要素の中の文字が描かれている範囲の左右の端（ラベルの要素は幅いっぱいに広がるので、文字の範囲を測る） */
const textEdgesOf = (locator: Locator) =>
    locator.evaluate((el) => {
        const range = document.createRange();
        range.selectNodeContents(el);
        const rect = range.getBoundingClientRect();
        return { left: rect.left, right: rect.right };
    });

test.describe('SC-09 献立作成 手順1 の配置', () => {
    test('FR-FLYER-01 PC では手順1がページの高さに収まり、10枚追加しても追加したチラシの一覧の中だけがスクロールする', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name === 'mobile', 'スマホはページ全体をスクロールする');
        await page.setViewportSize({ width: 1920, height: 1080 });
        await openFlyerPicker(page);
        const card = page.getByRole('region', { name: 'チラシを取り込む' });
        const empty = card.getByText('まだ追加されていません');
        await expect(empty).toBeVisible();
        await expect(page.getByRole('button', { name: '読み取る' })).toBeDisabled();
        await expect(page.getByText('チラシを追加すると読み取れます')).toBeVisible();
        expect(await countScrollingAncestors(empty)).toBe(0);
        await captureScreenAs(page, 'sc-09-step1-picker-1920x1080');

        // タイトルの行と取り込みのカードの左右の端は、ステッパーの最初と最後のラベルの端にそろう（ずれは 2px まで）
        const heading = page.getByRole('heading', { level: 1, name: /^献立作成/ });
        const leave = page.getByRole('button', { name: '作成を中断して一覧へ' });
        const misalignment = async () => {
            const firstLabel = await textEdgesOf(page.getByText('チラシ取り込み', { exact: true }));
            const lastLabel = await textEdgesOf(page.getByText('献立案', { exact: true }));
            const leaveBox = await leave.boundingBox();
            const cardBox = await card.boundingBox();
            if (!leaveBox || !cardBox) {
                return Infinity;
            }
            return Math.max(
                Math.abs((await textEdgesOf(heading)).left - firstLabel.left),
                Math.abs(leaveBox.x + leaveBox.width - lastLabel.right),
                Math.abs(cardBox.x - firstLabel.left),
                Math.abs(cardBox.x + cardBox.width - lastLabel.right),
            );
        };
        await expect.poll(misalignment).toBeLessThanOrEqual(2);

        await page.setViewportSize({ width: 1440, height: 900 });
        await expect(card).toBeVisible();
        await expect.poll(misalignment).toBeLessThanOrEqual(2);
        expect(await countScrollingAncestors(empty)).toBe(0);

        const flyer = readFileSync(FLYER_IMAGE);
        await page.getByTestId('file-input').setInputFiles(
            Array.from({ length: 10 }, (_, index) => ({ name: `flyer-${index + 1}.png`, mimeType: 'image/png', buffer: flyer })),
        );
        await expect(card.getByText('（10 / 10枚）')).toBeVisible({ timeout: 30_000 });
        await expect(card.getByRole('img', { name: 'flyer-10.png' })).toBeAttached();
        await expect(page.getByRole('button', { name: '読み取る' })).toBeEnabled();
        const list = card.getByRole('list', { name: '追加したチラシの一覧' });
        expect(await list.evaluate((el) => el.scrollHeight > el.clientHeight)).toBe(true);
        expect(await countScrollingAncestors(list)).toBe(0);
        await captureScreenAs(page, 'sc-09-step1-picker-1440x900-10');
    });

    test('FR-FLYER-01 画像を準備している間は「読み取る」を押せず、準備が終わると押せる', async ({ page }) => {
        // PDF のページ分けに使う worker の読み込みを止めて、準備中のままにする
        let releaseWorker = () => {};
        const workerGate = new Promise<void>((resolve) => {
            releaseWorker = resolve;
        });
        await page.route(/pdf\.worker/, async (route) => {
            await workerGate;
            await route.continue();
        });

        await openFlyerPicker(page);
        const added = page.getByRole('list', { name: '追加したチラシの一覧' }).getByRole('listitem');
        // 1枚目（画像）は準備し終えて、読み取れる状態にする
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await expect(added).toHaveCount(1);
        await expect(page.getByRole('button', { name: '読み取る' })).toBeEnabled();

        // PDF を準備している間は、1枚目があっても読み取れない
        await page.getByTestId('file-input').setInputFiles(FLYER_PDF);
        await expect(page.getByText('画像を準備しています…')).toBeVisible();
        await expect(page.getByRole('button', { name: '読み取る' })).toBeDisabled();

        releaseWorker();
        await expect(page.getByText('画像を準備しています…')).toBeHidden();
        await expect(added.nth(1)).toBeVisible();
        await expect(page.getByRole('button', { name: '読み取る' })).toBeEnabled();
    });

    test('SC-09 「作成を中断して一覧へ」は下書きとして残ることを知らせ、読み取っていない画像があるときは消えることを確かめる', async ({ page }, testInfo) => {
        await openFlyerPicker(page);
        await captureScreen(page, testInfo, 'sc-09-step1-picker');
        await page.getByRole('button', { name: '作成を中断して一覧へ' }).click();
        await expect(page).toHaveURL(/\/menus$/);
        await expect(page.getByText(/下書きとして残っています|ここまでの内容は保存されています/)).toBeVisible();

        await openFlyerPicker(page);
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await expect(page.getByRole('img', { name: 'flyer-01.png' })).toBeVisible();
        await page.getByRole('button', { name: '作成を中断して一覧へ' }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText('まだ読み取っていないチラシの画像は消えます', { exact: false })).toBeVisible();
        await dialog.getByRole('button', { name: 'やめる' }).click();
        await expect(dialog).toBeHidden();
        await expect(page.getByRole('img', { name: 'flyer-01.png' })).toBeVisible();

        await page.getByRole('button', { name: '作成を中断して一覧へ' }).click();
        await page.getByRole('dialog').getByRole('button', { name: '消して一覧へ' }).click();
        await expect(page).toHaveURL(/\/menus$/);

        // 手順4の送っていないチャットの依頼も、消えることを確かめる
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '献立案', exact: true }).click();
        if (testInfo.project.name === 'mobile') {
            await page.getByRole('tab', { name: 'チャット' }).click();
        }
        await page.getByLabel('チャットの依頼').fill('水曜はせんべいにしてください');
        await page.getByRole('button', { name: '作成を中断して一覧へ' }).click();
        await expect(page.getByRole('dialog').getByText('送っていないチャットの依頼は消えます', { exact: false })).toBeVisible();
        await page.getByRole('dialog').getByRole('button', { name: 'やめる' }).click();
        await expect(page.getByLabel('チャットの依頼')).toHaveValue('水曜はせんべいにしてください');
    });
});

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
        // 読み取りが終わってから結果が出るまでに、撮影の欄が一瞬でも出ないこと（読み直す前の古い状態で描かない）
        await page.evaluate(() => {
            const state = { sawReading: false, pickerAfterReading: false };
            (window as unknown as { flyerFlash: typeof state }).flyerFlash = state;
            const hasHeading = (text: string) => Array.from(document.querySelectorAll('h2')).some((h) => h.textContent === text);
            new MutationObserver(() => {
                if (!hasHeading('チラシを取り込む')) {
                    state.sawReading = true;
                } else if (state.sawReading) {
                    state.pickerAfterReading = true;
                }
            }).observe(document.body, { childList: true, subtree: true });
        });
        await page.getByRole('button', { name: '読み取る' }).click();
        await expect(page.getByText('読み取った商品 20件')).toBeVisible({ timeout: 30_000 });
        expect(await page.evaluate(() => (window as unknown as { flyerFlash: { pickerAfterReading: boolean } }).flyerFlash.pickerAfterReading)).toBe(false);
        await expect(page.getByText(/確定していない \d+件は、今週の商品に入りません/)).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-flyer');

        await page.getByRole('button', { name: '「同じ商品」をまとめて確定' }).click();
        await expect(page.getByText(/「同じ商品」の \d+件を確定しました/)).toBeVisible();
        await expect(page.getByRole('row', { name: /確定済み .*登録済みの商品と同じ/ }).first()).toBeVisible();
    });

    test('FR-FLYER-04a・SR-19 表記のない商品は「表記なし」と示し、新しい商品として確定すると今週の商品に 🟡 要確認 で入る', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const row = page.getByRole('row', { name: /ことりのピピ ラムネ/ }).first();
        // 「表記なし」のチップと、状態の下の理由（何を確かめるか）
        await expect(row.getByText('表記なし', { exact: true }).first()).toBeVisible();
        // 状態の下に、何を確かめるのか（表記なし）が出る。確定済みの行にも出る
        await expect(row.getByTitle(/表記なし/).first()).toBeVisible();
        const detail = await openFlyerItemDetail(page, 'ことりのピピ ラムネ');
        // 行を開くと、確かめることと、することが出る
        await expect(detail.getByRole('alert', { name: '確かめること' }).getByText('チラシでこの商品のアレルゲン表記を探してください', { exact: false })).toBeVisible();
        await expect(detail.getByText('表記なし（読み落としの可能性があります', { exact: false })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-confirm-detail');
        await detail.getByLabel('新しい商品として登録').check();
        await detail.getByRole('button', { name: /^確定(する|し直す)$/ }).click();
        await expect(page.getByText('「ことりのピピ ラムネ」を確定しました')).toBeVisible();

        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const listed = listedProductItem(page, 'ことりのピピ ラムネ');
        await expect(listed.getByText('要確認', { exact: true })).toBeVisible();
        await expect(listed.getByTestId('allergen-icon-Warning')).toBeVisible();
        await expect(listed.getByText('チラシにアレルゲンの表記がありません')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step2-listed');
    });

    test('FR-FLYER-06c 読み取った商品は状態の順に並び、初めは閉じていて、確定すると確定済みは下へ移って次の未確定の行が開く', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const grid = page.getByRole('grid', { name: '読み取った商品' });
        await expect(grid).toBeVisible();
        const statusesInOrder = async () =>
            (await grid.getByRole('row').allInnerTexts())
                .map((text) => text.trim().split(/\s/)[0])
                .filter((status) => ['警告あり', '要確認', '未確定', '確定済み'].includes(status));
        const expectSortedByStatus = async () => {
            const statuses = await statusesInOrder();
            const rank = (status: string) => ['警告あり', '要確認', '未確定', '確定済み'].indexOf(status);
            expect(statuses.map(rank)).toEqual([...statuses.map(rank)].sort((a, b) => a - b));
            return statuses;
        };
        const before = await expectSortedByStatus();
        expect(before.length).toBe(20);

        // 初めはどの行も開いていない
        await expect(grid.getByRole('button', { name: /の詳細を閉じる$/ })).toHaveCount(0);
        test.skip(before[0] === '確定済み', '未確定の商品が残っていない');

        // 一番上（未確定のもの）を開いて「取り込まない」で確定すると、次の未確定の行が開く
        const firstToggle = grid.getByRole('button', { name: /の詳細を開く$/ }).first();
        const openedName = (await firstToggle.getAttribute('aria-label'))!.match(/^「(.+)」の詳細を開く$/)![1];
        await firstToggle.click();
        const detail = page.getByRole('region', { name: `「${openedName}」を確かめる` });
        await detail.getByLabel('取り込まない（おやつに使わない）').check();
        await detail.getByRole('button', { name: /^確定(する|し直す)$/ }).click();
        await expect(page.getByText(`「${openedName}」を確定しました`)).toBeVisible();
        await expect(grid.getByRole('row', { name: new RegExp('確定済み .*' + escapeRegExp(openedName)) })).toBeVisible();
        const after = await expectSortedByStatus();
        expect(after.filter((status) => status === '確定済み').length).toBe(before.filter((status) => status === '確定済み').length + 1);
        const nextExpanded = grid.getByRole('button', { name: /の詳細を閉じる$/ });
        if (after.some((status) => status !== '確定済み')) {
            await expect(nextExpanded).toHaveCount(1);
            await expect(nextExpanded).not.toHaveAttribute('aria-label', `「${openedName}」の詳細を閉じる`);
        }
        await captureScreen(page, testInfo, 'sc-09-step1-next-opened');

        // タブで絞り込むと、その状態の行だけが出る
        await page.getByRole('tab', { name: /^確定済み \d+$/ }).click();
        expect(new Set(await statusesInOrder())).toEqual(new Set(['確定済み']));
        await page.getByRole('tab', { name: /^すべて \d+$/ }).click();
        expect((await statusesInOrder()).length).toBe(20);
    });

    test('FR-FLYER-06・06c 未確定の商品を1件ずつ確かめられ、確定すると次の商品へ進み、表では確定済みになる', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const grid = page.getByRole('grid', { name: '読み取った商品' });
        await expect(grid).toBeVisible();
        const start = page.getByRole('button', { name: /^未確定を1件ずつ確かめる（\d+件）$/ });
        test.skip((await start.count()) === 0, '未確定の商品が残っていない');
        const total = Number((await start.innerText()).match(/（(\d+)件）/)![1]);
        await start.click();
        const dialog = page.getByRole('dialog', { name: /未確定の商品を1件ずつ確かめる/ });
        await expect(dialog.getByText(`1 / ${total}件目`, { exact: false })).toBeVisible();
        await expect(dialog.getByRole('button', { name: '前へ' })).toBeDisabled();
        if (total >= 2) {
            await dialog.getByRole('button', { name: 'あとで（次へ）' }).click();
            await expect(dialog.getByText(`2 / ${total}件目`, { exact: false })).toBeVisible();
            await dialog.getByRole('button', { name: '前へ' }).click();
            await expect(dialog.getByText(`1 / ${total}件目`, { exact: false })).toBeVisible();
        }
        const firstName = (await dialog.getByRole('heading', { level: 3 }).innerText()).trim();
        await captureScreen(page, testInfo, 'sc-09-step1-review');

        // 決めるのはボタンを押したときだけ。確定すると次の未確定の商品へ進む
        const form = dialog.getByRole('region', { name: `「${firstName}」を確かめる` });
        await form.getByLabel('取り込まない（おやつに使わない）').check();
        await form.getByRole('button', { name: /^確定(する|し直す)$/ }).click();
        await expect(page.getByText(`「${firstName}」を確定しました`)).toBeVisible();
        if (total >= 2) {
            await expect(dialog.getByRole('heading', { level: 3 })).not.toHaveText(firstName);
        } else {
            await expect(dialog.getByText('すべて確定しました')).toBeVisible();
        }
        await dialog.getByRole('button', { name: '表に戻る' }).first().click();
        await expect(dialog).toHaveCount(0);
        await expect(grid.getByRole('row', { name: new RegExp('確定済み .*' + escapeRegExp(firstName)) })).toBeVisible();
    });

    test('FR-MENU-07a 「今週使う」は初めはオンで、切り替えるたびに保存される', async ({ page }) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const card = listedProductItem(page, 'きらきら 星のラムネ');
        const toggle = card.getByLabel('今週使う');
        const before = await toggle.isChecked();
        const saved = page.waitForResponse((response) => response.url().includes('/api/menus/setExcluded') && response.ok());
        await toggle.click();
        await saved;
        await page.reload();
        await expect(listedProductItem(page, 'きらきら 星のラムネ').getByLabel('今週使う')).toBeChecked({ checked: !before });
    });

    test('SR-14 原材料に探す言葉がある商品は、今週の商品で 🔴 使えません と理由が出る', async ({ page }) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        const card = listedProductItem(page, 'ころころ ココナッツサブレ');
        await expect(card.getByText('使えません', { exact: true })).toBeVisible();
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
        await expect(page.getByText(/実績 [\d,]+円 \/ 予算/)).toBeVisible();
        await expect(page.getByText('理由').first()).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step4-board');

        // 「要望をこう理解しました」は折りたためる（見出しは残る）
        const hint = page.getByText('人数・種別・見出しは、日ごとのカードの「詳細設定」で直せます。');
        await expect(hint).toBeVisible();
        await page.getByRole('button', { name: '要望の読み取りを折りたたむ' }).click();
        await expect(hint).toBeHidden();
        await expect(page.getByText('要望をこう理解しました')).toBeVisible();
        await page.getByRole('button', { name: '要望の読み取りを開く' }).click();
        await expect(hint).toBeVisible();
    });

    test('SR-04 上部の「確かめること」に、日ごとのカードの 🔴 使えません・🟡 要確認 の件数がまとめて出る（画面の外の日も数える）', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        if (testInfo.project.name === 'mobile') {
            await page.getByRole('tab', { name: '献立', exact: true }).click();
        }
        const days = page.getByRole('region', { name: /の献立$/ });
        await expect(days.first()).toBeVisible();
        const summary = page.getByRole('region', { name: '確かめること' });
        for (const label of ['使えません', '要確認']) {
            // 日ごとのカードの商品に付いた表示の数（横にスクロールしないと見えない日も DOM にはある）
            const count = await days.getByText(label, { exact: true }).count();
            if (count === 0) {
                await expect(summary.getByText(new RegExp(`^${label} \\d+件`))).toHaveCount(0);
            } else {
                await expect(summary.getByText(new RegExp(`^${label} ${count}件（`))).toBeVisible();
            }
        }
    });

    test('FR-COND-02 手順3の「いつもの条件」の「変更」からダイアログで条件を変えられ、この週の予算がすぐ反映される', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await page.getByRole('tab', { name: '要望の入力', exact: true }).click();
        const budgetLine = page.getByText(/^週の予算：/);
        const before = (await budgetLine.textContent()) ?? '';
        const original = before.replace(/[^\d]/g, '');
        await page.getByRole('button', { name: 'いつもの条件を変更' }).click();
        const dialog = page.getByRole('dialog', { name: 'いつもの条件を変える' });
        await expect(dialog.getByRole('tab', { name: '人数・予算・重複' })).toBeVisible();
        await expect(dialog.getByRole('tab', { name: '避ける品目' })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step3-conditions-dialog');

        // この週の予算を変えて、手順3の表示に出ることを確かめてから元に戻す（ほかのテストに響かないように）
        const field = dialog.getByLabel('この週の予算（税込）');
        await field.fill('12345');
        await dialog.getByRole('button', { name: '保存する' }).click();
        await expect(budgetLine).toHaveText(/12,345円/);
        await field.fill(original);
        await dialog.getByRole('button', { name: '保存する' }).click();
        await expect(budgetLine).toHaveText(before);

        await dialog.getByRole('tab', { name: '避ける品目' }).click();
        await expect(dialog.getByText(/いま避けている品目：/)).toBeVisible();
        await dialog.getByRole('button', { name: '閉じる' }).click();
        await expect(dialog).toBeHidden();
    });

    test('FR-MENU-28・30 予算に届かないときだけ「予算に合わせる」が出て、案を選ぶと反映される', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await expect(page.getByText(/実績 [\d,]+円 \/ 予算/)).toBeVisible();
        await expect(page.getByRole('button', { name: '確定して保存', exact: true })).toBeEnabled();
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
        // 商品のカードの「個数設定」から、ダイアログでパック数を直す
        const card = page.getByRole('region', { name: /の献立$/ }).first();
        const line = card.getByText(/^[\d,]+円 × \d+パック$/).first();
        await expect(line).toBeVisible();
        const match = /([\d,]+)円 × (\d+)パック/.exec((await line.textContent()) ?? '');
        const unitPrice = Number((match?.[1] ?? '0').replace(/,/g, ''));
        const current = Number(match?.[2] ?? '0');
        await card.getByRole('button', { name: /の個数設定$/ }).first().click();
        const dialog = page.getByRole('dialog', { name: /の個数$/ });
        const packInput = dialog.getByLabel('買うパック数');
        await expect(packInput).toHaveValue(String(current));
        await packInput.fill(String(current + 1));
        await dialog.getByRole('button', { name: '保存する' }).click();
        await expect(dialog).toBeHidden();
        // 金額（単価 × パック数）はサーバーが計算し直した値が出る
        const expected = (unitPrice * (current + 1)).toLocaleString('ja-JP');
        await expect(card.getByText(`${match?.[1]}円 × ${current + 1}パック`).first()).toBeVisible();
        await expect(card.getByText(`${expected}円`, { exact: true }).first()).toBeVisible();
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
        // 返答の Markdown は箇条書きとして出る（記号のまま出さない。D-91）
        const log = page.getByRole('log', { name: 'チャットの履歴' });
        await expect(log.getByRole('listitem').filter({ hasText: '変えたところ：日ごとの商品' })).toBeVisible();
        await expect(log).not.toContainText('**');
    });

    test('D-91 チャットで質問すると、献立を変えずに Markdown で答え、送った質問は入力欄から履歴に移る', async ({ page }) => {
        await openWeek(page, '再来週');
        const isMobile = test.info().project.name === 'mobile';
        if (isMobile) {
            await page.getByRole('tab', { name: 'チャット' }).click();
        }
        const input = page.getByLabel('チャットの依頼');
        await input.fill('月曜のおせんべいを選んだのはなぜ？');
        await page.getByRole('button', { name: '送る' }).click();
        const log = page.getByRole('log', { name: 'チャットの履歴' });
        await expect(input).toHaveValue('');
        await expect(log.getByText('月曜のおせんべいを選んだのはなぜ？')).toBeVisible();
        await expect(log.getByText('献立は変えていません。')).toBeVisible({ timeout: 30_000 });
        await expect(log.getByRole('listitem').filter({ hasText: 'しょっぱいもの・あまいものと、デザート・飲料を組み合わせています' })).toBeVisible();
        await expect(log).not.toContainText('**');
        // AI の返答はアレルゲンの判定ではないことを、返答の下に出す（SR-18）
        await expect(log.getByText('AI の返答はアレルゲンの判定ではありません', { exact: false })).toBeVisible();
        await expect(input).toBeEnabled();
        await page.screenshot({ path: test.info().outputPath('chat-answer.png'), fullPage: true });
    });

    test('D-91 チャットを消すと、確認のあとで履歴が消え、献立はそのまま残る', async ({ page }) => {
        await openWeek(page, '再来週');
        const isMobile = test.info().project.name === 'mobile';
        if (isMobile) {
            await page.getByRole('tab', { name: 'チャット' }).click();
        }
        const log = page.getByRole('log', { name: 'チャットの履歴' });
        await expect(log.getByText('献立は変えていません。')).toBeVisible();
        const daysBefore = await page.getByRole('region', { name: /の献立$/ }).count();
        await page.getByRole('button', { name: 'チャットを消す' }).click();
        const dialog = page.getByRole('dialog', { name: 'チャットを消しますか？' });
        await expect(dialog.getByText('献立と週の要望はそのまま残ります', { exact: false })).toBeVisible();
        await dialog.getByRole('button', { name: '消す' }).click();
        await expect(dialog).toBeHidden();
        await expect(log.getByText('何かお手伝いできることはありますか？')).toBeVisible();
        await expect(log.getByText('献立は変えていません。')).toBeHidden();
        await expect(page.getByRole('button', { name: 'チャットを消す' })).toBeDisabled();
        expect(await page.getByRole('region', { name: /の献立$/ }).count()).toBe(daysBefore);
    });

    test('FR-MENU-04 日ごとのカードの「詳細設定」で見出しを直すと、カードに出る', async ({ page }) => {
        await openWeek(page, '再来週');
        const card = page.getByRole('region', { name: /の献立$/ }).first();
        await card.getByRole('button', { name: /の詳細設定$/ }).click();
        const dialog = page.getByRole('dialog', { name: /の詳細設定$/ });
        const label = dialog.getByLabel('見出し');
        const before = await label.inputValue();
        // 何も直していないうちは保存できない
        await expect(dialog.getByRole('button', { name: '保存する' })).toBeDisabled();
        await label.fill('E2E の見出し');
        await dialog.getByRole('button', { name: '保存する' }).click();
        await expect(dialog).toBeHidden();
        await expect(card.getByText('E2E の見出し')).toBeVisible();
        // 元に戻す（ほかのテストに残さない）
        await card.getByRole('button', { name: /の詳細設定$/ }).click();
        await page.getByRole('dialog', { name: /の詳細設定$/ }).getByLabel('見出し').fill(before);
        await page.getByRole('dialog', { name: /の詳細設定$/ }).getByRole('button', { name: '保存する' }).click();
        await expect(page.getByRole('dialog', { name: /の詳細設定$/ })).toBeHidden();
    });

    test('D-91 PC では AIチャットを閉じて細い帯にでき、開くと入力中の文字が残っている', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name === 'mobile', 'スマホはタブで切り替えるため、開閉のボタンはない');
        await page.setViewportSize({ width: 1600, height: 900 });
        await openWeek(page, '再来週');
        const input = page.getByLabel('チャットの依頼');
        await input.fill('金曜をもう少し豪華に');
        await page.getByRole('button', { name: 'AIチャットを閉じる' }).click();
        await expect(page.getByRole('heading', { name: 'AIチャット' })).toBeHidden();
        await expect(page.getByRole('separator', { name: '献立とチャットの境目' })).toBeHidden();
        await page.screenshot({ path: testInfo.outputPath('chat-collapsed.png') });
        await page.getByRole('button', { name: 'AIチャットを開く' }).click();
        await expect(page.getByRole('heading', { name: 'AIチャット' })).toBeVisible();
        await expect(input).toHaveValue('金曜をもう少し豪華に');
        await input.fill('');
    });

    test('D-91 PC では使える商品を閉じて細い帯にでき、開くと元に戻る', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name === 'mobile', 'スマホはタブで切り替えるため、開閉のボタンはない');
        await page.setViewportSize({ width: 1600, height: 900 });
        await openWeek(page, '再来週');
        const panel = page.getByRole('region', { name: '使える商品' });
        await expect(panel).toBeVisible();
        await page.getByRole('button', { name: '使える商品を閉じる' }).click();
        await expect(panel).toBeHidden();
        await expect(page.getByRole('separator', { name: '使える商品と献立の境目' })).toBeHidden();
        await page.screenshot({ path: testInfo.outputPath('products-collapsed.png') });
        await page.getByRole('button', { name: '使える商品を開く' }).click();
        await expect(panel).toBeVisible();
        await expect(page.getByRole('separator', { name: '使える商品と献立の境目' })).toBeVisible();
    });

    test('FR-MENU-24 PC では使える商品・献立・チャットが左から並び、商品をドラッグして足す・別の日へ動かす・一覧へ戻して外すができる', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name === 'mobile', 'スマホはドラッグの代わりに「＋」から足す（次のテスト）');
        await page.setViewportSize({ width: 1600, height: 900 });
        await openWeek(page, '再来週');
        const panel = page.getByRole('region', { name: '使える商品' });
        const chat = page.getByRole('heading', { name: 'AIチャット' });
        const days = page.getByRole('region', { name: /の献立$/ });
        await expect(panel).toBeVisible();
        await expect(days.first()).toBeVisible();
        // 左から「使える商品」「献立」「チャット」の順に並ぶ
        const panelBox = await panel.boundingBox();
        const boardBox = await days.first().boundingBox();
        const chatBox = await chat.boundingBox();
        expect(panelBox!.x + panelBox!.width).toBeLessThanOrEqual(boardBox!.x);
        expect(boardBox!.x + boardBox!.width).toBeLessThanOrEqual(chatBox!.x);
        // コマンドバー（週の実績・予算と確定）は3列の上に、3列にまたがって出る
        const commandBar = page.getByRole('region', { name: '献立の操作' });
        const barBox = await commandBar.boundingBox();
        expect(barBox!.y + barBox!.height).toBeLessThanOrEqual(panelBox!.y);
        expect(barBox!.x).toBeLessThanOrEqual(panelBox!.x + 1);
        expect(barBox!.x + barBox!.width).toBeGreaterThanOrEqual(chatBox!.x + chatBox!.width - 1);
        await expect(commandBar.getByRole('button', { name: '確定して保存', exact: true })).toBeVisible();

        // まだ入っていない日が2日以上ある商品を探し、その1日目へドラッグする（データの状態に左右されないように、飛ばさずに探す）
        const handleOf = (day: Locator, productName: string) => day.getByLabel(`${productName}をつかんで、ほかの日か使える商品へ動かす`, { exact: true });
        const plusButtons = panel.getByRole('button', { name: /を日を選んで足す$/ });
        const dayCount = await days.count();
        let name = '';
        let emptyDays: Locator[] = [];
        for (let p = 0; p < (await plusButtons.count()) && emptyDays.length < 2; p++) {
            name = ((await plusButtons.nth(p).getAttribute('aria-label')) ?? '').replace(/を日を選んで足す$/, '');
            emptyDays = [];
            for (let i = 0; i < dayCount; i++) {
                if ((await handleOf(days.nth(i), name).count()) === 0) {
                    emptyDays.push(days.nth(i));
                }
            }
        }
        expect(emptyDays.length, 'まだ入っていない日が2日以上ある商品がない').toBeGreaterThanOrEqual(2);
        const handleIn = (day: Locator) => handleOf(day, name);
        const [from, to] = emptyDays;
        const added = page.waitForResponse((response) => response.url().includes('/api/menus/addItem') && response.ok());
        await dragByEvents(page, panel.getByLabel(name, { exact: true }), from);
        await added;
        await expect(handleIn(from)).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step4-drag');

        // 別の日へ動かす
        const moved = page.waitForResponse((response) => response.url().includes('/api/menus/moveItem') && response.ok());
        await dragByEvents(page, handleIn(from), to);
        await moved;
        await expect(handleIn(to)).toBeVisible();
        await expect(handleIn(from)).toHaveCount(0);
        // 読み込み直しても、動かした先の日に残っている（保存されている）
        await page.reload();
        await expect(handleIn(to)).toBeVisible();
        await expect(handleIn(from)).toHaveCount(0);

        // 使える商品の一覧へ戻すと外れる
        const removed = page.waitForResponse((response) => response.url().includes('/api/menus/removeItem') && response.ok());
        await dragByEvents(page, handleIn(to), panel);
        await removed;
        await expect(handleIn(to)).toHaveCount(0);
    });

    test('SC-09 PC では列の境目をドラッグ・矢印キーで動かして幅を変えられ、読み込み直しても残る', async ({ page }, testInfo) => {
        test.skip(testInfo.project.name === 'mobile', 'スマホは1列（タブで切り替える）');
        await page.setViewportSize({ width: 1600, height: 900 });
        await openWeek(page, '再来週');
        const panel = page.getByRole('region', { name: '使える商品' });
        const leftSplitter = page.getByRole('separator', { name: '使える商品と献立の境目' });
        const rightSplitter = page.getByRole('separator', { name: '献立とチャットの境目' });
        await expect(leftSplitter).toBeVisible();
        const widthOf = async (locator: Locator) => (await locator.boundingBox())!.width;
        const before = await widthOf(panel);

        // 左の境目を右へ 80px ドラッグすると、使える商品の列が広がる
        const box = (await leftSplitter.boundingBox())!;
        await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
        await page.mouse.down();
        await page.mouse.move(box.x + box.width / 2 + 80, box.y + box.height / 2, { steps: 5 });
        await page.mouse.up();
        await expect.poll(() => widthOf(panel)).toBeGreaterThan(before + 60);
        const widened = await widthOf(panel);

        // 右の境目は ← キーで動かすと、チャットの列が広がる
        const chatColumn = page.getByRole('heading', { name: 'AIチャット' });
        const chatBefore = await rightSplitter.getAttribute('aria-valuenow');
        await rightSplitter.focus();
        await page.keyboard.press('ArrowLeft');
        await expect(rightSplitter).not.toHaveAttribute('aria-valuenow', chatBefore ?? '');
        expect(Number(await rightSplitter.getAttribute('aria-valuenow'))).toBeGreaterThan(Number(chatBefore));
        await expect(chatColumn).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step4-splitter');

        // 読み込み直しても、変えた幅のまま
        await page.reload();
        await expect(panel).toBeVisible();
        await expect.poll(() => widthOf(panel)).toBeGreaterThan(widened - 2);

        // いったん狭くして（タブの1列になる）広げ直しても、選んだ幅に戻る
        await page.setViewportSize({ width: 1000, height: 900 });
        await expect(leftSplitter).toBeHidden();
        await page.setViewportSize({ width: 1600, height: 900 });
        await expect(leftSplitter).toBeVisible();
        await expect.poll(() => widthOf(panel)).toBeGreaterThan(widened - 2);
    });

    test('FR-MENU-24 使える商品の「＋」から日を選んで足せる（スマホは「使える商品」のタブ）', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        const isMobile = testInfo.project.name === 'mobile';
        if (isMobile) {
            await page.getByRole('tab', { name: '使える商品' }).click();
        }
        // コマンドバー（週の実績・予算と確定）は、どのタブでも見える
        await expect(page.getByRole('region', { name: '献立の操作' }).getByRole('button', { name: '確定して保存', exact: true })).toBeVisible();
        const panel = page.getByRole('region', { name: '使える商品' });
        const plusButtons = panel.getByRole('button', { name: /を日を選んで足す$/ });
        await expect(plusButtons.first()).toBeVisible();
        // まだ入っていない日がある商品を探す（すべての日に入っている商品は、日を選べない）
        const option = page.getByRole('menuitem', { name: /に足す$/, disabled: false }).first();
        let name = '';
        for (let p = 0; p < (await plusButtons.count()); p++) {
            name = ((await plusButtons.nth(p).getAttribute('aria-label')) ?? '').replace(/を日を選んで足す$/, '');
            await plusButtons.nth(p).click();
            await expect(page.getByRole('menuitem').first()).toBeVisible();
            if ((await option.count()) > 0) {
                break;
            }
            await page.keyboard.press('Escape');
            await expect(page.getByRole('menu')).toHaveCount(0);
        }
        await expect(option, 'まだ入っていない日がある商品がない').toBeVisible();
        const dayLabel = ((await option.textContent()) ?? '').replace(/に足す$/, '');
        await option.click();
        await expect(page.getByText(`${dayLabel}に「${name}」を足しました`)).toBeVisible();
        if (isMobile) {
            await captureScreen(page, testInfo, 'sc-09-step4-products');
            await page.getByRole('tab', { name: '献立', exact: true }).click();
        }
        const day = page.getByRole('region', { name: `${dayLabel}の献立` });
        const remove = day.getByRole('button', { name: `${name}を外す`, exact: true });
        await expect(remove).toBeVisible();
        // 後のテストに影響しないように外しておく
        await remove.click();
        await expect(remove).toHaveCount(0);
    });

    test('FR-MENU-32・SR-04 確定のダイアログで 🟡 の商品にチェックを付けると確定できる', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        await page.getByRole('button', { name: '確定して保存', exact: true }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText('保存の前に確かめること')).toBeVisible();
        const confirmButton = dialog.getByRole('button', { name: '保存して確定する' });
        const checkboxes = dialog.getByRole('checkbox');
        await expect(dialog.getByText(/保存できます|要確認の商品|保存できません/).first()).toBeVisible();
        const count = await checkboxes.count();
        const unchecked = dialog.getByRole('checkbox', { checked: false, disabled: false });
        if ((await unchecked.count()) > 0) {
            await expect(confirmButton).toBeDisabled();
            await expect(dialog.getByText('届いたらパッケージで', { exact: false }).first()).toBeVisible();
        }
        // 「メーカーへの確認が必要」は出さない（D-93。SR-06 は取り下げ）
        await expect(dialog.getByText('メーカーへの確認が必要', { exact: false })).toHaveCount(0);
        await captureScreen(page, testInfo, 'sc-09-confirm-dialog');
        for (let i = 0; i < count; i++) {
            const checkbox = checkboxes.nth(i);
            if (!(await checkbox.isDisabled())) {
                await checkbox.check();
            }
        }
        await confirmButton.click();
        await expect(page.getByText('献立を保存しました（確定済み）')).toBeVisible();
        await expect(page.getByText(/^確定済み（\d/)).toBeVisible();
    });
});

test.describe('SC-09 注文リスト', () => {
    test('FR-MENU-35 確定した献立で「注文リスト」を開くと、注文番号・商品名・数量と合計が出て、コピーできる', async ({ page, context }, testInfo) => {
        await context.grantPermissions(['clipboard-read', 'clipboard-write']);
        // 過去の週の確定済みの献立（ダミーデータ）を、献立案の手順で開く
        await page.goto('/menus/history');
        await page.getByRole('main').getByRole('button', { name: /開く$/ }).first().click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+$/);
        await page.goto(page.url() + '?step=4');
        await expect(page.getByText(/^確定済み（\d/)).toBeVisible();

        await page.getByRole('button', { name: /^注文リスト/ }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText(/^注文リスト（/)).toBeVisible();
        const table = dialog.getByRole('table', { name: '注文リスト' });
        await expect(table.getByRole('columnheader', { name: '注文番号' })).toBeVisible();
        await expect(table.getByRole('columnheader', { name: '数量' })).toBeVisible();
        await expect(table.getByText(/^\d{4}$/).first()).toBeVisible();
        await expect(table.getByText(/^合計（\d+品）$/)).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-order-list');

        await dialog.getByRole('button', { name: 'コピー' }).click();
        await expect(page.getByText('注文リストをコピーしました')).toBeVisible();
        const copied = await page.evaluate(() => navigator.clipboard.readText());
        // Windows のクリップボードは改行を \r\n にする
        const copiedLines = copied.split(/\r?\n/);
        expect(copiedLines[0]).toBe('注文番号\t商品名\t数量');
        expect(copiedLines[1]).toMatch(/^\d{4}\t.+\t\d+$/);
    });

    test('FR-MENU-35 注文リストの行に「注文した」のチェックを付けると保存され、読み込み直しても残り、外せる', async ({ page }) => {
        await page.goto('/menus/history');
        await page.getByRole('main').getByRole('button', { name: /開く$/ }).first().click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+$/);
        const url = page.url() + '?step=4';
        await page.goto(url);
        await page.getByRole('button', { name: /^注文リスト/ }).click();
        const dialog = page.getByRole('dialog');
        const first = dialog.getByRole('checkbox', { name: /を注文した$/ }).first();
        // チェックはサーバーに保存してから付くので、押したあとで状態を待つ（check() は押した直後に確かめるため使わない）。
        // 前のテスト（PC）で付けたままなら外してから始める
        if (await first.isChecked()) {
            await first.click();
            await expect(first).not.toBeChecked();
        }
        await expect(dialog.getByText(/^注文済み 0 \/ \d+品$/)).toBeVisible();
        await first.click();
        await expect(first).toBeChecked();
        await expect(dialog.getByText(/^(注文済み 1 \/ \d+品|すべて注文しました)/)).toBeVisible();

        await page.reload();
        await page.getByRole('button', { name: /^注文リスト（1\// }).click();
        const reopened = page.getByRole('dialog').getByRole('checkbox', { name: /を注文した$/ }).first();
        await expect(reopened).toBeChecked();
        await reopened.click();
        await expect(reopened).not.toBeChecked();
    });

    test('FR-MENU-35 下書きの献立には「注文リスト」を出さない', async ({ page }) => {
        // 来週はダミーデータに献立がなく、このファイルのテストでも確定しないので下書きのまま
        await openWeek(page, '来週');
        await page.goto(page.url().split('?')[0] + '?step=4');
        await expect(page.getByText('下書き', { exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: /^注文リスト/ })).toHaveCount(0);
    });
});
