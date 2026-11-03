using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 原材料名で探す言葉。商品名と原材料の全文の両方にかける（SR-14）。
/// </summary>
public sealed class AllergenKeyword : EntityBase
{
    /// <summary>品目</summary>
    public string AllergenId { get; set; } = string.Empty;

    /// <summary>品目</summary>
    [ForeignKey(nameof(AllergenId))]
    public Allergen Allergen { get; set; } = null!;

    /// <summary>探す言葉（例：ヘーゼルナッツ、プラリネ）</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>正規化した言葉（必ず TextNormalizer.ForKeywordSearch で作る）</summary>
    public string NormalizedKeyword { get; set; } = string.Empty;

    /// <summary>初期データの言葉か（初期データの言葉は削除できない。D-50）</summary>
    public bool IsSeed { get; set; }
}
