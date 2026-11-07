using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// チラシのアレルゲン表記の略記（例：「麦」→小麦、「え」→えび。D-96）。
/// チラシの表記を品目に対応づけるとき（FlyerAllergenPlanner.ParseLabel）だけ使い、語全体が一致したときだけ当てる。
/// 探す言葉（AllergenKeyword）とは別に持つ（1文字の略記を全文検索・商品名の検索で部分一致させると当たりすぎるため）。
/// ひらがなの「か」（かに）とカタカナの「カ」（カシューナッツ）は字で分ける（D-100、FR-IMPORT-32）。
/// </summary>
public sealed class AllergenLabelAbbreviation : EntityBase
{
    /// <summary>品目</summary>
    public string AllergenId { get; set; } = string.Empty;

    /// <summary>品目</summary>
    [ForeignKey(nameof(AllergenId))]
    public Allergen Allergen { get; set; } = null!;

    /// <summary>略記（例：麦）</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>正規化した略記（必ず TextNormalizer.ForLabelAbbreviation で作る。カタカナをひらがなにしない。FR-IMPORT-32）</summary>
    public string NormalizedAbbreviation { get; set; } = string.Empty;
}
