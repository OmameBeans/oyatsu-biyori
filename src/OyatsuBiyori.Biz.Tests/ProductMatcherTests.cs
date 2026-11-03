using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Tests;

public class ProductMatcherTests
{
    private static readonly ProductMatchOptions Options = new();

    private static Product Product(string name, string? volume = null, string? maker = null) => new()
    {
        Name = name,
        NormalizedName = TextNormalizer.ForProductMatch(name),
        Volume = volume,
        Maker = maker,
    };

    private static ProductMatchResult Match(string name, string? volume, string? maker, params Product[] products)
    {
        return ProductMatcher.Match(new ProductMatchInput(name, volume, maker), products, Options);
    }

    [Fact]
    public void 正規化名と内容量が一致すれば同じ商品の候補でClaudeに回さない()
    {
        var existing = Product("CO・OP もぐもぐクッキー", "40g（2袋×12個包装）");

        var result = Match("コープ　もぐもぐクッキー", "40g(2袋x12個包装)", null, existing);

        Assert.Equal([existing], result.SameProducts.Select(m => m.Product));
        Assert.True(result.IsDecidedByRule);
        Assert.True(result.CanBulkConfirmAsSame);
        Assert.False(result.NeedsJudgement);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("オヤツ製菓", true)]
    public void メーカーは両方に値があるときだけ比べる(string? maker, bool expectedCompared)
    {
        var existing = Product("もぐもぐクッキー", "120g", "おやつ製菓");

        var match = Assert.Single(Match("もぐもぐクッキー", "120g", maker, existing).SameProducts);

        Assert.Equal(expectedCompared, match.MakerCompared);
    }

    [Fact]
    public void メーカーが両方にないときも名前と内容量で同じ商品の候補にする()
    {
        Assert.Single(Match("もぐもぐクッキー", "120g", null, Product("もぐもぐクッキー", "120g")).SameProducts);
    }

    [Fact]
    public void メーカーが違えば同じ名前でも機械的に決めずClaudeに回す()
    {
        var existing = Product("もぐもぐクッキー", "120g", "おやつ製菓");

        var result = Match("もぐもぐクッキー", "120g", "ほかの製菓", existing);

        Assert.Empty(result.SameProducts);
        Assert.False(result.IsDecidedByRule);
        Assert.True(result.NeedsJudgement);
        Assert.Equal(1.0, result.Candidates.Single().Score);
    }

    [Fact]
    public void 正規化名が同じで内容量が違えば内容量違いの候補でClaudeに回さない()
    {
        var existing = Product("もぐもぐクッキー", "120g");

        var result = Match("ＣＯ・ＯＰ もぐもぐクッキー", "240g", null, existing);

        Assert.Empty(result.SameProducts);
        Assert.Equal([existing], result.VolumeVariants.Select(m => m.Product));
        Assert.False(result.CanBulkConfirmAsSame);
        Assert.False(result.NeedsJudgement);
    }

    [Theory]
    [InlineData(null, "120g")]
    [InlineData("120g", null)]
    [InlineData(null, null)]
    [InlineData("", "　")]
    public void 内容量が片方か両方になければ機械的に決めずClaudeに回す(string? existingVolume, string? flyerVolume)
    {
        var existing = Product("もぐもぐクッキー", existingVolume);

        var result = Match("もぐもぐクッキー", flyerVolume, null, existing);

        Assert.False(result.IsDecidedByRule);
        Assert.True(result.NeedsJudgement);
    }

    [Fact]
    public void 同じ商品の候補が2件以上ならまとめて確定せずClaudeにも回さない()
    {
        var first = Product("もぐもぐクッキー", "120g", "おやつ製菓");
        var second = Product("もぐもぐクッキー", "120g", "べつの製菓");

        var result = Match("もぐもぐクッキー", "120g", null, first, second);

        Assert.Equal(2, result.SameProducts.Count);
        Assert.False(result.CanBulkConfirmAsSame);
        Assert.False(result.NeedsJudgement);
    }

    [Fact]
    public void 似ているが一致しない商品はDice係数の上位5件をClaudeに回す()
    {
        var products = new[]
        {
            Product("もぐもぐクッキー チョコ味", "120g"),
            Product("もぐもぐクッキー いちご味", "120g"),
            Product("もぐもぐクッキー バター味", "120g"),
            Product("もぐもぐクッキー 抹茶味", "120g"),
            Product("もぐもぐクッキー きなこ味", "120g"),
            Product("もぐもぐクッキー ミルク味", "120g"),
            Product("のりしおポテト", "60g"),
        };

        var result = Match("もぐもぐクッキー ココア味", "120g", null, products);

        Assert.True(result.NeedsJudgement);
        Assert.Equal(5, result.Candidates.Count);
        Assert.DoesNotContain(result.Candidates, c => c.Product.Name == "のりしおポテト");
        Assert.True(result.Candidates.Zip(result.Candidates.Skip(1)).All(p => p.First.Score >= p.Second.Score));
    }

