---
paths:
  - "src/oyatsubiyori.client/**/*.ts"
  - "src/oyatsubiyori.client/**/*.tsx"
  - "src/oyatsubiyori.client/**/*.scss"
  - "src/oyatsubiyori.client/**/*.css"
---

# React コード規約（おやつ日和 クライアント側）

既存コードの書き方に合わせる。既存コードに不統一がある箇所は、新規コードではこの規約に従う。依頼されていない既存コードの書き換えはしない。

## 環境

- React 19 / TypeScript（`strict`、`verbatimModuleSyntax`）/ Vite
- UI：MUI（`@mui/material`, `@mui/icons-material`）
- サーバー状態：TanStack Query v5 / ルーティング：react-router-dom v7 / 通知：notistack / 日付：dayjs
- 上記のライブラリは導入済み。PDF は pdf.js（`pdfjs-dist`）、画面の E2E は Playwright（`@playwright/test`）を使う
- ファイルは **UTF-8** で保存する（Shift-JIS で保存しない）
- 変更後は `src/oyatsubiyori.client` で `npm run lint` と `npm run build` が通ることを確認する

## 判定・計算はクライアントでしない

- アレルゲンの判定・金額や1人あたり金額・週の予算の計算・重複チェック・商品名の正規化と照合は、クライアントでしない。API が返した結果（区分・理由・金額）をそのまま表示する
- これらは Biz に置いて xUnit でテストする（NFR-MNT-02）。クライアントにはユニットテストを置かず、画面は E2E で確かめる（次の節）。クライアントで判定や計算をするとテストで守られなくなるため、必要になったら API の側に足す

## E2E（Playwright。D-87）

- 画面を作ったり変えたりしたら、その画面の E2E を `e2e/` に足すか直し、コミット前に `npm run e2e` を走らせる（手順は `e2e/README.md`）
- テストのファイルは画面ごと（`sc-09-menu-edit.spec.ts` など）。テストの名前は要件 ID から始め、満たすべき条件を日本語で書く（`FR-MENU-14 🔴 の商品を入れると確定できない`）
- 要素は役割と文言で探す（`getByRole`・`getByText`）。CSS のクラスや DOM の構造に頼らない
- 画面ごとに PC とスマホの画像を1枚ずつ撮り、目で見て崩れがないか確かめる（ピクセルの比較はしない）
- Claude は偽物に差し替えて走らせる。本物の Claude と本番の環境では走らせない
- 使うデータは TD-02 のダミーデータだけ

## PC・スマホ両対応の画面

利用者は学童の職員で、PC とスマホのどちらからも使う。

- **1つの画面を PC とスマホの両方に対応させる**（レスポンシブ）。PC 用とスマホ用で別々の画面を作らない
- 幅による切り替えは MUI のブレークポイント（`sx` の `{ xs: ..., md: ... }`、`useMediaQuery`）で行う
- 一覧は、スマホでも崩れないように MUI の `List` や `Card` を基本にする。PC では横に並べるなどして広い画面を活かす
- ボタンなどはスマホで押しやすい大きさにする（PC でも困らない）
- PC（`md` 以上）では画面全体を縦にスクロールさせず、見出し・絞り込み・ボタンを固定して、一覧や明細の部分だけをスクロールする（表の見出しは `stickyHeader`）。スマホではページ全体をスクロールする（[05 §4](../../docs/requirements/05-screens.md)。見本は `mocks/` の `body.fit`）
- 画像の取り込み（チラシ・パッケージ）は、「カメラで撮る」と「ファイルを選ぶ」の2つのボタンを並べる（SC-08、SC-09 の手順1）
  - 「カメラで撮る」：`<input type="file" accept="image/*" capture="environment">`。スマホで背面カメラを直接起動する
  - 「ファイルを選ぶ」：`capture` を付けない `<input type="file">`。アルバムや PC のファイルから選べる。`accept` はチラシなら `image/*,application/pdf`、パッケージなら `image/*`
  - PC ではファイルのドラッグ＆ドロップでも追加できるようにする（チラシ）
  - ファイルの大きさには上限（1ファイル 20MB（仮））を設ける。上限を超えたファイルや対応していない形式は、理由を日本語で表示する（NFR-UX-02a）
