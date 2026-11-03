# CLAUDE.md

## プロジェクト概要

**おやつ日和（oyatsu-biyori）**：学童保育の職員向けの、おやつ献立アシスタント Web アプリ。

コープのチラシを撮影（または画像・PDF をアップロード）するだけで、1週間分のおやつ献立案を Claude が理由つきで提案する。職員はチャットで修正を依頼できる。アレルゲンの最終確認は、必ず職員がパッケージの原材料表示を見て行う。

献立の条件は2種類ある。

- **固定条件**：アレルギー・予算（週ごと。既定の週の予算を、献立ごとに上書きできる）・人数。事前に設定し、アプリ側のロジックで必ずチェックする（Claude の判断に任せない）。子どもは登録せず、施設の「避ける品目」の一覧だけを持つ。全員が同じおやつを食べるため、避ける品目を含む商品は献立に入れない
- **重複ルール**：しょっぱいもの・あまいものは一定期間（1〜2か月）同じ商品を出さない。アプリが警告する
- **週の要望**：それ以外の条件（「しょっぱいものとゼリー系を合わせたい」「金曜は誕生日会なので豪華に」「水曜は子どもが○○人」など）。献立作成時に自然言語で伝え、Claude が読み取って反映する。要望の文章でアレルギーの条件を緩めることはできない

- 利用者：学童保育の職員1人（開発者がいない現場。PC とスマホの両方から使う）。アカウントは1つで、職員の管理機能は作らない
- 背景：ハッカソン「Built with Claude: Japan 2026」（テーマ1「あなたにしかない深さを」）の提出作品
- **締切：2026-11-08（日）20:00 JST**。優先度「必須」の機能から作る
- 要件定義は `docs/requirements/`（要件 ID：`FR-` 機能、`SR-` 安全、`NFR-` 非機能、`HK-` ハッカソンの提出物）。実装前に該当する要件を確認する
- 企画の詳細は `docs/context/kikaku-memo.md`、サーバー構成は `docs/context/server-checklist.md` を参照（どちらも手元のみ。リポジトリには含めない）

### 機能の優先順位

| 優先度 | 機能 |
| --- | --- |
| 必須 | 条件設定（人数、既定の週の予算、施設の避ける品目 ※子どもは登録しない） |
| 必須 | チラシ取り込み（撮影またはファイル（画像・PDF） → 商品抽出 → 既存商品と照合 → 新規登録） |
| 必須 | 献立作成（週の要望を文章で伝え、Claude が理由つきで案を出し、チャットで修正。週の予算に合わせる入れ替え案を含む） |
| 必須 | 商品一覧・編集（パッケージ撮影によるアレルゲン確認を含む） |
| 必須 | 献立一覧（週ごとの状態。献立作成の入口。ログイン直後の画面） |
| 必須 | ランディングページ（ルート。ログイン不要・実データを出さない） |
| あると良い | 献立一覧の中身の表示・検索・印刷 |
| 余裕があれば | 献立一覧（カレンダー形式） |

## 技術スタック

- フロント：React 19 + TypeScript（Vite）。Visual Studio の「React and ASP.NET Core」テンプレートで作成。開発時は SPA プロキシ（`Microsoft.AspNetCore.SpaProxy`）で Vite を起動し、Vite のプロキシで API に接続する。本番ではビルド結果を ASP.NET Core から配信する（`MapStaticAssets()` + `MapFallbackToFile("/index.html")`）
- API：ASP.NET Core（.NET 10）。DI・EF Core・リポジトリパターン
- DB：SQLite（EF Core）
- Claude の呼び出し：公式 C# SDK（NuGet の `Anthropic` パッケージ）
- テスト：xUnit（Biz のロジック）、Playwright（画面の E2E。D-87）。クライアントのユニットテストは置かない
- 本番：XServer VPS for Windows Server（2GB）+ IIS、`https://oyatsu-biyori.com`
- Next.js は使わない

### フォルダ構成

```
OyatsuBiyori.slnx            ソリューション
src/
├── Directory.Build.props    全 C# プロジェクト共通の設定（.NET 10・Nullable など）
├── OyatsuBiyori.Biz/        ドメイン・ユースケース（他に依存しない）
├── OyatsuBiyori.Data/       EF Core（SQLite）による永続化
├── OyatsuBiyori.Migrations/ EF Core のマイグレーション（Data から分けたもの）
├── OyatsuBiyori.Claude/     Claude API の呼び出し
├── OyatsuBiyori.Server/     ASP.NET Core Web API（起動プロジェクト）
└── oyatsubiyori.client/     React（Vite）。e2e/ に画面の E2E（Playwright）
docs/requirements/           要件定義（md が正。index.html は生成物）
samples/                     ダミーデータ（架空のチラシ・パッケージ画像・投入データ）。実データは置かない
tools/                       開発用の道具（アプリには含めない）
```

