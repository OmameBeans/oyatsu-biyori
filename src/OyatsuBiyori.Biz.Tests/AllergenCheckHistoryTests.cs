using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class AllergenCheckHistoryTests
{
    private static ProductAllergenStatus Status(params AllergenCheck[] checks)
    {
        return AllergenCheckHistory.From(checks).GetStatus(Today, RecheckDays);
    }

    [Fact]
    public void 記録がなければ不明()
    {
        Assert.Equal(ProductAllergenStatus.Unknown, Status());
    }

    [Fact]
    public void 最新が参考なら参考()
    {
        Assert.Equal(ProductAllergenStatus.Reference, Status(ConfirmedAll(Today.AddDays(-10), []), Reference(Today.AddDays(-1))));
    }

    [Theory]
    [InlineData(90, ProductAllergenStatus.Confirmed)]
    [InlineData(91, ProductAllergenStatus.RecheckNeeded)]
    public void 確認済みは再確認期間を過ぎたら要再確認(int daysAgo, ProductAllergenStatus expected)
    {
        Assert.Equal(expected, Status(ConfirmedAll(Today.AddDays(-daysAgo), [])));
    }

    [Fact]
    public void 確認済みで避ける品目を含んでもステータスは確認済み()
    {
        // 判定は「含む」、ステータスは「確認済み」（T1-06）
        var check = ConfirmedAll(Today.AddDays(-10), ["hazelnut"]);

        Assert.Equal(ProductAllergenStatus.Confirmed, Status(check));
        Assert.Equal(AllergenJudgementKind.Contains, AllergenJudge.Judge("ちょこっとサンド", [check], Avoid("hazelnut"), Today, RecheckDays).Kind);
    }

    [Fact]
    public void 最終確認日は最新の確認済みの日本時間の日付で最新が参考でも返す()
    {
        var history = AllergenCheckHistory.From([
            ConfirmedAll(Today.AddDays(-40), []),
            ConfirmedAll(Today.AddDays(-10), []),
            Reference(Today.AddDays(-1)),
        ]);

        Assert.Equal(Today.AddDays(-10), history.LastConfirmedDate);
    }

    [Theory]
    [InlineData(15, 0)]
    [InlineData(23, 30)]
    public void UTCで夜の記録は日本時間では翌日の確認日になる(int hour, int minute)
    {
        // UTC 2026-08-04 の夜 = 日本時間 2026-08-05。今日（11-03）から 90日前なので期限内
        var check = ConfirmedAll(Today, []);
        check.CheckedAt = new DateTime(2026, 8, 4, hour, minute, 0, DateTimeKind.Utc);

        var history = AllergenCheckHistory.From([check]);

        Assert.Equal(new DateOnly(2026, 8, 5), history.LastConfirmedDate);
        Assert.Equal(ProductAllergenStatus.Confirmed, history.GetStatus(Today, RecheckDays));
    }

    [Fact]
    public void 同じ日時の確認済みと参考はどちらも有効な記録で最新は参考()
    {
        var confirmed = ConfirmedAll(Today.AddDays(-1), []);
        var reference = Reference(Today.AddDays(-1), "milk");
        reference.CheckedAt = confirmed.CheckedAt;

        var history = AllergenCheckHistory.From([reference, confirmed]);

        Assert.Same(reference, history.Latest);
        Assert.Same(confirmed, history.LatestConfirmed);
        Assert.Equal(2, history.ValidChecks.Count);
        Assert.Contains(Allergen("milk").Id, history.ContainedAllergenIds);
    }

    [Fact]
    public void 確認済みで含まないの品目もあとの参考で含むになる()
    {
        var history = AllergenCheckHistory.From([ConfirmedAll(Today.AddDays(-20), []), Reference(Today.AddDays(-1), "milk")]);

        Assert.Contains(Allergen("milk").Id, history.ContainedAllergenIds);
    }

    [Fact]
    public void 同じ日時の確認済みが2件あればどちらも有効な記録()
    {
        var first = ConfirmedAll(Today.AddDays(-3), ["milk"]);
        var second = ConfirmedAll(Today.AddDays(-3), []);
        second.CheckedAt = first.CheckedAt;

        var history = AllergenCheckHistory.From([first, second]);

        Assert.Equal(2, history.ValidChecks.Count);
        Assert.Contains(Allergen("milk").Id, history.ContainedAllergenIds);
    }

    [Fact]
    public void 状態の日付は最新の記録の日本時間の日付()
    {
        var history = AllergenCheckHistory.From([ConfirmedAll(Today.AddDays(-20), []), Reference(Today.AddDays(-2))]);

        Assert.Equal(Today.AddDays(-2), history.LatestDate);
        Assert.Equal(Today.AddDays(-20), history.LastConfirmedDate);
    }

    [Fact]
    public void 情報源がパッケージでない確認済みの記録があれば止める()
    {
        var check = ConfirmedAll(Today.AddDays(-1), []);
        check.Source = AllergenCheckSource.Flyer;

        Assert.Throws<InvalidOperationException>(() => AllergenCheckHistory.From([check]));
    }

    [Fact]
    public void 原材料の全文がない確認済みの記録があれば止める()
    {
        var check = ConfirmedAll(Today.AddDays(-1), [], rawIngredients: null);
        check.RawIngredientsOcr = " ";

        Assert.Throws<InvalidOperationException>(() => AllergenCheckHistory.From([check]));
    }

    [Fact]
    public void 職員が手で入れた全文だけの確認済みは受け付ける()
    {
        // Claude が使えないとき、職員が全文を手で入力して確認済みにしてよい（SR-12）
        var check = ConfirmedAll(Today.AddDays(-1), [], rawIngredients: "じゃがいも、食塩");
        check.RawIngredientsOcr = null;

        Assert.Equal(ProductAllergenStatus.Confirmed, AllergenCheckHistory.From([check]).GetStatus(Today, RecheckDays));
    }

    [Fact]
    public void 品目の行が読み込まれていない確認済みの記録があれば止める()
    {
        var check = ConfirmedAll(Today.AddDays(-1), []);
        check.Items = [];

        Assert.Throws<InvalidOperationException>(() => AllergenCheckHistory.From([check]));
    }

    [Fact]
    public void 参考だけなら最終確認日はない()
    {
        Assert.Null(AllergenCheckHistory.From([Reference(Today.AddDays(-1))]).LastConfirmedDate);
    }

    [Fact]
    public void 有効な記録は最新の確認済みとそれより後の記録()
    {
        var old = Reference(Today.AddDays(-30), "egg");
        var confirmed = ConfirmedAll(Today.AddDays(-20), []);
        var later = Reference(Today.AddDays(-5), "milk");

        var history = AllergenCheckHistory.From([later, old, confirmed]);

        Assert.Equal([confirmed.Id, later.Id], history.ValidChecks.Select(c => c.Id));
        Assert.Equal([Allergen("milk").Id], history.ContainedAllergenIds);
    }

    [Fact]
    public void 確認済みがなければすべての記録が有効()
    {
        var history = AllergenCheckHistory.From([Reference(Today.AddDays(-30), "egg"), Reference(Today.AddDays(-5), "milk")]);

        Assert.Equal(2, history.ValidChecks.Count);
        Assert.Equal(2, history.ContainedAllergenIds.Count);
    }
}
