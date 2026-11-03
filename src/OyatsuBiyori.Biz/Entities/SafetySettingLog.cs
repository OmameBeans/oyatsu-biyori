using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 避ける品目・探す言葉・再確認期間の変更の記録（追加のみ。FR-AVOID-06）。
/// </summary>
public sealed class SafetySettingLog : EntityBase
{
    /// <summary>操作した日時（UTC）</summary>
    public DateTime OperatedAt { get; set; }

    /// <summary>操作した職員</summary>
    public string? OperatedBy { get; set; }

    /// <summary>種類</summary>
    public SafetySettingLogKind Kind { get; set; }

    /// <summary>対象の品目（外部キーにしない。品目を削除しても記録は残す。再確認期間の変更では NULL）</summary>
    public string? AllergenId { get; set; }

    /// <summary>内容（品目名も書く。例：「ヘーゼルナッツ」に探す言葉「プラリネ」を追加）</summary>
    public string Detail { get; set; } = string.Empty;
}
