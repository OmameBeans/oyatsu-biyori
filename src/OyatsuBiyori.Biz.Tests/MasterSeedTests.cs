using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;

namespace OyatsuBiyori.Biz.Tests;

public class MasterSeedTests
{
    [Fact]
    public void 品目は29品目と施設の追加品目5つ()
    {
        var allergens = MasterSeed.Allergens().ToList();

        Assert.Equal(9, allergens.Count(a => a.Category == AllergenCategory.Specified));
        Assert.Equal(20, allergens.Count(a => a.Category == AllergenCategory.Recommended));
        Assert.Equal(5, allergens.Count(a => a.Category == AllergenCategory.FacilityAdded));
    }

    [Fact]
    public void 表示が確実でない品目は推奨20品目と経過措置中のカシューナッツ()
    {
        var notGuaranteed = MasterSeed.Allergens().Where(a => a.LabelNotGuaranteed).ToList();

        Assert.Equal(21, notGuaranteed.Count);
        Assert.All(notGuaranteed.Where(a => a.Category != AllergenCategory.Recommended), a => Assert.Equal("cashew", a.Code));
    }

    [Fact]
    public void シードしたすべての探す言葉が自分の原文に当たる()
    {
        foreach (var keyword in MasterSeed.Keywords())
        {
            Assert.False(string.IsNullOrEmpty(keyword.NormalizedKeyword), keyword.Keyword);
            Assert.Equal(TextNormalizer.ForKeywordSearch(keyword.Keyword), keyword.NormalizedKeyword);

            // 全角・改行・カタカナの違いがあっても当たる
            var withBreak = keyword.Keyword.Length > 1 ? keyword.Keyword.Insert(1, "\n") : keyword.Keyword;
            Assert.Contains(keyword.NormalizedKeyword, TextNormalizer.ForKeywordSearch("原材料：" + withBreak + "、砂糖"), StringComparison.Ordinal);
            Assert.True(keyword.IsSeed);
        }
    }

    [Fact]
    public void 探す言葉に商品名で誤って当たりやすい短い言葉を入れない()
    {
        Assert.All(MasterSeed.Keywords(), k => Assert.True(k.NormalizedKeyword.Length >= 2, k.Keyword));
    }

    [Fact]
    public void D_100_チラシの略記はカを足した7つで_正規化は略記の照合と同じ関数で作る()
    {
        var abbreviations = MasterSeed.LabelAbbreviations().ToList();
        var codesById = MasterSeed.Allergens().ToDictionary(a => a.Id, a => a.Code);

        Assert.Equal(
            new Dictionary<string, string> { ["麦"] = "wheat", ["え"] = "shrimp", ["か"] = "crab", ["落"] = "peanut", ["そ"] = "buckwheat", ["く"] = "walnut", ["カ"] = "cashew" },
            abbreviations.ToDictionary(a => a.Abbreviation, a => codesById[a.AllergenId]));
        // カタカナをひらがなにしない（「カ」はカタカナのまま。FR-IMPORT-32）
        Assert.All(abbreviations, a => Assert.Equal(TextNormalizer.ForLabelAbbreviation(a.Abbreviation), a.NormalizedAbbreviation));
        Assert.Equal("カ", abbreviations.Single(a => a.Abbreviation == "カ").NormalizedAbbreviation);
        Assert.Equal(abbreviations.Count, abbreviations.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void ナッツ類グループは落花生とココナッツを含む11品目で全品目に探す言葉がある()
    {
        var nuts = MasterSeed.Groups().Single(g => g.Name == "ナッツ類（すべて）");
        var memberIds = MasterSeed.GroupMembers().Where(m => m.AllergenGroupId == nuts.Id).Select(m => m.AllergenId).ToList();
        var keywordAllergenIds = MasterSeed.Keywords().Select(k => k.AllergenId).ToHashSet();

        Assert.Equal(11, memberIds.Count);
        Assert.Contains(MasterSeed.AllergenId("peanut"), memberIds);
        Assert.Contains(MasterSeed.AllergenId("coconut"), memberIds);
        Assert.All(memberIds, id => Assert.Contains(id, keywordAllergenIds));
    }

    [Fact]
    public void ヘーゼルナッツの探す言葉に分かりにくい原材料名も入っている()
    {
        var hazelnut = MasterSeed.Keywords().Where(k => k.AllergenId == MasterSeed.AllergenId("hazelnut")).Select(k => k.Keyword).ToList();

        Assert.Contains("ヘーゼルナッツ", hazelnut);
        Assert.Contains("ジャンドゥーヤ", hazelnut);
        Assert.Contains("プラリネ", hazelnut);
    }

    [Theory]
    [InlineData("coconut", "ナタデココ")]
    [InlineData("almond", "プラリネ")]
    [InlineData("almond", "マカロン")]
    [InlineData("hazelnut", "ノワゼット")]
    [InlineData("hazelnut", "ジャンドゥイヤ")]
    [InlineData("hazelnut", "ヘーゼルペースト")]
    [InlineData("peanut", "落花生")]
    [InlineData("walnut", "胡桃")]
    public void 分かりにくい原材料名でも品目の探す言葉に当たる(string code, string text)
    {
        var keywords = MasterSeed.Keywords().Where(k => k.AllergenId == MasterSeed.AllergenId(code));
        var normalized = TextNormalizer.ForKeywordSearch("原材料：" + text + "、砂糖");

        Assert.Contains(keywords, k => normalized.Contains(k.NormalizedKeyword, StringComparison.Ordinal));
    }

    [Fact]
    public void カシューナッツは特定原材料で表示が確実でない()
    {
        var cashew = MasterSeed.Allergens().Single(a => a.Code == "cashew");

        Assert.Equal(AllergenCategory.Specified, cashew.Category);
        Assert.True(cashew.LabelNotGuaranteed);
    }

    [Fact]
    public void 甲殻類グループはえびとかに()
    {
        var crustacean = MasterSeed.Groups().Single(g => g.Name == "甲殻類");
        var members = MasterSeed.GroupMembers().Where(m => m.AllergenGroupId == crustacean.Id).Select(m => m.AllergenId).ToHashSet();

        Assert.Equal([MasterSeed.AllergenId("shrimp"), MasterSeed.AllergenId("crab")], members.Order().ToArray().Length == 2 ? members.OrderBy(x => x == MasterSeed.AllergenId("crab")).ToArray() : []);
    }

    [Fact]
    public void ドーナッツに当たるナッツだけの言葉は入れない()
    {
        var normalized = TextNormalizer.ForKeywordSearch("ドーナッツ");

        Assert.DoesNotContain(MasterSeed.Keywords(), k => normalized.Contains(k.NormalizedKeyword, StringComparison.Ordinal));
    }

    [Fact]
    public void 主キーは毎回同じ値になる()
    {
        Assert.Equal(MasterSeed.Allergens().Select(a => a.Id), MasterSeed.Allergens().Select(a => a.Id));
        Assert.Equal(SeedIds.For("allergen:egg"), MasterSeed.AllergenId("egg"));
        Assert.True(Guid.TryParse(MasterSeed.AllergenId("egg"), out _));
    }

    [Fact]
    public void 施設の条件の初期値は月から金で再確認90日で人数と予算は未設定()
    {
        var setting = MasterSeed.Setting();

        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], setting.ServingWeekdays);
        Assert.Equal(90, setting.RecheckDays);
        Assert.Null(setting.DefaultChildrenCount);
        Assert.Null(setting.DefaultWeeklyBudget);
        Assert.DoesNotContain(setting.DuplicateAvoidDays, r => r.SnackType == SnackType.Smooth);
    }
}
