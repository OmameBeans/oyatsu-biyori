---
paths:
  - "src/**/*.cs"
  - "src/**/*.csproj"
---

# C# コード規約（おやつ日和 サーバー側）

既存コードの書き方に合わせる。既存コードに不統一がある箇所は、新規コードではこの規約に従う。依頼されていない既存コードの書き換えはしない。

## 環境

- .NET 10 / `Nullable` 有効 / `ImplicitUsings` 有効 / `LangVersion` latest（[src/Directory.Build.props](../../src/Directory.Build.props)）
- ファイルは **UTF-8（BOM付き）・CRLF** で保存する。Shift-JIS で保存しない（文字化けの原因になる）

## プロジェクト構成と依存方向

| プロジェクト | 役割 | 主なフォルダ |
| --- | --- | --- |
| `OyatsuBiyori.Biz` | ドメイン・ユースケース。他プロジェクトに依存しない | `Abstractions/` `Entities/` `Interfaces/` `ApplicationServices/` `Extensions/` |
| `OyatsuBiyori.Data` | EF Core（SQLite）による永続化 | `DbContexts/` `Repositories/` `QueryServices/` `Managers/` |
| `OyatsuBiyori.Migrations` | EF Core のマイグレーションだけを置く（D-40） | `Migrations/` |
| `OyatsuBiyori.Claude` | Claude API の呼び出し（チラシ読み取り、アレルゲン抽出、献立提案、商品照合の判定） | `Services/` `Prompts/` |
| `OyatsuBiyori.Server` | ASP.NET Core Web API。React のビルド結果も配信する | `Controllers/` |

- 依存方向は `Server → Data / Claude → Biz`。**Biz から Data・Claude・Server を参照しない**
- マイグレーションは EF Core の「別のプロジェクトを使う」手順で分ける。Data（`AddData`）の `UseSqlite` で `MigrationsAssembly("OyatsuBiyori.Migrations")` を指定し、`Migrations → Data`、`Server → Migrations` の向きで参照する。Data から Migrations を参照しない
- 外部リソース（DB・Claude API）へのアクセスは Biz の `Interfaces/` にインターフェースを定義し、Data / Claude で実装する
- DI 登録は各プロジェクトの拡張メソッド（`AddBiz` / `AddData` / `AddClaude`）に追加する。`Program.cs` に直接書かない

## 書式

- インデントはスペース4つ、波括弧は改行して書く（Allman スタイル）
- 名前空間はファイルスコープ形式 `namespace X;` を使う（ブロック形式は使わない）
- `if` が1行でも波括弧を省略しない
- 空のコレクション初期化はコレクション式 `[]` を使う

## 命名

- クラス・メソッド・プロパティ・enum 値：PascalCase
- private フィールド：`_camelCase`
- インターフェース：`I` プレフィックス（`IProductRepository`）
- 非同期メソッドは `Async` サフィックス（`GetAsync`, `CreateAsync`）
- 種類ごとのサフィックス：`XxxApplicationService` / `XxxRepository` / `XxxQueryService` / `XxxController` / `XxxExtensions`
- Claude を使うクラスは用途で名付ける（`FlyerReader`, `AllergenExtractor`, `MenuPlanner`, `FlyerReviewer` など）

## クラス設計

- 継承を想定しないクラスは `sealed` にする
- 依存はコンストラクタで受け取り、null チェックしてから readonly フィールドに代入する。依存が少なければプライマリコンストラクタでよい

```csharp
public sealed class ProductApplicationService(IProductRepository productRepository, IUserContext userContext)
{
    private readonly IProductRepository _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
}
```

## エンティティ

