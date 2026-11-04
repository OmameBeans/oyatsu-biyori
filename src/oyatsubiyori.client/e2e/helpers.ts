import type { Page, TestInfo } from '@playwright/test';

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
