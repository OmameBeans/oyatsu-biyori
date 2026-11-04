using System.Text;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Menus;

/// <summary>
/// 献立案の作成・チャットでの組み直し・予算に合わせる入れ替え案（バックグラウンド。T4-04・05・08・09）。
/// エージェントの案は必ず Biz で検証し、直しきれなかったエラーの商品は外して示す（FR-MENU-15・SR-18）。
/// 処理全体の期限を超えて止めた場合も、それまでに出された最後の案を検証して、エラーの商品は外して使う。
/// </summary>
public sealed class MenuProposalApplicationService(
    IMenuRepository menuRepository,
    ISettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    MenuContextFactory contextFactory,
    IMenuPlannerAgent agent,
    IBudgetFitExplainer budgetFitExplainer,
    IBackgroundJobService backgroundJobs,
    IUserContext userContext,
    TimeProvider timeProvider,
    MenuProposalOptions options)
{
    /// <summary>献立案の作成の処理の種類（チャットとあわせて、同じ献立で同時に1つだけ）</summary>
    public const string ProposalJobKind = "menu-proposal";

    /// <summary>チャットでの組み直しの処理の種類（失敗したときに「作り直す」を出さないよう、作成と分ける）</summary>
    public const string ChatJobKind = "menu-chat";

    /// <summary>予算に合わせる案の処理の種類</summary>
    public const string BudgetFitJobKind = "budget-fit";

    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly MenuContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly IMenuPlannerAgent _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    private readonly IBudgetFitExplainer _budgetFitExplainer = budgetFitExplainer ?? throw new ArgumentNullException(nameof(budgetFitExplainer));
    private readonly IBackgroundJobService _jobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly MenuProposalOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private const string NotFound = "献立が見つかりません。献立一覧から開き直してください";

    /// <summary>献立案の作成を受け付ける（要望は先に保存する。失敗しても要望は残る）</summary>
    public async Task<Result<string>> StartProposalAsync(string? menuId, string? requestText)
    {
        return await StartAsync(menuId, MenuAgentMode.Create, requestText);
    }

    /// <summary>チャットでの組み直しを受け付ける</summary>
    public async Task<Result<string>> StartChatAsync(string? menuId, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Result<string>.Error("直してほしいことを入れてください");
        }

        if (message.Length > 1000)
        {
            return Result<string>.Error("1回の依頼は1000文字までにしてください");
        }

        return await StartAsync(menuId, MenuAgentMode.Chat, message);
    }

    private async Task<Result<string>> StartAsync(string? menuId, MenuAgentMode mode, string? text)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<string>.Error(NotFound);
        }

        if (menu.ListedProducts.Count == 0)
        {
            return Result<string>.Error("今週の商品がありません。先にチラシを取り込んでください");
        }

        // 確かめる → 保存する → 受け付ける を1つの鍵で守る（同じ献立で2つ動かさず、弾いた依頼を履歴に残さない）
        await StartLock.WaitAsync();
        try
        {
            return await StartCoreAsync(menu, mode, text);
        }
        finally
        {
            StartLock.Release();
        }
    }

    private async Task<Result<string>> StartCoreAsync(Menu menu, MenuAgentMode mode, string? text)
    {
        if (IsProposalRunning(menu.Id))
        {
            return Result<string>.Error("献立を考えている途中です。終わるまで待ってください");
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        if (mode == MenuAgentMode.Create)
        {
            // 要望は先に保存する（失敗しても入力を消さない。NFR-UX-04）
            menu.RequestText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            menu.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
            menu.UpdateUserId = user?.UserId;
        }
        else
        {
            _menus.AddMessage(new MenuMessage
            {
                MenuId = menu.Id,
                Role = MenuMessageRole.Staff,
                Content = text!.Trim(),
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime,
                UpdateUserId = user?.UserId,
            });
        }

        await _unitOfWork.SaveChangesAsync();
        var key = menu.Id;
        var jobId = _jobs.Enqueue(mode == MenuAgentMode.Create ? ProposalJobKind : ChatJobKind, key, user, _options.JobDeadline, async (services, progress, cancellationToken) =>
        {
            var service = (MenuProposalApplicationService)services.GetService(typeof(MenuProposalApplicationService))!;
            var result = await service.RunAsync(key, mode, text, progress, cancellationToken);
            var outcome = new Result<object?>();
            outcome.ErrorMessages.AddRange(result.ErrorMessages);
            outcome.Value = result.Value;
            return outcome;
        });
        return Result<string>.Ok(jobId);
    }

    private static readonly SemaphoreSlim StartLock = new(1, 1);

    /// <summary>その献立で、献立案の作成・組み直しが動いているか（動いているあいだは手で直させない）</summary>
    public bool IsProposalRunning(string menuId) => IsRunning(_jobs, menuId);

    /// <summary>作成・チャットのどちらかが動いているか</summary>
    public static bool IsRunning(IBackgroundJobService jobs, string menuId) =>
        jobs.FindLatest(ProposalJobKind, menuId) is { State: BackgroundJobState.Queued or BackgroundJobState.Running }
        || jobs.FindLatest(ChatJobKind, menuId) is { State: BackgroundJobState.Queued or BackgroundJobState.Running };

    /// <summary>献立の処理の、いちばん新しい状態（再開時・別の端末から）</summary>
    public BackgroundJobStatus? GetLatestJob(string? menuId, string kind) =>
        string.IsNullOrEmpty(menuId) ? null : _jobs.FindLatest(kind, menuId);

    /// <summary>献立案の作成・組み直しの本体（バックグラウンドの中で呼ぶ）</summary>
    public async Task<Result<MenuProposalOutcome>> RunAsync(string menuId, MenuAgentMode mode, string? text, IJobProgress progress, CancellationToken cancellationToken)
    {
        var menu = await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuProposalOutcome>.Error(NotFound);
        }

        var setting = await _settings.GetSettingAsync();
        var context = await _contextFactory.ForMenuAsync(menu);
        var defaultChildren = setting.DefaultChildrenCount ?? menu.Days.Select(d => d.ChildrenCount).DefaultIfEmpty(0).Max();
        var tools = new MenuAgentTools(menu, context, mode, defaultChildren);
        var (from, to) = DuplicateChecker.HistoryRange(menu.WeekStart, setting.DuplicateAvoidDays);
        var recent = await _menus.GetConfirmedInRangeAsync(from, to);
        var history = mode == MenuAgentMode.Chat ? await _menus.GetMessagesAsync(menu.Id) : [];

        progress.Report(mode == MenuAgentMode.Create ? "要望を読み取って、献立を考えています" : "ご依頼に合わせて、献立を組み直しています");
        var weekRequest = mode == MenuAgentMode.Chat ? MenuAgentTools.BuildWeekRequestText(menu) : null;
        var request = new MenuAgentRequest(mode, tools.BuildConditionsText(setting, recent), text, history, weekRequest);
        Result<MenuAgentRunResult> run;
        try
        {
            run = await _agent.RunAsync(request, tools, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 期限を超えて止めたときも、出されていた最後の案は検証して使う（T4-05）
            run = Result<MenuAgentRunResult>.Error("時間がかかりすぎたため、途中で止めました。");
        }

        var submission = tools.AcceptedSubmission ?? tools.LastSubmission;
        if (submission is null)
        {
            var error = new Result<MenuProposalOutcome>();
            error.ErrorMessages.AddRange(run.ErrorMessages);
            error.ErrorMessages.Add("献立案を作れませんでした。もう一度試すか、「自分で組む」で手で作ってください。");
            return error;
        }

        progress.Report("条件を確かめて、直しています");
        // 考えているあいだに避ける品目・確認の記録が変わっていてもよいように、最後の検証は判定を作り直してから行う
        var latestContext = await _contextFactory.ForMenuAsync(menu, submission.Proposal.Days.SelectMany(d => d.ProductIds));
        var itemsBefore = menu.Days.SelectMany(d => d.Items).ToList();

        // 要望の読み取り（作成）・頼まれた日ごとの値（チャット）を本物の献立に当て、案を反映して検証する（道具の試しと同じ処理。
        // チャットで案に出てこない日は、今の商品のまま残す）
        var (applied, proposalIssues) = MenuSubmissionApplier.Apply(menu, submission.Proposal, submission.Interpreted, submission.ExplicitDates, mode, defaultChildren, latestContext);
        var validation = MenuValidator.Validate(menu, latestContext, proposalIssues);
        var removed = new List<string>();
        if (validation.HasError)
        {
            // 直しきれなかったエラーの商品は外して示す（FR-MENU-15）。作れなかった商品（存在しない ID など）も示す。
            // アレルゲンの理由の文言は画面の共通部品で出すため、ここでは「アレルゲンの判定」とだけ書く
            removed.AddRange(validation.Errors.Select(RemovedLine));
            MenuValidator.RemoveErrorItems(menu, validation);
        }

        // 献立から外れた商品は、はっきり消す（孤児の削除に頼らない）
        var itemsAfter = menu.Days.SelectMany(d => d.Items).ToHashSet();
        foreach (var item in itemsBefore.Where(i => !itemsAfter.Contains(i)))
        {
            _menus.RemoveItem(item);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (menu.Status == MenuStatus.Confirmed)
        {
            // 組み直したら下書きに戻し、もう一度確定してもらう（U-16）
            menu.Status = MenuStatus.Draft;
        }

        if (mode == MenuAgentMode.Create)
        {
            // 新しい案を作ったら、前の案へのチャットの履歴は捨てる
            _menus.RemoveMessages(await _menus.GetMessagesAsync(menu.Id));
        }

        var message = new StringBuilder(string.IsNullOrWhiteSpace(submission.Explanation) ? "献立案を作りました。" : submission.Explanation.Trim());
        if (!tools.IsAccepted)
        {
            message.AppendLine().AppendLine().Append("※ 条件をすべて満たす案にできませんでした。");
        }

        if (removed.Count > 0)
        {
            message.AppendLine().AppendLine().AppendLine("※ 次の商品は条件を満たさないため外しました（アプリの判定）：");
            foreach (var line in removed)
            {
                message.AppendLine("- " + line);
            }
        }

        if (mode == MenuAgentMode.Chat && applied.KeptStaffEdits.Count > 0)
        {
            message.AppendLine().AppendLine().Append("※ 職員が直した値は、そのままにしています（").Append(string.Join("、", applied.KeptStaffEdits.Select(k => MenuIssueText.DateLabel(k.Date)))).Append("）。");
        }

        _menus.AddMessage(new MenuMessage
        {
            MenuId = menu.Id,
            Role = MenuMessageRole.Claude,
            Content = message.ToString().TrimEnd(),
            CreatedAt = now,
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
        menu.UpdateDateTime = now;
        menu.UpdateUserId = user?.UserId;
        try
        {
            await _unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<MenuProposalOutcome>.Error("考えているあいだに、別の画面で献立が変わったため、案を保存できませんでした。献立を読み込み直して、もう一度試してください。");
        }

        return Result<MenuProposalOutcome>.Ok(new MenuProposalOutcome(tools.IsAccepted, removed, applied.NotApplied.Select(n => $"{n.Text}：{n.Reason}").ToList(), submission.IsBudgetFitRequest && mode == MenuAgentMode.Chat));
    }

    /// <summary>予算に合わせる案を受け付ける（予算に届かないときだけ。D-67）</summary>
    public async Task<Result<string>> StartBudgetFitAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<string>.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result<string>.Error("献立を考えている途中です。終わるまで待ってください");
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var key = menu.Id;
        var jobId = _jobs.Enqueue(BudgetFitJobKind, key, user, _options.BudgetFitDeadline, async (services, progress, cancellationToken) =>
        {
            var service = (MenuProposalApplicationService)services.GetService(typeof(MenuProposalApplicationService))!;
            var result = await service.RunBudgetFitAsync(key, progress, cancellationToken);
            var outcome = new Result<object?>();
            outcome.ErrorMessages.AddRange(result.ErrorMessages);
            outcome.Value = result.Value;
            return outcome;
        });
        return Result<string>.Ok(jobId);
    }

    /// <summary>
    /// 予算に合わせる案の本体：アプリが全通り試した候補（T1-14）を Claude に渡し、番号だけを選ばせて説明させる（FR-MENU-30）。
    /// 返ってきた番号が候補にあることを確かめ、表示と反映にはアプリが計算した案を使う。Claude が使えなければ説明なしで上位を示す。
    /// </summary>
    public async Task<Result<BudgetFitView>> RunBudgetFitAsync(string menuId, IJobProgress progress, CancellationToken cancellationToken)
    {
        var menu = await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<BudgetFitView>.Error(NotFound);
        }

        progress.Report("予算に近づく組み合わせを探しています");
        var context = await _contextFactory.ForMenuAsync(menu);
        var search = BudgetFitSearcher.Search(menu, context);
        if (!search.IsUnderBudget)
        {
            return Result<BudgetFitView>.Error("週の実績が週の予算に届いているため、入れ替え案はありません");
        }

        var all = search.Options.ToList();
        if (all.Count == 0)
        {
            return Result<BudgetFitView>.Ok(new BudgetFitView(search.CurrentTotal, search.Budget, [], "今の献立から1か所だけ変えて予算に近づける案は見つかりませんでした。手で入れ替えてみてください。"));
        }

        var optionsText = new StringBuilder("入れ替え案の候補：\n");
        for (var i = 0; i < all.Count; i++)
        {
            optionsText.AppendLine($"[{i}] {Describe(all[i], context)}");
        }

        progress.Report("要望に合う案を選んでいます");
        var choices = await _budgetFitExplainer.ChooseAsync(optionsText.ToString(), menu.RequestText, cancellationToken);
        var views = new List<BudgetFitOptionView>();
        string? note = null;
        if (choices.HasError || choices.Value is null)
        {
            note = "AI の説明は付けられませんでした（差の小さい順に示しています）。";
        }
        else
        {
            foreach (var choice in choices.Value.Where(c => c.Index >= 0 && c.Index < all.Count).DistinctBy(c => c.Index).Take(BudgetFitSearcher.MaxOptions))
            {
                views.Add(ToView(all[choice.Index], context, choice.Explanation));
            }
        }

        if (views.Count == 0)
        {
            views.AddRange(all.Take(BudgetFitSearcher.MaxOptions).Select(o => ToView(o, context, null)));
        }

        return Result<BudgetFitView>.Ok(new BudgetFitView(search.CurrentTotal, search.Budget, views, note));
    }

    /// <summary>
    /// 選んだ入れ替え案を反映する（FR-MENU-30a）。献立が変わっていないかを確かめ、今の献立で探し直した案の中にあるものだけを反映する。
    /// </summary>
    public async Task<Result> ApplyBudgetFitAsync(string? menuId, BudgetFitOptionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result.Error("献立を考えている途中です。終わるまで待ってください");
        }

        var context = await _contextFactory.ForMenuAsync(menu);
        var option = BudgetFitSearcher.Search(menu, context).Options.FirstOrDefault(o =>
            o.Kind == input.Kind
            && o.Date == input.Date
            && o.MenuItemId == input.MenuItemId
            && o.NewProductId == input.NewProductId
            && o.NewPackCount == input.NewPackCount
            && o.CurrentTotal == input.CurrentTotal);
        if (option is null)
        {
            return Result.Error("献立が変わったため、この案は使えなくなりました。「予算に合わせる」をもう一度押してください");
        }

        var applied = BudgetFitSearcher.Apply(menu, option, context);
        if (applied.HasError)
        {
            return applied;
        }

        if (menu.Status == MenuStatus.Confirmed)
        {
            menu.Status = MenuStatus.Draft;
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        menu.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
        menu.UpdateUserId = user?.UserId;
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    private static string RemovedLine(MenuIssue issue)
    {
        var date = issue.Date is { } d ? MenuIssueText.DateLabel(d) : "週全体";
        var product = issue.ProductName is not null ? $"「{issue.ProductName}」" : "商品";
        return issue.Kind == MenuIssueKind.AllergenBlocked
            ? $"{date} {product}：アレルゲンの判定で 🔴 使えません のため"
            : issue.ToLine();
    }

    private static string Describe(BudgetFitOption option, MenuContext context)
    {
        var date = MenuIssueText.DateLabel(option.Date);
        var newName = context.FindProduct(option.NewProductId)?.Name ?? option.NewProductId;
        var oldName = option.OldProductId is null ? null : context.FindProduct(option.OldProductId)?.Name ?? option.OldProductId;
        var what = option.Kind switch
        {
            BudgetFitKind.Replace => $"{date}の「{oldName}」を「{newName}」（{option.NewPackCount}パック）に替える",
            BudgetFitKind.Add => $"{date}に「{newName}」（{option.NewPackCount}パック）を足す",
            BudgetFitKind.PackIncrease => $"{date}の「{newName}」を {option.NewPackCount}パックに増やす",
            _ => $"{date}の「{newName}」を {option.NewPackCount}パックに減らす",
        };
        var diff = option.Difference == 0 ? "週の予算ちょうど" : $"週の予算まであと {option.Difference:N0}円";
        var warning = option.UsesWarningProduct ? "（アレルゲンの確認が要る商品）" : string.Empty;
        var shortage = option.HasShortage ? "（全員分に足りなくなる）" : string.Empty;
        return $"{what} → 週の実績 {option.NewTotal:N0}円・{diff}{warning}{shortage}";
    }

    private static BudgetFitOptionView ToView(BudgetFitOption option, MenuContext context, string? explanation) => new(
        option.Kind,
        option.Date,
        option.MenuItemId,
        option.OldProductId,
        option.NewProductId,
        option.NewPackCount,
        option.CurrentTotal,
        option.NewTotal,
        option.Difference,
        option.UsesWarningProduct,
        option.HasShortage,
        Describe(option, context),
        explanation);
}

/// <summary>献立案の処理の設定</summary>
public sealed class MenuProposalOptions
{
    /// <summary>献立案の作成・組み直しの処理全体の期限（目安3分。呼び出しのタイムアウトと再試行に合わせる）</summary>
    public TimeSpan JobDeadline { get; set; } = TimeSpan.FromMinutes(6);

    /// <summary>予算に合わせる案の処理全体の期限</summary>
    public TimeSpan BudgetFitDeadline { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>献立案の作成・組み直しの結果</summary>
/// <param name="Accepted">条件をすべて満たす案として受け付けたか</param>
/// <param name="RemovedItems">直しきれず外した商品</param>
/// <param name="NotAppliedRequests">反映しなかった要望</param>
/// <param name="IsBudgetFitRequest">チャットの依頼が「予算に合わせて」だったか（画面で入れ替え案を出す。U-12）</param>
public sealed record MenuProposalOutcome(bool Accepted, IReadOnlyList<string> RemovedItems, IReadOnlyList<string> NotAppliedRequests, bool IsBudgetFitRequest);

/// <summary>予算に合わせる案</summary>
public sealed record BudgetFitView(int CurrentTotal, int Budget, IReadOnlyList<BudgetFitOptionView> Options, string? Note);

/// <summary>予算に合わせる案の1件（反映するときは、この値をそのまま送り返す）</summary>
public sealed record BudgetFitOptionView(
    BudgetFitKind Kind,
    DateOnly Date,
    string? MenuItemId,
    string? OldProductId,
    string NewProductId,
    int NewPackCount,
    int CurrentTotal,
    int NewTotal,
    int Difference,
    bool UsesWarningProduct,
    bool HasShortage,
    string Description,
    string? Explanation);

/// <summary>反映する入れ替え案（画面から送り返された値。今の献立で探し直した案と照らし合わせる）</summary>
public sealed record BudgetFitOptionInput(BudgetFitKind Kind, DateOnly Date, string? MenuItemId, string NewProductId, int NewPackCount, int CurrentTotal);
