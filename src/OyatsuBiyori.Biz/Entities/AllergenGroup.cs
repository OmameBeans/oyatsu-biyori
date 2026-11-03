using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 避ける品目をまとめて選ぶためのグループ（ナッツ類（すべて）・甲殻類）。
/// </summary>
public sealed class AllergenGroup : EntityBase
{
    /// <summary>グループ名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>表示順</summary>
    public int SortOrder { get; set; }

    /// <summary>含まれる品目</summary>
    public List<AllergenGroupMember> Members { get; set; } = [];
}

/// <summary>
/// グループに含まれる品目。
/// </summary>
public sealed class AllergenGroupMember : EntityBase
{
    /// <summary>グループ</summary>
    public string AllergenGroupId { get; set; } = string.Empty;

    /// <summary>グループ</summary>
    [ForeignKey(nameof(AllergenGroupId))]
    public AllergenGroup AllergenGroup { get; set; } = null!;

    /// <summary>品目</summary>
    public string AllergenId { get; set; } = string.Empty;

    /// <summary>品目</summary>
    [ForeignKey(nameof(AllergenId))]
    public Allergen Allergen { get; set; } = null!;
}
