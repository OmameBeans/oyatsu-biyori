---
name: reset-demo
description: おやつ日和の手元のデモ環境（開発用の SQLite）を作り直し、TD-02 のダミーデータ（架空の商品・確定献立・確認の履歴・条件設定）を入れる。「デモ環境を作り直して」「ダミーデータを入れて」「画面の画像を撮る前に初期化して」などのときに使う。本番では使わない。
---

# デモ環境の作り直し（TD-02）

手元の開発用 DB（`src/OyatsuBiyori.Server/oyatsu-biyori.dev.db`）を消して作り直し、ダミーデータを入れる。

## 前提

- 開発の環境（`ASPNETCORE_ENVIRONMENT=Development`）だけで動く。本番・IIS・`.dev.` / `.e2e.` を含まない接続文字列では `DemoDataGuard` が止める
- 初期アカウントは user-secrets で入れておく（リポジトリには入れない）：

```
dotnet user-secrets set "Auth:InitialUserName" "<ユーザー名>" --project src/OyatsuBiyori.Server
dotnet user-secrets set "Auth:InitialPassword" "<初期パスワード>" --project src/OyatsuBiyori.Server
```

## 手順

1. 起動中のサーバーがあれば止める（DB ファイルを使っているため）
2. リポジトリのルートで実行する：

```
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/OyatsuBiyori.Server --no-launch-profile -- --reset-demo
```

3. 「デモ環境を作り直しました」と出たら、ふつうに起動する（`dotnet run --project src/OyatsuBiyori.Server`）
4. 最初のログインでパスワードの変更を求められる（初期パスワードのため）

初期アカウントは、アカウントが1つもない DB で起動したときだけ作られる（`AccountBootstrapper`）。すでにアカウントがある DB では、user-secrets の値を変えても使われない。user-secrets で入れたユーザー名で入れないときは、DB に別のアカウントが残っていないか疑い、この手順で作り直す。

## 入るデータ（日付は実行した日からの相対値）

- 条件設定：50人・既定の週の予算 20,000円・ナッツ類（すべて）を避ける
- 商品 約20件：確認済み（20日前）・参考（チラシ「表記なし」・コープのサイト）・不明・要再確認（100日前）・未確認の品目あり・使わない が混ざる。チラシ `samples/flyers/flyer-01` と同じ商品・味違い・内容量違いを含む
- 確定献立：2週前・先週・今週（今週の「いちごゼリー」は「確認し直し」になる）
- 来週の献立はない（チラシの取り込みからのデモに使う）

商品・メーカーの名前はすべて架空。実データは入れない。
