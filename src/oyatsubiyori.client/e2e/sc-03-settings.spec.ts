import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { COLOR_MODE } from '../src/shared/type/colorMode';
import type { ColorMode } from '../src/shared/type/colorMode';
import { COLOR_THEME } from '../src/shared/type/colorTheme';
import type { ColorTheme } from '../src/shared/type/colorTheme';
import { PREFERENCE_KEY } from '../src/shared/type/preferenceKey';
import { captureScreen, expectReadable } from './helpers';

/**
 * 横スクロールが出ないこと：ページ全体の幅が画面の幅を超えず（スマホ）、渡した要素の右端が main の右端の中にある。
 * PC（md 以上）は main が overflow hidden で中身だけがスクロールするので、ページ全体の幅だけではみ出しを見逃さないよう main と比べる。
 */
const expectNoHorizontalOverflow = async (page: Page, locators: Locator[]) => {
    const viewport = page.viewportSize();
    if (!viewport) throw new Error('画面の大きさが取れません');
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidth).toBeLessThanOrEqual(viewport.width);
    const mainBox = await page.getByRole('main').boundingBox();
    if (!mainBox) throw new Error('main が表示されていません');
    for (const locator of locators) {
        const box = await locator.boundingBox();
        if (!box) throw new Error(`${locator} が表示されていません`);
        expect(box.x + box.width).toBeLessThanOrEqual(mainBox.x + mainBox.width);
    }
};

test.describe('SC-03・SC-04・SC-12 設定', () => {
    test('FR-COND-01 条件設定を保存できる（ログイン直後でも Antiforgery が通る）', async ({ page }, testInfo) => {
        await page.goto('/settings');
        const count = page.getByLabel('通常の人数');
        await expect(count).toHaveValue('50');
        await count.fill('52');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('条件設定を保存しました')).toBeVisible();
        await page.reload();
        await expect(page.getByLabel('通常の人数')).toHaveValue('52');
        // SC-03 フォームはカードに載せる。PC でもスマホでも横にはみ出さない（#6）
        await expectNoHorizontalOverflow(page, [
            page.getByLabel('既定の週の予算（税込）'),
            page.getByLabel('のどごし', { exact: true }),
            page.getByLabel('重複についての補足ルール（AI に伝えます）'),
            page.getByRole('button', { name: '保存する' }),
        ]);
        await captureScreen(page, testInfo, 'sc-03-conditions');
        await page.getByLabel('通常の人数').fill('50');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('条件設定を保存しました')).toBeVisible();
    });

    test('FR-COND 受け入れ条件 人数が0だと保存できず、次にすることが出る', async ({ page }) => {
        await page.goto('/settings');
        await page.getByLabel('通常の人数').fill('0');
        await page.getByRole('button', { name: '保存する' }).click();
        await expect(page.getByText('通常の人数は1以上の整数で入れてください')).toBeVisible();
    });

    test('FR-AVOID-05 避ける品目を外すときは確認のダイアログが出る', async ({ page }, testInfo) => {
        await page.goto('/settings?tab=avoided');
        const hazelnut = page.getByRole('checkbox', { name: 'ヘーゼルナッツを避ける' });
        await expect(hazelnut).toBeChecked();
        await captureScreen(page, testInfo, 'sc-04-avoided');
        await hazelnut.click();
        await expect(page.getByText('この品目を避ける子がもういないことを確認しましたか？', { exact: false })).toBeVisible();
        await page.getByRole('button', { name: 'やめる' }).click();
        await expect(hazelnut).toBeChecked();
    });

    test('FR-AVOID-07 探す言葉を足して消せる（初期データの言葉は消せない）', async ({ page }) => {
        await page.goto('/settings?tab=avoided');
        await page.getByRole('button', { name: 'ヘーゼルナッツの探す言葉' }).click();
        const keyword = `ヘーゼルテスト${test.info().project.name}`;
        await page.getByLabel('探す言葉を足す').first().fill(keyword);
        await page.getByRole('button', { name: '足す' }).first().click();
        const chip = page.getByRole('button', { name: keyword });
        await expect(chip).toBeVisible();
        await chip.locator('svg').click();
        await page.getByRole('button', { name: '消す', exact: true }).click();
        await expect(chip).toHaveCount(0);
    });

    test('FR-AUTH-03a パスワードは12文字未満にできない', async ({ page }, testInfo) => {
        await page.goto('/settings?tab=password');
        await page.getByLabel('今のパスワード').fill('e2e-dummy-password-0001');
        await page.getByLabel('新しいパスワード（12文字以上）').fill('short');
        await page.getByLabel('新しいパスワード（確認）').fill('short');
        await page.getByRole('button', { name: 'パスワードを変更する' }).click();
        await expect(page.getByText('パスワードは12文字以上にしてください')).toBeVisible();
        await expectNoHorizontalOverflow(page, [page.getByLabel('新しいパスワード（確認）'), page.getByRole('button', { name: 'パスワードを変更する' })]);
        await captureScreen(page, testInfo, 'sc-12-password');
    });
});

