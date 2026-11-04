using System.Text.Json;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// 献立づくりのエージェントの道具（T4-04）。道具は本物の献立を変えず、submit_menu は Biz の検証を通ったときだけ受け付ける。
/// </summary>
public class MenuAgentToolsTests
{
    private static (Menu Menu, MenuContext Context, Product Safe, Product Warning, Product Blocked) Setup()
    {
        var safe = Safe("さくさくビスケット");
        var warning = ReferenceOnly("ふわふわゼリー", SnackType.Smooth);
        var blocked = Snack("ころころサブレ", SnackType.Sweet, 10, 1, ConfirmedAll(Today.AddDays(-5), [], rawIngredients: "小麦粉、ヘーゼルナッツペースト"));
        var menu = NewMenu(20000, 50, (safe, 300), (warning, 250), (blocked, 280));
        var context = Context([safe, warning, blocked], Nuts());
        return (menu, context, safe, warning, blocked);
    }

    private static MenuAgentProposal Proposal(params (int Day, string[] ProductIds)[] days) =>
        new(days.Select(d => new MenuAgentProposalDay(D(d.Day), d.ProductIds, "理由")).ToList(), []);

    [Fact]
    public void search_products_は入れられない商品を返さない()
    {
        var (menu, context, safe, warning, blocked) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);

        using var json = JsonDocument.Parse(tools.SearchProducts(null, null));
        var ids = json.RootElement.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();

        Assert.Contains(safe.Id, ids);
        Assert.Contains(warning.Id, ids);
        Assert.DoesNotContain(blocked.Id, ids);
    }

    [Fact]
    public void search_products_は警告の商品に印を付ける()
    {
        var (menu, context, _, warning, _) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);

        using var json = JsonDocument.Parse(tools.SearchProducts("ゼリー", null));
        var product = Assert.Single(json.RootElement.GetProperty("products").EnumerateArray());

        Assert.Equal(warning.Id, product.GetProperty("id").GetString());
        Assert.True(product.GetProperty("needsCheck").GetBoolean());
    }

    [Fact]
    public void SR_18_submit_menu_は入れられない商品を含む案を受け付けず問題を返す()
    {
        var (menu, context, safe, _, blocked) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);

        var reply = tools.SubmitMenu(new MenuAgentSubmission(Proposal((0, [safe.Id, blocked.Id])), new InterpretedRequests(), [], "説明", false));

        Assert.False(tools.IsAccepted);
        Assert.Contains("受け付けられません", reply);
        Assert.Contains("ヘーゼルナッツ", reply);
        Assert.Same(tools.LastSubmission, tools.LastSubmission);
        Assert.NotNull(tools.LastSubmission);
    }

    [Fact]
    public void submit_menu_は検証を通った案を受け付ける()
    {
        var (menu, context, safe, warning, _) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);

        tools.SubmitMenu(new MenuAgentSubmission(Proposal((0, [safe.Id]), (1, [warning.Id])), new InterpretedRequests(), [], "説明", false));

        Assert.True(tools.IsAccepted);
        Assert.Equal(1, tools.SubmitCount);
    }

    [Fact]
    public void submit_menu_は存在しない商品IDを受け付けない()
    {
        var (menu, context, _, _, _) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);

        tools.SubmitMenu(new MenuAgentSubmission(Proposal((0, ["存在しない商品"])), new InterpretedRequests(), [], "説明", false));

        Assert.False(tools.IsAccepted);
    }

    [Fact]
    public void 道具で試しても本物の献立は変わらない()
    {
        var (menu, context, safe, warning, _) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);
        var interpreted = new InterpretedRequests { Days = [new InterpretedDay(D(2), 30, null, "誕生日会")] };

        tools.CheckBudget(Proposal((0, [safe.Id]), (1, [warning.Id])));
        tools.SubmitMenu(new MenuAgentSubmission(Proposal((0, [safe.Id])), interpreted, [], "説明", false));

        Assert.All(menu.Days, d => Assert.Empty(d.Items));
        Assert.All(menu.Days, d => Assert.Equal(50, d.ChildrenCount));
        Assert.All(menu.Days, d => Assert.Null(d.Label));
    }

    [Fact]
    public void check_budget_は要望で変えた人数でパック数と金額を計算する()
    {
        var (menu, context, safe, _, _) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [safe.Id], null)], [new InterpretedDay(D(0), 30, null, null)]);

        using var json = JsonDocument.Parse(tools.CheckBudget(proposal));
        var monday = json.RootElement.GetProperty("days").EnumerateArray().First();

        // 30人 × 1個 ÷ 入数10 = 3パック × 300円
        Assert.Equal(30, monday.GetProperty("childrenCount").GetInt32());
        Assert.Equal(900, monday.GetProperty("amount").GetInt32());
        Assert.Equal(900, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public void 条件の文章には候補だけが入り_入れられない商品は入らない()
    {
        var (menu, context, safe, _, blocked) = Setup();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Create, 50);
        var setting = new Setting { RecheckDays = 90, DuplicateAvoidDays = [new DuplicateAvoidRule(SnackType.Salty, 30)] };

        var text = tools.BuildConditionsText(setting, []);

        Assert.Contains(safe.Id, text);
        Assert.DoesNotContain(blocked.Id, text);
        Assert.Contains("ヘーゼルナッツ", text);
        Assert.Contains("20,000円", text);
    }

    [Fact]
    public void 写しは同じIDで中身を持ち_写しを変えても元は変わらない()
    {
        var (menu, _, safe, _, _) = Setup();
        Put(menu, 0, safe);

        var clone = MenuCloner.Clone(menu);
        clone.Days[0].Items.Clear();
        clone.Days[0].ChildrenCount = 10;
        clone.ListedProducts.Clear();

        Assert.Equal(menu.Id, clone.Id);
        Assert.Equal(menu.Days[0].Id, clone.Days[0].Id);
        Assert.Single(menu.Days[0].Items);
        Assert.Equal(50, menu.Days[0].ChildrenCount);
        Assert.Equal(3, menu.ListedProducts.Count);
    }
    [Fact]
    public void FR_MENU_22_チャットに渡す週の要望には_作成時の文章と読み取った方針が入る()
    {
        var menu = NewMenu(20000, 50);
        menu.RequestText = "金曜は誕生日会です。しょっぱいものとゼリー系を合わせたい";
        menu.InterpretedRequests = new InterpretedRequests
        {
            Policies = [new InterpretedPolicy("しょっぱいものとゼリー系を合わせる日を作る", null), new InterpretedPolicy("豪華にする", [D(4)])],
        };

        var text = MenuAgentTools.BuildWeekRequestText(menu);

        Assert.NotNull(text);
        Assert.Contains("金曜は誕生日会です。しょっぱいものとゼリー系を合わせたい", text);
        Assert.Contains("- しょっぱいものとゼリー系を合わせる日を作る", text);
        Assert.Contains($"- 豪華にする（{MenuIssueText.DateLabel(D(4))}）", text);
    }

    [Fact]
    public void FR_MENU_22_要望も方針もなければ_週の要望は渡さない()
    {
        var menu = NewMenu(20000, 50);
        menu.RequestText = "  ";
        menu.InterpretedRequests = new InterpretedRequests();

        Assert.Null(MenuAgentTools.BuildWeekRequestText(menu));
    }
}