    [Fact]
    public void 上位5件は似ている順に選び6件目以降は落とす()
    {
        // 共通部分の長さを変えて、似ている度合いに差をつける（並べる順は似ていない順）
        var products = new[]
        {
            Product("もぐもぐ"),
            Product("もぐもぐく"),
            Product("もぐもぐくっ"),
            Product("もぐもぐくっき"),
            Product("もぐもぐくっきー"),
            Product("もぐもぐくっきーち"),
        };

        var result = Match("もぐもぐくっきーちょこ", null, null, products);

        Assert.Equal(5, result.Candidates.Count);
        Assert.Equal("もぐもぐくっきーち", result.Candidates[0].Product.Name);
        Assert.DoesNotContain(result.Candidates, c => c.Product.Name == "もぐもぐ");
    }

    [Fact]
    public void しきい値ちょうどの商品は候補に入る()
    {
        // 「あいうか」と「あいうき」：バイグラム 3 と 3、共通2つ → 4 / 6。しきい値をちょうどその値にして「以上」を確かめる
        var score = ProductMatcher.DiceCoefficient("あいうか", "あいうき");
        var options = new ProductMatchOptions { DiceThreshold = score };

        var result = ProductMatcher.Match(new ProductMatchInput("あいうか", null, null), [Product("あいうき")], options);

        Assert.Single(result.Candidates);
    }

    [Fact]
    public void しきい値より似ていなければ候補にせずClaudeにも回さない()
    {
        var result = Match("ふわふわゼリー", "6個", null, Product("のりしおポテト", "60g"));

        Assert.Empty(result.Candidates);
        Assert.False(result.NeedsJudgement);
    }

    [Fact]
    public void しきい値は設定で変えられる()
    {
        var existing = Product("もぐもぐクッキー チョコ味");
        var strict = new ProductMatchOptions { DiceThreshold = 0.99 };

        var result = ProductMatcher.Match(new ProductMatchInput("もぐもぐクッキー いちご味", null, null), [existing], strict);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void ルールで決まった商品は候補に重ねて出さない()
    {
        var same = Product("もぐもぐクッキー", "120g");
        var flavor = Product("もぐもぐクッキー いちご味", "120g");

        var result = Match("もぐもぐクッキー", "120g", null, same, flavor);

        Assert.Equal([same], result.SameProducts.Select(m => m.Product));
        Assert.DoesNotContain(result.Candidates, c => c.Product == same);
        Assert.False(result.NeedsJudgement);
    }

    [Fact]
    public void 使わない商品も照合する()
    {
        var disabled = Product("もぐもぐクッキー", "120g");
        disabled.IsDisabled = true;
        var disabledFlavor = Product("もぐもぐクッキー いちご味", "120g");
        disabledFlavor.IsDisabled = true;

        Assert.Single(Match("もぐもぐクッキー", "120g", null, disabled).SameProducts);
        Assert.Single(Match("もぐもぐクッキー チョコ味", "120g", null, disabledFlavor).Candidates);
    }

    [Fact]
    public void 保存済みの正規化名ではなく商品名から正規化し直して比べる()
    {
        var existing = Product("もぐもぐクッキー", "120g");
        existing.NormalizedName = "古い正規化の値";

        Assert.Single(Match("もぐもぐクッキー", "120g", null, existing).SameProducts);
    }

    [Fact]
    public void 味違いは内容量が同じでも同じ商品にせずClaudeに回す()
    {
        // 味違いにアレルゲン確認を引き継がない（SR-05）
        var existing = Product("もぐもぐクッキー チョコ味", "120g");

        var result = Match("もぐもぐクッキー いちご味", "120g", null, existing);

        Assert.Empty(result.SameProducts);
        Assert.Empty(result.VolumeVariants);
        Assert.True(result.NeedsJudgement);
    }

    [Fact]
    public void コープの表記の有無は同じ名前として扱いメーカー未確認を示す()
    {
        // 商品名のコープの表記は照合では除く（3.1）。チラシ側にメーカーがないので「メーカー未確認」と示す
        var existing = Product("もぐもぐクッキー", "120g", "おやつ製菓");

        var match = Assert.Single(Match("コープ もぐもぐクッキー", "120g", null, existing).SameProducts);

        Assert.False(match.MakerCompared);
    }

    [Fact]
    public void 一文字の商品名でも例外にならない()
    {
        var result = Match("飴", "1袋", null, Product("飴", "2袋"), Product("のりしおポテト", "60g"));

        Assert.Single(result.VolumeVariants);
    }

    [Theory]
    [InlineData("ab", "ab", 1.0)]
    [InlineData("abcd", "abce", 2.0 * 2 / 6)]
    [InlineData("abc", "xyz", 0.0)]
    [InlineData("a", "a", 1.0)]
    [InlineData("a", "ab", 0.0)]
    [InlineData("", "ab", 0.0)]
    [InlineData("aaaa", "aa", 2.0 * 1 / 4)]
    public void Dice係数は文字バイグラムを重複も数えて求める(string a, string b, double expected)
    {
        Assert.Equal(expected, ProductMatcher.DiceCoefficient(a, b), 6);
    }
}
