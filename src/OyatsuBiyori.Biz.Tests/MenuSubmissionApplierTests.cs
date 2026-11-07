using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// エージェントの案を献立に当てる処理（道具の試しと本物の反映で共通）。
/// レビューで見つかったバグ：チャットで一部の日だけ（または日ごとの値だけ）を出すと、ほかの日の商品がすべて消えていた。
/// </summary>
public class MenuSubmissionApplierTests
{
    private static (Menu Menu, MenuContext Context, Product A, Product B, Product C) FullMenu()
    {
        var a = Safe("さくさくビスケット");
        var b = Safe("もぐもぐせんべい", SnackType.Salty);
        var c = Safe("ぷるぷるゼリー", SnackType.Smooth);
        var menu = NewMenu(20000, 50, (a, 300), (b, 250), (c, 280));
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, a);
            menu.Days[i].Reason = $"前の理由{i}";
        }

        return (menu, Context([a, b, c]), a, b, c);
    }

    [Fact]
    public void チャットで日ごとの値だけを出しても_商品は消えない()
    {
        var (menu, context, a, _, _) = FullMenu();
        var interpreted = new InterpretedRequests { Days = [new InterpretedDay(D(4), 30, null, null)] };

        MenuSubmissionApplier.Apply(menu, new MenuAgentProposal([], []), interpreted, [D(4)], MenuAgentMode.Chat, 50, context);

        Assert.All(menu.Days, d => Assert.Equal(a.Id, Assert.Single(d.Items).ProductId));
        Assert.Equal(30, menu.Days[4].ChildrenCount);
        // 人数を変えた日はパック数を式で求め直す（30人 ÷ 入数10 = 3パック）
        Assert.Equal(3, menu.Days[4].Items[0].PackCount);
    }

    [Fact]
    public void チャットで1日だけを出したら_ほかの日は商品と理由のまま残る()
    {
        var (menu, context, a, b, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(2), [b.Id], "せんべいにしました")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(b.Id, Assert.Single(menu.Days[2].Items).ProductId);
        Assert.Equal("せんべいにしました", menu.Days[2].Reason);
        foreach (var index in new[] { 0, 1, 3, 4 })
        {
            Assert.Equal(a.Id, Assert.Single(menu.Days[index].Items).ProductId);
            Assert.Equal($"前の理由{index}", menu.Days[index].Reason);
        }
    }

    [Fact]
    public void チャットで日を空のリストで出したら_その日だけ空にする()
    {
        var (menu, context, _, _, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(1), [], "この日はなしにしました")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Empty(menu.Days[1].Items);
        Assert.All(menu.Days.Where((_, i) => i != 1), d => Assert.Single(d.Items));
    }

    [Fact]
    public void 作成では案で1週間分を置き換え_案にない日は空にする()
    {
        var (menu, context, _, b, c) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [b.Id, c.Id], "理由")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Equal(2, menu.Days[0].Items.Count);
        Assert.All(menu.Days.Skip(1), d => Assert.Empty(d.Items));
    }

    [Fact]
    public void 同じ商品を残した日は_確定時の確認とパック数の上書きを残す()
    {
        var (menu, context, a, _, _) = FullMenu();
        var item = menu.Days[0].Items[0];
        item.PackCount = 9;
        item.AcknowledgedAt = NoonUtc(Today);
        item.AcknowledgedJudgement = AllergenJudgementKind.Reference;

        MenuSubmissionApplier.Apply(menu, new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由")], []), new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        var kept = Assert.Single(menu.Days[0].Items);
        Assert.Same(item, kept);
        Assert.Equal(9, kept.PackCount);
        Assert.NotNull(kept.AcknowledgedAt);
    }

    // 何人分買うかの指定（servings。在庫で補う日など。FR-MENU-08b・D-102）。人数50人・入数10の商品で確かめる

    [Fact]
    public void 作成で何人分を指定した商品は_その人数分のパック数になり_ほかの商品は全員分のまま()
    {
        var (menu, context, _, b, c) = FullMenu();
        var proposal = new MenuAgentProposal(
            [new MenuAgentProposalDay(D(1), [b.Id, c.Id], "理由", [new MenuAgentServing(b.Id, 40)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Empty(issues);
        var items = menu.Days[1].Items;
        Assert.Equal(4, items.Single(i => i.ProductId == b.Id).PackCount);
        Assert.Equal(1000, items.Single(i => i.ProductId == b.Id).Amount);
        Assert.Equal(5, items.Single(i => i.ProductId == c.Id).PackCount);
        // 全員分に足りないので警告になる（確定は止めない）
        var validation = MenuValidator.Validate(menu, context, issues);
        Assert.False(validation.HasError);
        Assert.Contains(validation.Warnings, i => i.Kind == MenuIssueKind.PackShort && i.ProductId == b.Id);
    }

    [Fact]
    public void 何人分は切り上げのパック数にする()
    {
        var (menu, context, a, _, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 41)])], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        // 41人 × 1個 ÷ 入数10 = 4.1 → 5パック
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void チャットで人数も変えた日は_人数で求め直したあとに何人分を当てる()
    {
        var (menu, context, a, _, _) = FullMenu();
        var interpreted = new InterpretedRequests { Days = [new InterpretedDay(D(2), 30, null, null)] };
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(2), [a.Id], "理由", [new MenuAgentServing(a.Id, 20)])], []);

        MenuSubmissionApplier.Apply(menu, proposal, interpreted, [D(2)], MenuAgentMode.Chat, 50, context);

        Assert.Equal(30, menu.Days[2].ChildrenCount);
        Assert.Equal(2, menu.Days[2].Items[0].PackCount);
    }

    [Fact]
    public void チャットで指定のない商品と日は_今のパック数のまま残る()
    {
        var (menu, context, a, _, _) = FullMenu();
        menu.Days[0].Items[0].PackCount = 4;
        menu.Days[3].Items[0].PackCount = 3;
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [])], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(4, menu.Days[0].Items[0].PackCount);
        Assert.Equal(3, menu.Days[3].Items[0].PackCount);
    }

    [Fact]
    public void その日に入れていない商品の何人分はエラーにし_パック数を変えない()
    {
        var (menu, context, a, b, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(b.Id, 40)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        var issue = Assert.Single(issues);
        Assert.Equal(MenuIssueSeverity.Error, issue.Severity);
        Assert.Equal(MenuIssueKind.InvalidServing, issue.Kind);
        // 商品を外さないように、献立の商品には結びつけない
        Assert.Null(issue.MenuItemId);
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InputValidator.MaxChildrenCount + 1)]
    public void 範囲外の何人分はエラーにし_パック数を変えない(int servings)
    {
        var (menu, context, a, _, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, servings)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(MenuIssueKind.InvalidServing, Assert.Single(issues).Kind);
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
        Assert.Single(menu.Days[0].Items);
    }

    [Fact]
    public void 同じ商品に何人分が2つあればエラーにし_どちらも当てない()
    {
        var (menu, context, a, _, _) = FullMenu();
        var proposal = new MenuAgentProposal(
            [new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 40), new MenuAgentServing(a.Id, 30)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(MenuIssueKind.InvalidServing, Assert.Single(issues).Kind);
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 入れられない商品と一緒に出した何人分は_商品が外れても例外にならない()
    {
        var (menu, context, a, _, _) = FullMenu();
        var blocked = Snack("ころころサブレ", SnackType.Sweet, 10, 1, ConfirmedAll(Today.AddDays(-5), [], rawIngredients: "小麦粉、ヘーゼルナッツペースト"));
        menu.ListedProducts.Add(new ListedProduct(blocked.Id, 280, "1234"));
        var blockedContext = Context([a, blocked], Nuts());
        var proposal = new MenuAgentProposal(
            [new MenuAgentProposalDay(D(0), [a.Id, blocked.Id], "理由", [new MenuAgentServing(blocked.Id, 40), new MenuAgentServing(a.Id, 40)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, blockedContext);
        var validation = MenuValidator.Validate(menu, blockedContext, issues);
        MenuValidator.RemoveErrorItems(menu, validation);

        Assert.Contains(validation.Errors, i => i.Kind == MenuIssueKind.AllergenBlocked && i.ProductId == blocked.Id);
        Assert.Equal(a.Id, Assert.Single(menu.Days[0].Items).ProductId);
        Assert.Equal(4, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 何人分のエラーがある案は_submit_menu_で受け付けない()
    {
        var (menu, context, a, b, _) = FullMenu();
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Chat, 50);
        var submission = new MenuAgentSubmission(
            new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(b.Id, 40)])], []),
            new InterpretedRequests(), [], "説明", false);

        tools.SubmitMenu(submission);

        Assert.False(tools.IsAccepted);
    }

    [Fact]
    public void 今の献立の文章には_全員分に足りない商品の何人分を書く()
    {
        var (menu, context, a, _, _) = FullMenu();
        menu.Days[1].Items[0].PackCount = 4;
        var tools = new MenuAgentTools(menu, context, MenuAgentMode.Chat, 50);

        var text = tools.BuildConditionsText(new Setting { RecheckDays = 90 }, []);

        Assert.Contains($"{a.Id}・1人1個・4パック・40人分まで（全員分より少ない）", text);
        Assert.Contains($"{a.Id}・1人1個・5パック）", text);
    }

    [Fact]
    public void 同じ日付の案が2つに分かれていても_同じ商品への二重の指定はエラーにする()
    {
        var (menu, context, a, _, _) = FullMenu();
        var proposal = new MenuAgentProposal(
            [
                new MenuAgentProposalDay(D(1), [a.Id], "理由", [new MenuAgentServing(a.Id, 40)]),
                new MenuAgentProposalDay(D(1), [], null, [new MenuAgentServing(a.Id, 20)]),
            ], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(MenuIssueKind.InvalidServing, Assert.Single(issues).Kind);
        Assert.Equal(5, menu.Days[1].Items[0].PackCount);
    }

    [Fact]
    public void productIds_に入れたが作れなかった商品の何人分は_重ねてエラーにしない()
    {
        var (menu, context, a, _, _) = FullMenu();
        var unlisted = Safe("ぱりぱりチップス", SnackType.Salty);
        var withUnlisted = Context([a, unlisted]);
        var proposal = new MenuAgentProposal(
            [new MenuAgentProposalDay(D(0), [a.Id, unlisted.Id], "理由", [new MenuAgentServing(unlisted.Id, 40)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, withUnlisted);

        Assert.Equal(MenuIssueKind.NotListed, Assert.Single(issues).Kind);
    }

    [Fact]
    public void 作り直しでは_前に減らしたパック数を全員分に戻し_多めの上書きは残す()
    {
        var (menu, context, a, _, _) = FullMenu();
        menu.Days[0].Items[0].PackCount = 4;
        menu.Days[1].Items[0].PackCount = 7;
        var proposal = new MenuAgentProposal(
            [
                new MenuAgentProposalDay(D(0), [a.Id], "理由", []),
                new MenuAgentProposalDay(D(1), [a.Id], "理由", []),
            ], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
        Assert.Equal(7, menu.Days[1].Items[0].PackCount);
    }

    [Fact]
    public void 作り直しでも_今回の案の何人分は当てる()
    {
        var (menu, context, a, _, _) = FullMenu();
        menu.Days[0].Items[0].PackCount = 2;
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 40)])], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Equal(4, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 何人分は献立の商品の1人あたりの個数で計算する()
    {
        var (menu, context, a, _, _) = FullMenu();
        menu.Days[0].Items[0].PerChildCount = 2;
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 20)])], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        // 20人 × 2個 ÷ 入数10 = 4パック
        Assert.Equal(4, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 何人分を当てても_作り直しで全員分に戻しても_確定時の確認は残る()
    {
        var (menu, context, a, _, _) = FullMenu();
        foreach (var index in new[] { 0, 1 })
        {
            var item = menu.Days[index].Items[0];
            item.AcknowledgedAt = NoonUtc(Today);
            item.AcknowledgedJudgement = AllergenJudgementKind.Reference;
            item.AcknowledgedJudgementFingerprint = "指紋";
        }

        menu.Days[1].Items[0].PackCount = 2;
        var proposal = new MenuAgentProposal(
            [
                new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 40)]),
                new MenuAgentProposalDay(D(1), [a.Id], "理由", []),
            ], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Equal(4, menu.Days[0].Items[0].PackCount);
        Assert.Equal(5, menu.Days[1].Items[0].PackCount);
        Assert.All(menu.Days.Take(2).Select(d => d.Items[0]), i =>
        {
            Assert.NotNull(i.AcknowledgedAt);
            Assert.Equal(AllergenJudgementKind.Reference, i.AcknowledgedJudgement);
            Assert.Equal("指紋", i.AcknowledgedJudgementFingerprint);
        });
    }

    [Fact]
    public void 入数がない商品の何人分はエラーにし_パック数を変えない()
    {
        var (menu, context, a, _, _) = FullMenu();
        a.PiecesPerPack = null;
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由", [new MenuAgentServing(a.Id, 40)])], []);

        var (_, issues) = MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        var issue = Assert.Single(issues);
        Assert.Equal(MenuIssueKind.InvalidServing, issue.Kind);
        Assert.Null(issue.MenuItemId);
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 写しは確定時の確認も写す()
    {
        var (menu, _, _, _, _) = FullMenu();
        var item = menu.Days[0].Items[0];
        item.AcknowledgedAt = NoonUtc(Today);
        item.AcknowledgedJudgement = AllergenJudgementKind.Reference;
        item.AcknowledgedJudgementFingerprint = "指紋";

        var clone = MenuCloner.Clone(menu).Days[0].Items[0];

        Assert.Equal(item.AcknowledgedAt, clone.AcknowledgedAt);
        Assert.Equal(item.AcknowledgedJudgement, clone.AcknowledgedJudgement);
        Assert.Equal("指紋", clone.AcknowledgedJudgementFingerprint);
        Assert.NotSame(item, clone);
    }
}
