# 画面の E2E（Playwright）

画面の要件を、要件 ID つきのテストで確かめます（D-87・T0-15）。

## 走らせ方

```
cd src/oyatsubiyori.client
npm run e2e
```

- サーバーは `ASPNETCORE_ENVIRONMENT=E2E` で起動します（`https://localhost:7175`）。DB（`oyatsu-biyori.e2e.db`）は毎回作り直し、TD-02 のダミーデータを入れます
- **Claude は偽物**です（`OyatsuBiyori.Server/E2E/FakeClaudeServices.cs`）。チラシの読み取りは `samples/flyers/flyer-01.expected.json` を返します。費用がかからず、毎回同じ結果になります。Biz の検証・アレルゲンの判定は本物を通します
- 偽の Claude とダミーデータは E2E の環境でだけ登録されます。本番（環境名・接続文字列・IIS）では `DemoDataGuard` が止めます
- 画面は Vite の開発サーバー（`https://localhost:60235`）から開きます
- 初めて走らせるときは、ブラウザを入れます：`npx playwright install chromium`

## プロジェクト

| 名前 | 中身 |
| --- | --- |
| setup | 最初に1回ログインして、状態を `e2e/.auth/state.json` に保存する |
| pc | 1280×800 |
| mobile | Chromium のスマホの幅（Pixel 7） |

同じ DB を使うため、1つずつ順に走らせます（`workers: 1`）。テストは、前のテスト（PC）で変わったデータでも通るように書きます。

## 決まり

- テストのファイルは画面ごと（`sc-09-menu-edit.spec.ts` など）
- テストの名前は要件 ID から始め、満たすべき条件を日本語で書く（`FR-MENU-14 🔴 の商品を入れると確定できない`）
- 要素は役割と文言で探す（`getByRole`・`getByText`）。CSS のクラスや DOM の構造に頼らない
- 画面ごとに PC とスマホの画像を1枚ずつ `e2e/screenshots/` に撮る（`captureScreen`。目で見る用で、ピクセルの比較はしない。iOS Safari は実機で確かめる）
- 使うデータは TD-02 のダミーデータだけ