/** 色のテーマの表示名（設定の「見た目」タブのラジオボタンの名前。#2） */
const COLOR_THEME_LABELS = ['オレンジ（いつもの色）', 'あお', 'むらさき', 'グレー'] as const;

/** primary の色が出ているところの色を読む：選ばれたタブの文字の色と、「見た目」タブの「色のテーマ」で選ばれたラジオの色 */
const readPrimaryColors = async (page: Page) => ({
    tab: await page.getByRole('tab', { name: '見た目', exact: true }).evaluate((el) => getComputedStyle(el).color),
    radio: await page
        .getByRole('radiogroup', { name: '色のテーマ' })
        .getByRole('radio', { checked: true })
        .evaluate((el) => getComputedStyle(el.parentElement ?? el).color),
});

/** PC のサイドバーの選ばれた項目（設定）の線と背景の色。スマホはサイドバーが閉じているので読まない */
const readSidebarColors = async (page: Page) => {
    const selected = page.getByRole('navigation', { name: 'メニュー', exact: true }).getByRole('link', { name: '設定', exact: true });
    return {
        border: await selected.evaluate((el) => getComputedStyle(el).borderLeftColor),
        background: await selected.evaluate((el) => getComputedStyle(el).backgroundColor),
    };
};

/** 🔴🟡🟢 のバッジの文字の色と、バッジ（文字の親）の背景・枠の色 */
const readBadgeColorsOf = async (page: Page, text: string) =>
    page
        .getByText(text, { exact: true })
        .first()
        .evaluate((el) => {
            const badge = getComputedStyle(el.parentElement ?? el);
            return [getComputedStyle(el).color, badge.backgroundColor, badge.borderColor].join(' / ');
        });

