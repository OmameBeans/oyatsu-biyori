import { readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import {
    captureScreen,
    captureScreenAs,
    FLYER_CATEGORIES,
    flyerCategoriesInOrder,
    flyerCategoryChip,
    flyerGrid,
    flyerRow,
    flyerTab,
    flyerTabCount,
    listedProductItem,
    openFlyerItemDetail,
    openRemovedListings,
} from './helpers';

const FLYER_IMAGE = path.resolve(process.cwd(), '../../samples/flyers/flyer-02.png');
const FLYER_PDF = path.resolve(process.cwd(), '../../samples/flyers/flyer-02.pdf');

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
        await expect(page.getByRole('img', { name: 'flyer-02.png' })).toBeVisible();
        await page.getByRole('button', { name: '作成を中断して一覧へ' }).click();
        const dialog = page.getByRole('dialog');
        await expect(dialog.getByText('まだ読み取っていないチラシの画像は消えます', { exact: false })).toBeVisible();
        await dialog.getByRole('button', { name: 'やめる' }).click();
        await expect(dialog).toBeHidden();
        await expect(page.getByRole('img', { name: 'flyer-02.png' })).toBeVisible();

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
    /*
     * 手順1 チラシ取り込みの分類（D-101・09 の 11）。偽の Claude は flyer-02 の20商品を返す（e2e/README.md の表）：
     * 食品以外の1件（キッチン スポンジ）は外した掲載品になり、一覧は19件。ダミーデータはナッツ類を避けている。
     * PC では先に flow-hazelnut.spec.ts が同じ週で「取り込みを終える」ので、取り込めるの商品は登録済みになっている。
     * PC のあとにスマホが同じ DB で走るので、PC で登録した商品はスマホでも登録済みになる（件数を決め打ちしない）
     */
    test('FR-IMPORT-01・FR-FLYER-01 チラシを読み取ると、取り込みの分類のタブに件数が出て、再入力 → 取り込める → 使えない → 確定済みの順に並ぶ', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await page.getByText(/1ページを半分ずつ|取り込み済みです|読み取った商品/).first().waitFor();
        const reimport = page.getByRole('button', { name: 'チラシを取り込み直す' });
        if (await reimport.first().isVisible()) {
            await reimport.first().click();
        }
        await expect(page.getByText('1ページを半分ずつ近づけて撮ってください', { exact: false })).toBeVisible();
        await page.getByTestId('file-input').setInputFiles(FLYER_IMAGE);
        await expect(page.getByRole('img', { name: 'flyer-02.png' })).toBeVisible();
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
        // flyer-02 の20商品のうち、食品以外の1件は外した掲載品になる（FR-IMPORT-20）。入数が分からない大袋も一覧に入る（FR-IMPORT-10）
        await expect(page.getByText('読み取った商品 19件')).toBeVisible({ timeout: 30_000 });
        expect(await page.evaluate(() => (window as unknown as { flyerFlash: { pickerAfterReading: boolean } }).flyerFlash.pickerAfterReading)).toBe(false);
        // 今週の商品に入らない件数（FR-FLYER-06b・09 の 5.1）と、「取り込みを終える」で取り込む内訳
        await expect(page.getByText(/^今週の商品に入らない \d+件（使えない \d+件・再入力 \d+件）$/)).toBeVisible();
        await expect(page.getByText(/^取り込みを終えると今週の商品に入る：新しい商品 \d+件・登録済み \d+件$/)).toBeVisible();

        // タブに分類ごとの件数が出て、合計が一覧の件数になる。まだ何も取り込んでいない
        await expect(page.getByRole('heading', { name: /^取り込みの分類/ })).toBeVisible();
        expect(await flyerTabCount(page, 'すべて')).toBe(19);
        const counts = await Promise.all(FLYER_CATEGORIES.map((label) => flyerTabCount(page, label)));
        expect(counts.reduce((a, b) => a + b, 0)).toBe(19);
        expect(await flyerTabCount(page, '確定済み')).toBe(0);
        for (const label of ['再入力', '取り込める', '使えない'] as const) {
            expect(await flyerTabCount(page, label)).toBeGreaterThan(0);
        }
        // 「取り込みを終える」の件数は、取り込めるのタブの件数と同じ
        const ready = await flyerTabCount(page, '取り込める');
        await expect(page.getByRole('button', { name: `取り込みを終える（取り込める ${ready}件）` })).toBeEnabled();

        // D-101 でなくした操作は出ない（要確認・使えるの区別、まとめての確定・登録、表記なしの見比べ、このままでよい）
        for (const name of [/まとめて確定/, /まとめて登録/, /表記なしの商品をチラシと見比べる/, /このままでよい/]) {
            await expect(page.getByRole('button', { name })).toHaveCount(0);
        }
        await expect(page.getByRole('tab', { name: /^(要確認|使える) \d+$/ })).toHaveCount(0);

        // 並びは 再入力 → 取り込める → 使えない → 確定済み
        const categories = await flyerCategoriesInOrder(page);
        expect(categories).toHaveLength(19);
        const rank = (category: string) => FLYER_CATEGORIES.indexOf(category as (typeof FLYER_CATEGORIES)[number]);
        expect(categories.map(rank)).toEqual([...categories.map(rank)].sort((a, b) => a - b));
        expect(categories.every((category) => rank(category) >= 0)).toBe(true);

        // 各分類に当たる商品（ダミーのチラシに入れた商品）
        await expect(flyerCategoryChip(flyerRow(page, 'こつぶ しお豆スナック'))).toHaveText('使えない');
        await expect(flyerCategoryChip(flyerRow(page, 'ふんわり 抹茶ロール'))).toHaveText('再入力');
        await expect(flyerRow(page, 'ふんわり 抹茶ロール').getByText('税込価格が読めない')).toBeVisible();
        await expect(flyerCategoryChip(flyerRow(page, 'にこにこ ミニドーナツ'))).toHaveText('再入力');
        await expect(flyerRow(page, 'にこにこ ミニドーナツ').getByText('メーカーが読めない')).toBeVisible();
        await expect(flyerCategoryChip(flyerRow(page, 'くまのポッケ ミルクビスケット'))).toHaveText('取り込める');
        await expect(flyerRow(page, 'くまのポッケ ミルクビスケット').getByText('登録済みの商品と同じ')).toBeVisible();
        // 読み取りの検証の指摘は再入力にせず、行に注意として出すだけ（09 の 3.8）
        const stick = flyerRow(page, 'チーズ味 ひねりスティック');
        await expect(flyerCategoryChip(stick)).toHaveText('取り込める');
        await expect(stick.getByText(/^注意：.*検証の指摘/)).toBeVisible();
        // 入数の候補が内容量の表記と合わない商品も、候補を使って取り込める（注意だけ）
        const stickPotato = flyerRow(page, 'コンソメ ポテトスティック');
        await expect(flyerCategoryChip(stickPotato)).toHaveText('取り込める');
        await expect(stickPotato.getByText(/^注意：.*入数の候補が合わない/)).toBeVisible();
        if (testInfo.project.name === 'pc') {
            // 入数が分からない大袋は使えない（スマホは、PC で入数を入れて登録したので、商品一覧の入数で取り込める）
            await expect(flyerCategoryChip(flyerRow(page, 'うすしお ポテトチップス 大袋'))).toHaveText('使えない');
            await expect(flyerRow(page, 'うすしお ポテトチップス 大袋').getByText('入数が分からない')).toBeVisible();
            // ほかの商品は、先に flow-hazelnut.spec.ts で取り込んだので登録済み。ショコラはパッケージでヘーゼルナッツが見つかって使えない
            await expect(flyerCategoryChip(flyerRow(page, 'ことりのさくさくショコラ'))).toHaveText('使えない');
            await expect(flyerCategoryChip(flyerRow(page, 'いちごチョコがけ ライスパフ'))).toHaveText('取り込める');
        }

        // タブで絞り込むと、その分類の行だけが出る
        await flyerTab(page, '使えない').click();
        expect(new Set(await flyerCategoriesInOrder(page))).toEqual(new Set(['使えない']));
        await flyerTab(page, 'すべて').click();
        expect(await flyerCategoriesInOrder(page)).toHaveLength(19);
        // 初めはどの行も開いていない（FR-FLYER-06c）
        await expect(flyerGrid(page).getByRole('button', { name: /の詳細を閉じる$/ })).toHaveCount(0);

        // 表の中だけをスクロールし、画面（手順1の枠）はスクロールしない（PC。表の下のボタンも画面の中に見えている）
        if (testInfo.project.name !== 'mobile') {
            // 19行は画面に入りきらないが、表の下のボタンはスクロールせずに見えている
            await expect(page.getByRole('button', { name: 'チラシを取り込み直す' })).toBeInViewport();
            await expect(page.getByRole('button', { name: /^取り込みを終える/ })).toBeInViewport();
        }
        await captureScreen(page, testInfo, 'sc-09-step1-flyer');

        // 食品以外の掲載品は、黙って消さずに理由つきで「外した掲載品」に出る（FR-IMPORT-20。表の下のボタンから開く）
        const removed = await openRemovedListings(page);
        await expect(removed.getByText('キッチン スポンジ 3個', { exact: true })).toBeVisible();
        await expect(removed.getByText('食品以外：台所用品のため')).toBeVisible();
        await removed.getByRole('button', { name: '閉じる' }).click();
        await expect(flyerRow(page, 'キッチン スポンジ 3個')).toHaveCount(0);
    });

    test('FR-IMPORT-30〜32 略記の表記（「（乳・麦）」「（乳・カ）」）の商品は、補正した品目で表示され、「カ」はカシューナッツになる', async ({ page }) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await expect(flyerGrid(page)).toBeVisible();

        // D-96：チラシの略記（「乳・麦」の「麦」）は小麦に当たり、「品目不明」にならない（偽の Claude は表記を略記のまま渡す）
        await expect(flyerGrid(page).getByText(/品目不明/)).toHaveCount(0);
        const biscuit = flyerRow(page, 'くまのポッケ ミルクビスケット');
        await expect(biscuit.getByText('「乳・麦」')).toBeVisible();
        await expect(biscuit.getByText('麦→小麦', { exact: true })).toBeVisible();

        // カタカナの「カ」はカシューナッツ（ひらがなの「か」＝かにと字で分ける）。ナッツ類を避けているので使えない
        const cashew = flyerRow(page, 'こつぶ しお豆スナック');
        await expect(cashew.getByText('「乳・カ」')).toBeVisible();
        await expect(cashew.getByText('カ→カシューナッツ', { exact: true })).toBeVisible();
        await expect(flyerCategoryChip(cashew)).toHaveText('使えない');
        await expect(cashew.getByText('カシューナッツを含みます', { exact: true })).toBeVisible();
        await expect(cashew.getByText('かに', { exact: false })).toHaveCount(0);

        // 使えないの行を開くと、今週の商品に入らない理由が出る
        const detail = await openFlyerItemDetail(page, 'こつぶ しお豆スナック');
        await expect(detail.getByRole('alert', { name: '今週の商品に入らない理由' })).toBeVisible();
        // 理由は、チラシのアレルゲン表記の欄のそばに出る
        await expect(detail.getByText('カシューナッツを含みます（チラシの表記）', { exact: false })).toBeVisible();
        // 取り込みを終えると写しに入る表記（献立の確定で見比べる表記）を出す
        await expect(detail.getByText('献立の確定で見比べる表記：乳・カ', { exact: false })).toBeVisible();
    });

    test('FR-IMPORT-12 登録済みの商品と名前・メーカーが同じで内容量が違っても取り込めるで、取り込みを終えるとチラシの値で商品一覧を上書きする', async ({ page }) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const row = flyerRow(page, 'CO・OP さくさくクッキー');
        await expect(row).toBeVisible();
        // 内容量が違っても再入力にしない（D-101。注意を出して自動で進める）
        await expect(flyerCategoryChip(row)).toHaveText('取り込める');
        const detail = await openFlyerItemDetail(page, 'CO・OP さくさくクッキー');
        await expect(detail.getByText('この商品は、取り込みを終えると今週の商品に入ります', { exact: false })).toBeVisible();
        if ((await row.getByText(/^注意：.*内容量が変わった/).count()) > 0) {
            // まだ商品一覧が10袋入のまま（このファイルだけを走らせたとき）：注意に「原材料も見てください」を添える
            await expect(detail.getByText(/^注意：内容量が変わりました（商品一覧 .+ → チラシ 5袋入・5個）。チラシの値で商品一覧を上書きします。届いたらパッケージの原材料も見てください$/)).toBeVisible();
        } else {
            // 先に flow-hazelnut.spec.ts で取り込みを終えたので、商品一覧はチラシの値（5袋入・5個）に上書きされ、注意は出ない
            await expect(detail.getByText(/5袋入・入数 5$/)).toBeVisible();
        }
        await flyerGrid(page).getByRole('button', { name: '「CO・OP さくさくクッキー」の詳細を閉じる' }).click();
    });

    test('FR-FLYER-06・09 の 6 再入力の商品を1件ずつ直せ、外すと次の商品へ進む', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await expect(flyerGrid(page)).toBeVisible();
        const start = page.getByRole('button', { name: /^再入力を1件ずつ直す（\d+件）$/ });
        test.skip((await start.count()) === 0, '再入力の商品が残っていない');
        const total = Number((await start.innerText()).match(/（(\d+)件）/)![1]);
        expect(total).toBe(await flyerTabCount(page, '再入力'));
        await start.click();
        const dialog = page.getByRole('dialog', { name: /再入力を1件ずつ直す/ });
        await expect(dialog.getByText(`1 / ${total}件目`, { exact: false })).toBeVisible();
        await expect(dialog.getByRole('button', { name: '前へ' })).toBeDisabled();
        if (total >= 2) {
            await dialog.getByRole('button', { name: 'あとで（次へ）' }).click();
            await expect(dialog.getByText(`2 / ${total}件目`, { exact: false })).toBeVisible();
            await dialog.getByRole('button', { name: '前へ' }).click();
            await expect(dialog.getByText(`1 / ${total}件目`, { exact: false })).toBeVisible();
        }
        // 出るのは再入力の商品だけ。取り込みのボタンはない（取り込みは「取り込みを終える」でまとめて行う）
        await expect(dialog.getByLabel(/^取り込みの分類：/)).toHaveText('再入力');
        await expect(dialog.getByRole('button', { name: /^(確定する|新しい商品として登録|このままでよい)$/ })).toHaveCount(0);
        const firstName = (await dialog.getByRole('heading', { level: 3 }).innerText()).trim();
        await captureScreen(page, testInfo, 'sc-09-step1-review');

        // 「外す」と外した掲載品に移り、次の商品へ進む（「外す」「表に戻る」はダイアログのフッターにある）
        await dialog.getByRole('button', { name: '外す', exact: true }).click();
        await expect(page.getByText(`「${firstName}」を外しました`, { exact: false })).toBeVisible();
        if (total >= 2) {
            await expect(dialog.getByRole('heading', { level: 3 })).not.toHaveText(firstName);
        } else {
            await expect(dialog.getByText('すべて直しました')).toBeVisible();
        }
        await dialog.getByRole('button', { name: '表に戻る' }).first().click();
        await expect(dialog).toHaveCount(0);

        // 戻しておく（あとのテストの件数を変えないため）
        const removed = await openRemovedListings(page);
        await removed.getByRole('button', { name: `「${firstName}」を戻す` }).click();
        await expect(removed.getByText(firstName, { exact: true })).toHaveCount(0);
        // ダイアログが開いている間は後ろの表が読み上げから隠れるので、閉じてから表を見る
        await removed.getByRole('button', { name: '閉じる' }).click();
        await expect(flyerRow(page, firstName)).toBeVisible();
    });

    test('FR-IMPORT-03・09 の 5.3 再入力の商品は、税込価格・メーカーを入れると分類し直され、取り込めるになる', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await expect(flyerGrid(page)).toBeVisible();

        // 税込価格が読めない商品：理由の欄（税込価格）が開いていて、入れて「直した内容で分類し直す」を押すと再入力でなくなる
        const roll = flyerRow(page, 'ふんわり 抹茶ロール');
        await expect(flyerCategoryChip(roll)).toHaveText('再入力');
        const rollDetail = await openFlyerItemDetail(page, 'ふんわり 抹茶ロール');
        await expect(rollDetail.getByText('税込価格が読めませんでした', { exact: false })).toBeVisible();
        await rollDetail.getByLabel('税込価格（円）').fill('375');
        await rollDetail.getByRole('button', { name: '直した内容で分類し直す' }).click();
        await expect(page.getByText('「ふんわり 抹茶ロール」を直して、分類し直しました')).toBeVisible();
        await expect(flyerCategoryChip(roll)).toHaveText('取り込める');
        if (testInfo.project.name === 'pc') {
            // 一致する商品がないので「新しい商品」の印が付く（スマホは PC で登録したので付かない）
            await expect(roll.getByText('新しい商品', { exact: true }).first()).toBeVisible();
            await expect(rollDetail.getByText('この商品は、取り込みを終えると今週の商品に入ります（新しい商品として登録します）', { exact: false })).toBeVisible();
        }

        // メーカーが読めない商品：メーカーを入れると、登録済みの商品と照合して分類し直す（FR-IMPORT-03）
        const donut = flyerRow(page, 'にこにこ ミニドーナツ');
        await expect(flyerCategoryChip(donut)).toHaveText('再入力');
        await expect(donut.getByText('まだ分かりません')).toBeVisible();
        const donutDetail = await openFlyerItemDetail(page, 'にこにこ ミニドーナツ');
        await donutDetail.getByLabel('メーカー').fill('にこにこフーズ');
        await donutDetail.getByRole('button', { name: '直した内容で分類し直す' }).click();
        await expect(page.getByText('「にこにこ ミニドーナツ」を直して、分類し直しました')).toBeVisible();
        await expect(flyerCategoryChip(donut)).toHaveText('取り込める');
        await flyerGrid(page).getByRole('button', { name: '「にこにこ ミニドーナツ」の詳細を閉じる' }).click();
    });

    test('FR-IMPORT-10・11 入数が分からない大袋は使えないで、入数を入れると分類し直され、取り込めるになって注意が出る', async ({ page }) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const row = flyerRow(page, 'うすしお ポテトチップス 大袋');
        await expect(row).toBeVisible();
        // スマホは、PC で入数を入れて登録したので、商品一覧の入数で取り込めるになる
        test.skip((await flyerCategoryChip(row).innerText()) !== '使えない', 'PC で入数を入れて登録済み');
        await row.getByLabel('「うすしお ポテトチップス 大袋」の入数').fill('10');
        await row.getByRole('button', { name: '分類し直す' }).click();
        await expect(page.getByText('「うすしお ポテトチップス 大袋」の入数を入れて、分類し直しました', { exact: false })).toBeVisible();
        await expect(flyerCategoryChip(row)).toHaveText('取り込める');
        await expect(row.getByLabel('「うすしお ポテトチップス 大袋」の入数')).toHaveCount(0);

        // 入れた入数は読み取りの検証を通っていないので、注意として出す（分類は変えない）
        await expect(row.getByText(/^注意：.*検証を通っていない/)).toBeVisible();
        const detail = await openFlyerItemDetail(page, 'うすしお ポテトチップス 大袋');
        await expect(detail.getByText('入数は職員が入れました。読み取りの検証を通っていません', { exact: false })).toBeVisible();
        await flyerGrid(page).getByRole('button', { name: '「うすしお ポテトチップス 大袋」の詳細を閉じる' }).click();
    });

    test('FR-IMPORT-21・22 「外す」で外した掲載品に移り、「戻す」で同じ分類に戻る', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const name = 'いちごチョコがけ ライスパフ';
        const row = flyerRow(page, name);
        await expect(row).toBeVisible();
        const before = await flyerCategoryChip(row).innerText();

        const detail = await openFlyerItemDetail(page, name);
        await detail.getByRole('button', { name: '外す', exact: true }).click();
        await expect(page.getByText(`「${name}」を外しました`, { exact: false })).toBeVisible();
        await expect(row).toHaveCount(0);
        const removed = await openRemovedListings(page);
        await expect(removed.getByText(name, { exact: true })).toBeVisible();
        await expect(removed.getByText('職員が外しました')).toBeVisible();
        await expect(page.getByText('読み取った商品 18件')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-removed');

        await removed.getByRole('button', { name: `「${name}」を戻す` }).click();
        await expect(page.getByText(`「${name}」を一覧に戻しました`)).toBeVisible();
        await expect(removed.getByText(name, { exact: true })).toHaveCount(0);
        await removed.getByRole('button', { name: '閉じる' }).click();
        await expect(flyerCategoryChip(row)).toHaveText(before);
        await expect(page.getByText('読み取った商品 19件')).toBeVisible();
    });

    test('FR-FLYER-07・D-101 「取り込みを終える」1回で取り込めるの商品が今週の商品に入り、直している途中の行は反映するか捨てるかを聞く。使えない商品は入らない', async ({ page }, testInfo) => {
        await openWeek(page, '来週');
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        const grid = flyerGrid(page);
        await expect(grid).toBeVisible();

        // 行を開いて直している途中（まだ送っていない）
        const biscuitName = 'くまのポッケ ミルクビスケット';
        const detail = await openFlyerItemDetail(page, biscuitName);
        await detail.getByRole('button', { name: 'ほかの項目も直す' }).click();
        const orderNo = detail.getByLabel('注文番号');
        const originalOrderNo = await orderNo.inputValue();
        await orderNo.fill('9999');
        await expect(detail.getByText('直した内容は、まだ送っていません', { exact: false })).toBeVisible();

        const finish = page.getByRole('button', { name: /^取り込みを終える（取り込める \d+件）$/ });
        const ready = Number((await finish.innerText()).match(/ (\d+)件）/)![1]);
        expect(ready).toBe(await flyerTabCount(page, '取り込める'));
        await finish.click();

        // 直している途中の行があると、反映するか捨てるかを聞く（09 の 5.1）
        const pendingDialog = page.getByRole('dialog', { name: `「${biscuitName}」を直している途中です` });
        await expect(pendingDialog).toBeVisible();
        await expect(pendingDialog.getByRole('button', { name: '直した内容を反映して終える' })).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step1-finish-pending');
        await pendingDialog.getByRole('button', { name: '直した内容を捨てて終える' }).click();
        await expect(pendingDialog).toBeHidden();

        // 取り込める商品は、新しい商品も登録済みの商品も1回で取り込み、今週の商品（手順2）へ進む
        await expect(page.getByText(/^取り込みを終えました（新しく登録 \d+件・登録済みの商品を上書き \d+件）/).first()).toBeVisible();
        await expect(page.getByText('今週の献立に使わない商品があれば、「今週使う」をオフにしてください', { exact: false })).toBeVisible();
        await expect(listedProductItem(page, biscuitName)).toBeVisible();

        // 手順1に戻ると、取り込んだ行は確定済みになっている
        await page.getByRole('tab', { name: 'チラシ取り込み', exact: true }).click();
        await expect(page.getByRole('button', { name: '取り込みを終える（取り込める 0件）' })).toBeVisible();
        expect(await flyerTabCount(page, '確定済み')).toBe(ready);
        await expect(flyerCategoryChip(flyerRow(page, biscuitName))).toHaveText('確定済み');
        await expect(flyerRow(page, biscuitName).getByText('登録済みの商品を上書きしました')).toBeVisible();
        await expect(flyerCategoryChip(flyerRow(page, 'こつぶ しお豆スナック'))).toHaveText('使えない');
        // 捨てた内容は送っていない（開き直すと読み取った注文番号のまま）
        const reopened = await openFlyerItemDetail(page, biscuitName);
        await expect(reopened.getByText('直すと今週の商品からいったん外れ', { exact: false })).toBeVisible();
        await reopened.getByRole('button', { name: 'ほかの項目も直す' }).click();
        await expect(reopened.getByLabel('注文番号')).toHaveValue(originalOrderNo);
        await flyerGrid(page).getByRole('button', { name: `「${biscuitName}」の詳細を閉じる` }).click();

        // 確定済みのタブには確定済みの行だけが出る
        await flyerTab(page, '確定済み').click();
        expect(new Set(await flyerCategoriesInOrder(page))).toEqual(new Set(['確定済み']));
        await flyerTab(page, 'すべて').click();
        await captureScreen(page, testInfo, 'sc-09-step1-finished');

        // 取り込んだ商品は今週の商品に入り、使えない商品は入らない
        await page.getByRole('tab', { name: '今週の商品', exact: true }).click();
        await expect(listedProductItem(page, biscuitName)).toBeVisible();
        await expect(listedProductItem(page, 'ふんわり 抹茶ロール')).toBeVisible();
        await expect(listedProductItem(page, 'こつぶ しお豆スナック')).toHaveCount(0);
        // 表記のない商品は、参考（表記なし）の記録で 🟡 要確認 として入る（「含まない」にしない。SR-01・SR-19）
        const ramune = listedProductItem(page, 'ことりのピピ ラムネ');
        await expect(ramune.getByText('要確認', { exact: true })).toBeVisible();
        await expect(ramune.getByTestId('allergen-icon-Warning')).toBeVisible();
        await expect(ramune.getByText('チラシにアレルゲンの表記がありません')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step2-listed');
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
        await expect(page.getByText(/実績 [\d,]+円 \/ 予算/)).toBeVisible({ timeout: 30_000 });
        if (testInfo.project.name === 'mobile') {
            await page.getByRole('tab', { name: '献立', exact: true }).click();
        }
        await expect(page.getByText('理由').first()).toBeVisible({ timeout: 30_000 });
        await captureScreen(page, testInfo, 'sc-09-step4-board');

        // 「要望をこう理解しました」は、AI チャットの見出しのボタンからダイアログで開く（入力した要望も出る）
        if (testInfo.project.name === 'mobile') {
            await page.getByRole('tab', { name: 'AIチャット', exact: true }).click();
        }
        await page.getByRole('button', { name: /^週の要望とAIの理解/ }).click();
        const dialog = page.getByRole('dialog', { name: '週の要望とAIの理解' });
        await expect(dialog.getByText('要望をこう理解しました')).toBeVisible();
        await expect(dialog.getByText('しょっぱいものとゼリー系を合わせて出したい').first()).toBeVisible();
        await expect(dialog.getByText('人数・種別・見出しは、日ごとのカードの「詳細設定」で直せます。')).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-step4-requests');
        await dialog.getByRole('button', { name: '閉じる' }).click();
        await expect(dialog).toBeHidden();
    });

    test('SR-04 コマンドバーの「確かめること」に、日ごとのカードの 🔴 使えません・🟡 要確認 の件数が出て、押すと日ごとの中身が開く（画面の外の日も数える）', async ({ page }, testInfo) => {
        await openWeek(page, '再来週');
        if (testInfo.project.name === 'mobile') {
            await page.getByRole('tab', { name: '献立', exact: true }).click();
        }
        const days = page.getByRole('region', { name: /の献立$/ });
        await expect(days.first()).toBeVisible();
        const summary = page.getByRole('region', { name: '確かめること' });
        const counts: Record<string, number> = {};
        for (const label of ['使えません', '要確認']) {
            // 日ごとのカードの商品に付いた表示の数（横にスクロールしないと見えない日も DOM にはある）
            const count = await days.getByText(label, { exact: true }).count();
            counts[label] = count;
            if (count === 0) {
                await expect(summary.getByRole('button', { name: new RegExp(`^${label} \\d+件`) })).toHaveCount(0);
            } else {
                await expect(summary.getByRole('button', { name: new RegExp(`^${label} ${count}件$`) })).toBeVisible();
            }
        }
        // どのボタンからも同じダイアログが開き、中の 🔴/🟡 の数がコマンドバーの件数と同じ（画面の外の日も含む）
        const dialog = page.getByRole('dialog', { name: '確かめること' });
        for (const label of ['使えません', '要確認'].filter((l) => counts[l] > 0)) {
            await summary.getByRole('button', { name: new RegExp(`^${label} `) }).click();
            for (const l of ['使えません', '要確認']) {
                await expect(dialog.getByText(l, { exact: true })).toHaveCount(counts[l]);
            }
            await expect(dialog.getByRole('button', { name: 'パッケージで確認する' })).toHaveCount(counts['使えません'] + counts['要確認']);
            await captureScreen(page, testInfo, 'sc-09-step4-issues');
            // 「パッケージで確認する」は画面を離れずに、アレルゲン確認のダイアログを重ねて開く（記録はしない）
            const url = page.url();
            await dialog.getByRole('button', { name: 'パッケージで確認する' }).first().click();
            const checkDialog = page.getByRole('dialog', { name: /^アレルゲン確認：/ });
            await expect(checkDialog.getByRole('button', { name: 'パッケージで確認して記録' })).toBeVisible();
            expect(page.url()).toBe(url);
            await checkDialog.getByRole('button', { name: '閉じる', exact: true }).click();
            await expect(checkDialog).toBeHidden();
            await expect(dialog).toBeVisible();
            await dialog.getByRole('button', { name: '閉じる' }).click();
            await expect(dialog).toBeHidden();
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
        // 商品のカードを開き、「個数設定」から、ダイアログでパック数を直す（直したあとも開いたまま）
        const card = page.getByRole('region', { name: /の献立$/ }).first();
        await card.getByRole('button', { name: /の詳細を開く$/ }).first().click();
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
        await day.getByRole('button', { name: `${name}の詳細を開く`, exact: true }).click();
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
        // 🟡 の商品のチェックに、今週のチラシの表記の原文と、見比べて原材料を確かめる案内を添える（D-101。写しに原文がある商品だけ）
        const flyerLabelLines = dialog.getByText(/^今週のチラシの表記：/);
        const lineCount = await flyerLabelLines.count();
        for (let i = 0; i < lineCount; i++) {
            await expect(flyerLabelLines.nth(i)).toHaveText(/^今週のチラシの表記：(表記なし|「.+」)。チラシの表記と見比べて、パッケージが届いたら原材料を確かめてください$/);
        }
        // 小魚あられの写しには、ダミーデータで「表記なし」を入れてある（e2e/README.md）
        if ((await dialog.getByRole('checkbox', { name: /小魚あられ/ }).count()) > 0) {
            await expect(dialog.getByRole('checkbox', { name: /小魚あられ.*今週のチラシの表記：表記なし。チラシの表記と見比べて/ })).toBeVisible();
        }
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
        // 注文番号は3桁・4桁がある（ダミーのチラシ flyer-03 は3桁）
        await expect(table.getByText(/^\d{3,4}$/).first()).toBeVisible();
        await expect(table.getByText(/^合計（\d+品）$/)).toBeVisible();
        await captureScreen(page, testInfo, 'sc-09-order-list');

        await dialog.getByRole('button', { name: 'コピー' }).click();
        await expect(page.getByText('注文リストをコピーしました')).toBeVisible();
        const copied = await page.evaluate(() => navigator.clipboard.readText());
        // Windows のクリップボードは改行を \r\n にする
        const copiedLines = copied.split(/\r?\n/);
        expect(copiedLines[0]).toBe('注文番号\t商品名\t数量');
        expect(copiedLines[1]).toMatch(/^\d{3,4}\t.+\t\d+$/);
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
