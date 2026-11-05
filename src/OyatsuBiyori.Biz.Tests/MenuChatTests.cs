using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// チャットで、質問には献立を変えずに答え、直す依頼では流した説明を履歴に残すこと（D-91）を、決まった動きの偽のエージェントで確かめる。
/// </summary>
public class MenuChatTests
{
    private static MenuProposalApplicationService Create(InMemoryData data, IMenuPlannerAgent agent)
    {
        var time = new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today)));
        var factory = new MenuContextFactory(new FakeProductRepository(data), new FakeSettingRepository(data), new FakeMenuRepository(data), time);
        return new MenuProposalApplicationService(
            new FakeMenuRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            factory,
            agent,
            new NoBudgetFitExplainer(),
            new FakeJobs(),
            new FakeUserContext(),
            time,
            new MenuProposalOptions());
    }

    private static (InMemoryData Data, Menu Menu, Product Senbei, Product Jelly) ConfirmedMenu()
    {
        var data = new InMemoryData();
        var senbei = Safe("ぱりぱりせんべい", SnackType.Salty);
        var jelly = Safe("ぷるぷるゼリー", SnackType.Smooth);
        data.Products.AddRange([senbei, jelly]);
        var menu = NewMenu(20000, 50, (senbei, 300), (jelly, 250));
        Put(menu, 0, senbei);
        menu.Status = MenuStatus.Confirmed;
        data.Menus.Add(menu);
        return (data, menu, senbei, jelly);
    }

    [Fact]
    public async Task D_91_質問には文章で答え_献立と確定の状態は変えない()
    {
        var (data, menu, senbei, _) = ConfirmedMenu();
        var updatedAt = menu.UpdateDateTime;
        var service = Create(data, new ScriptedAgent(_ => { }, "**せんべい**を選んだのは、しょっぱいものの日だからです。"));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜のせんべいはなぜ？", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.True(result.Value!.IsAnswer);
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
        Assert.Equal(updatedAt, menu.UpdateDateTime);
        var item = Assert.Single(menu.Days.SelectMany(d => d.Items));
        Assert.Equal(senbei.Id, item.ProductId);
        var reply = Assert.Single(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.StartsWith("**せんべい**を選んだのは、しょっぱいものの日だからです。", reply.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SR_18_文章だけの返答には献立を変えていないことをアプリが書き足す()
    {
        // 直す依頼を Claude が質問と読み違え、文章だけで「外しました」と答えた
        var (data, menu, senbei, _) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(_ => { }, "月曜の**せんべい**を外しました。"));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜のせんべいを外して", new NullProgress(), CancellationToken.None);

        Assert.True(result.Value!.IsAnswer);
        Assert.Equal(senbei.Id, Assert.Single(menu.Days.SelectMany(d => d.Items)).ProductId);
        var reply = Assert.Single(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.EndsWith(MenuProposalApplicationService.AnswerNote, reply.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D_91_直す依頼では_流した説明を履歴に残し_下書きに戻す()
    {
        var (data, menu, _, jelly) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(
            tools => tools.SubmitMenu(Submission(jelly, "submit_menu の説明")),
            "月曜を**ゼリー**にしました。"));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜はゼリーにして", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.False(result.Value!.IsAnswer);
        Assert.Equal(MenuStatus.Draft, menu.Status);
        var item = Assert.Single(menu.Days.SelectMany(d => d.Items));
        Assert.Equal(jelly.Id, item.ProductId);
        var reply = Assert.Single(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.StartsWith("月曜を**ゼリー**にしました。", reply.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D_91_説明を書けなかったときは_submit_menu_の説明を残す()
    {
        var (data, menu, _, jelly) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(tools => tools.SubmitMenu(Submission(jelly, "submit_menu の説明")), string.Empty));

        await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜はゼリーにして", new NullProgress(), CancellationToken.None);

        var reply = Assert.Single(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.StartsWith("submit_menu の説明", reply.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task D_91_案も返答もなければ_エラーにして履歴に残さない()
    {
        var (data, menu, _, _) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(_ => { }, "   "));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜はゼリーにして", new NullProgress(), CancellationToken.None);

        Assert.True(result.HasError);
        Assert.DoesNotContain(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
    }

    [Fact]
    public async Task D_91_SR_18_却下された案のあとの文章は_質問への返答にも説明にも使わない()
    {
        var (data, menu, _, _) = ConfirmedMenu();
        data.Avoid("egg");
        var boro = Snack("たまごボーロ", SnackType.Sweet, 10, 1, ConfirmedAll(Today, ["egg"]));
        data.Products.Add(boro);
        menu.ListedProducts.Add(new ListedProduct(boro.Id, 200, "5678"));
        var service = Create(data, new ScriptedAgent(
            tools => tools.SubmitMenu(Submission(boro, "submit_menu の説明")),
            "月曜をたまごボーロにしました。卵は入っていません。"));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Chat, "月曜はたまごボーロにして", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.False(result.Value!.IsAnswer);
        Assert.DoesNotContain(menu.Days.SelectMany(d => d.Items), i => i.ProductId == boro.Id);
        var reply = Assert.Single(data.Messages, m => m.Role == MenuMessageRole.Claude);
        Assert.DoesNotContain("卵は入っていません", reply.Content, StringComparison.Ordinal);
        Assert.Contains("外しました", reply.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 作成のときは_返答の文章があっても質問として扱わない()
    {
        var (data, menu, _, _) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(_ => { }, "献立の考え方です"));

        var result = await service.RunAsync(menu.Id, MenuAgentMode.Create, "しょっぱいものを多めに", new NullProgress(), CancellationToken.None);

        Assert.True(result.HasError);
        Assert.DoesNotContain(data.Messages, m => m.Role == MenuMessageRole.Claude);
    }

    [Fact]
    public async Task チャットを消すと_履歴だけが消え_献立と確定の状態は変わらない()
    {
        var (data, menu, senbei, _) = ConfirmedMenu();
        data.Messages.Add(new MenuMessage { MenuId = menu.Id, Role = MenuMessageRole.Staff, Content = "質問" });
        data.Messages.Add(new MenuMessage { MenuId = menu.Id, Role = MenuMessageRole.Claude, Content = "返答" });
        data.Messages.Add(new MenuMessage { MenuId = "別の献立", Role = MenuMessageRole.Staff, Content = "残る" });
        var service = Create(data, new ScriptedAgent(_ => { }, string.Empty));

        var result = await service.ClearChatAsync(menu.Id);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.DoesNotContain(data.Messages, m => m.MenuId == menu.Id);
        Assert.Single(data.Messages, m => m.MenuId == "別の献立");
        Assert.Equal(MenuStatus.Confirmed, menu.Status);
        Assert.Equal(senbei.Id, Assert.Single(menu.Days.SelectMany(d => d.Items)).ProductId);
    }

    [Fact]
    public async Task 見つからない献立のチャットは消せない()
    {
        var (data, _, _, _) = ConfirmedMenu();
        var service = Create(data, new ScriptedAgent(_ => { }, string.Empty));

        var result = await service.ClearChatAsync("ない献立");

        Assert.True(result.HasError);
    }

    private static MenuAgentSubmission Submission(Product product, string explanation) => new(
        new MenuAgentProposal([new MenuAgentProposalDay(D(0), [product.Id], "テストの理由")], []),
        new InterpretedRequests(),
        [],
        explanation,
        false);

    /// <summary>決まった動き（道具の呼び方）のあと、決まった返答の文章を返す偽のエージェント</summary>
    private sealed class ScriptedAgent(Action<IMenuAgentTools> script, string replyText) : IMenuPlannerAgent
    {
        public Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken)
        {
            script(tools);
            progress.ReportText(replyText);
            return Task.FromResult(Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(tools.IsAccepted, 1, replyText)));
        }
    }

    private sealed class NoBudgetFitExplainer : IBudgetFitExplainer
    {
        public Task<Result<IReadOnlyList<BudgetFitChoice>>> ChooseAsync(string optionsText, string? requestText, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<BudgetFitChoice>>.Error("使わない"));
    }
}