- `EntityBase`（`Id` / `UpdateDateTime` / `UpdateUserId`）を継承する（`Biz/Abstractions/` に作る）。`UpdateUserId` は NULL 可（マスタの初期データとシステムの処理のため）。初期データ（シード）の `Id` と日時は固定の値にする
- 日時は UTC で保存する。`DateTime` の列にはすべて、読み込んだ値を `Kind=Utc` にする ValueConverter を付ける。日付の判定（「今日」「1日」の区切り）は日本時間で行う
- `Id` は `string` 型で、新規作成時に `Guid.NewGuid().ToString()` を設定する（企画の「主キーはアプリ側で振る UUID」）
- `string` プロパティは `= string.Empty`、コレクションは `= []` で初期化する。必須のナビゲーションプロパティは `= null!`
- NULL 可の項目（JAN コードなど）は `string?` にする。JAN コードは UNIQUE インデックスを付ける
- 外部キーには `[ForeignKey(nameof(XxxId))]` を付ける
- enum の表示名（アレルゲンの確認ステータスなど）は `Extensions/` の拡張メソッド（`ToDisplayString()`）で日本語を返す
- エンティティを変更したら、SQLite のマイグレーションを `OyatsuBiyori.Migrations` に作成する（`dotnet ef migrations add` に `--project src/OyatsuBiyori.Migrations --startup-project src/OyatsuBiyori.Server` を付ける）
- 本番の DB へは、手元でマイグレーションから SQL を作り、サーバーで適用する（D-42）。サーバー上でビルドしない

## アレルゲンに関わるコード

詳細は [CLAUDE.md](../../CLAUDE.md) の「アレルゲンの扱い」。コードでは特に次を守る。

- アレルゲン確認（`AllergenCheck`）は**更新・削除せず、追加のみ**にする。現在の状態は最新の記録から求める
- 判定の結果は true / false にせず、「入れられない（エラー）/ 警告 / 安全」の3つで返す（[03 §5](../../docs/requirements/03-allergen-safety.md)、D-41）。ステータスより先に「含む」「含む可能性あり」を調べ、どの条件にも当たらないときだけ「安全」にする。「参考」「不明」「要再確認」は「安全」にしない（「不明」は、避ける品目が1つでもあれば「入れられない」）
- Claude の判定結果をそのまま「確認済み」として保存しない。「確認済み」にできるのは職員の操作だけ
- これらの判定ロジックは Biz に置き、xUnit でテストする

## ApplicationService（Biz）

- 戻り値は `Result` / `Result<T>`（`Biz/Result.cs` に作る。`ErrorMessages` と `HasError` を持つ）
- 入力チェックの失敗や業務エラーは例外にしない。`ErrorMessages` に日本語のメッセージを入れて返す
- `UpdateDateTime` は注入した `TimeProvider` の UTC（`_timeProvider.GetUtcNow().UtcDateTime`）にする（D-41）。`DateTime.Now` は使わない。`UpdateUserId` は `IUserContext.GetCurrentUserInfoAsync()` から取得して、ApplicationService で設定する
- 複数の書き込みをまとめるときは `ITransactionManager` を使い、失敗時と例外時に `RollbackAsync()` する

```csharp
public async Task<Result> DeleteAsync(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        var result = new Result();
        result.ErrorMessages.Add("商品IDが設定されていません");
        return result;
    }
    // ...
}
```

## Repository / QueryService（Data）

- 取得系：見つからない場合は `null` を返す（戻り値は `Task<T?>`）。一覧は `Task<IEnumerable<T>>`
- 更新系（Add / Update / Delete）：成功したら `true`、失敗したら `false` を返す
- 更新は「既存エンティティを取得して必要なプロパティだけ書き換え、`SaveChangesAsync`」の形で行う
- 画面表示用に複数テーブルをまとめて取得する場合は、Repository ではなく `XxxQueryService` と `XxxQueryServiceModel` を作る

## Claude 呼び出し（Claude）

[claude-api.md](claude-api.md) に従う。

## Controller（Server）

- ルートは `[Route("api/[controller]/[action]")]` + `[ApiController]`
- 取得は `[HttpGet]`、作成・更新・削除は `[HttpPost]`
- 業務ロジックは書かず、ApplicationService を呼んで `new JsonResult(result)` を返すだけにする
- エンティティ以外のリクエストボディは、Controller 内に `sealed class XxxRequest` として定義する
- 画像のアップロードは `IFormFile` で受け取る。IIS で受け取れる大きさの上限は Server プロジェクトの `web.config` に書く

## ログ

- `ILogger<T>` を注入し、構造化ログのテンプレートを使う（`_logger.LogInformation("商品を取得: {ProductId}", id)`）。文字列補間 `$"..."` は使わない
- 例外は `_logger.LogError(ex, "...")` で記録してから `throw;` で再スローする（`throw ex;` は使わない）
