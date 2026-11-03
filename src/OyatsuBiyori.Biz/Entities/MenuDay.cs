using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 日ごとの献立。
/// </summary>
public sealed class MenuDay : EntityBase
{
    /// <summary>献立</summary>
    public string MenuId { get; set; } = string.Empty;

    /// <summary>献立</summary>
    [ForeignKey(nameof(MenuId))]
    public Menu Menu { get; set; } = null!;

    /// <summary>日付（日本時間）</summary>
    public DateOnly Date { get; set; }

    /// <summary>その日の人数（0以上）</summary>
    public int ChildrenCount { get; set; }

    /// <summary>人数の出どころ</summary>
    public ChildrenCountSource ChildrenCountSource { get; set; }

    /// <summary>日の種類</summary>
    public DayType DayType { get; set; }

    /// <summary>日の種類を職員が直したか（要望で作り直しても上書きしない。D-57）</summary>
    public bool DayTypeEdited { get; set; }

    /// <summary>日の見出し（例：誕生日会）</summary>
    public string? Label { get; set; }

    /// <summary>日の見出しを職員が直したか（D-57）</summary>
    public bool LabelEdited { get; set; }

    /// <summary>Claude が示した理由</summary>
    public string? Reason { get; set; }

    /// <summary>日ごとの商品</summary>
    public List<MenuItem> Items { get; set; } = [];
}
