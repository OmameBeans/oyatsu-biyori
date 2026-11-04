import { defineConfig, devices } from '@playwright/test';

/**
 * 画面の E2E（T0-15・D-87）。サーバーを E2E の環境（偽の Claude・ダミーデータ・毎回作り直す SQLite）で起動し、
 * Vite の開発サーバー経由で画面を開く。手順は e2e/README.md。
 */
const API_PORT = 7175;
const WEB_PORT = 60235;
const BASE_URL = `https://localhost:${WEB_PORT}`;

export default defineConfig({
    testDir: './e2e',
    outputDir: './e2e/test-results',
    // 同じ DB を使うため、1つずつ順に走らせる
    fullyParallel: false,
    workers: 1,
    retries: 0,
    timeout: 60_000,
    expect: { timeout: 10_000 },
    reporter: [['list'], ['html', { outputFolder: './e2e/report', open: 'never' }]],
    use: {
        baseURL: BASE_URL,
        ignoreHTTPSErrors: true,
        locale: 'ja-JP',
        timezoneId: 'Asia/Tokyo',
        trace: 'retain-on-failure',
        screenshot: 'only-on-failure',
    },
    projects: [
        { name: 'setup', testMatch: /auth\.setup\.ts/ },
        {
            name: 'pc',
            dependencies: ['setup'],
            testIgnore: /auth\.setup\.ts/,
            use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 }, storageState: './e2e/.auth/state.json' },
        },
        {
            name: 'mobile',
            dependencies: ['pc'],
            testIgnore: /auth\.setup\.ts/,
            use: { ...devices['Pixel 7'], storageState: './e2e/.auth/state.json' },
        },
    ],
    webServer: [
        {
            command: 'dotnet run --project ../OyatsuBiyori.Server --no-launch-profile',
            url: `https://localhost:${API_PORT}/api/auth/status`,
            ignoreHTTPSErrors: true,
            reuseExistingServer: false,
            timeout: 180_000,
            env: {
                ASPNETCORE_ENVIRONMENT: 'E2E',
                ASPNETCORE_URLS: `https://localhost:${API_PORT}`,
            },
        },
        {
            command: `npx vite --port ${WEB_PORT} --strictPort`,
            url: BASE_URL,
            ignoreHTTPSErrors: true,
            reuseExistingServer: false,
            timeout: 120_000,
            env: {
                ASPNETCORE_HTTPS_PORT: String(API_PORT),
                DEV_SERVER_PORT: String(WEB_PORT),
            },
        },
    ],
});
