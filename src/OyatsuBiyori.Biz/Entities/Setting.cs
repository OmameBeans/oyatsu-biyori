using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 施設の条件（1件だけ）。FR-COND
/// </summary>
public sealed class Setting : EntityBase
{
    /// <summary>通常時の人数（未設定のあいだは献立を作らず、条件設定へ案内する）</summary>
    public int? DefaultChildrenCount { get; set; }

    /// <summary>おやつを出す曜日（既定：月〜金）</summary>
    public List<DayOfWeek> ServingWeekdays { get; set; } = [];

    /// <summary>アレルゲンの再確認期間（日。既定 90）</summary>
    public int RecheckDays { get; set; }

    /// <summary>分類ごとの重複させない期間。配列にない分類は制限なし</summary>
    public List<DuplicateAvoidRule> DuplicateAvoidDays { get; set; } = [];

    /// <summary>既定の週の予算（円・税込。未設定のあいだは献立を作らない）</summary>
    public int? DefaultWeeklyBudget { get; set; }

    /// <summary>重複についての補足ルール（自由記述。Claude に渡す）</summary>
    public string? DuplicateRuleNote { get; set; }
}

/// <summary>分類ごとの重複させない期間</summary>
/// <param name="SnackType">おやつの分類（「対象外」は不可）</param>
/// <param name="Days">日数（1以上）</param>
public sealed record DuplicateAvoidRule(SnackType SnackType, int Days);
