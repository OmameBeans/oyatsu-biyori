using OyatsuBiyori.Biz;

namespace OyatsuBiyori.Biz.Tests;

public class FlyerReadingValidatorTests
{
    [Theory]
    [InlineData(298, 321)] // 298×1.08=321.84 → 切り捨て
    [InlineData(298, 322)] // 四捨五入・切り上げ
    [InlineData(298, 327)] // 298×1.10=327.8 → 切り捨て
    [InlineData(298, 328)] // 四捨五入・切り上げ
    [InlineData(100, 108)]
    [InlineData(100, 110)]
    [InlineData(250, 270)]
    [InlineData(1980, 2138)] // 2138.4 → 切り捨て・四捨五入
    [InlineData(1980, 2139)] // 切り上げ
    public void 税込は税抜の8パーセントか10パーセントの端数処理のどれかに合えば整合(int excluding, int including)
    {
        Assert.True(FlyerReadingValidator.IsTaxConsistent(excluding, including));
    }

    [Theory]
    [InlineData(298, 320)]
    [InlineData(298, 323)]
    [InlineData(298, 329)]
    [InlineData(298, 298)]
    [InlineData(398, 328)] // 298 と読み違えたときの税込
    public void 合わない税込は整合しない(int excluding, int including)
    {
        Assert.False(FlyerReadingValidator.IsTaxConsistent(excluding, including));
    }

    [Fact]
    public void 四捨五入の境界は切り上げる側でも合う()
    {
        // 125×1.08=135 ちょうど、 75×1.10=82.5 → 四捨五入で83・切り捨てで82
        Assert.True(FlyerReadingValidator.IsTaxConsistent(125, 135));
        Assert.True(FlyerReadingValidator.IsTaxConsistent(75, 82));
        Assert.True(FlyerReadingValidator.IsTaxConsistent(75, 83));
        Assert.False(FlyerReadingValidator.IsTaxConsistent(75, 84));
    }

    [Fact]
    public void 税込と税抜が合わない商品に印を付ける()
    {
        var issues = FlyerReadingValidator.Validate([new FlyerReadItem("さくさくクッキー", "10袋入", 328, 398, 10)]);

        var issue = Assert.Single(issues);
        Assert.Equal(FlyerReadingIssueKind.TaxMismatch, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Fact]
    public void 税抜がなければ整合は見ない()
    {
        var issues = FlyerReadingValidator.Validate([new FlyerReadItem("さくさくクッキー", "10袋入", 328, null, 10)]);

        Assert.Empty(issues);
    }

    [Fact]
    public void 必須の項目の抜けに印を付ける()
    {
        var issues = FlyerReadingValidator.Validate([new FlyerReadItem(" ", null, null, 298, null)]);

        Assert.Contains(issues, x => x.Kind == FlyerReadingIssueKind.MissingName);
        Assert.Contains(issues, x => x.Kind == FlyerReadingIssueKind.MissingVolume);
        Assert.Contains(issues, x => x.Kind == FlyerReadingIssueKind.MissingPrice);
        Assert.DoesNotContain(issues, x => x.Kind == FlyerReadingIssueKind.TaxMismatch);
    }

    [Fact]
    public void 同じチラシの中の二重は正規化名と正規化した内容量で見つける()
    {
        var issues = FlyerReadingValidator.Validate(
        [
            new FlyerReadItem("CO・OP ポテトチップス うすしお", "40g（2袋×12個包装）", 398, null, 12),
            new FlyerReadItem("ふわふわゼリー", "10個入", 298, null, 10),
            new FlyerReadItem("ポテトチップス　ウスシオ", "40g(2袋x12個包装)", 398, null, 12),
        ]);

        var duplicates = issues.Where(x => x.Kind == FlyerReadingIssueKind.Duplicate).ToList();
        Assert.Equal([0, 2], duplicates.Select(x => x.Index));
        Assert.Contains("3件目", duplicates[0].Message);
        Assert.Contains("1件目", duplicates[1].Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void 税込が1未満なら読み取れていない扱い(int price)
    {
        var issues = FlyerReadingValidator.Validate([new FlyerReadItem("さくさくクッキー", "10袋入", price, 298, 10)]);

        var issue = Assert.Single(issues);
        Assert.Equal(FlyerReadingIssueKind.MissingPrice, issue.Kind);
    }

    [Fact]
    public void 三重の読み取りはそれぞれにほかの2件を示す()
    {
        var item = new FlyerReadItem("さくさくクッキー", "10袋入", 298, null, 10);

        var duplicates = FlyerReadingValidator.Validate([item, item, item]).Where(x => x.Kind == FlyerReadingIssueKind.Duplicate).ToList();

        Assert.Equal(3, duplicates.Count);
        Assert.Contains("2件目・3件目", duplicates[0].Message);
    }

    [Fact]
    public void 正規化すると空になる商品名どうしは二重にしない()
    {
        var item = new FlyerReadItem("コープ", "10袋入", 298, null, 10);

        Assert.DoesNotContain(FlyerReadingValidator.Validate([item, item]), x => x.Kind == FlyerReadingIssueKind.Duplicate);
    }

    [Fact]
    public void 内容量が違えば二重にしない()
    {
        var issues = FlyerReadingValidator.Validate(
        [
            new FlyerReadItem("ポテトチップス うすしお", "40g（2袋×12個包装）", 398, null, 12),
            new FlyerReadItem("ポテトチップス うすしお", "60g×12袋", 598, null, 12),
        ]);

        Assert.DoesNotContain(issues, x => x.Kind == FlyerReadingIssueKind.Duplicate);
    }

    [Theory]
    [InlineData("10袋入", 10, true)]
    [InlineData("40g（2袋×12個包装）", 12, true)]
    [InlineData("40g（2袋×12個包装）", 24, true)]
    [InlineData("40g（2袋×12個包装）", 2, true)]
    [InlineData("４ｇ×３袋", 3, true)]
    [InlineData("100ml×18", 18, true)]
    [InlineData("10袋入", 12, false)]
    [InlineData("120g", 10, false)]
    [InlineData("10袋入", null, false)]
    [InlineData("10袋入", 0, false)]
    public void 入数の候補が内容量の表記から出せるか(string volume, int? candidate, bool expected)
    {
        Assert.Equal(expected, FlyerReadingValidator.IsPiecesCandidateSupported(volume, candidate));
    }

    [Fact]
    public void 入数の候補がない商品に印を付ける()
    {
        var issues = FlyerReadingValidator.Validate([new FlyerReadItem("たっぷりプリン", "1個", 128, null, null)]);

        var issue = Assert.Single(issues);
        Assert.Equal(FlyerReadingIssueKind.PiecesUnclear, issue.Kind);
    }
}