test.describe('#2 見た目（色のテーマ）', () => {
    test('#2 色のテーマを選ぶと、再読み込みなしで色が変わり、再読み込みしても残る', async ({ page }, testInfo) => {
        const isPc = testInfo.project.name === 'pc';
        await page.goto('/settings?tab=appearance');
        await expect(page.getByRole('radio', { name: 'オレンジ（いつもの色）' })).toBeChecked();
        const before = await readPrimaryColors(page);
        const sidebarBefore = isPc ? await readSidebarColors(page) : null;

        await page.getByRole('radio', { name: 'あお' }).check();
        await expect(page.getByRole('radio', { name: 'あお' })).toBeChecked();
        await expect(page.getByRole('radio', { name: 'オレンジ（いつもの色）' })).not.toBeChecked();
        await expect.poll(async () => (await readPrimaryColors(page)).tab).not.toBe(before.tab);
        const after = await readPrimaryColors(page);
        expect(after.radio).not.toBe(before.radio);
        if (sidebarBefore) {
            const sidebarAfter = await readSidebarColors(page);
            expect(sidebarAfter.border).not.toBe(sidebarBefore.border);
            expect(sidebarAfter.background).not.toBe(sidebarBefore.background);
        }
        await expectNoHorizontalOverflow(page, [page.getByRole('radio', { name: 'グレー' })]);
        await captureScreen(page, testInfo, 'sc-03-appearance');

        await page.reload();
        await expect(page.getByRole('radio', { name: 'あお' })).toBeChecked();
        expect(await readPrimaryColors(page)).toEqual(after);
    });

    test('#2 ブラウザに保存できなくても、既定の色で画面が出て、テーマを選べる', async ({ page }) => {
        await page.addInitScript(() => {
            const fail = () => {
                throw new Error('保存は使えません（E2E）');
            };
            Storage.prototype.getItem = fail;
            Storage.prototype.setItem = fail;
        });
        await page.goto('/settings?tab=appearance');
        await expect(page.getByRole('radio', { name: 'オレンジ（いつもの色）' })).toBeChecked();
        const before = await readPrimaryColors(page);
        await page.getByRole('radio', { name: 'むらさき' }).check();
        await expect(page.getByRole('radio', { name: 'むらさき' })).toBeChecked();
        await expect.poll(async () => (await readPrimaryColors(page)).tab).not.toBe(before.tab);
    });

    test('#2 どのテーマでも 🔴🟡🟢 の色は変わらない（テーマごとに商品一覧の画像を撮る）', async ({ page }, testInfo) => {
        let baseline: string[] | null = null;
        for (const [index, label] of COLOR_THEME_LABELS.entries()) {
            await page.goto('/settings?tab=appearance');
            await page.getByRole('radio', { name: label }).check();
            await expect(page.getByRole('radio', { name: label })).toBeChecked();
            await page.goto('/products');
            await expect(page.getByRole('heading', { name: '商品一覧' })).toBeVisible();
            const colors = [
                await readBadgeColorsOf(page, '🔴 使えません'),
                await readBadgeColorsOf(page, '🟡 要確認'),
                await readBadgeColorsOf(page, '🟢 使えます'),
            ];
            if (baseline === null) {
                baseline = colors;
            } else {
                expect(colors).toEqual(baseline);
            }
            // PC は一覧の部分だけがスクロールするので、画面を縦に伸ばして 🔴🟡🟢 がすべて1枚に入るようにする（撮ったら元の大きさに戻す）
            const viewport = page.viewportSize();
            if (testInfo.project.name === 'pc' && viewport) {
                await page.setViewportSize({ width: viewport.width, height: 2400 });
            }
            await captureScreen(page, testInfo, `theme-${index + 1}-products`);
            if (testInfo.project.name === 'pc' && viewport) {
                await page.setViewportSize(viewport);
            }
        }
    });
});

test.describe('#2 見た目（ログイン前の画面）', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('#2 保存した色のテーマで、ランディングとログインの画面が崩れずに出る', async ({ page }, testInfo) => {
        /** primary を使う要素の色：ランディングの「ログイン」ボタンの背景と、ログイン画面の「おやつ日和について」のリンクの文字 */
        const readLandingColor = () => page.getByRole('link', { name: 'ログイン' }).evaluate((el) => getComputedStyle(el).backgroundColor);
        const readLoginColor = () => page.getByRole('link', { name: 'おやつ日和について' }).evaluate((el) => getComputedStyle(el).color);

        // 保存なし（既定の色）で開いたときの色
        await page.goto('/');
        await expect(page.getByRole('heading', { name: '安全への考え方' })).toBeVisible();
        const landingDefault = await readLandingColor();
        await page.goto('/login');
        await expect(page.getByRole('button', { name: 'ログイン' })).toBeVisible();
        const loginDefault = await readLoginColor();

        // むらさきを保存してから開くと、その色で出る
        await page.evaluate(({ key, theme }) => window.localStorage.setItem(key, theme), { key: PREFERENCE_KEY.COLOR_THEME, theme: COLOR_THEME.PURPLE });
        await page.goto('/');
        await expect(page.getByRole('heading', { name: '安全への考え方' })).toBeVisible();
        await expect(page.getByRole('link', { name: 'ログイン' })).toBeVisible();
        expect(await readLandingColor()).not.toBe(landingDefault);
        await captureScreen(page, testInfo, 'theme-landing');
        await page.goto('/login');
        await expect(page.getByRole('button', { name: 'ログイン' })).toBeVisible();
        expect(await readLoginColor()).not.toBe(loginDefault);
        await captureScreen(page, testInfo, 'theme-login');
    });
});

