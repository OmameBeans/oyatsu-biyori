using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using OyatsuBiyori.Biz.Tests.Fakes;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// 献立づくりのエージェントのセッション（Managed Agents。D-94）を確かめる。
/// チャットでは続け、作り直し・履歴を消したとき・失敗したとき・条件（避ける品目・候補）が変わったときは新しく始め、
/// 使わなくなったセッションは Anthropic 側から消す。
/// </summary>
public class MenuAgentSessionTests
{
    private static MenuProposalApplicationService Create(InMemoryData data, IMenuPlannerAgent agent, RecordingCleaner cleaner)
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
            new MenuProposalOptions(),
            cleaner);
    }

    private static (InMemoryData Data, Menu Menu, Product Senbei) DraftMenu()
    {
        var data = new InMemoryData();
        var senbei = Safe("ぱりぱりせんべい", SnackType.Salty);
        data.Products.Add(senbei);
        var menu = NewMenu(20000, 50, (senbei, 300));
        Put(menu, 0, senbei);
        data.Menus.Add(menu);
        return (data, menu, senbei);
    }

    /// <summary>献立案を作って、セッションと条件の指紋を覚えさせる</summary>
    private static async Task CreateProposalAsync(InMemoryData data, Menu menu, Product product, string sessionId)
    {
        var agent = new SessionAgent(tools => tools.SubmitMenu(Submission(product)), string.Empty, sessionId);
        var result = await Create(data, agent, new RecordingCleaner()).RunAsync(menu.Id, MenuAgentMode.Create, null, new NullProgress(), CancellationToken.None);
        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
    }

    [Fact]
    public async Task D_94_作成では前のセッションを渡さず_新しいセッションを覚えて_前のセッションは消す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_前の案");
        var agent = new SessionAgent(tools => tools.SubmitMenu(Submission(senbei)), string.Empty, "sesn_作り直し");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Create, null, new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Null(agent.ReceivedSessionId);
        Assert.Equal("sesn_作り直し", menu.AgentSessionId);
        Assert.NotNull(menu.AgentSessionFingerprint);
        Assert.Equal(["sesn_前の案"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_条件が変わらなければ_チャットで前のセッションを続ける()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var agent = new SessionAgent(_ => { }, "せんべいはしょっぱいものの日だからです。", "sesn_作成");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Chat, "月曜のせんべいはなぜ？", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal("sesn_作成", agent.ReceivedSessionId);
        Assert.Equal("sesn_作成", menu.AgentSessionId);
        Assert.Empty(cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_避ける品目が変わったら_前のセッション_古い候補を覚えている_は続けずに消す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var hazelnut = Allergen("hazelnut");
        data.Avoided.Add(new AvoidedAllergen { AllergenId = hazelnut.Id, Allergen = hazelnut });
        var agent = new SessionAgent(_ => { }, "ほかの候補はありません。", "sesn_新");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Chat, "ほかにしょっぱいものの候補はある？", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Null(agent.ReceivedSessionId);
        Assert.Equal("sesn_新", menu.AgentSessionId);
        Assert.Equal(["sesn_作成"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_エージェントの版が変わったら_前のセッション_古いプロンプトのまま_は続けずに消す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_版1");
        var agent = new SessionAgent(_ => { }, "火曜のせんべいは40人分にできます。", "sesn_版4", version: "agent_テスト:4");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Chat, "特定のおやつを40人分にできますか？", new NullProgress(), CancellationToken.None);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Null(agent.ReceivedSessionId);
        Assert.Equal("sesn_版4", menu.AgentSessionId);
        Assert.Equal(["sesn_版1"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_失敗したときはセッションを忘れ_次は新しく始める()
    {
        // 案は出したが、そのあとセッションが壊れた（期限切れなど）。壊れたセッションはエージェントが消す
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var agent = new SessionAgent(tools => tools.SubmitMenu(Submission(senbei)), string.Empty, "sesn_作成", fail: true);
        var cleaner = new RecordingCleaner();

        await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Chat, "月曜はそのままで", new NullProgress(), CancellationToken.None);

        Assert.Null(menu.AgentSessionId);
        Assert.Null(menu.AgentSessionFingerprint);
        Assert.Equal(["sesn_作成"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_作り直しで案が出なかったら_新しいセッションは消し_前のセッションは献立に残す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_前の案");
        var agent = new SessionAgent(_ => { }, string.Empty, "sesn_案なし");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Create, null, new NullProgress(), CancellationToken.None);

        Assert.True(result.HasError);
        Assert.Equal("sesn_前の案", menu.AgentSessionId);
        Assert.Equal(["sesn_案なし"], cleaner.Forgotten);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task D_94_保存が別の画面の更新とぶつかったら_エラーにして_新しいセッションは消す(bool isQuestion)
    {
        // isQuestion：質問への返答（文章だけ）の保存か、献立案の保存か
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var hazelnut = Allergen("hazelnut");
        data.Avoided.Add(new AvoidedAllergen { AllergenId = hazelnut.Id, Allergen = hazelnut });
        Action<IMenuAgentTools> script = isQuestion ? _ => { } : tools => tools.SubmitMenu(Submission(senbei));
        var agent = new SessionAgent(script, isQuestion ? "せんべいはしょっぱいものの日だからです。" : string.Empty, "sesn_新");
        var cleaner = new RecordingCleaner();
        data.IsSaveConflicting = true;

        var result = await Create(data, agent, cleaner).RunAsync(menu.Id, MenuAgentMode.Chat, "月曜のせんべいはなぜ？", new NullProgress(), CancellationToken.None);

        Assert.True(result.HasError);
        // 献立の行には前のセッションが残っているので、前のセッションは消さない（新しいセッションだけ消す）
        Assert.Equal(["sesn_新"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_チャットの履歴を消したらセッションも消す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var cleaner = new RecordingCleaner();

        var result = await Create(data, new SessionAgent(_ => { }, string.Empty, null), cleaner).ClearChatAsync(menu.Id);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Null(menu.AgentSessionId);
        Assert.Null(menu.AgentSessionFingerprint);
        Assert.Equal(["sesn_作成"], cleaner.Forgotten);
    }

    [Fact]
    public async Task D_94_献立を削除したらセッションも消す()
    {
        var (data, menu, senbei) = DraftMenu();
        await CreateProposalAsync(data, menu, senbei, "sesn_作成");
        var cleaner = new RecordingCleaner();
        var time = new FixedTimeProvider(new DateTimeOffset(NoonUtc(Today)));
        var factory = new MenuContextFactory(new FakeProductRepository(data), new FakeSettingRepository(data), new FakeMenuRepository(data), time);
        var service = new MenuApplicationService(
            new FakeMenuRepository(data),
            new FakeProductRepository(data),
            new FakeSettingRepository(data),
            new FakeUnitOfWork(data),
            factory,
            new FakeUserContext(),
            time,
            new FakeJobs(),
            cleaner);

        var result = await service.DeleteAsync(menu.Id);

        Assert.False(result.HasError, string.Join(",", result.ErrorMessages));
        Assert.Equal(["sesn_作成"], cleaner.Forgotten);
    }

    private static MenuAgentSubmission Submission(Product product) => new(
        new MenuAgentProposal([new MenuAgentProposalDay(D(0), [product.Id], "テストの理由")], []),
        new InterpretedRequests(),
        [],
        "テストの説明",
        false);

    /// <summary>受け取ったセッションを覚え、決まったセッションを返す偽のエージェント</summary>
    private sealed class SessionAgent(Action<IMenuAgentTools> script, string replyText, string? returnSessionId, bool fail = false, string version = "agent_テスト:1") : IMenuPlannerAgent
    {
        public string? ReceivedSessionId { get; private set; }

        public string SessionVersion => version;

        public Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken)
        {
            ReceivedSessionId = request.SessionId;
            script(tools);
            return Task.FromResult(fail
                ? Result<MenuAgentRunResult>.Error("AI の処理でエラーになりました。")
                : Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(tools.IsAccepted, 1, replyText, returnSessionId)));
        }
    }

    /// <summary>消したセッションを覚える後片付け</summary>
    private sealed class RecordingCleaner : IMenuAgentSessionCleaner
    {
        public List<string> Forgotten { get; } = [];

        public Task ForgetAsync(string sessionId)
        {
            Forgotten.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoBudgetFitExplainer : IBudgetFitExplainer
    {
        public Task<Result<IReadOnlyList<BudgetFitChoice>>> ChooseAsync(string optionsText, string? requestText, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<BudgetFitChoice>>.Error("使わない"));
    }
}
