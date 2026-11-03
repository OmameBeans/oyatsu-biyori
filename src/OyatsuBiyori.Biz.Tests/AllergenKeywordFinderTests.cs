using OyatsuBiyori.Biz;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class AllergenKeywordFinderTests
{
    [Fact]
    public void 当たった言葉と品目と元の全文での範囲を返す()
    {
        const string text = "砂糖、ヘーゼルナッツペースト、ココア";

        var matches = AllergenKeywordFinder.Find(text, Avoid("hazelnut"));

        var match = Assert.Single(matches, m => m.Keyword == "ヘーゼルナッツ");
        Assert.Equal(Allergen("hazelnut").Id, match.AllergenId);
        Assert.Equal(3, match.Start);
        Assert.Equal("ヘーゼルナッツ", match.MatchedText);
    }

    [Fact]
    public void 改行をまたいで当たった範囲は改行を含めて元の全文に戻す()
    {
        const string text = "砂糖、ヘーゼル\r\nナッツペースト";

        var match = Assert.Single(AllergenKeywordFinder.Find(text, Avoid("hazelnut")), m => m.Keyword == "ヘーゼルナッツ");

        Assert.Equal("ヘーゼル\r\nナッツ", match.MatchedText);
        Assert.Equal(text.IndexOf('ヘ'), match.Start);
    }

    [Fact]
    public void 半角カナや全角の書き方でも元の範囲に戻す()
    {
        const string text = "小麦粉、ﾍｰｾﾞﾙﾅｯﾂﾍﾟｰｽﾄ";

        var match = Assert.Single(AllergenKeywordFinder.Find(text, Avoid("hazelnut")), m => m.Keyword == "ヘーゼルナッツ");

        Assert.Equal("ﾍｰｾﾞﾙﾅｯﾂ", match.MatchedText);
    }

    [Fact]
    public void ひらがなで書かれていても当たる()
    {
        var matches = AllergenKeywordFinder.Find("まつのみ入り、松の実", Avoid("pine_nut"));

        Assert.Contains(matches, m => m.MatchedText == "松の実");
    }

    [Fact]
    public void 同じ言葉が何度も出ればすべて返す()
    {
        var matches = AllergenKeywordFinder.Find("ピーナッツ、砂糖、ピーナッツバター", Avoid("peanut"))
            .Where(m => m.Keyword == "ピーナッツ")
            .ToList();

        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Start < matches[1].Start);
    }

    [Fact]
    public void 複数の品目に当たる言葉はそれぞれの品目で返す()
    {
        // 「プラリネ」はアーモンドとヘーゼルナッツの両方の言葉
        var matches = AllergenKeywordFinder.Find("チョコレート、プラリネ", Nuts());

        Assert.Contains(matches, m => m.AllergenId == Allergen("almond").Id && m.Keyword == "プラリネ");
        Assert.Contains(matches, m => m.AllergenId == Allergen("hazelnut").Id && m.Keyword == "プラリネ");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("　\n")]
    [InlineData("じゃがいも、植物油、食塩")]
    public void 当たらなければ空を返す(string? text)
    {
        Assert.Empty(AllergenKeywordFinder.Find(text, Nuts()));
    }

    [Fact]
    public void 正規化して空になる言葉は無視する()
    {
        var allergen = new Entities.Allergen { Name = "記号だけ", Category = Entities.AllergenCategory.FacilityAdded };
        allergen.Keywords.Add(new Entities.AllergenKeyword { AllergenId = allergen.Id, Keyword = "・－" });

        Assert.Empty(AllergenKeywordFinder.Find("砂糖、・－、小麦粉", [allergen]));
    }

    [Fact]
    public void 探す品目に入れていない品目の言葉は探さない()
    {
        Assert.Empty(AllergenKeywordFinder.Find("ヘーゼルナッツペースト", Avoid("walnut")));
    }
}
