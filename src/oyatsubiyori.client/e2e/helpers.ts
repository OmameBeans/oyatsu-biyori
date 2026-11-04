import { expect } from '@playwright/test';
import type { Locator, Page, TestInfo } from '@playwright/test';

/** E2E のアカウント（appsettings.E2E.json のダミーの値。本番のものではない） */
export const E2E_USER = {
    userName: 'staff',
    password: 'e2e-dummy-password-0001',
} as const;

/**
 * 画面の画像を撮る（目で見て崩れがないか確かめる用。ピクセルの比較はしない）。
 * 保存先は e2e/screenshots/<画面>-<pc|mobile>.png（.gitignore）。
 */
export const captureScreen = async (page: Page, testInfo: TestInfo, name: string) => {
    await page.screenshot({ path: `./e2e/screenshots/${name}-${testInfo.project.name}.png`, fullPage: true });
};

/**
 * 画面の画像を、名前をそのまま付けて撮る（1つのテストの中で PC とスマホの両方を撮るとき用）。
 * ダイアログの出てくる動き（フェード）の途中で撮ると後ろの画面が透けるので、動きを終わらせてから撮る。
 */
export const captureScreenAs = async (page: Page, fileName: string) => {
    await page.screenshot({ path: `./e2e/screenshots/${fileName}.png`, fullPage: true, animations: 'disabled' });
};

/**
 * 文字と、その後ろに見えている背景との比（WCAG のコントラスト比）を、ブラウザが計算したあとの色から求める。
 * 背景は、要素から外側へ向かって半透明の背景色を重ねて求める。
 * opacity と、背景画像・グラデーション（background-image。MUI のダークの Paper の白の重ねもこれ）は計算に入れない。
 * そのため、途中の要素に opacity が 1 より小さいもの・背景画像があるものがあれば、正しく計算できないので 0 を返す（検査は落ちる側に倒す）。
 * ダイアログが出てくる動きの途中も opacity が 1 より小さいので、呼ぶ側は expect.poll で待つ（expectReadable）。
 */
export const contrastRatioOf = async (locator: Locator) =>
    locator.evaluate((el) => {
        for (let node: Element | null = el; node; node = node.parentElement) {
            const style = getComputedStyle(node);
            if (Number(style.opacity) < 1 || style.backgroundImage !== 'none') return 0;
        }
        const parse = (value: string): [number, number, number, number] => {
            const parts = (value.match(/[\d.]+/g) ?? []).map(Number);
            return [parts[0] ?? 0, parts[1] ?? 0, parts[2] ?? 0, parts.length >= 4 ? parts[3] : 1];
        };
        const over = (top: [number, number, number, number], bottom: [number, number, number]): [number, number, number] => [
            top[0] * top[3] + bottom[0] * (1 - top[3]),
            top[1] * top[3] + bottom[1] * (1 - top[3]),
            top[2] * top[3] + bottom[2] * (1 - top[3]),
        ];
        const layers: [number, number, number, number][] = [];
        for (let node: Element | null = el; node; node = node.parentElement) {
            const background = parse(getComputedStyle(node).backgroundColor);
            if (background[3] > 0) layers.push(background);
            if (background[3] >= 1) break;
        }
        // いちばん外側まで透けていたら、ブラウザの地の色（color-scheme が dark なら黒に近い色）とみなす
        const isDarkCanvas = getComputedStyle(document.documentElement).colorScheme.includes('dark');
        let background: [number, number, number] = isDarkCanvas ? [18, 18, 18] : [255, 255, 255];
        for (const layer of layers.reverse()) background = over(layer, background);
        const text = over(parse(getComputedStyle(el).color), background);
        const luminance = ([r, g, b]: [number, number, number]) => {
            const channel = (c: number) => {
                const v = c / 255;
                return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
            };
            return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
        };
        const [high, low] = [luminance(text), luminance(background)].sort((a, b) => b - a);
        return (high + 0.05) / (low + 0.05);
    });

/** 文字が背景と 4.5:1 以上（WCAG AA）で読める。出てくる動きの途中などで計算できないあいだは待つ */
export const expectReadable = async (locator: Locator) => {
    await expect(locator).toBeVisible();
    await expect.poll(() => contrastRatioOf(locator)).toBeGreaterThanOrEqual(4.5);
};
