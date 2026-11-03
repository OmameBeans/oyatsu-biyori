using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// アレルゲン確認の記録（追加のみ。更新・削除しない。SR-07）。
/// </summary>
public sealed class AllergenCheck : EntityBase
{
    /// <summary>商品</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>商品</summary>
    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    /// <summary>ステータス（確認済み / 参考）</summary>
    public AllergenCheckStatus Status { get; set; }

    /// <summary>情報源（「確認済み」はパッケージだけ）</summary>
    public AllergenCheckSource Source { get; set; }

    /// <summary>情報源の補足（チラシの表記の原文「乳・小麦」「表記なし」、当たった探す言葉など）</summary>
    public string? SourceNote { get; set; }

    /// <summary>Claude が読み取った原材料表示の全文（手入力のときは NULL）</summary>
    public string? RawIngredientsOcr { get; set; }

    /// <summary>職員が確かめた原材料表示の全文（「確認済み」では必須。SR-12）</summary>
    public string? RawIngredients { get; set; }

    /// <summary>同一工場・コンタミの注意書き（SR-11）</summary>
    public string? ContaminationNote { get; set; }

    /// <summary>確認日時（UTC）</summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>確認者（必須。SR-08）</summary>
    public string CheckedBy { get; set; } = string.Empty;

    /// <summary>品目ごとの含む / 含まない（「参考」では「含む」の品目だけ）</summary>
    public List<AllergenCheckItem> Items { get; set; } = [];
}

/// <summary>
/// 品目ごとの含む / 含まない。行がない品目は「未確定」で、「含まない」ではない。
/// </summary>
public sealed class AllergenCheckItem : EntityBase
{
    /// <summary>確認の記録</summary>
    public string AllergenCheckId { get; set; } = string.Empty;

    /// <summary>確認の記録</summary>
    [ForeignKey(nameof(AllergenCheckId))]
    public AllergenCheck AllergenCheck { get; set; } = null!;

    /// <summary>品目</summary>
    public string AllergenId { get; set; } = string.Empty;

    /// <summary>品目</summary>
    [ForeignKey(nameof(AllergenId))]
    public Allergen Allergen { get; set; } = null!;

    /// <summary>含むか（false は「含まない」）</summary>
    public bool Contains { get; set; }
}