- 依存方向は `Server → Data / Claude → Biz`。マイグレーションは `Migrations → Data` で参照し、`Server → Migrations` で読み込む（Data から Migrations は参照しない。Data で `MigrationsAssembly("OyatsuBiyori.Migrations")` を指定する。D-40）
- コーディング規約は `.claude/rules/` にある（C#：`csharp.md`、React：`react.md`）。該当ファイルを編集するときは必ず従う
- 新しいプロジェクトやフォルダ構成の変更は、ユーザーが決める。勝手に作らず、必要になったら提案して確認を取ること（テストプロジェクトは `src/OyatsuBiyori.Biz.Tests/`（xUnit）を1つ、マイグレーションは `src/OyatsuBiyori.Migrations/`、画面の E2E は `src/oyatsubiyori.client/e2e/` と決定済み）

### よく使うコマンド

- ビルド：`dotnet build OyatsuBiyori.slnx`
- 起動：`dotnet run --project src/OyatsuBiyori.Server`（Vite も自動で起動する）
- フロントの確認：`src/oyatsubiyori.client` で `npm run lint` と `npm run build`
- コミット前の確認：`python tools/precheck/precheck.py`（差分からリスクの区分を決め、ビルド・テスト・lint・E2E のうち必要なものを走らせる。`--dry-run` で区分だけ見る）
- 画面の E2E：`src/oyatsubiyori.client` で `npm run e2e`（サーバーを E2E の環境・偽の Claude・ダミーデータで起動する。T0-15 で作る）
- 本番用の出力：`dotnet publish src/OyatsuBiyori.Server -c Release`
- 要件定義の HTML を作り直す：`python tools/build-requirements-html/build.py`（`docs/requirements/*.md` を直したら必ず実行する。`index.html` は直接編集しない）
- ダミーのチラシ・パッケージを作り直す：`python tools/build-samples/build.py`（`samples/flyers/*.json`・`samples/packages/*.json` を直したら実行する。画像・PDF・正解データ（`*.expected.json`）を作る。Edge か Chrome が必要）
- 実装タスクの HTML を作り直す：`python tools/build-tasks-html/build.py`（`docs/implementation-tasks/implementation-tasks.md` を直したら実行する。出力は同じフォルダの `index.html`）

## Claude（API）の使い方

- モデルは用途ごとに選ぶ。既定は `claude-sonnet-5-5`、商品照合の判定は `claude-haiku-5-5`。献立提案で精度が足りない場合のみ `claude-opus-5-5` を検討する（対応表は `docs/requirements/02-functional.md` の 9）
- モデル名はコードに直書きせず、用途ごとに設定（`appsettings.json` など）から読む
- 献立案はアプリ（Biz）で検証し、問題を Claude に返して直させる（FR-MENU-15）。Claude の返答でアレルゲンの判定を変えない（SR-18）
- API キーはリポジトリに入れない。開発時は `dotnet user-secrets` か環境変数 `ANTHROPIC_API_KEY` を使う
- 主な用途：チラシの読み取り（画像入力）、原材料表示からのアレルゲン抽出、週の要望の読み取りと献立の提案・理由の説明（1回の呼び出し。D-66）、チャットでの組み直し、商品照合での「同じ商品・味違い・内容量違い・別商品」の判定

## アレルゲンの扱い（最重要・安全に直結）

子どもの安全に関わるため、以下は必ず守る。迷ったら「確認する側」に倒す。

