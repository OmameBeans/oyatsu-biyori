---
name: reset-demo
description: おやつ日和の手元のデモ環境（開発用の SQLite）を作り直し、TD-02 のダミーデータ（架空の商品・確定献立・確認の履歴・条件設定）を入れる。「デモ環境を作り直して」「ダミーデータを入れて」「画面の画像を撮る前に初期化して」などのときに使う。本番では使わない。
---

# デモ環境の作り直し（TD-02）

手元の開発用 DB（`src/OyatsuBiyori.Server/oyatsu-biyori.dev.db`）を消して作り直し、ダミーデータを入れる。中身は `ResetDemoAsync`（`src/OyatsuBiyori.Server/E2E/E2EEnvironment.cs`）で、DB を消す → マイグレーションをすべて当てる → 初期アカウントを作る（`AccountBootstrapper`）→ `DemoDataSeeder` でダミーデータを入れる、の順に進む。

## 前提

- 開発の環境（`ASPNETCORE_ENVIRONMENT=Development`）だけで動く。本番・IIS・`.dev.` / `.e2e.` を含まない接続文字列では `DemoDataGuard` が止める
- 初期アカウントは user-secrets で入れておく（リポジトリには入れない）：

```
dotnet user-secrets set "Auth:InitialUserName" "<ユーザー名>" --project src/OyatsuBiyori.Server
dotnet user-secrets set "Auth:InitialPassword" "<初期パスワード>" --project src/OyatsuBiyori.Server
```

## 手順

1. 起動中のサーバーがあれば止める（DB ファイルを使っているため）
2. リポジトリのルートで実行する（Bash ツール）：

```
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/OyatsuBiyori.Server --no-launch-profile -- --reset-demo
```

PowerShell の場合：

```
$env:ASPNETCORE_ENVIRONMENT = 'Development'; dotnet run --project src/OyatsuBiyori.Server --no-launch-profile -- --reset-demo
```

3. 最後に「デモ環境を作り直しました」と出て、その前のログに「ダミーデータを入れました（商品 19件）」があれば成功。ふつうに起動する（`dotnet run --project src/OyatsuBiyori.Server`）
4. 最初のログインでパスワードの変更を求められる（初期パスワードのため）

初期アカウントは、アカウントが1つもない DB で起動したときだけ作られる（`AccountBootstrapper`）。すでにアカウントがある DB では、user-secrets の値を変えても使われない。user-secrets で入れたユーザー名で入れないときは、DB に別のアカウントが残っていないか疑い、この手順で作り直す。

## 入るデータ（日付は実行した日からの相対値）

- 条件設定：50人・既定の週の予算 20,000円・ナッツ類（すべて）を避ける・重複の補足ルール「味違いの商品は、同じ週に続けて出さない」。出す曜日・再確認の期間・重複させない期間はマスタの既定値のまま
- マスタ（アレルゲン・探す言葉・チラシの表記の略記・グループ）：マイグレーションの初期データで入る（シーダーでは入れない）
- 商品 19件：確認済み（20日前）・参考（チラシ「表記なし」・コープのサイト）・不明・要再確認（100日前）・未確認の品目あり（松の実）・使わない が混ざる。確認の記録は 16件
  - チラシ `samples/flyers/flyer-01`〜`flyer-03` と同じ商品（注文番号もチラシにそろえる）：やさいせんべい（flyer-01）・ミルクビスケット（flyer-02）・いちごゼリー・ヨーグルトゼリー（flyer-03）
  - 取り込みの分類（D-100。[09](../../../docs/requirements/09-flyer-import.md)）の確かめ用：ポテトチップス うすしおは、flyer-02 の のりしお と名前が違うので別の商品（取り込むと「要確認」。アレルゲン確認を引き継がない）。さくさくクッキー（flyer-02）・小魚あられ・海苔せんべい（flyer-01）は、チラシと名前＋メーカーが同じで内容量が違う同じ商品（取り込むと「再入力」で、マスタとチラシの内容量・入数を並べて示す。確定すると内容量・入数をチラシの値で上書きする）
- 確定献立：2週前・先週・今週（今週の金曜の「いちごゼリー」は「確認し直し」になる）
- 来週の献立はない（チラシの取り込みからのデモに使う）
- 空のまま始まるもの：
  - 注文リストの「注文した」のチェック（`menus.OrderChecks`）。確定献立の注文リストは、すべて未チェックで出る
  - 献立づくりのエージェントのセッション（`menus.AgentSessionId`・`AgentSessionFingerprint`。D-94）。献立案を作ったときに新しく作られる。作り直しても、Anthropic 側の古いセッションは消えない（手元の DB から参照されなくなるだけ）
  - API の使用記録（`api_usage_logs`）。1日の上限（`Claude:DailyCostLimitUsd`。D-95）の数え直しにもなる
  - チャットの履歴・週の要望・Claude が読み取った要望

商品・メーカーの名前はすべて架空。実データは入れない。

## 列や表が増えたときの見直し

マイグレーションを足したら、このスキルとシーダーが追いついているかを確かめる。

1. 前回シーダーを直してから足したマイグレーションを見る：`git log --oneline -- src/OyatsuBiyori.Server/DemoData` と `src/OyatsuBiyori.Migrations/Migrations/` の一覧を比べる
2. 足した列ごとに決める
   - NULL 可・空の配列で困らない（セッション・チェックの記録など）→ シーダーは直さず、上の「空のまま始まるもの」に書き足す
   - 必須・デモで見せたい（新しい条件設定など）→ `DemoDataSeeder` で値を入れ、上の「入るデータ」に書く
   - マスタ（`HasData`）→ マイグレーションで入るので、シーダーは直さない
3. 手元の DB を消さずに通るか試す（接続文字列をスクラッチパッドの一時ファイルに向ける。ファイル名に `.dev.` を含める）：

```
ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__SQLite="Data Source=<スクラッチパッド>/check.dev.db" dotnet run --project src/OyatsuBiyori.Server --no-launch-profile -- --reset-demo
```

4. E2E（`npm run e2e`）も同じ `DemoDataSeeder` を使う（`includeE2EDraft: true` で下書き2件が増える）。シーダーを直したら E2E も走らせる
