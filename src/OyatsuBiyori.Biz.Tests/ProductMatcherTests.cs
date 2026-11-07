using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>商品照合（D-100、09 の 2・10 の「照合」）</summary>
public class ProductMatcherTests
{
    private static Product Product(string name, string? maker, string? volume = null, DateOnly? listedWeekStart = null, DateTime? updatedAt = null, string? id = null)
    {
        var product = new Product
        {
            Name = name,
            NormalizedName = TextNormalizer.ForProductMatch(name),
            Maker = maker,
            Volume = volume,
            ListedWeekStart = listedWeekStart,
            UpdateDateTime = updatedAt ?? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        if (id is not null)
        {
            product.Id = id;
        }

        return product;
    }

    [Fact]
    public void 正規化した商品名とメーカーが両方同じときだけ一致する()
    {
        var existing = Product("もぐもぐクッキー", "おやつ製菓");

        var result = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [existing]);

        Assert.Equal(ProductMatchOutcome.Matched, result.Outcome);
        Assert.True(result.IsMatched);
        Assert.Same(existing, result.Product);
        Assert.Empty(result.OtherMatches);
    }

    [Fact]
    public void 商品名が同じでもメーカーが違えば一致しない()
    {
        var existing = Product("もぐもぐクッキー", "おやつ製菓");

        var result = ProductMatcher.Match("もぐもぐクッキー", "ひだまり食品", [existing]);

        Assert.Equal(ProductMatchOutcome.NotFound, result.Outcome);
        Assert.Null(result.Product);
    }

    [Fact]
    public void メーカーが同じでも商品名が違えば一致しない()
    {
        // 味違い（似ている商品）は別の商品（Dice の候補は作らない。FR-IMPORT-05）
        var existing = Product("もぐもぐクッキー いちご味", "おやつ製菓");

        var result = ProductMatcher.Match("もぐもぐクッキー ばなな味", "おやつ製菓", [existing]);

        Assert.Equal(ProductMatchOutcome.NotFound, result.Outcome);
    }

    [Theory]
    [InlineData("ＣＯ・ＯＰ　モグモグクッキー", "オヤツ製菓")]
    [InlineData("コープ もぐもぐクッキー", "おやつ 製菓")]
    [InlineData("CO·OP もぐもぐ・クッキー", "ｵﾔﾂ製菓")]
    [InlineData("もぐもぐクッキー！", "（おやつ製菓）")]
    public void 全角半角_カタカナひらがな_コープ_記号空白の違いでは一致する(string name, string maker)
    {
        var existing = Product("CO・OP もぐもぐクッキー", "おやつ製菓");

        var result = ProductMatcher.Match(name, maker, [existing]);

        Assert.Same(existing, result.Product);
    }

    [Fact]
    public void メーカーのコープは除かないので_メーカーがコープだけの商品も照合できる()
    {
        var existing = Product("CO・OP ぷちえびせん", "コープ");

        var same = ProductMatcher.Match("コープ ぷちえびせん", "ｺｰﾌﾟ", [existing]);
        var other = ProductMatcher.Match("コープ ぷちえびせん", "おやつ製菓", [existing]);

        Assert.Same(existing, same.Product);
        Assert.Equal(ProductMatchOutcome.NotFound, other.Outcome);
    }

    [Fact]
    public void 内容量が違っても一致する()
    {
        var existing = Product("もぐもぐクッキー", "おやつ製菓", "10袋入");

        var result = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [existing]);

