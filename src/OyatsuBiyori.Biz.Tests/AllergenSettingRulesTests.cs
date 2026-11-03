using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class AllergenSettingRulesTests
{
    [Fact]
    public void 初期データの言葉は削除できない()
    {
        var hazelnut = Allergen("hazelnut");
        var seed = hazelnut.Keywords.First(k => k.IsSeed);

        var result = AllergenSettingRules.CanDeleteKeyword(seed, hazelnut);

        Assert.True(result.HasError);
    }

    [Fact]
    public void 施設が足した言葉は削除できる()
    {
        var allergen = NewFacilityAllergen("テストナッツ", "テストペースト");
        var added = allergen.Keywords.Single(k => k.Keyword == "テストペースト");

        Assert.False(AllergenSettingRules.CanDeleteKeyword(added, allergen).HasError);
    }

    [Fact]
    public void 施設の追加品目の最後の1語は削除できない()
    {
        var allergen = NewFacilityAllergen("テストナッツ");
        var only = Assert.Single(allergen.Keywords);

        var result = AllergenSettingRules.CanDeleteKeyword(only, allergen);

        Assert.True(result.HasError);
    }

    [Fact]
    public void 品目と言葉が対応していなければ例外()
    {
        var allergen = NewFacilityAllergen("テストナッツ");
        var other = NewFacilityAllergen("べつのナッツ");

        Assert.Throws<ArgumentException>(() => AllergenSettingRules.CanDeleteKeyword(other.Keywords[0], allergen));
    }

    [Fact]
    public void 施設の追加品目を登録すると品目名が探す言葉に入る()
    {
        var result = AllergenSettingRules.CreateFacilityAllergen("テストナッツ", ["テストペースト"], AllAllergens);

        Assert.False(result.HasError);
        var allergen = result.Value!;
        Assert.Equal(AllergenCategory.FacilityAdded, allergen.Category);
        Assert.False(allergen.LabelNotGuaranteed);
        Assert.Equal(["テストナッツ", "テストペースト"], allergen.Keywords.Select(k => k.Keyword));
        Assert.All(allergen.Keywords, k =>
        {
            Assert.False(k.IsSeed);
            Assert.Equal(TextNormalizer.ForKeywordSearch(k.Keyword), k.NormalizedKeyword);
            Assert.Equal(allergen.Id, k.AllergenId);
        });
        Assert.True(allergen.SortOrder > AllAllergens.Max(a => a.SortOrder));
    }

    [Fact]
    public void 品目名と同じ言葉を重ねて渡しても1つにする()
    {
        var result = AllergenSettingRules.CreateFacilityAllergen("テストナッツ", ["ﾃｽﾄﾅｯﾂ"], AllAllergens);

        Assert.Single(result.Value!.Keywords);
    }

    [Theory]
    [InlineData("ヘーゼルナッツ")]
    [InlineData("ﾍｰｾﾞﾙﾅｯﾂ")]
    [InlineData("小麦")]
    public void 既存の品目と同じ名前は登録できない(string name)
    {
        Assert.True(AllergenSettingRules.CreateFacilityAllergen(name, [], AllAllergens).HasError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("・・")]
    [InlineData(null)]
    public void 品目名が空なら登録できない(string? name)
    {
        Assert.True(AllergenSettingRules.CreateFacilityAllergen(name, [], AllAllergens).HasError);
    }

    [Fact]
    public void 記号だけの言葉を添えたら登録できない()
    {
        Assert.True(AllergenSettingRules.CreateFacilityAllergen("テストナッツ", ["－－"], AllAllergens).HasError);
    }

    [Fact]
    public void 言葉を足すときは正規化して重複を見る()
    {
        var hazelnut = Allergen("hazelnut");

        Assert.True(AllergenSettingRules.CreateKeyword(hazelnut, "ﾌﾟﾗﾘﾈ").HasError);
        Assert.True(AllergenSettingRules.CreateKeyword(hazelnut, " ").HasError);

        var added = AllergenSettingRules.CreateKeyword(hazelnut, "ヘーゼル粉末");
        Assert.False(added.HasError);
        Assert.False(added.Value!.IsSeed);
        Assert.Equal(TextNormalizer.ForKeywordSearch("ヘーゼル粉末"), added.Value.NormalizedKeyword);
    }

    [Fact]
    public void 法令の品目は削除も改名もできない()
    {
        var wheat = Allergen("wheat");

        Assert.True(AllergenSettingRules.CanDeleteAllergen(wheat, false, false, false).HasError);
        Assert.True(AllergenSettingRules.CanRenameAllergen(wheat).HasError);
    }

    [Fact]
    public void 施設の追加品目はどこからも使われていなければ削除できる()
    {
        var allergen = NewFacilityAllergen("テストナッツ");

        Assert.False(AllergenSettingRules.CanDeleteAllergen(allergen, false, false, false).HasError);
        Assert.False(AllergenSettingRules.CanRenameAllergen(allergen).HasError);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void 施設の追加品目は避ける品目かグループか確認の記録で使われていれば削除できない(bool isAvoided, bool isInGroup, bool isUsedByChecks)
    {
        var allergen = NewFacilityAllergen("テストナッツ");

        Assert.True(AllergenSettingRules.CanDeleteAllergen(allergen, isAvoided, isInGroup, isUsedByChecks).HasError);
    }

    private static Allergen NewFacilityAllergen(string name, params string[] keywords)
    {
        return AllergenSettingRules.CreateFacilityAllergen(name, keywords, AllAllergens).Value!;
    }
}
