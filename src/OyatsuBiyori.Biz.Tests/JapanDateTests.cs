using Microsoft.Extensions.Time.Testing;
using OyatsuBiyori.Biz;

namespace OyatsuBiyori.Biz.Tests;

public class JapanDateTests
{
    [Fact]
    public void 日本時間の0時から9時はUTCでは前日だが日本時間の日付になる()
    {
        // 日本時間 2026-11-09 08:59 = UTC 2026-11-08 23:59
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 23, 59, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 9), JapanDate.Today(clock));
    }

    [Fact]
    public void UTCの15時は日本時間では翌日の0時になる()
    {
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 11, 9), JapanDate.FromUtc(utc));
    }

    [Fact]
    public void UTCの14時59分は日本時間ではまだ同じ日()
    {
        var utc = new DateTime(2026, 11, 8, 14, 59, 59, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 11, 8), JapanDate.FromUtc(utc));
    }

    [Fact]
    public void SQLiteから読んだKindがUnspecifiedの日時もUTCとして扱う()
    {
        var unspecified = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateOnly(2026, 11, 9), JapanDate.FromUtc(unspecified));
    }

    [Fact]
    public void Localの日時は受け付けない()
    {
        var local = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => JapanDate.FromUtc(local));
    }

    [Fact]
    public void 日本時間のその日の0時はUTCでは前日の15時()
    {
        Assert.Equal(new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc), JapanDate.StartOfDayUtc(new DateOnly(2026, 11, 9)));
    }

    [Theory]
    [InlineData(2026, 11, 2, 2026, 11, 2)] // 月曜はその日
    [InlineData(2026, 11, 6, 2026, 11, 2)] // 金曜
    [InlineData(2026, 11, 8, 2026, 11, 2)] // 日曜は前の月曜
    [InlineData(2026, 11, 9, 2026, 11, 9)] // 次の月曜
    [InlineData(2027, 1, 1, 2026, 12, 28)] // 年をまたぐ
    public void 週の初日は月曜(int y, int m, int d, int ey, int em, int ed)
    {
        Assert.Equal(new DateOnly(ey, em, ed), JapanDate.WeekStart(new DateOnly(y, m, d)));
    }
}
