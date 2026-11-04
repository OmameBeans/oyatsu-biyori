using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// チラシの取り込みの途中の状態（確定前の読み取り結果）。サーバーのメモリに献立と結びつけて24時間だけ持つ（FR-FLYER-10・D-36）。
/// 説明文はここにだけ持ち、DB には保存しない（探す言葉を探すためだけに使う。FR-FLYER-03）。
/// </summary>
public sealed class FlyerImportSession
{
    /// <summary>同時に書き換えないための鍵</summary>
    public object SyncRoot { get; } = new();

    /// <summary>取り込みの ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>献立</summary>
    public string MenuId { get; init; } = string.Empty;

    /// <summary>作った日時（UTC）</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>取り込みを始めたときに、献立に掲載品の写しがあったか（取り込み直し。最初の確定で写しを空にする。T1-18）</summary>
    public bool IsReimport { get; init; }

    /// <summary>この取り込みで、もう1件以上確定したか（取り込み直しで写しを空にしたか）</summary>
    public bool HasConfirmedAny { get; set; }

    /// <summary>読み取りの検証（Claude）ができなかったか（FR-FLYER 受け入れ条件）</summary>
    public bool ReviewFailed { get; set; }

    /// <summary>読み取りの検証が指摘した、読み落とした商品（手入力の候補）</summary>
    public List<FlyerMissedProduct> MissedProducts { get; } = [];

    /// <summary>商品</summary>
    public List<FlyerImportItem> Items { get; } = [];
}

/// <summary>取り込みの1商品</summary>
public sealed class FlyerImportItem
{
    /// <summary>ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>手入力した商品か（チラシの読み取りと同じ扱い。D-73）</summary>
    public bool IsManual { get; init; }

    /// <summary>読み取った内容</summary>
    public FlyerReadProduct Read { get; init; } = null!;

    /// <summary>アレルゲン表記をマスタの品目に対応づけたもの（品目 ID。アプリでもう一度対応づけた結果を含む）</summary>
    public List<string> LabelAllergenIds { get; init; } = [];

    /// <summary>対応づけできなかった表記の語</summary>
    public List<string> UnmappedTerms { get; init; } = [];

    /// <summary>アプリの検証・読み取りの検証の指摘</summary>
    public List<FlyerItemIssue> Issues { get; } = [];

    /// <summary>照合の結果</summary>
    public FlyerItemMatch Match { get; set; } = new();

    /// <summary>確定した内容（未確定なら null）</summary>
    public FlyerItemConfirmation? Confirmation { get; set; }
}

/// <summary>商品の指摘（Source：app はアプリの検証、review は読み取りの検証）</summary>
public sealed record FlyerItemIssue(string Source, string Field, string Message);

/// <summary>照合の結果（候補まで。確定は職員）</summary>
public sealed class FlyerItemMatch
{
    /// <summary>「同じ商品」の候補（ルール）</summary>
    public List<FlyerMatchProduct> SameProducts { get; init; } = [];

    /// <summary>「内容量違い」の候補（ルール）</summary>
    public List<FlyerMatchProduct> VolumeVariants { get; init; } = [];

    /// <summary>似ている候補（Dice 係数）</summary>
    public List<FlyerMatchProduct> Candidates { get; init; } = [];

    /// <summary>Claude の判定（なければ null）</summary>
    public ProductMatchJudgement? Judgement { get; set; }

    /// <summary>判定を回したが、失敗・上限で判定がない</summary>
    public bool JudgementSkipped { get; set; }

    /// <summary>まとめて確定できるか（同じ商品の候補がちょうど1件）</summary>
    public bool CanBulkConfirmAsSame => SameProducts.Count == 1;
}

/// <summary>照合の候補の商品</summary>
public sealed record FlyerMatchProduct(string ProductId, string Name, string? Maker, string? Volume, bool MakerCompared, bool IsDisabled, double? Score);

/// <summary>取り込みの選択（FR-FLYER-06）</summary>
public enum FlyerItemChoice
{
    /// <summary>既存の商品と同じ</summary>
    Same,

    /// <summary>味違い・内容量違いとして登録（別の商品。アレルゲン確認を引き継がない）</summary>
    Variant,

    /// <summary>新しい商品として登録</summary>
    New,

    /// <summary>取り込まない</summary>
    Skip,
}

/// <summary>確定した内容</summary>
/// <param name="Choice">選択</param>
/// <param name="ProductId">使った・作った商品（取り込まないなら null）</param>
/// <param name="ProductName">商品名</param>
/// <param name="AddedReference">「参考」の記録を足したか</param>
public sealed record FlyerItemConfirmation(FlyerItemChoice Choice, string? ProductId, string? ProductName, bool AddedReference);
