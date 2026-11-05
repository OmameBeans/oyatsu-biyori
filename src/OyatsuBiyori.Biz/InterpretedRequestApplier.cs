using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 要望の読み取り結果（interpreted_requests。U-06）を日ごとの献立に反映する（FR-MENU-03・04・22a、D-44・D-57、U-05・U-19、T1-22）。
/// 反映するのは人数・日の種類・見出しだけ。アレルギー・予算・重複の期間は、そもそも受け取る形にない（要望で緩められない）。
/// 職員が画面で直した値は、要望の読み取りでは上書きしない。チャットでは、依頼がはっきり変えるもの（明示の日）だけ上書きする。
/// </summary>
public static class InterpretedRequestApplier
{
    /// <summary>
    /// 要望の読み取り結果を反映する（献立案の作成・「要望を直す」での作り直し）。
    /// 前の要望で入れた値（職員が直していないもの）は、いったん既定に戻してから反映する（消した要望が残らないように）。
    /// </summary>
    /// <param name="menu">献立（Days・Items を読み込んでおくこと）</param>
    /// <param name="interpreted">読み取り結果</param>
    /// <param name="defaultChildrenCount">要望で変えていない日の人数（通常の人数）</param>
    /// <param name="piecesPerPack">商品の入数（人数が変わった日のパック数を求め直すため）</param>
    public static InterpretedRequestApplyResult ApplyRequest(Menu menu, InterpretedRequests interpreted, int defaultChildrenCount, Func<string, int?> piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(interpreted);
        ArgumentNullException.ThrowIfNull(piecesPerPack);
        ArgumentOutOfRangeException.ThrowIfNegative(defaultChildrenCount);

        var before = menu.Days.ToDictionary(d => d.Id, d => (d.ChildrenCount, d.DayType));
        var beforeLabels = menu.Days.ToDictionary(d => d.Id, d => d.Label);
        foreach (var day in menu.Days)
        {
            if (day.ChildrenCountSource == ChildrenCountSource.FromRequest)
            {
                day.ChildrenCount = defaultChildrenCount;
                day.ChildrenCountSource = ChildrenCountSource.Default;
            }

            if (!day.DayTypeEdited)
            {
                day.DayType = DayType.Normal;
            }

            if (!day.LabelEdited)
            {
                day.Label = null;
            }
        }

        var result = Apply(menu, interpreted.Days, _ => false);
        result.NotApplied.InsertRange(0, interpreted.Rejected);

        // 変えた値は、戻す前の値と比べて求め直す（同じ要望をもう一度当てても「変えた」にしない。消した要望で戻した値は含める）
        result.Applied.Clear();
        foreach (var day in menu.Days.OrderBy(d => d.Date))
        {
            var (count, dayType) = before[day.Id];
            if (count != day.ChildrenCount)
            {
                result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.ChildrenCount, count.ToString(), day.ChildrenCount.ToString()));
            }

