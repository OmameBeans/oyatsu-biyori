import { expect, test } from '@playwright/test';
import type { Locator, Page } from '@playwright/test';
import { captureScreen } from './helpers';

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

/** primary の色が出ているところの色を読む：選ばれたタブの文字の色と、「見た目」タブのラジオの色 */
const readPrimaryColors = async (page: Page) => ({
    tab: await page.getByRole('tab', { name: '見た目', exact: true }).evaluate((el) => getComputedStyle(el).color),
    radio: await page.getByRole('radio', { checked: true }).evaluate((el) => getComputedStyle(el.parentElement ?? el).color),
});

/** PC のサイドバーの選ばれた項目（設定）の線と背景の色。スマホはサイドバーが閉じているので読まない */
const readSidebarColors = async (page: Page) => {
    const selected = page.getByRole('navigation', { name: 'メニュー', exact: true }).getByRole('link', { name: '設定', exact: true });
    return {
        border: await selected.evaluate((el) => getComputedStyle(el).borderLeftColor),
        background: await selected.evaluate((el) => getComputedStyle(el).backgroundColor),
    };
};

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
        /** バッジの文字の色と、バッジ（文字の親）の背景・枠の色 */
        const readBadgeColors = async (text: string) =>
            page
                .getByText(text, { exact: true })
                .first()
                .evaluate((el) => {
                    const badge = getComputedStyle(el.parentElement ?? el);
                    return [getComputedStyle(el).color, badge.backgroundColor, badge.borderColor].join(' / ');
                });
        let baseline: string[] | null = null;
        for (const [index, label] of COLOR_THEME_LABELS.entries()) {
            await page.goto('/settings?tab=appearance');
            await page.getByRole('radio', { name: label }).check();
            await expect(page.getByRole('radio', { name: label })).toBeChecked();
            await page.goto('/products');
            await expect(page.getByRole('heading', { name: '商品一覧' })).toBeVisible();
            const colors = [await readBadgeColors('🔴 使えません'), await readBadgeColors('🟡 要確認'), await readBadgeColors('🟢 使えます')];
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
        await page.evaluate(() => window.localStorage.setItem('oyatsu-biyori.color-theme', 'purple'));
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
