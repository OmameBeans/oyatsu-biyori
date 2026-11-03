# 04. データ要件

論理的なデータ構造を示す。物理設計（型・インデックス・マイグレーション）は実装時に決める。

## 1. 方針

- 主キーはアプリ側で振る UUID（文字列）
- すべてのエンティティに更新日時・更新者を持たせる（`EntityBase`）。更新者は NULL 可（マスタのシードと、システムの処理で更新するため）。シードの主キーと日時は固定の値にする（[07 の D-80](07-open-questions.md)）
- JAN コードは NULL 可・UNIQUE（チラシには JAN がないため）
- アレルゲン確認は上書きせず、履歴として積み上げる
- チラシ・パッケージの画像は保存しない。抽出した事実だけを残す
- チラシ自体は保存しない。価格・注文番号などは取り込むたびに商品に上書きする。献立の候補は、その献立に保存した掲載品の写し（`menus.listed_products`）で決める（FR-FLYER-07）
- 価格は号ごとに変わるため、献立の商品には献立に入れた時点の価格・注文番号を保存する（過去の献立の金額が変わらないようにする）
- 日時は UTC で保存する。SQLite は日時が UTC かどうかを保存しないため、読み出した日時はすべて UTC として扱う（画面で9時間ずれないようにする。D-80）。「今日」「週（月曜始まり）」「月」の判定は日本時間（Windows のタイムゾーン ID は `Tokyo Standard Time`）の日付で行う。日付だけの項目（`week_start`・`date`）は日本時間の日付をそのまま持つ
- アレルゲンの判定（入れられない / 警告 / 安全）と、届いた後の警告（FR-MENU-36）は保存せず、表示・検証のたびに求める（[07 の D-15](07-open-questions.md)）

## 2. エンティティ一覧

```
settings ─────────── 施設の条件（1件）
users ────────────── 職員アカウント（1件）
avoided_allergens ── 施設の避ける品目（子どもは登録しない）
safety_setting_logs ─ 避ける品目・探す言葉の変更の記録（追加のみ）
api_usage_logs ───── Claude API の呼び出しの記録（費用・1日の上限）
allergens ────────┬── アレルゲンマスタ（29品目 + 施設で追加した品目）
                  └── allergen_keywords ── 原材料名で探す言葉
allergen_groups ──┬── グループ（ナッツ類など）
                  └── allergen_group_members

products ─────┬───── 商品
              ├── product_tags ────── タグ（キャラクター・味・種類）
              └── allergen_checks ─── アレルゲン確認の記録（追加のみ）
                      └── allergen_check_items ── 品目ごとの含む / 含まない

menus ────────┬───── 週の献立
              ├── menu_days ───────── 日ごとの献立
              │       └── menu_items ── 日ごとの商品
              └── menu_messages ───── チャットの履歴
```

## 3. 項目

### settings（施設の条件）

| 項目 | 説明 |
| --- | --- |
| default_children_count | 通常時の人数（未設定を許す。未設定のあいだは献立を作らず、条件設定へ案内する） |
| serving_weekdays | おやつを出す曜日 |
| recheck_days | アレルゲンの再確認期間（日） |
| duplicate_avoid_days | 分類ごとの重複させない期間。`{ 分類, 日数 }` の配列（JSON）。例：`[{"snack_type":"salty","days":30},{"snack_type":"sweet","days":30}]`。分類は products の snack_type の値（「対象外」は不可）、同じ分類は1件まで、日数は1以上の整数。配列にない分類は制限なし |
| default_weekly_budget | 既定の週の予算（円・税込）。献立を作るときに menus.weekly_budget に写す。未設定を許す（未設定のあいだは献立を作らず、条件設定へ案内する） |
| duplicate_rule_note | 重複についての補足ルール（自由記述・NULL 可・Claude に渡す。期間の設定を緩めることはできない） |

### users（職員）

使うのは職員1人なので、レコードは1件だけ（ASP.NET Core Identity のテーブルを使う）。

