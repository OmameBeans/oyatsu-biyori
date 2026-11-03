using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 商品。チラシを取り込むたびに価格・注文番号などを上書きする。
/// </summary>
public sealed class Product : EntityBase
{
    /// <summary>JAN コード（NULL 可・UNIQUE。チラシには載っていない）</summary>
    public string? JanCode { get; set; }

    /// <summary>商品名（チラシ・パッケージの表記）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>照合用に正規化した商品名（保存時に TextNormalizer.ForProductMatch で作る）</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>メーカー</summary>
    public string? Maker { get; set; }

    /// <summary>内容量（例：10袋入、120g）</summary>
    public string? Volume { get; set; }

    /// <summary>入数（注文できる単位1つで何人分か。NULL の商品は献立の候補にしない）</summary>
    public int? PiecesPerPack { get; set; }

    /// <summary>1人あたりの個数の既定値（既定1。献立に入れるときに写す）</summary>
    public int DefaultPerChildCount { get; set; } = 1;

    /// <summary>おやつの分類</summary>
    public SnackType SnackType { get; set; }

    /// <summary>最後にチラシに載った週の初日（商品一覧での表示用）</summary>
    public DateOnly? ListedWeekStart { get; set; }

    /// <summary>税込価格（注文できる単位1つあたり。NULL の商品は献立の候補にしない）</summary>
    public int? Price { get; set; }

    /// <summary>税抜価格</summary>
    public int? PriceExcludingTax { get; set; }

    /// <summary>注文番号（号ごとに変わる）</summary>
    public string? OrderNo { get; set; }

    /// <summary>対象年齢の表示（例：1歳頃から）</summary>
    public string? TargetAgeLabel { get; set; }

    /// <summary>職員のメモ（献立提案のため Claude に渡す）</summary>
    public string? Memo { get; set; }

    /// <summary>使わない商品か（削除はせず、献立の候補から外す）</summary>
    public bool IsDisabled { get; set; }

    /// <summary>タグ</summary>
    public List<ProductTag> Tags { get; set; } = [];

    /// <summary>アレルゲン確認の記録（追加のみ）</summary>
    public List<AllergenCheck> AllergenChecks { get; set; } = [];
}
