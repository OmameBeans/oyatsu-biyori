namespace OyatsuBiyori.Biz;

/// <summary>
/// 日本時間の日付を扱う（NFR-OPS-07）。日時は UTC で保存し、「今日」「週」の判定は日本時間の日付で行う。
/// 時計は TimeProvider で受け取り、テストで日付を固定できるようにする。
/// </summary>
public static class JapanDate
{
    /// <summary>日本時間のタイムゾーン（Windows の ID。Linux では .NET が IANA の ID に読み替える）</summary>
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

    /// <summary>日本時間の今日</summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return FromUtc(timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>UTC の日時を、日本時間の日付に直す</summary>
    public static DateOnly FromUtc(DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("日時は UTC で渡してください", nameof(utcDateTime));
        }

        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone));
    }

    /// <summary>日本時間のその日の0時を UTC の日時で返す（「1日」の区切りで数えるため）</summary>
    public static DateTime StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
    }

    /// <summary>その日を含む週の初日（月曜）</summary>
    public static DateOnly WeekStart(DateOnly date)
    {
        // DayOfWeek は日曜が 0 なので、月曜からの日数に直す
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }
}