| 項目 | 説明 |
| --- | --- |
| id | 主キー（Identity の `Id`。checked_by・added_by が参照） |
| user_name | ログイン名（Identity の `UserName`） |
| password_hash | パスワードのハッシュ（Identity の `PasswordHash`） |
| must_change_password | パスワードの変更が必要か（初期パスワードのままのあいだ立てる。ログインした直後にパスワード変更の画面を出すのに使う。FR-AUTH-03b・[07 の D-55](07-open-questions.md)） |

### avoided_allergens（施設の避ける品目）

子どもは登録しない。全員が同じおやつを食べるため、施設として避ける品目だけを持つ。

| 項目 | 説明 |
| --- | --- |
| allergen_id | → allergens（UNIQUE） |
| added_at / added_by | 追加した日時・職員（→ users） |

- 追加・削除の記録は `safety_setting_logs` に残す（FR-AVOID-06）

### safety_setting_logs（安全に関わる設定の変更の記録）※追加のみ

| 項目 | 説明 |
| --- | --- |
| operated_at / operated_by | 操作した日時・職員（→ users） |
| kind | 避ける品目の追加 / 避ける品目の削除 / 探す言葉の追加 / 探す言葉の削除 / 再確認期間の変更 |
| allergen_id | 対象の品目の ID（再確認期間の変更では NULL）。外部キーにしない（施設の追加品目を削除しても、記録を残すため） |
| detail | 内容。品目名も書き、品目を削除したあとも何の記録か分かるようにする（例：ヘーゼルナッツの探す言葉「プラリネ」、再確認期間 90日 → 60日） |

- 施設の追加品目そのものの登録・削除は記録しない（[07 の D-72](07-open-questions.md)）

### allergens（アレルゲンマスタ）

| 項目 | 説明 |
| --- | --- |
| code | 識別子（例：`egg`, `milk`） |
| name | 表示名（例：「卵」） |
| category | 特定原材料 / 特定原材料に準ずるもの / 施設で追加 |
| label_not_guaranteed | 表示が確実でない品目か（推奨20品目と、経過措置中のカシューナッツ）。SR-06 の「メーカーへの確認が必要」に使う。制度が変わったらマスタの値を変える |
| sort_order | 表示順 |

- 施設の追加品目は、確認の記録（`allergen_check_items`）・避ける品目（`avoided_allergens`）・グループ（`allergen_group_members`）のどれかから参照されていれば削除できない。`safety_setting_logs` からの参照は数えない（[07 の D-56](07-open-questions.md)）

### allergen_keywords（原材料名で探す言葉）

| 項目 | 説明 |
| --- | --- |
| allergen_id | → allergens |
| keyword | 原材料名で探す言葉（例：「ヘーゼルナッツ」「ジャンドゥーヤ」「プラリネ」） |
| normalized_keyword | 正規化した言葉（検索に使う） |
| is_seed | マスタの初期データとして入れた言葉か。初期データの言葉は削除できず、施設が足した言葉だけ削除できる（[07 の D-50](07-open-questions.md)） |

### allergen_groups / allergen_group_members（グループ）

| 項目 | 説明 |
| --- | --- |
| name | グループ名（例：「ナッツ類（すべて）」） |
| allergen_id（members） | グループに含まれる品目 → allergens |

### products（商品）

| 項目 | 説明 |
| --- | --- |
| jan_code | JAN コード（NULL 可・UNIQUE） |
| name | 商品名（チラシ・パッケージの表記） |
| normalized_name | 正規化した商品名（保存時に生成） |
| maker | メーカー |
| volume | 内容量（例：「10袋入」「120g」） |
| pieces_per_pack | 入数（注文できる単位1つで何人分か。献立の個数計算に使う。NULL の商品は献立の候補にしない） |
| default_per_child_count | 1人あたりの個数の既定値（既定1。献立に入れるときに menu_items に写す） |
| snack_type | おやつの分類：しょっぱいもの / あまいもの / のどごしのいいもの / 対象外（重複ルールと組み合わせに使う） |
| listed_week_start | 最後にチラシに載った週の初日（NULL 可。商品一覧での表示用。献立の候補は menus.listed_products で決める） |
| price | 税込価格（注文できる単位1つあたり。チラシを取り込むたびに上書き。NULL の商品は献立の候補にしない） |
| price_excluding_tax | 税抜価格（チラシに載っていれば。取り込むたびに上書き） |
| order_no | 注文番号（号ごとに変わるため、取り込むたびに上書き） |
| target_age_label | 対象年齢の表示（例：「1歳頃から」） |
| memo | 職員のメモ（「不人気」など） |
| is_disabled | 使わない商品か |