/** 画面の背景（body）の色 */
const readBackground = (page: Page) => page.evaluate(() => getComputedStyle(document.body).backgroundColor);

/** 色が暗いか（rgb の明るさの目安が半分より下）。ダークの背景になったかを見る */
const isDarkColor = (color: string) => {
    const [r, g, b] = (color.match(/\d+/g) ?? []).map(Number);
    return 0.299 * r + 0.587 * g + 0.114 * b < 128;
};

/**
 * ブラウザの上の帯の色（<meta name="theme-color">）。meta は画面に出ない要素で、役割や文言では探せないので CSS のセレクタで探す。
 * App が useEffect で書き換えるので、値を確かめるときは toHaveAttribute で待つ
 */
const browserBar = (page: Page) => page.locator('meta[name="theme-color"]');

/** 見た目の設定をこの端末のブラウザに保存する（保存は同じオリジンのページを開いてから行う）。このあと開いた画面から効く */
const storeAppearance = async (page: Page, appearance: { mode: ColorMode; theme?: ColorTheme }) => {
    await page.evaluate(
        ({ keys, mode, theme }) => {
            window.localStorage.setItem(keys.COLOR_MODE, mode);
            if (theme) {
                window.localStorage.setItem(keys.COLOR_THEME, theme);
            }
        },
        { keys: PREFERENCE_KEY, ...appearance },
    );
};

/** 色のテーマの保存する値（並びは COLOR_THEME_LABELS と同じ） */
const COLOR_THEME_IDS = Object.values(COLOR_THEME);

