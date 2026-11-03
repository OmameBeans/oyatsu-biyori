using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// チャットの履歴（Claude 側は説明文だけを保存する。D-58）。
/// </summary>
public sealed class MenuMessage : EntityBase
{
    /// <summary>献立</summary>
    public string MenuId { get; set; } = string.Empty;

    /// <summary>献立</summary>
    [ForeignKey(nameof(MenuId))]
    public Menu Menu { get; set; } = null!;

    /// <summary>発言者</summary>
    public MenuMessageRole Role { get; set; }

    /// <summary>本文</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>送った日時（UTC）</summary>
    public DateTime CreatedAt { get; set; }
}