### product_tags（商品のタグ）

| 項目 | 説明 |
| --- | --- |
| product_id | → products |
| kind | キャラクター / 味・種類 |
| value | タグの値。キャラクターは名前をそのまま持つ。味・種類は決まった語（「スナック」「ゼリー」など。語の一覧は [02 の FR-FLYER-03a](02-functional.md#3-チラシ取り込みfr-flyer)）から選ぶ（[07 の D-61・D-62](07-open-questions.md)） |
| source | Claude の読み取り / 職員が入力 |

### allergen_checks（アレルゲン確認の記録）※追加のみ

| 項目 | 説明 |
| --- | --- |
| product_id | → products |
| status | 確認済み / 参考（「不明」の記録は作らない。記録のない商品が「不明」。[03 の 3](03-allergen-safety.md#3-ステータス)） |
| source | パッケージ / コープのサイト / チラシ / その他（「確認済み」はパッケージだけ） |
| source_note | 情報源の補足（例：チラシの表記の原文「乳・小麦」「表記なし」、「コープのサイトで確認」）。「表記なし」かどうかは、情報源がチラシで「含む」品目がないことから求め、印の列は持たない（[07 の D-16](07-open-questions.md)） |
| raw_ingredients_ocr | Claude が読み取った原材料表示の全文（手入力のときは NULL） |
| raw_ingredients | 職員が確かめた原材料表示の全文（読み違いを直したもの・手で入力したもの。「確認済み」では必須。[SR-12](03-allergen-safety.md)）。探す言葉の検索は、この列と `raw_ingredients_ocr` の両方にかける |
| contamination_note | 同一工場・コンタミの注意書き |
| checked_at | 確認日時 |
| checked_by | 確認者（→ users） |

### allergen_check_items

| 項目 | 説明 |
| --- | --- |
| allergen_check_id | → allergen_checks |
| allergen_id | → allergens |
| contains | 含む / 含まない |

- 「確認済み」の記録では、29品目と、記録した時点で避ける品目に入っていた施設の追加品目をすべて保存する
- 「参考」の記録では「含む」の品目だけを保存する。行がない品目は「未確定」で、「含まない」ではない

### menus（週の献立）

| 項目 | 説明 |
| --- | --- |
| week_start | 対象週の初日（月曜。UNIQUE。1つの週に献立は1つ） |
| note | 週のメモ（NULL 可。「ハロウィン会」など。FR-LIST-00a） |
| weekly_budget | その週の予算（円・税込）。作るときに settings.default_weekly_budget を写し、職員が上書きできる（店で買うおやつの日がある週は下げる など） |
| listed_products | その週の掲載品の写し。`{ product_id, price, order_no }` の配列（JSON）。チラシの取り込みを確定したときに保存し、取り込み直すと置き換える。献立の候補・「その週の掲載品」の判定・献立に入れるときの価格に使う（FR-FLYER-07） |
| excluded_product_ids | 「今週は使わない」と指定した商品の ID の配列（JSON。FR-MENU-07b） |
| request_text | 職員が入力した週の要望（原文） |
| interpreted_requests | Claude が読み取った要望の一覧（「こう理解しました」の内容） |
| status | 下書き / 確定。確定済みの献立を直し始めたら下書きに戻す（[07 の D-54](07-open-questions.md)）。警告の商品にチェックを付け直すだけ（`menu_items.acknowledged_at`・`acknowledged_judgement` の更新）では確定のまま（D-71） |
| confirmed_at, confirmed_by | 確定日時・確定者 |
| row_version | 同時に保存したときの衝突を見つけるための値（EF Core の同時実行トークン。FR-MENU-31b）。行を更新するたびに SQLite のトリガーで新しい値にする（[07 の D-46](07-open-questions.md)）。`menu_days`・`menu_items` だけを変える保存でも、必ず `menus` の行を更新して値を進める |

### menu_days（日ごとの献立）

| 項目 | 説明 |
| --- | --- |
| menu_id | → menus |
| date | 日付 |
| children_count | その日の人数（通常人数、または要望・職員の修正で変えた値） |
| children_count_source | 人数の出どころ：通常 / 要望から読み取り / 職員が修正 |
| day_type | 通常 / 手作り / おやつなし（祝日・休みも「おやつなし」で表す） |
| day_type_edited | 日の種類を職員が直したか |
| label | 日の見出し（NULL 可。「誕生日会」など。要望から読み取るか、職員が入力） |
| label_edited | 日の見出しを職員が直したか |
| reason | Claude が示した理由 |

- 「要望を直す」で献立案を作り直しても、職員が直した値（人数の出どころが「職員が修正」のもの・`day_type_edited`・`label_edited` が立っているもの）は、要望の読み取りで上書きしない（[07 の D-57](07-open-questions.md)）

### menu_items（日ごとの商品）

| 項目 | 説明 |
| --- | --- |
| menu_day_id | → menu_days |
| product_id | → products |
| unit_price | 1パックの税込価格（献立に入れた時点の値を保存。掲載品の写しの価格。チラシを取り込み直しても変わらない） |
| order_no | 注文番号（献立に入れた時点の値を保存。注文リストに使う） |
| per_child_count | 1人あたりの個数（既定1） |
| pack_count | 必要パック数（保存する値）。既定は式（[02 の金額計算の定義](02-functional.md#金額計算の定義)）で求める。入れ替え案（パック数の ±1）と職員の修正で上書きでき、その日の人数か1人あたりの個数を変えたら式で求め直す。全員分に足りない数なら警告する（[07 の D-43](07-open-questions.md)） |
| amount | 金額（pack_count × unit_price の計算値）。日の種類が「手作り」「おやつなし」の日は、商品があっても0のまま（[07 の D-77](07-open-questions.md)） |
| acknowledged_at | 確定時に、この商品の警告を職員が確認した日時（NULL＝未確認） |
| acknowledged_judgement | 確認したときの判定（参考 / 要再確認 / 未確認の品目あり など）。今の判定と違えば、確認していない扱いに戻す（[SR-22](03-allergen-safety.md)） |

### menu_messages（チャットの履歴）

| 項目 | 説明 |
| --- | --- |
| menu_id | → menus |
| role | 職員 / Claude |
| content | 本文 |
| created_at | 送った日時 |

### api_usage_logs（Claude API の呼び出しの記録）

| 項目 | 説明 |
| --- | --- |
| called_at | 呼び出した日時 |
| purpose | 用途（チラシの読み取り / 読み取りの検証 / 照合の判定 / アレルゲン抽出 / 献立の提案（要望の読み取りを含む。D-66） / チャット など） |
| model | モデル名 |
| input_tokens / output_tokens | 入力・出力のトークン数（キャッシュの分も分けて持つ） |
| succeeded | 成功したか |

- 本文・画像は保存しない。1週間あたりの費用の計算（NFR-COST-03・04）と、1日の呼び出し回数の上限（NFR-COST-05）に使う

## 4. サンプルデータ

- リポジトリに入れるシード・テストデータは**ダミーのみ**（架空の商品名）
- 実データの入った SQLite ファイルはコミットしない（`.gitignore` で除外済み）
- マスタの初期データ（29品目・施設の追加品目と探す言葉・グループ）は、マイグレーションのシードとして入れる。シードの探す言葉は `is_seed` を立てる。`settings` の1件もシードで入れる（通常の人数と既定の週の予算は未設定のまま）
