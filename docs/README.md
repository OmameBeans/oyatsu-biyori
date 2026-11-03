# ドキュメント

おやつ日和（oyatsu-biyori）の設計資料です。

## 構成

```
docs/
├── README.md                     この文書
├── requirements/                 要件定義
│   ├── 01-overview.md            目的・利用者・スコープ・用語
│   ├── 02-functional.md          機能要件（機能ごとの要件と受け入れ条件）
│   ├── 03-allergen-safety.md     アレルゲンの扱い（安全要件）
│   ├── 04-data-model.md          データ要件（エンティティと項目）
│   ├── 05-screens.md             画面一覧と画面遷移
│   ├── 06-non-functional.md      非機能要件（セキュリティ・性能・運用など）
│   ├── 07-open-questions.md      未決事項・現場へのヒアリング項目
│   ├── 08-hackathon.md           提出物・デモ・現場での試用・Claude Code の活用（ハッカソン）
│   └── index.html                上の md をまとめた閲覧用ページ（生成物・直接編集しない）
├── implementation-tasks/         実装タスク
│   ├── implementation-tasks.md   要件定義を実装の単位に分解したもの
│   └── index.html                上の md の閲覧用ページ（生成物・直接編集しない）
└── context/                      手元だけの資料（公開しない・.gitignore で除外）
```

## 読み方

- まず [01-overview.md](requirements/01-overview.md) で全体像をつかむ
- 実装するときは [02-functional.md](requirements/02-functional.md) の要件 ID（`FR-xxx-nn`）を参照する
- アレルゲンに関わる実装は、必ず [03-allergen-safety.md](requirements/03-allergen-safety.md) を確認する
- 決まっていないことは [07-open-questions.md](requirements/07-open-questions.md) に集め、決まったら各文書に反映する
- ブラウザで読むときは [index.html](requirements/index.html) を開く（サイドバーに目次がある）

## 閲覧用 HTML の作り直し

`index.html` は md から生成している。**md を直したら、リポジトリのルートで次を実行して作り直す**（Python 3 が必要）。

```
python tools/build-requirements-html/build.py
```

見た目（CSS）や目次の動きを変えるときは `tools/build-requirements-html/template.html` を編集する。

実装タスク（`implementation-tasks/implementation-tasks.md`）を直したときは、次を実行して同じフォルダの `index.html` を作り直す。タスクの表は1件ずつのカードになり、文字や「安全レビュー」「xUnit」の印で絞り込める。見た目は `tools/build-tasks-html/template.html` で変える。

```
python tools/build-tasks-html/build.py
```

## 表記のルール

- 優先度は **必須 / あると良い / 余裕があれば** の3段階
- 要件 ID は `FR`（機能要件）、`SR`（安全要件）、`NFR`（非機能要件）、`HK`（ハッカソンの提出物・開発の進め方）で始める
- 仮の値（しきい値・期間など）には **（仮）** と付け、現場で確かめてから確定する
