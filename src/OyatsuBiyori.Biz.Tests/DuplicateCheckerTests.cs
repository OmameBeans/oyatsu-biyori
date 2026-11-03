using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;

namespace OyatsuBiyori.Biz.Tests;

public class DuplicateCheckerTests
{
    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    [InlineData(1, true)]
    public void 確定献立で出した日からの日数が期間以内なら重複(int daysAgo, bool expected)
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var menu = NewMenu(20000, 50, (chips, 398));
        Put(menu, 2, chips);
        var history = new[] { new ServedItem("past-menu", D(2).AddDays(-daysAgo), chips.Id) };

        var hits = DuplicateChecker.Check(menu, Context([chips], history: history));

        Assert.Equal(expected, hits.Any());
        if (expected)
        {
            Assert.Equal(daysAgo, hits[0].DaysApart);
            Assert.Contains($"{daysAgo}日前", hits[0].Message);
        }
    }

    [Fact]
    public void 後の週の確定献立も期間内なら出す予定として警告する()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var menu = NewMenu(20000, 50, (chips, 398));
        Put(menu, 4, chips);
        var history = new[] { new ServedItem("next-menu", D(4).AddDays(3), chips.Id) };

        var hit = Assert.Single(DuplicateChecker.Check(menu, Context([chips], history: history)));

        Assert.Equal(-3, hit.DaysApart);
        Assert.Contains("3日後", hit.Message);
        Assert.Contains("出す予定", hit.Message);
    }

    [Fact]
    public void 分類の期間が設定なしなら確定献立とは比べない()
    {
        var jelly = Safe("ぷるぷるゼリー", SnackType.Smooth);
        var menu = NewMenu(20000, 50, (jelly, 298));
        Put(menu, 0, jelly);
        var history = new[] { new ServedItem("past-menu", D(0).AddDays(-7), jelly.Id) };

        Assert.Empty(DuplicateChecker.Check(menu, Context([jelly], history: history)));
    }

    [Fact]
    public void 期間は分類ごとの設定を使う()
    {
        var cookie = Safe("さくさくクッキー", SnackType.Sweet);
        var menu = NewMenu(20000, 50, (cookie, 298));
        Put(menu, 0, cookie);
        var history = new[] { new ServedItem("past-menu", D(0).AddDays(-45), cookie.Id) };

        var rules = new[] { new DuplicateAvoidRule(SnackType.Sweet, 60) };

        Assert.Single(DuplicateChecker.Check(menu, Context([cookie], history: history, rules: rules)));
    }

    [Fact]
    public void 同じ週の中の重複は分類の期間にかかわらず両方の日に警告する()
    {
        var jelly = Safe("ぷるぷるゼリー", SnackType.Smooth);
        var menu = NewMenu(20000, 50, (jelly, 298));
        var monday = Put(menu, 0, jelly);
        var thursday = Put(menu, 3, jelly);

        var hits = DuplicateChecker.Check(menu, Context([jelly]));

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.Item == monday && h.OtherDate == D(3) && h.SameMenu);
        Assert.Contains(hits, h => h.Item == thursday && h.OtherDate == D(0) && h.DaysApart == 3);
    }

    [Fact]
    public void 同じ日に同じ商品が2つあれば警告する()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50, (cookie, 298));
        Put(menu, 1, cookie);
        PutRaw(menu, 1, cookie.Id);

        var hits = DuplicateChecker.Check(menu, Context([cookie]));

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal("同じ日に同じ商品が2つ入っています", h.Message));
    }

    [Fact]
    public void 対象の献立自身が確定済みでも履歴としては数えない()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var menu = NewMenu(20000, 50, (chips, 398));
        Put(menu, 2, chips);
        menu.Status = MenuStatus.Confirmed;
        var history = ServedItem.FromMenus([menu]);

        Assert.Empty(DuplicateChecker.Check(menu, Context([chips], history: history)));
    }

    [Fact]
    public void 履歴は確定献立の通常の日だけから作る()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var confirmed = NewMenu(20000, 50, (chips, 398));
        Put(confirmed, 0, chips);
        Put(confirmed, 1, chips);
        confirmed.Days[1].DayType = DayType.NoSnack;
        confirmed.Status = MenuStatus.Confirmed;
        var draft = NewMenu(20000, 50, (chips, 398));
        Put(draft, 2, chips);

        var history = ServedItem.FromMenus([confirmed, draft]);

        var served = Assert.Single(history);
        Assert.Equal(D(0), served.Date);
    }

    [Fact]
    public void 手順2の一覧は週の初日から数えた日数を返す()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var history = new[]
        {
            new ServedItem("a", WeekStart.AddDays(-40), chips.Id),
            new ServedItem("b", WeekStart.AddDays(-12), chips.Id),
            new ServedItem("c", WeekStart.AddDays(-5), "other"),
        };

        Assert.Equal(12, DuplicateChecker.DaysSinceServed(chips, WeekStart, "this", Context([chips], history: history)));
    }

    [Fact]
    public void 手順2の一覧は対象の献立自身を数えない()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var history = new[] { new ServedItem("this", WeekStart.AddDays(-3), chips.Id) };

        Assert.Null(DuplicateChecker.DaysSinceServed(chips, WeekStart, "this", Context([chips], history: history)));
        Assert.Equal(3, DuplicateChecker.DaysSinceServed(chips, WeekStart, "other", Context([chips], history: history)));
    }

    [Fact]
    public void 手順2の一覧は期間を過ぎていれば出さない()
    {
        var chips = Safe("うすしおポテト", SnackType.Salty);
        var history = new[] { new ServedItem("a", WeekStart.AddDays(-31), chips.Id) };

        Assert.Null(DuplicateChecker.DaysSinceServed(chips, WeekStart, "this", Context([chips], history: history)));
    }

    [Fact]
    public void Claudeに渡す範囲は最長の期間に1週分を足す()
    {
        var rules = new[] { new DuplicateAvoidRule(SnackType.Salty, 30), new DuplicateAvoidRule(SnackType.Sweet, 60) };

        var (from, to) = DuplicateChecker.HistoryRange(WeekStart, rules);

        Assert.Equal(WeekStart.AddDays(-67), from);
        Assert.Equal(WeekStart.AddDays(6 + 60), to);
    }

    [Fact]
    public void 期間の設定がなければ前後1週分()
    {
        var (from, to) = DuplicateChecker.HistoryRange(WeekStart, []);

        Assert.Equal(WeekStart.AddDays(-7), from);
        Assert.Equal(WeekStart.AddDays(13), to);
    }
}
