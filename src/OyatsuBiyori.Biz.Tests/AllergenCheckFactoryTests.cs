using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class AllergenCheckFactoryTests
{
    private static readonly DateTime Now = NoonUtc(Today);

    /// <summary>29品目すべてを決めた入力（contains 以外は含まない）</summary>
    private static List<AllergenItemDecision> Decide29(params string[] contains)
    {
        return AllAllergens
            .Where(a => a.Category != AllergenCategory.FacilityAdded)
            .Select(a => new AllergenItemDecision(a.Id, contains.Contains(a.Code)))
            .ToList();
    }

    private static Result<AllergenCheck> Create(AllergenCheckRequest request, params string[] avoidedCodes)
    {
        return AllergenCheckFactory.Create(request, "product-1", AllAllergens, avoidedCodes.Select(c => Allergen(c).Id), Now, "staff-1");
    }

    private static AllergenCheckRequest Package(List<AllergenItemDecision> items, string? raw = "小麦粉、砂糖、植物油脂") =>
        new(AllergenCheckSource.Package, null, raw, raw, null, items);

    [Fact]
    public void パッケージで29品目を決めれば確認済みになる()
    {
        var result = Create(Package(Decide29("wheat", "milk")));

        Assert.False(result.HasError);
        var check = result.Value!;
        Assert.Equal(AllergenCheckStatus.Confirmed, check.Status);
        Assert.Equal(29, check.Items.Count);
        Assert.Equal(2, check.Items.Count(i => i.Contains));
        Assert.Equal("staff-1", check.CheckedBy);
        Assert.Equal(Now, check.CheckedAt);
        Assert.Equal("product-1", check.ProductId);
        Assert.All(check.Items, i => Assert.Equal(check.Id, i.AllergenCheckId));
    }

    [Fact]
    public void 確認済みの記録には情報源と全文2列と注意書きと補足を残す()
    {
        var request = new AllergenCheckRequest(AllergenCheckSource.Package, "裏面と側面を撮影",
            "小麦粉、砂糖、ヘ一ゼルナッツ", "小麦粉、砂糖、ヘーゼルナッツ", "本品製造工場では卵を含む製品を生産しています", Decide29("wheat"));

        var check = Create(request).Value!;

        Assert.Equal(AllergenCheckSource.Package, check.Source);
        Assert.Equal("裏面と側面を撮影", check.SourceNote);
        Assert.Equal("小麦粉、砂糖、ヘ一ゼルナッツ", check.RawIngredientsOcr);
        Assert.Equal("小麦粉、砂糖、ヘーゼルナッツ", check.RawIngredients);
        Assert.Equal("本品製造工場では卵を含む製品を生産しています", check.ContaminationNote);
        Assert.Equal("staff-1", check.UpdateUserId);
    }

    [Fact]
    public void 参考の記録にも情報源と補足を残し空白だけの値はnullにする()
    {
        var request = new AllergenCheckRequest(AllergenCheckSource.CoopSite, "コープのサイトで確認", "  ", null, " \n ", []);

        var check = Create(request).Value!;

        Assert.Equal(AllergenCheckSource.CoopSite, check.Source);
        Assert.Equal("コープのサイトで確認", check.SourceNote);
        Assert.Null(check.RawIngredientsOcr);
        Assert.Null(check.RawIngredients);
        Assert.Null(check.ContaminationNote);
    }

    [Fact]
    public void 作った確認済みの記録は判定の前提を満たす()
    {
        var check = Create(Package(Decide29())).Value!;

        var history = AllergenCheckHistory.From([check]);

        Assert.Same(check, history.LatestConfirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void 全文がなければ確認済みにできない(string? raw)
    {
        var result = Create(Package(Decide29(), raw));

        Assert.True(result.HasError);
    }

    [Fact]
    public void Claudeの読み取りだけで職員の全文がなければ確認済みにできない()
    {
        var request = new AllergenCheckRequest(AllergenCheckSource.Package, null, "小麦粉、砂糖", null, null, Decide29());

        Assert.True(Create(request).HasError);
    }

    [Fact]
    public void ヘーゼルナッツを含まないと決めても全文に探す言葉があれば判定は入れられない()
    {
        var items = Decide29("wheat", "milk", "soybean");
        items.Add(new AllergenItemDecision(Allergen("hazelnut").Id, false));
        var request = Package(items, "小麦粉、砂糖、ヘーゼルナッツペースト、ココア");

        var check = Create(request, "hazelnut").Value!;
        var product = Product("ココアクッキー", check);
        var judgement = AllergenJudge.Judge(product, Avoid("hazelnut"), Today, RecheckDays);

        Assert.Equal(AllergenJudgementCategory.Blocked, judgement.Category);
        Assert.Equal(AllergenJudgementKind.MayContain, judgement.Kind);
    }

    [Fact]
    public void 決めていない品目があれば確認済みにできない()
    {
        var items = Decide29().Where(i => i.AllergenId != Allergen("cashew").Id).ToList();

        var result = Create(Package(items));

        Assert.True(result.HasError);
        Assert.Contains("カシューナッツ", result.ErrorMessages[0]);
    }

    [Fact]
    public void 避ける品目に施設の追加品目があればその品目も決めないと確認済みにできない()
    {
        var result = Create(Package(Decide29()), "hazelnut");

        Assert.True(result.HasError);
        Assert.Contains("ヘーゼルナッツ", result.ErrorMessages[0]);
    }

    [Fact]
    public void 避ける追加品目も決めれば確認済みにできる()
    {
        var items = Decide29();
        items.Add(new AllergenItemDecision(Allergen("hazelnut").Id, false));

        var result = Create(Package(items), "hazelnut");

        Assert.False(result.HasError);
        Assert.Equal(30, result.Value!.Items.Count);
    }

    [Fact]
    public void 避けていない追加品目は決めなくても確認済みにできる()
    {
        Assert.False(Create(Package(Decide29()), "milk").HasError);
    }

    [Fact]
    public void 同じ品目を2回選んだらエラー()
    {
        var items = Decide29();
        items.Add(new AllergenItemDecision(Allergen("milk").Id, true));

        Assert.True(Create(Package(items)).HasError);
    }

    [Fact]
    public void マスタにない品目はエラー()
    {
        var items = Decide29();
        items.Add(new AllergenItemDecision("unknown-id", true));

        Assert.True(Create(Package(items)).HasError);
    }

    [Theory]
    [InlineData(AllergenCheckSource.CoopSite)]
    [InlineData(AllergenCheckSource.Flyer)]
    [InlineData(AllergenCheckSource.Other)]
    public void パッケージ以外は参考になり全文なしでよく含むの品目だけ保存する(AllergenCheckSource source)
    {
        var request = new AllergenCheckRequest(source, "コープのサイトで確認", null, null, null,
            [new AllergenItemDecision(Allergen("egg").Id, true), new AllergenItemDecision(Allergen("milk").Id, false)]);

        var result = Create(request);

        Assert.False(result.HasError);
        Assert.Equal(AllergenCheckStatus.Reference, result.Value!.Status);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(Allergen("egg").Id, item.AllergenId);
        Assert.True(item.Contains);
    }

    [Fact]
    public void 参考は品目なしでも記録できる()
    {
        var request = new AllergenCheckRequest(AllergenCheckSource.Other, "メーカーに電話", null, null, null, []);

        var result = Create(request);

        Assert.False(result.HasError);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public void 確認日時がUTCでなければ例外()
    {
        Assert.Throws<ArgumentException>(() => AllergenCheckFactory.Create(Package(Decide29()), "product-1", AllAllergens, [], DateTime.SpecifyKind(Now, DateTimeKind.Local), "staff-1"));
    }

    [Fact]
    public void 確認者がなければ例外()
    {
        Assert.ThrowsAny<ArgumentException>(() => AllergenCheckFactory.Create(Package(Decide29()), "product-1", AllAllergens, [], Now, " "));
    }

    [Fact]
    public void 決まっていない情報源はエラー()
    {
        var request = new AllergenCheckRequest((AllergenCheckSource)99, null, null, null, null, []);

        Assert.True(Create(request).HasError);
    }

    [Fact]
    public void 味と種類のタグは決まった語だけ()
    {
        var ok = AllergenCheckFactory.CreateTags("product-1", [new ProductTagInput(ProductTagKind.Flavor, "チョコ"), new ProductTagInput(ProductTagKind.Character, "くまのモコ")], ProductTagSource.Claude, Now, "staff-1");
        Assert.False(ok.HasError);
        Assert.Equal(2, ok.Value!.Count);

        var ng = AllergenCheckFactory.CreateTags("product-1", [new ProductTagInput(ProductTagKind.Flavor, "チョコレート")], ProductTagSource.Claude, Now, "staff-1");
        Assert.True(ng.HasError);
    }

    [Fact]
    public void 空のタグはエラーで同じタグは1つにする()
    {
        Assert.True(AllergenCheckFactory.CreateTags("product-1", [new ProductTagInput(ProductTagKind.Character, " ")], ProductTagSource.Staff, Now, null).HasError);

        var result = AllergenCheckFactory.CreateTags("product-1", [new ProductTagInput(ProductTagKind.Flavor, "グミ"), new ProductTagInput(ProductTagKind.Flavor, "グミ")], ProductTagSource.Staff, Now, null);
        Assert.Single(result.Value!);
    }
}
