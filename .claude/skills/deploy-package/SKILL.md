---
name: deploy-package
description: おやつ日和を本番（IIS）に配置するための成果物を手元で作る。dotnet publish・本番に当て済みのマイグレーションからの差分の SQL（BOM なし）・中身の確かめ（空の DB で当て済みの状態を再現して差分を当てる）・zip までを行い、サーバーで行う手順を実際のパスで示す。「デプロイ用を作って」「本番に出したい」「配置の準備をして」「publish して」など、2回目以降の配置のときに使う。サーバーの操作そのもの（RDP の先）はできないので、手順を示してユーザーに行ってもらう。
---

# 配置の成果物を作る（T8-02 の2回目以降）

手順の正は手元の `docs/context/deploy-runbook.md`（公開しない）。**最初に必ず読む**。実際のサーバーのパス・本番に当て済みの最後のマイグレーション・前回の配置の記録はそこにある。このファイル（公開される）にはサーバーのパスや ID を書かない。

## 1. 前提を確かめる

1. `git status --short` を見る。コミットしていない変更があれば、件数を伝え「コミットしてから作るか、作業ツリーのまま作るか」を聞く（どちらでもよいが、作業ツリーから作ったら記録にそう書く）。コミットはユーザーが指示したときだけ
2. 手順書の「本番に当て済みの最後のマイグレーション」を読む。書いていない・古いかもしれないときは、ユーザーにサーバーで次を実行してもらい、いちばん下の ID を教えてもらう（推測しない）：
   ```
   sqlite3 <本番の DB のパス> "select MigrationId from __EFMigrationsHistory order by MigrationId;"
   ```