            if (dayType != day.DayType)
            {
                result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.DayType, dayType.ToString(), day.DayType.ToString()));
            }

            if (beforeLabels[day.Id] != day.Label)
            {
                result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.Label, beforeLabels[day.Id], day.Label));
            }
        }
        menu.InterpretedRequests = new InterpretedRequests
        {
            // 献立にない日・受け付けなかった人数は「こう理解しました」に残さない（反映しなかった要望で示す）
            Days = interpreted.Days
                .Where(d => menu.Days.Any(x => x.Date == d.Date))
                .Select(d => d.ChildrenCount is int count && InputValidator.ValidateDayChildrenCount(count).HasError ? d with { ChildrenCount = null } : d)
                .ToList(),
            Policies = [.. interpreted.Policies],
            Rejected = [.. result.NotApplied],
        };

        Recalculate(menu, before, piecesPerPack);
        return result;
    }

    /// <summary>
    /// チャットで頼まれた日ごとの値を反映する（U-05）。前の値は戻さずに足す。職員が直した値は explicitDates の日だけ上書きする。
    /// 変えた値は「要望をこう理解しました」（menu.InterpretedRequests）に足す。
    /// </summary>
    /// <param name="menu">献立</param>
    /// <param name="days">日ごとの値（変えるものだけ）</param>
    /// <param name="explicitDates">職員が直した値も変えてよい日（依頼がその日の値をはっきり変えるもの）</param>
    /// <param name="piecesPerPack">商品の入数</param>
    public static InterpretedRequestApplyResult ApplyChat(Menu menu, IReadOnlyList<InterpretedDay> days, IReadOnlySet<DateOnly> explicitDates, Func<string, int?> piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(explicitDates);
        ArgumentNullException.ThrowIfNull(piecesPerPack);

        var before = menu.Days.ToDictionary(d => d.Id, d => (d.ChildrenCount, d.DayType));
        var result = Apply(menu, days, explicitDates.Contains);

        var interpreted = menu.InterpretedRequests ?? new InterpretedRequests();
        // 変えた項目だけを、その日の「こう理解しました」に重ねる（職員が直して残した値は要望の値として記録しない）
        foreach (var changes in result.Applied.GroupBy(c => c.Date))
        {
            var day = menu.Days.First(d => d.Date == changes.Key);
            var entry = interpreted.Days.FirstOrDefault(d => d.Date == changes.Key) ?? new InterpretedDay(day.Date, null, null, null);
            foreach (var change in changes)
            {
                entry = change.Field switch
                {
                    InterpretedField.ChildrenCount => entry with { ChildrenCount = day.ChildrenCount },
                    InterpretedField.DayType => entry with { DayType = day.DayType },
                    _ => entry with { Label = day.Label },
                };
            }

            interpreted.Days.RemoveAll(d => d.Date == changes.Key);
            interpreted.Days.Add(entry);
        }

        interpreted.Days.Sort((a, b) => a.Date.CompareTo(b.Date));
        interpreted.Rejected.AddRange(result.NotApplied);
        menu.InterpretedRequests = interpreted;

        Recalculate(menu, before, piecesPerPack);
        return result;
    }

    private static InterpretedRequestApplyResult Apply(Menu menu, IEnumerable<InterpretedDay> days, Func<DateOnly, bool> overwriteStaffEdits)
    {
        var result = new InterpretedRequestApplyResult();
        foreach (var value in days)
        {
            var day = menu.Days.FirstOrDefault(d => d.Date == value.Date);
            var label = MenuIssueText.DateLabel(value.Date);
            if (day is null)
            {
                result.NotApplied.Add(new RejectedRequest($"{label}の変更", "この日はおやつを出す日ではないため、反映しませんでした"));
                continue;
            }

            var overwrite = overwriteStaffEdits(value.Date);

            if (value.ChildrenCount is int count)
            {
                var validation = InputValidator.ValidateDayChildrenCount(count);
                if (validation.HasError)
                {
                    result.NotApplied.Add(new RejectedRequest($"{label}の人数 {count}人", validation.ErrorMessages[0]));
                }
                else if (day.ChildrenCountSource == ChildrenCountSource.EditedByStaff && !overwrite)
                {
                    result.KeptStaffEdits.Add(new InterpretedChange(day.Date, InterpretedField.ChildrenCount, day.ChildrenCount.ToString(), count.ToString()));
                }
                else if (day.ChildrenCount != count || day.ChildrenCountSource != ChildrenCountSource.FromRequest)
                {
                    result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.ChildrenCount, day.ChildrenCount.ToString(), count.ToString()));
                    day.ChildrenCount = count;
                    day.ChildrenCountSource = ChildrenCountSource.FromRequest;
                }
            }

            if (value.DayType is DayType dayType)
            {
                if (!Enum.IsDefined(dayType))
                {
                    result.NotApplied.Add(new RejectedRequest($"{label}の種別", "種別が読み取れませんでした"));
                }
                else if (day.DayTypeEdited && !overwrite)
                {
                    result.KeptStaffEdits.Add(new InterpretedChange(day.Date, InterpretedField.DayType, day.DayType.ToString(), dayType.ToString()));
                }
                else if (day.DayType != dayType || day.DayTypeEdited)
                {
                    result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.DayType, day.DayType.ToString(), dayType.ToString()));
                    day.DayType = dayType;
                    day.DayTypeEdited = false;
                }
            }

            if (value.Label is not null)
            {
                var newLabel = string.IsNullOrWhiteSpace(value.Label) ? null : value.Label.Trim();
                if (day.LabelEdited && !overwrite)
                {
                    result.KeptStaffEdits.Add(new InterpretedChange(day.Date, InterpretedField.Label, day.Label, newLabel));
                }
                else if (day.Label != newLabel || day.LabelEdited)
                {
                    result.Applied.Add(new InterpretedChange(day.Date, InterpretedField.Label, day.Label, newLabel));
                    day.Label = newLabel;
                    day.LabelEdited = false;
                }
            }
        }

        return result;
    }

    /// <summary>人数が変わった日はパック数を式で求め直し、日の種類が変わった日は金額を求め直す（U-04・D-77）</summary>
    private static void Recalculate(Menu menu, Dictionary<string, (int ChildrenCount, DayType DayType)> before, Func<string, int?> piecesPerPack)
    {
        foreach (var day in menu.Days)
        {
            var (childrenCount, dayType) = before[day.Id];
            if (day.ChildrenCount != childrenCount)
            {
                MenuItemFactory.RecalculatePackCounts(day, piecesPerPack);
            }
            else if (day.DayType != dayType)
            {
                MenuItemFactory.RefreshAmounts(day);
            }
        }
    }
}

/// <summary>反映した値の項目</summary>
public enum InterpretedField
{
    /// <summary>人数</summary>
    ChildrenCount,

    /// <summary>日の種類</summary>
    DayType,

    /// <summary>見出し</summary>
    Label,
}

/// <summary>日ごとの値の変更</summary>
/// <param name="Date">日</param>
/// <param name="Field">項目</param>
/// <param name="OldValue">前の値</param>
/// <param name="NewValue">新しい値（職員が直した値を残したときは、要望が求めた値）</param>
public sealed record InterpretedChange(DateOnly Date, InterpretedField Field, string? OldValue, string? NewValue);

/// <summary>要望の反映の結果</summary>
public sealed class InterpretedRequestApplyResult
{
    /// <summary>反映した値</summary>
    public List<InterpretedChange> Applied { get; } = [];

    /// <summary>職員が直した値のため、変えなかったもの（「職員が直した値を残しました」と示す）</summary>
    public List<InterpretedChange> KeptStaffEdits { get; } = [];

    /// <summary>反映しなかった要望（読み取りの rejected と、献立にない日付・不正な値。警告で示す）</summary>
    public List<RejectedRequest> NotApplied { get; } = [];
}