- アレルゲンは「🔴 使えません / 🟡 要確認 / 🟢 使えます」の3つの表示と、理由・次にすることで示す（[03 §6](../../docs/requirements/03-allergen-safety.md)）。色だけでなく文言とアイコンでも示し、見落とされない位置に出す
  - 色は判定の区分で決める（入れられない：`error` ／ 警告：`warning` ／ 安全：`success`）。ステータス（確認済み・参考・不明・要再確認）で色を決めない
  - **アレルゲンの文言（表示名・理由・次にすること）は共通部品（T4-01）だけから出す**。各画面に直接書かない。「参考」「要再確認」などのステータスの名前は、商品詳細の履歴以外に出さない
  - 警告の文言には、何をすればよいかを含める（「パッケージの原材料表示を撮影して確認してください」）
- ログイン後の画面の目立つ場所に「アレルゲンの最終確認は、必ずパッケージの原材料表示で行ってください」と常に表示する
- 週の要望とチャットの入力欄の近くに「子どもの名前は書かないでください」と表示する（Claude に送るため。NFR-SEC-05a）

## ディレクトリ構成（Feature-Sliced Design）

```
src/
├── app/       アプリ全体の初期化（App, Router, Layout, Provider）
├── pages/     画面単位（landing, login, menu-list, menu-edit, product-list, product-detail, settings）
├── features/  ユーザー操作の単位（flyer-upload, allergen-check, menu-chat, product-match …）
├── entities/  ドメインモデルと API（product, allergen-check, menu, avoided-allergen …）。子どもは登録しないので child は作らない
└── shared/    ドメインに依存しない共通部品（type, ui）
```

- 依存は **上のレイヤーから下のレイヤーへの一方向**（`app → pages → features → entities → shared`）
  - 同じレイヤーのスライス同士は import しない（例：`features/menu-chat` から `features/flyer-upload` を参照しない）
- スライス名（フォルダ名）は kebab-case（`allergen-check`, `menu-edit`）
- スライスの中は次のセグメントに分ける
  - `ui/`：コンポーネント
  - `api/`：fetch 関数（`<entity>.ts`）と TanStack Query フック（`queries.ts`）
  - `model/`：型定義、Context など
- 各スライスに `index.ts` を置いて公開する API を明示し、**他のスライスからは `index.ts` 経由でのみ import する**（`../../pages/product-list/ui/ProductListPage` のように内部ファイルを直接参照しない）
- import は相対パスで書く（`vite.config.ts` に `@` エイリアスはあるが使わない）

## ファイル・命名

- コンポーネントファイル：PascalCase（`AllergenCheckDialog.tsx`）。それ以外の `.ts`：camelCase（`allergenCheck.ts`, `queries.ts`）
- コンポーネント・型：PascalCase。変数・関数：camelCase
- イベントハンドラは `handleXxx`、props で受け取るコールバックは `onXxx`
- boolean は `isXxx` / `hasXxx`（`isOpen`, `isEditMode`, `isLoading`）
- 定数オブジェクトは `as const` を付け、キーは UPPER_SNAKE_CASE にして、値から型を作る（アレルゲンの確認ステータス、エンドポイントなど。`shared/type/` に置く）
- 29品目・施設の追加品目・グループ・探す言葉などのマスタは、フロントの定数にしない。API でサーバーのマスタから取る（品目が変わってもフロントを直さずに済むように。D-80）

## コンポーネント

- アロー関数 + **名前付き export** で書く。`export default` は使わない
- Props は `interface XxxProps` で定義し、引数で分割代入する。`React.FC` は使わない