3. **D-100（チラシ取り込みの作り直し）を初めて出すとき**は、出す前に、本番の DB で**メーカーが空の商品の件数**をユーザーに確かめてもらう。D-100 から照合は「商品名＋メーカー」の完全一致だけになり、メーカーが空の商品はどのチラシの商品とも一致しなくなる（次の取り込みで新しい商品として二重に登録される。[09 の 9](../../../docs/requirements/09-flyer-import.md#9-データで変えること)・FR-IMPORT-03）。読むだけの SQL なので、アプリを止めなくてよい：
   ```
   sqlite3 <本番の DB のパス> "select count(*) from products where IsDisabled = 0 and trim(replace(coalesce(Maker, ''), '　', '')) = '';"
   sqlite3 <本番の DB のパス> "select Id, Name, Volume, ListedWeekStart from products where IsDisabled = 0 and trim(replace(coalesce(Maker, ''), '　', '')) = '' order by Name;"
   ```
   - 0件なら、そのまま進める
   - 1件以上なら、一覧（2つめの SQL）をユーザーに見てもらい、**配置の前か直後（次の取り込みより前）に、商品一覧でメーカーを入れてもらう**。件数と対応を手順書の「配置の記録」に書く。DB を SQL で直接書き換えない（更新者・更新日時が残らないため）
   - 記号だけのメーカー（「－」など）も、アプリの正規化では空になる。2つめの SQL の対象外なので、`select Maker, count(*) from products group by Maker order by 2 desc;` でメーカーの値を一度見てもらう
   - 同じ名前＋メーカーの商品が2件以上あっても（内容量違いを別に登録した名残など）まとめなくてよい（最後に載った週が新しい方を使う。FR-IMPORT-04）

## 2. 成果物を作る

出力先は `%TEMP%\oyatsu-pub-<yyyyMMdd-HHmm>`（作った日時。同じ日に何度作っても区別できる。zip の名前にも同じ日時を付ける）。PowerShell の変数は呼び出しをまたいで残らないので、以降の手順の `$out` は、ここで出たフォルダのパスを入れ直して使う：

```powershell
$out = "$env:TEMP\oyatsu-pub-$(Get-Date -Format yyyyMMdd-HHmm)"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
Write-Output "OUT=$out"
dotnet publish src/OyatsuBiyori.Server -c Release -o "$out\app"
dotnet ef migrations list --project src/OyatsuBiyori.Migrations --startup-project src/OyatsuBiyori.Server
```

- `migrations list` で、当て済みの ID より後ろにあるものが今回当てるもの。**なければマイグレーションは不要**（SQL を作らず、サーバーの手順から SQL の行を外す）
- あれば、差分の SQL と、検証用に「当て済みまで」の SQL を作る：
  ```powershell
  $applied = '<当て済みの最後の ID>'
  $ef = '--project','src/OyatsuBiyori.Migrations','--startup-project','src/OyatsuBiyori.Server'
  dotnet ef migrations script $applied @ef -o "$out\migrate.sql"
  dotnet ef migrations script 0 $applied @ef -o "$out\base-for-check.sql"
  $t = [IO.File]::ReadAllText("$out\migrate.sql"); [IO.File]::WriteAllText("$out\migrate-nobom.sql", $t, (New-Object Text.UTF8Encoding $false))
  ```
  `<当て済みの ID>` を渡すと「そこまでは当て済み」の意味になり、それより後ろだけが出る。SQLite では `--idempotent` が使えない（NFR-OPS-01a）

## 3. 確かめる

**成果物**（すべて満たさなければ止めて報告する）：
- `app\wwwroot\index.html` がある
- `app\appsettings.E2E.json` と `*.db*` がない
- `app\appsettings.json` に秘密（API キー・パスワード）がなく、`ConnectionStrings:SQLite` が空のまま
- `app\web.config` が `hostingModel="inprocess"` で `maxAllowedContentLength` がある
- `OyatsuBiyori.Claude.dll` にプロンプト（`OyatsuBiyori.Claude.Prompts.*.md`）が入っている。数は `src/OyatsuBiyori.Claude/Prompts/*.md` と同じ

**差分の SQL**（マイグレーションがあるとき）：

```
python -I .claude/skills/deploy-package/check_migration.py "<out>/base-for-check.sql" "<out>/migrate-nobom.sql"
```

当て済みの状態を空の DB で再現し、差分を当て、記録の行・`integrity_check`・危ない文（`DROP TABLE`・`RENAME`・`DELETE FROM`）を調べる。危ない文が出たら、どの表のデータが消える・移るのかを SQL を読んで説明し、ユーザーの OK を取ってから進める（SQLite では列の変更が「新しい表を作ってコピーし、古い表を消す」形になる。コピーの `INSERT INTO ... SELECT` があれば、データは移る）。

## 4. zip にする

```powershell
$items = @("$out\app", ".claude\skills\deploy-package\app_offline.htm"); if (Test-Path "$out\migrate-nobom.sql") { $items += "$out\migrate-nobom.sql" }
$stamp = (Split-Path $out -Leaf) -replace '^oyatsu-pub-', ''   # フォルダと同じ日時（yyyyMMdd-HHmm）
Compress-Archive -Path $items -DestinationPath "$out\oyatsu-biyori-deploy-$stamp.zip" -Force
```

`base-for-check.sql` と BOM ありの `migrate.sql` は入れない（検証用・予備）。`app_offline.htm`（メンテナンス中のページ）は `app` の外、zip の直下に入れる。

## メンテナンス中のページ（app_offline.htm）

アプリのフォルダの直下に `app_offline.htm` を置くと、ASP.NET Core Module がアプリを止め（DLL と DB のロックが外れる）、すべての要求にこのページを 503 で返す。消すとアプリが起動する。

- **アプリケーションプールは止めない**。プールを止めると IIS が素の「503 Service Unavailable」を返し、このページは出ない
- アプリが止まっているので、ページの中身（CSS・アイコン）はすべてファイルの中に書く。外のファイルを読ませない
- `robocopy ... /MIR /XF app_offline.htm` で、入れ替えのあいだも消されずに残る

## 5. 伝える・記録する

- zip のフルパス、作った元（コミットか作業ツリーか）、今回当てるマイグレーション（なし / ID の一覧）、確かめた結果を伝える
- 手順書の「0. 置き場所」の実際のパスで、サーバーの手順（手順書の 11. を短くしたもの）を示す：`app_offline.htm` を app の直下に置いて止める（数秒待つ） → **バックアップ（必ず先に）** → 前の app を残す → 入れ替え → （あれば）SQL を `-bail` で当てて `integrity_check` → `app_offline.htm` を消して起動 → ログインとチラシ1件の読み取りで確かめる
- 手順書の「1. 手元で作るもの」の「作成済み」の行を今回のものに書き換える
- ユーザーから「載せた」と聞いたら、手順書の「本番に当て済みの最後のマイグレーション」と「配置の記録」を更新する。`implementation-tasks.md` は、T8 のタスクの状態が変わるときだけ直す（直したら `python tools/build-tasks-html/build.py`）

## してはいけないこと

- サーバー上でビルド・`dotnet ef database update` をしない（手元で SQL を作ってサーバーで当てる。D-42）
- 当て済みの ID を推測しない。違う ID から作ると、同じ SQL を2回当てて失敗するか、足りない SQL になる
- 本番の DB に `reset-demo` やダミーデータを入れない
- サーバーのパス・IP・キーをこのファイルやリポジトリの他のファイルに書かない（手順書は `docs/context/` で公開しない）
