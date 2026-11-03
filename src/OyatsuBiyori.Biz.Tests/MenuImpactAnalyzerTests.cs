using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class MenuImpactAnalyzerTests
{
    [Fact]
    public void 入れられないになった商品を使う今日以降の日を下書きも確定も返す()
    {
        var cookie = Safe("さくさくクッキー");
        var draft = NewMenu(20000, 50, (cookie, 300));
        Put(draft, 0, cookie);
        var confirmed = NewMenu(20000, 50, (cookie, 300));
        confirmed.WeekStart = WeekStart.AddDays(7);
        confirmed.Status = MenuStatus.Confirmed;
        Put(confirmed, 1, cookie);

        // 届いたパッケージにヘーゼルナッツペーストがあった
        cookie.AllergenChecks.Add(ConfirmedAll(Today, ["wheat", "milk", "soybean"], rawIngredients: "小麦粉、砂糖、ヘーゼルナッツペースト"));
        var impact = MenuImpactAnalyzer.Analyze([draft, confirmed], Context([cookie], Nuts()));

        Assert.Equal(2, impact.BlockedUsages.Count);
        Assert.Contains(impact.BlockedUsages, u => u.MenuId == draft.Id && u.Status == MenuStatus.Draft);
        Assert.Contains(impact.BlockedUsages, u => u.MenuId == confirmed.Id && u.Status == MenuStatus.Confirmed);
        Assert.All(impact.BlockedUsages, u => Assert.Equal(AllergenJudgementKind.MayContain, u.Judgement!.Kind));
        Assert.Empty(impact.RecheckUsages);
    }

    [Fact]
    public void 避ける品目を足したら確定済みの不明の商品は入れられないとして示す()
    {
        var unknown = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var menu = NewMenu(20000, 50, (unknown, 300));
        Put(menu, 0, unknown);
        menu.Status = MenuStatus.Confirmed;
        Assert.Empty(MenuImpactAnalyzer.Analyze([menu], Context([unknown])).BlockedUsages);

        var usage = Assert.Single(MenuImpactAnalyzer.Analyze([menu], Context([unknown], Avoid("egg"))).BlockedUsages);

        Assert.Equal(AllergenJudgementKind.Unknown, usage.Judgement!.Kind);
    }

    [Fact]
    public void 探す言葉を足したら全文に当たる確定済みの商品は入れられないとして示す()
    {
        var cookie = Snack("ココアクッキー", SnackType.Sweet, 10, 1,
            ConfirmedAll(Today.AddDays(-5), ["wheat"], rawIngredients: "小麦粉、砂糖、テストナッツ粉末"));
        var menu = NewMenu(20000, 50, (cookie, 300));
        Put(menu, 0, cookie);
        menu.Status = MenuStatus.Confirmed;
        var facility = AllergenSettingRules.CreateFacilityAllergen("ほかのナッツ", [], AllAllergens).Value!;
        var avoided = new List<Allergen> { facility };
        Assert.Empty(MenuImpactAnalyzer.Analyze([menu], Context([cookie], avoided)).BlockedUsages);

        facility.Keywords.Add(AllergenSettingRules.CreateKeyword(facility, "テストナッツ").Value!);

        var usage = Assert.Single(MenuImpactAnalyzer.Analyze([menu], Context([cookie], avoided)).BlockedUsages);
        Assert.Equal(AllergenJudgementKind.MayContain, usage.Judgement!.Kind);
    }

    [Fact]
    public void 今日の日は含め昨日の日は含めない()
    {
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var menu = NewMenu(20000, 50, (egg, 300));
        Put(menu, 0, egg);
        Put(menu, 1, egg);

        var impact = MenuImpactAnalyzer.Analyze([menu], Context([egg], Avoid("egg"), today: D(1)));

        Assert.Equal([D(1)], impact.BlockedUsages.Select(u => u.Date));
    }

    [Fact]
    public void おやつなしの日の入れられない商品も示す()
    {
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var menu = NewMenu(20000, 50, (egg, 300));
        Put(menu, 0, egg);
        menu.Days[0].DayType = DayType.NoSnack;

        Assert.Single(MenuImpactAnalyzer.Analyze([menu], Context([egg], Avoid("egg"))).BlockedUsages);
    }

    [Fact]
    public void 確定済みで判定が警告の範囲で変わったら確認し直し()
    {
        var cookie = Snack("さくさくクッキー", SnackType.Sweet, 10, 1, ConfirmedAll(Today.AddDays(-85), []));
        var menu = NewMenu(20000, 50, (cookie, 300));
        Put(menu, 0, cookie);
        menu.Status = MenuStatus.Confirmed;
        Assert.Equal(AllergenJudgementCategory.Safe, Context([cookie]).Judge(cookie).Category);

        // 安全で確定 → 献立の週の前に再確認期間を過ぎた（確認日から92日）
        var impact = MenuImpactAnalyzer.Analyze([menu], Context([cookie], today: Today.AddDays(7)));

        var usage = Assert.Single(impact.RecheckUsages);
        Assert.Equal(AllergenJudgementKind.RecheckNeeded, usage.Judgement!.Kind);
        Assert.Empty(impact.BlockedUsages);
    }

    [Fact]
    public void カードで確認し直せば確認し直しが消える()
    {
        var reference = ReferenceOnly("ぷるぷるゼリー");
        var menu = NewMenu(20000, 50, (reference, 300));
        var item = Put(menu, 0, reference);
        menu.Status = MenuStatus.Confirmed;
        MenuConfirmationChecker.Acknowledge(item, Context([reference], Avoid("milk")), NoonUtc(Today));
        reference.AllergenChecks.Add(TestData.Reference(Today, "soybean"));

        var context = Context([reference], Avoid("milk"));
        Assert.Single(MenuImpactAnalyzer.Analyze([menu], context).RecheckUsages);

        MenuConfirmationChecker.Acknowledge(item, context, NoonUtc(Today));
        Assert.Empty(MenuImpactAnalyzer.Analyze([menu], Context([reference], Avoid("milk"))).RecheckUsages);
    }

    [Fact]
    public void 下書きは確認し直しに数えない()
    {
        var reference = ReferenceOnly("ぷるぷるゼリー");
        var menu = NewMenu(20000, 50, (reference, 300));
        Put(menu, 0, reference);

        Assert.Empty(MenuImpactAnalyzer.Analyze([menu], Context([reference], Avoid("milk"))).RecheckUsages);
    }

    [Fact]
    public void 商品を読めなかったら確認する側に倒して入れられないとして示す()
    {
        var menu = NewMenu(20000, 50);
        PutRaw(menu, 0, "missing-product");

        var usage = Assert.Single(MenuImpactAnalyzer.Analyze([menu], Context([])).BlockedUsages);

        Assert.Null(usage.Judgement);
    }

    [Fact]
    public void 商品を使う今日以降の献立と日付を判定にかかわらず返す()
    {
        var cookie = Safe("さくさくクッキー");
        var other = Safe("べつのクッキー");
        var menu = NewMenu(20000, 50, (cookie, 300), (other, 300));
        Put(menu, 0, cookie);
        Put(menu, 2, cookie);
        Put(menu, 3, other);

        var usages = MenuImpactAnalyzer.FindUsages(cookie.Id, [menu], D(1));

        var usage = Assert.Single(usages);
        Assert.Equal(D(2), usage.Date);
        Assert.Equal(WeekStart, usage.WeekStart);
    }
}
