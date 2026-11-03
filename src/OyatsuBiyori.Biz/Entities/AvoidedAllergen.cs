using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 施設の避ける品目（子どもは登録しない。全員が同じおやつを食べる）。
/// </summary>
public sealed class AvoidedAllergen : EntityBase
{
    /// <summary>品目（UNIQUE）</summary>
    public string AllergenId { get; set; } = string.Empty;

    /// <summary>品目</summary>
    [ForeignKey(nameof(AllergenId))]
    public Allergen Allergen { get; set; } = null!;

    /// <summary>追加した日時（UTC）</summary>
    public DateTime AddedAt { get; set; }

    /// <summary>追加した職員</summary>
    public string? AddedBy { get; set; }
}