        Assert.Same(existing, result.Product);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("　")]
    [InlineData("・")]
    public void メーカーが空のチラシの商品は照合しない(string? maker)
    {
        var existing = Product("もぐもぐクッキー", null);

        var result = ProductMatcher.Match("もぐもぐクッキー", maker, [existing]);

        // 「一致なし」ではなく「照合できない」（再入力になる。FR-IMPORT-03）
        Assert.Equal(ProductMatchOutcome.NotMatchable, result.Outcome);
        Assert.Equal(ProductNotMatchableReason.MissingMaker, result.NotMatchableReason);
        Assert.Null(result.Product);
    }

    [Fact]
    public void 商品名が空のチラシの商品は照合しない()
    {
        var result = ProductMatcher.Match("　", "おやつ製菓", [Product("もぐもぐクッキー", "おやつ製菓")]);

        Assert.Equal(ProductMatchOutcome.NotMatchable, result.Outcome);
        Assert.Equal(ProductNotMatchableReason.MissingName, result.NotMatchableReason);
    }

    [Fact]
    public void 商品名がコープだけなら正規化で空になり照合しない()
    {
        var result = ProductMatcher.Match("CO・OP", "コープ", [Product("コープ", "コープ")]);

        Assert.Equal(ProductNotMatchableReason.MissingName, result.NotMatchableReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("　")]
    public void メーカーが空のマスタの商品とは一致しない(string? maker)
    {
        var existing = Product("もぐもぐクッキー", maker);

        var result = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [existing]);

        Assert.Equal(ProductMatchOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public void 一致が2件以上なら最後に載った週が新しい方を使う()
    {
        var older = Product("もぐもぐクッキー", "おやつ製菓", "10袋入", new DateOnly(2026, 10, 5), updatedAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Product("もぐもぐクッキー", "おやつ製菓", "12袋入", new DateOnly(2026, 10, 26), updatedAt: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        var neverListed = Product("もぐもぐクッキー", "おやつ製菓", "8袋入", null, updatedAt: new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));

        var result = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [older, neverListed, newer]);

        Assert.Same(newer, result.Product);
        Assert.Equal([older, neverListed], result.OtherMatches);
    }

    [Fact]
    public void 載った週が同じなら更新日時が新しい方_それも同じならIDの順で使う()
    {
        var week = new DateOnly(2026, 10, 26);
        var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldUpdate = Product("もぐもぐクッキー", "おやつ製菓", listedWeekStart: week, updatedAt: at, id: "a");
        var newUpdate = Product("もぐもぐクッキー", "おやつ製菓", listedWeekStart: week, updatedAt: at.AddDays(1), id: "c");
        var sameUpdateB = Product("もぐもぐクッキー", "おやつ製菓", listedWeekStart: null, updatedAt: at, id: "b2");
        var sameUpdateA = Product("もぐもぐクッキー", "おやつ製菓", listedWeekStart: null, updatedAt: at, id: "b1");

        var byUpdate = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [oldUpdate, newUpdate]);
        var byId = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [sameUpdateB, sameUpdateA]);

        Assert.Same(newUpdate, byUpdate.Product);
        Assert.Same(sameUpdateA, byId.Product);
    }

    [Fact]
    public void 使わない商品も照合する()
    {
        var disabled = Product("もぐもぐクッキー", "おやつ製菓");
        disabled.IsDisabled = true;

        var result = ProductMatcher.Match("もぐもぐクッキー", "おやつ製菓", [disabled]);

        Assert.Same(disabled, result.Product);
    }

    [Fact]
    public void 保存済みの正規化名ではなく商品名とメーカーから正規化し直して比べる()
    {
        var existing = Product("もぐもぐクッキー", "おやつ製菓");
        existing.NormalizedName = "古い値";

        var result = ProductMatcher.Match("モグモグクッキー", "おやつ製菓", [existing]);

        Assert.Same(existing, result.Product);
    }

    [Fact]
    public void 照合の鍵は商品名とメーカーを正規化したもの()
    {
        var key = ProductMatcher.KeyOf("CO・OP モグモグ クッキー", "オヤツ製菓");

        Assert.Equal("もぐもぐくっきー", key.Name);
        Assert.Equal("おやつ製菓", key.Maker);
        Assert.True(key.IsMatchable);
        Assert.False(ProductMatcher.KeyOf("もぐもぐクッキー", null).IsMatchable);
    }
}