test.describe('#4 見た目（明るさ）', () => {
    test('#4 「ダーク」を選ぶと、再読み込みなしで背景が暗くなり、再読み込みしても残る', async ({ page }, testInfo) => {
        await page.emulateMedia({ colorScheme: 'light' });
        await page.goto('/settings?tab=appearance');
        const modeGroup = page.getByRole('radiogroup', { name: '明るさ' });
        await expect(modeGroup.getByRole('radio', { name: 'ライト' })).toBeChecked();
        const lightBackground = await readBackground(page);
        expect(isDarkColor(lightBackground)).toBe(false);
        const lightBar = await browserBar(page).getAttribute('content');
        if (!lightBar) throw new Error('theme-color が読めません');

        await modeGroup.getByRole('radio', { name: 'ダーク' }).check();
        await expect(modeGroup.getByRole('radio', { name: 'ダーク' })).toBeChecked();
        await expect.poll(() => readBackground(page)).not.toBe(lightBackground);
        const darkBackground = await readBackground(page);
        expect(isDarkColor(darkBackground)).toBe(true);
        // ブラウザの上の帯は、ライトはテーマの色、ダークは背景の色
        await expect(browserBar(page)).not.toHaveAttribute('content', lightBar);
        // 色のテーマの選択はそのまま
        await expect(page.getByRole('radiogroup', { name: '色のテーマ' }).getByRole('radio', { name: 'オレンジ（いつもの色）' })).toBeChecked();
        await expectNoHorizontalOverflow(page, [modeGroup.getByRole('radio', { name: 'システム（端末の設定に合わせる）' })]);
        await captureScreen(page, testInfo, 'dark-sc-03-appearance');

        await page.reload();
        await expect(modeGroup.getByRole('radio', { name: 'ダーク' })).toBeChecked();
        expect(await readBackground(page)).toBe(darkBackground);

        // ライトに戻すと、元の背景に戻る
        await modeGroup.getByRole('radio', { name: 'ライト' }).check();
        await expect.poll(() => readBackground(page)).toBe(lightBackground);
        await expect(browserBar(page)).toHaveAttribute('content', lightBar);
    });

    test('#4 「システム」は端末の設定（ライト・ダーク）に合わせ、端末の設定が変わるとすぐ切り替わる', async ({ page }) => {
        await page.emulateMedia({ colorScheme: 'light' });
        await page.goto('/settings?tab=appearance');
        const modeGroup = page.getByRole('radiogroup', { name: '明るさ' });
        await modeGroup.getByRole('radio', { name: 'システム（端末の設定に合わせる）' }).check();
        await expect(modeGroup.getByRole('radio', { name: 'システム（端末の設定に合わせる）' })).toBeChecked();
        await expect.poll(async () => isDarkColor(await readBackground(page))).toBe(false);

        await page.emulateMedia({ colorScheme: 'dark' });
        await expect.poll(async () => isDarkColor(await readBackground(page))).toBe(true);
        await page.reload();
        await expect(modeGroup.getByRole('radio', { name: 'システム（端末の設定に合わせる）' })).toBeChecked();
        expect(isDarkColor(await readBackground(page))).toBe(true);

        await page.emulateMedia({ colorScheme: 'light' });
        await expect.poll(async () => isDarkColor(await readBackground(page))).toBe(false);
    });

    test('#4 ブラウザに保存できなくても、ライトで画面が出て、明るさを選べる', async ({ page }) => {
        await page.addInitScript(() => {
            const fail = () => {
                throw new Error('保存は使えません（E2E）');
            };
            Storage.prototype.getItem = fail;
            Storage.prototype.setItem = fail;
        });
        await page.emulateMedia({ colorScheme: 'dark' });
        await page.goto('/settings?tab=appearance');
        const modeGroup = page.getByRole('radiogroup', { name: '明るさ' });
        await expect(modeGroup.getByRole('radio', { name: 'ライト' })).toBeChecked();
        expect(isDarkColor(await readBackground(page))).toBe(false);
        await modeGroup.getByRole('radio', { name: 'ダーク' }).check();
        await expect.poll(async () => isDarkColor(await readBackground(page))).toBe(true);
    });

    test('#4 ダークでも、どの色のテーマでも 🔴🟡🟢 の色は同じ（テーマごとに商品一覧の画像を撮る）', async ({ page }, testInfo) => {
        let baseline: string[] | null = null;
        for (const [index, theme] of COLOR_THEME_IDS.entries()) {
            await page.goto('/settings?tab=appearance');
            await storeAppearance(page, { mode: COLOR_MODE.DARK, theme });
            await page.goto('/products');
            await expect(page.getByRole('heading', { name: '商品一覧' })).toBeVisible();
            expect(isDarkColor(await readBackground(page))).toBe(true);
            const colors = [
                await readBadgeColorsOf(page, '🔴 使えません'),
                await readBadgeColorsOf(page, '🟡 要確認'),
                await readBadgeColorsOf(page, '🟢 使えます'),
            ];
            if (baseline === null) {
                baseline = colors;
            } else {
                expect(colors).toEqual(baseline);
            }
            // 注意書き・🔴 の理由・次にすることは、ダークでも背景と 4.5:1 以上で読める
            for (const text of [
                page.getByText('アレルゲンの最終確認は、必ずパッケージの原材料表示で行ってください'),
                page.getByText('アレルゲンの情報がありません', { exact: true }).first(),
                page.getByText('パッケージの原材料表示を撮って確認してください', { exact: true }).first(),
            ]) {
                await expectReadable(text);
            }
            const viewport = page.viewportSize();
            if (testInfo.project.name === 'pc' && viewport) {
                await page.setViewportSize({ width: viewport.width, height: 2400 });
            }
            await captureScreen(page, testInfo, `dark-theme-${index + 1}-products`);
            if (testInfo.project.name === 'pc' && viewport) {
                await page.setViewportSize(viewport);
            }
        }
    });

    test('#4 ダークで、ログイン後の画面が崩れずに出る（画面ごとに画像を撮る）', async ({ page }, testInfo) => {
        test.setTimeout(120_000);
        await page.goto('/settings?tab=appearance');
        await storeAppearance(page, { mode: COLOR_MODE.DARK });

        // 献立一覧
        await page.goto('/menus');
        await expect(page.getByRole('heading', { name: '献立一覧' })).toBeVisible();
        expect(isDarkColor(await readBackground(page))).toBe(true);
        // ダークでも、ボタンの丸み（BASE_THEME_OPTIONS の borderRadius: 999）が引き継がれている
        const openButton = page.getByRole('region', { name: /^今週 / }).getByRole('button', { name: '開く', exact: true });
        expect(await openButton.evaluate((el) => getComputedStyle(el).borderTopLeftRadius)).toBe('999px');
        await captureScreen(page, testInfo, 'dark-sc-10-menu-list');

        // 献立の編集（確定済みの今週の献立。日ごとのカードに 🔴🟡🟢 と注記が出る）
        await openButton.click();
        await expect(page).toHaveURL(/\/menus\/[0-9a-f-]+/);
        // URL が変わっても、次の画面の読み込みが終わるまでは一覧が残るので、見出しが替わるのを待つ
        await expect(page.getByRole('heading', { level: 1, name: /^献立作成/ })).toBeVisible();
        await expect(page.getByText(/週の実績 [\d,]+円 \/ 週の予算/)).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-09-step4-board');

        // 商品詳細
        await page.goto('/products');
        await page.getByRole('region', { name: 'うさぎのミミ いちごゼリー' }).getByRole('link').click();
        await expect(page.getByRole('heading', { name: 'うさぎのミミ いちごゼリー' })).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-07-product-detail');

        // アレルゲン確認（記録はしない）
        await page.goto('/products');
        await page.getByRole('region', { name: 'ぽかぽか さつまいもチップス' }).getByRole('link').click();
        await page.getByRole('link', { name: 'パッケージで確認する' }).click();
        await expect(page.getByRole('button', { name: 'パッケージで確認して記録' })).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-08-allergen-check');

        // 設定：条件設定・避ける品目（外すときの確認のダイアログ）・パスワード変更
        await page.goto('/settings');
        await expect(page.getByLabel('通常の人数')).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-03-conditions');
        await page.goto('/settings?tab=avoided');
        const hazelnut = page.getByRole('checkbox', { name: 'ヘーゼルナッツを避ける' });
        await expect(hazelnut).toBeChecked();
        await hazelnut.click();
        await expect(page.getByText('この品目を避ける子がもういないことを確認しましたか？', { exact: false })).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-04-avoided-dialog');
        await page.getByRole('button', { name: 'やめる' }).click();
        await expect(hazelnut).toBeChecked();
        await page.goto('/settings?tab=password');
        await expect(page.getByLabel('今のパスワード')).toBeVisible();
        await captureScreen(page, testInfo, 'dark-sc-12-password');
    });
});

test.describe('#4 見た目（明るさ・ログイン前の画面）', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('#4 ダークを保存すると、ランディングとログインの画面も暗い背景で崩れずに出る', async ({ page }, testInfo) => {
        await page.goto('/');
        await expect(page.getByRole('heading', { name: '安全への考え方' })).toBeVisible();
        expect(isDarkColor(await readBackground(page))).toBe(false);
        await storeAppearance(page, { mode: COLOR_MODE.DARK });

        await page.goto('/');
        await expect(page.getByRole('heading', { name: '安全への考え方' })).toBeVisible();
        expect(isDarkColor(await readBackground(page))).toBe(true);
        await captureScreen(page, testInfo, 'dark-sc-13-landing');
        await page.goto('/login');
        await expect(page.getByRole('button', { name: 'ログイン' })).toBeVisible();
        expect(isDarkColor(await readBackground(page))).toBe(true);
        await captureScreen(page, testInfo, 'dark-sc-01-login');
    });
});
