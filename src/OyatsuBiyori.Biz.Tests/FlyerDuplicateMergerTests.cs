using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>二重の読み取りのまとめ（FR-IMPORT-06、09 の 10 の「照合」）</summary>
public class FlyerDuplicateMergerTests
{
    private static FlyerReadProduct Read(
        string name,
        string? maker = "ひだまり製菓",
        string? volume = "10袋入",
        int? price = 216,
        int? excluding = 200,
        string? orderNo = "8101",
        string? label = "乳・小麦",
        IReadOnlyList<string>? codes = null,
        IReadOnlyList<string>? unmapped = null,
        string? description = null,
        int? pieces = 10) => new(
        name, maker, volume, excluding, price, orderNo, null, label, label is null, codes ?? [], unmapped ?? [], description, null, pieces, "10袋入りのため", SnackType.Sweet, [], []);

    [Fact]
    public void 入数の候補が食い違えば_食い違った値を両方持ち_読めなかった行は数えない()
    {
        var conflicted = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", pieces: 6), Read("もぐもぐクッキー", pieces: 12)]);
        var oneUnread = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", pieces: null), Read("もぐもぐクッキー", pieces: 12)]);

        var conflict = Assert.Single(Assert.Single(conflicted).Conflicts);
        Assert.Equal(FlyerItemField.PiecesPerPack, conflict.Field);
        Assert.Equal(["6", "12"], conflict.Values);
        var merged = Assert.Single(oneUnread);
        Assert.Empty(merged.Conflicts);
        Assert.Equal(12, merged.Read.PiecesPerPackCandidate);
    }

    [Fact]
    public void 同じ商品を2回読んだら1件にまとめ_アレルゲンの品目は和集合になる()
    {
        var top = Read("もぐもぐクッキー", label: "乳・小麦", codes: ["milk", "wheat"], unmapped: ["ﾅｿ"], description: "さくさく");
        var bottom = Read("ＣＯ・ＯＰ モグモグクッキー", maker: "ﾋﾀﾞﾏﾘ製菓", label: "卵", codes: ["egg"], unmapped: ["ふめい"], description: "ヘーゼルナッツ入り");
        var other = Read("ぽりぽりスナック");

        var merged = FlyerDuplicateMerger.Merge([top, other, bottom]);

        Assert.Equal(2, merged.Count);
        var cookie = merged[0];
        Assert.True(cookie.IsMerged);
        Assert.Equal([0, 2], cookie.SourceIndexes);
        Assert.Empty(cookie.Conflicts);
        Assert.Equal(["milk", "wheat", "egg"], cookie.Read.AllergenCodes);
        Assert.Equal(["ﾅｿ", "ふめい"], cookie.Read.UnmappedAllergenTerms);
        Assert.Equal("乳・小麦／卵", cookie.Read.AllergenLabelText);
        Assert.Contains("ヘーゼルナッツ", cookie.Read.Description);
        Assert.Contains("さくさく", cookie.Read.Description);
        Assert.Equal("もぐもぐクッキー", cookie.Read.Name);
        Assert.False(merged[1].IsMerged);
    }

    [Fact]
    public void 値がすべて同じならまとめるだけで食い違いにしない()
    {
        var merged = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー"), Read("もぐもぐクッキー", volume: "10袋 入")]);

        var item = Assert.Single(merged);
        Assert.Empty(item.Conflicts);
        Assert.Equal(216, item.Read.PriceIncludingTax);
    }

    [Fact]
    public void 価格が食い違えば_食い違った値を両方持つ()
    {
        var merged = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", price: 216), Read("もぐもぐクッキー", price: 270)]);

        var conflict = Assert.Single(Assert.Single(merged).Conflicts);
        Assert.Equal(FlyerItemField.PriceIncludingTax, conflict.Field);
        Assert.Equal(["216", "270"], conflict.Values);
    }

    [Fact]
    public void 税抜_内容量_注文番号の食い違いも持つ()
    {
        var merged = FlyerDuplicateMerger.Merge([
            Read("もぐもぐクッキー", excluding: 200, volume: "10袋入", orderNo: "8101"),
            Read("もぐもぐクッキー", excluding: 250, volume: "12袋入", orderNo: "8102"),
        ]);

        Assert.Equal(
            [FlyerItemField.PriceExcludingTax, FlyerItemField.Volume, FlyerItemField.OrderNo],
            Assert.Single(merged).Conflicts.Select(c => c.Field));
    }

    [Fact]
    public void 片方だけ読めなかった値は食い違いにせず_読めた値を使う()
    {
        var merged = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", price: null, orderNo: null), Read("もぐもぐクッキー", price: 216, orderNo: "8101")]);

        var item = Assert.Single(merged);
        Assert.Empty(item.Conflicts);
        Assert.Equal(216, item.Read.PriceIncludingTax);
        Assert.Equal("8101", item.Read.OrderNo);
    }

    [Fact]
    public void 表記なしは_どれも表記なしと読んだときだけ表記なしにする()
    {
        var mixed = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", label: null), Read("もぐもぐクッキー", label: "卵", codes: ["egg"])]);
        var allAbsent = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", label: null), Read("もぐもぐクッキー", label: null)]);

        Assert.False(Assert.Single(mixed).Read.AllergenLabelAbsent);
        Assert.Equal("卵", Assert.Single(mixed).Read.AllergenLabelText);
        Assert.Equal(["egg"], Assert.Single(mixed).Read.AllergenCodes);
        Assert.True(Assert.Single(allAbsent).Read.AllergenLabelAbsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void メーカーが空の行はまとめない(string? maker)
    {
        var merged = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", maker: maker), Read("もぐもぐクッキー", maker: maker), Read("もぐもぐクッキー")]);

        Assert.Equal(3, merged.Count);
        Assert.All(merged, m => Assert.False(m.IsMerged));
    }

    [Fact]
    public void メーカーが違えばまとめない()
    {
        var merged = FlyerDuplicateMerger.Merge([Read("もぐもぐクッキー", maker: "ひだまり製菓"), Read("もぐもぐクッキー", maker: "おやつ食品")]);

        Assert.Equal(2, merged.Count);
    }
}