/// <summary>
/// アレルゲンの判定の表示用の材料（T4-01）。区分・種類は判定のまま、理由の材料だけを足す。
/// </summary>
public class AllergenJudgementViewTests
{
    [Fact]
    public void 参考でチラシに表記がないときは_表記なしの印が立つ()
    {
        var product = MenuTestData.ReferenceOnly("ふわふわゼリー");
        var judgement = AllergenJudge.Judge(product, Nuts(), Today, RecheckDays);

        var view = AllergenJudgementView.From(product, judgement, Today);

        Assert.Equal(AllergenJudgementCategory.Warning, view.Category);
        Assert.Equal(AllergenCheckSource.Flyer, view.ReferenceSource);
        Assert.True(view.ReferenceHasNoLabel);
        Assert.False(view.ReferenceAfterConfirmed);
    }

    [Fact]
    public void 参考の記録だけで含む品目は_パッケージ以外から来た印が立つ()
    {
        var product = MenuTestData.ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var judgement = AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays);

        var view = AllergenJudgementView.From(product, judgement, Today);

        Assert.Equal(AllergenJudgementCategory.Blocked, view.Category);
        Assert.Equal(["卵"], view.ContainedAllergens);
        Assert.True(view.ContainsFromReference);
    }

    [Fact]
    public void 確認済みで含む品目は_パッケージ以外から来た印が立たない()
    {
        var product = TestData.Product("たまごクッキー", ConfirmedAll(Today.AddDays(-5), ["egg"]));
        var judgement = AllergenJudge.Judge(product, Avoid("egg"), Today, RecheckDays);

        var view = AllergenJudgementView.From(product, judgement, Today);

        Assert.Equal(AllergenJudgementKind.Contains, view.Kind);
        Assert.False(view.ContainsFromReference);
    }

    [Fact]
    public void 要再確認では確認からの日数を返す()
    {
        var product = TestData.Product("いちごゼリー", ConfirmedAll(Today.AddDays(-100), []));
        var judgement = AllergenJudge.Judge(product, Nuts(), Today, RecheckDays);

        var view = AllergenJudgementView.From(product, judgement, Today);

        Assert.Equal(AllergenJudgementKind.RecheckNeeded, view.Kind);
        Assert.Equal(100, view.DaysSinceConfirmed);
    }

    [Fact]
    public void SR_14_当たった探す言葉は_当たった場所と言葉を返す()
    {
        var product = TestData.Product("ショコラ", ConfirmedAll(Today.AddDays(-5), [], rawIngredients: "小麦粉、ヘーゼルナッ\nツペースト"));
        var judgement = AllergenJudge.Judge(product, Nuts(), Today, RecheckDays);

        var view = AllergenJudgementView.From(product, judgement, Today);

        Assert.Equal(AllergenJudgementKind.MayContain, view.Kind);
        Assert.Contains(view.KeywordHits, h => h.AllergenName == "ヘーゼルナッツ" && h.Source == KeywordHitSource.RawIngredients);
    }

    [Fact]
    public void パッケージで確認する商品は_要確認と_情報がないため使えない商品()
    {
        var unknown = TestData.Product("なにもない");
        var reference = MenuTestData.ReferenceOnly("参考だけ");
        var contains = TestData.Product("たまごクッキー", ConfirmedAll(Today.AddDays(-5), ["egg"]));
        var avoided = Avoid("egg");

        Assert.True(MenuApplicationService.NeedsPackageCheck(AllergenJudge.Judge(unknown, avoided, Today, RecheckDays)));
        Assert.True(MenuApplicationService.NeedsPackageCheck(AllergenJudge.Judge(reference, avoided, Today, RecheckDays)));
        Assert.False(MenuApplicationService.NeedsPackageCheck(AllergenJudge.Judge(contains, avoided, Today, RecheckDays)));
    }

}