```tsx
interface ProductListProps {
    onEdit: (product: Product) => void;
    onCheckAllergen: (product: Product) => void;
}

export const ProductList = ({ onEdit, onCheckAllergen }: ProductListProps) => {
    const { data: products } = useProducts();
    // ...
};
```

- 子要素を受け取るだけのコンポーネントは `PropsWithChildren` を使う
- 型だけを import するときは `import type` か `type` 修飾子を付ける（`verbatimModuleSyntax` が有効なため必須）
- ダイアログは `isOpen` / `onClose` / `onSuccess` を props で受け取り、開閉の状態は呼び出し側のページが持つ

## データ取得（entities/*/api）

- エンドポイントは `shared/type/endpoint.ts` の定数を使う。URL を直接書かない。API のパスは `/api/` で始まる（開発時は `vite.config.ts` のプロキシで `^/api` をサーバーへ転送する）
- fetch 関数は `Promise<GenericResult<T>>`（サーバーの `Result<T>` に対応する型。`shared/type/` に置く）を返し、`credentials: 'include'` を付ける
- `queries.ts` には次のものを置く
  - クエリキーのファクトリ `xxxKeys`（`all` / `lists()` / `detail(id)` …）
  - 取得用フック：`useSuspenseQuery` を使う。`result.hasError` が true なら `errorMessages` を結合して `throw new Error(...)` する
  - 更新用フック：`useMutation` を使い、`onSuccess` で関連するクエリを `invalidateQueries` する
- フック名は `useXxxs`（一覧）/ `useXxx`（1件）/ `useCreateXxx` / `useUpdateXxx` / `useDeleteXxx`
- `model/` の型のプロパティ名はサーバーの JSON に合わせて camelCase にする。日時は `string` で受け取る
- 作成時の引数型は `Omit<Xxx, 'id' | 'updateDateTime' | 'updateUserId'>` にする
- 画像のアップロードは `FormData` で送る。選んだ画像は、確定するか画面を離れるまでメモリ（コンポーネントの状態）にだけ持ち、失敗したときの「もう一度試す」に使う。localStorage・sessionStorage・IndexedDB などには保存しない。確定したら、または画面を離れたら破棄する（D-41）

## 読み込み中・エラー・通知

- `useSuspenseQuery` を使うコンポーネントは、呼び出し側で `<Suspense>` で囲む。fallback は `CircularProgress` を中央に配置する
- Claude の処理（チラシ読み取り、献立提案など）は時間がかかるので、何をしているかを文言で表示する（「チラシを読み取っています…」）
  - 処理中は実行ボタンを押せなくして、二重送信を防ぐ（`useMutation` の `isPending` を使う）
  - 失敗しても入力済みの内容（要望の文章・選んだ画像など）を消さず、「もう一度試す」で再試行できるようにする（NFR-UX-04）
- フォーム内のエラーは MUI の `<Alert severity="error">` で表示する
- 操作結果の通知は notistack の `enqueueSnackbar(message, { variant: 'success' | 'error' })` を使う
- 画面に表示する文言・エラーメッセージは日本語で書く。職員向けなので専門用語を避ける（例：「正規化」「照合」は出さず「同じ商品かどうか」と書く）
- エラーメッセージには、次に何をすればよいかを書く（NFR-UX-03）

## スタイル

- 基本は MUI の `sx` プロップで書く
- MUI v9 の `Stack` は `alignItems`・`justifyContent`・`flexWrap` などを props で受け取らない（型エラーになる）。`sx={{ alignItems: 'center' }}` のように `sx` に書く。`direction`・`spacing`・`useFlexGap` は props のまま
- MUI の `Stepper`（`nonLinear` の `StepButton`）は役割が `tab` になる。E2E では `getByRole('tab', { name: '…', exact: true })` で探す
- 複雑なレイアウトは CSS Modules（`Xxx.module.scss`）を使う。グローバルな `.css` は新しく作らない