- ステータスは3段階：**確認済み**（職員がパッケージで確認）／**参考**（サイトやチラシの情報。要確認）／**不明**
- **「不明」の商品を「食べられる」扱いにしない**。避ける品目が1つでもあれば、「不明」の商品は献立に入れられない。「参考」の商品を献立に使う場合は警告を出し、確定前に確認を求める
- 「含む」品目は最新の記録1件ではなく「有効な記録」（最新の確認済みと、それより後のすべての記録）から求める。「参考」の記録で「含む」は減らない
- 判定はステータスより先に「含む」「含む可能性あり」を見る。結果は「入れられない（エラー。確定できない）/ 警告（確認すれば確定できる）/ 安全」の3つ（`docs/requirements/03-allergen-safety.md` の 5）
- 「探す言葉」は商品名と原材料の全文の両方で探す。チラシの取り込みで「確認済み」の商品に「参考」の記録を足さない（SR-14・SR-20）
- AI の判定結果はあくまで候補。確定は必ず職員が行う UI にする
- **29品目に入らない食材がある**（ヘーゼルナッツなど。現場で見落としの実例あり）。施設で追加した品目は、アレルゲン表示ではなく原材料表示の全文から探す。「ナッツ類」はグループとして扱う（詳細は `docs/requirements/03-allergen-safety.md`）
- 2026年4月の改正で **29品目**（特定原材料9・準ずるもの20）になった。表示義務があるのは特定原材料だけで、カシューナッツは2028-03-31 まで経過措置中。推奨20品目と経過措置中のカシューナッツ（マスタの「表示が確実でない」品目）は表示されないことがあるため、避ける品目に含まれる場合は「表示なし＝安全」とせず「メーカーへの確認が必要」と表示する
- アレルゲン確認は上書きせず、履歴（`allergen_checks`）として積み上げる。最新の記録を現在の状態とする。確認日・確認者を記録し、一定期間（例：3か月）経ったら「要再確認」に戻す

## データ設計の方針

- 主キーはアプリ側で振る UUID。JAN コードは NULL 可・UNIQUE（チラシには JAN がないため）
- 商品照合：保存時と検索時に同じ正規化関数を使う（NFKC、カタカナ→ひらがな、「CO・OP」「コープ」の除去、記号・空白の除去）。ルールで決められるもの（完全一致・内容量違い）はアプリが機械的に決め、それ以外は文字バイグラムの Dice 係数で上位5件 → 迷うものだけ Claude（Haiku）が判定 → 職員が確定。迷う場合は別商品として扱う（D-63）
- 商品は数百件規模の想定なので、類似度はアプリ側で全件計算してよい

## 公開リポジトリのルール

リポジトリは公開される。以下は**絶対にコミットしない**。

- 子どもの情報、実際の商品データ、チラシやパッケージの画像
- API キー、パスワード、サーバーや自宅の IP アドレス
- `docs/context/`（`.gitignore` で除外済み）

テストやサンプルには**ダミーデータ**だけを使う。他社のデータベースをスクレイピングして取り込むコードも書かない。

## 開発の進め方

- **コミットとプッシュは、ユーザーが指示したときだけ行う**
- 開発は `main` ブランチで直接行う（1人で開発するため、作業用のブランチは切らない。審査ではリポジトリの既定ブランチが見られるので、`main` を常に最新に保つ）
- **コミット前の手順は `REVIEW.md` に従う**。変えたものでリスクの区分（高・中・低）を決め、区分に応じて確かめる
  - 中・高：ビルド・テスト・lint（`dotnet build OyatsuBiyori.slnx`・`dotnet test src/OyatsuBiyori.Biz.Tests`・フロントの `npm run lint` と `npm run build`。画面を変えたら `npm run e2e` も）を `python tools/precheck/precheck.py` でまとめて実行し（確認していない差分のコミットはフックが止める）、その結果をユーザーに見せる。そのうえで `/code-review` で差分をレビューする
  - 高：アレルゲンの判定・献立の検証・アレルゲン確認の記録・Claude の出力の扱いに触れる変更は、サブエージェント `allergen-safety-reviewer` でもレビューする。コミットメッセージの本文に「確かめ方・リスク・戻し方」を書く
  - レビューの往復は3回まで。同じ種類の指摘が2回出たら、`CLAUDE.md`・`.claude/rules/`・`REVIEW.md` の方に書き足す
- CI（`.github/workflows/ci.yml`）：main への push ごとに、ビルド・Biz のテスト・フロントの lint とビルドを走らせる。失敗したら次の作業より先に直す
- フック（`.claude/settings.json`）：コミット前に公開してはいけないもの（`docs/context/`・DB・`samples/` 以外の画像や PDF・API キー・IP アドレス）を確かめて止める。中・高の変更で `precheck.py` が通っていない差分のコミットも止める。Biz の `.cs` を編集すると xUnit を走らせる
- コードのコメント・コミットメッセージ・UI の文言は**日本語**。識別子（クラス名・変数名など）は英語
- ロジック（アレルゲン判定、金額・1人あたり金額・予算の実績の計算、予算に合わせる入れ替え案の探索、重複チェック、商品名の正規化・照合）には xUnit のユニットテストを書く
- 本番サーバー上ではビルドしない。手元で `dotnet publish` した成果物を配置する
- 本番は HTTPS 必須。実データを扱う前に最低限のログイン機能を入れる
