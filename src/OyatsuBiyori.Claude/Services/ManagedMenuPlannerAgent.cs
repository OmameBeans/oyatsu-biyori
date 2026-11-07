using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Claude.Services;

/// <summary>
/// 献立づくりのエージェントの Managed Agents 版（D-94）。エージェント（モデル・システムプロンプト・5つの道具）は agents/oyatsu-menu-planner/ のファイルから
/// ant apply で Anthropic 側に作っておき、ここでは献立づくりのたびにセッションを開く（チャットは同じセッションを続ける）。
/// ループと会話の履歴は Anthropic 側が持ち、道具（カスタムツール）はこのサーバーで Biz（IMenuAgentTools）が実行する。
/// submit_menu は Biz の検証を通らなければ受け付けない（FR-MENU-15・SR-18。Messages API 版と同じ）。
/// モデルの呼び出しが終わるたびに記録し、1日の金額の上限（D-95）に数える。
/// </summary>
public sealed class ManagedMenuPlannerAgent(
    AnthropicClient client,
    ClaudeRequestRunner runner,
    MenuAgentSessionCleaner cleaner,
    IOptions<ClaudeOptions> options,
    ILogger<ManagedMenuPlannerAgent> logger) : IMenuPlannerAgent
{
    /// <summary>流れが静かなとき、取りこぼしをイベント一覧で確かめる間隔</summary>
    private static readonly TimeSpan QuietPollInterval = TimeSpan.FromSeconds(15);

    private readonly AnthropicClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly MenuAgentSessionCleaner _cleaner = cleaner ?? throw new ArgumentNullException(nameof(cleaner));
    private readonly ClaudeOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<ManagedMenuPlannerAgent> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string SessionVersion =>
        $"{_options.ManagedAgents.MenuPlannerAgentId}:{_options.ManagedAgents.MenuPlannerAgentVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "latest"}";

    public async Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(progress);

        var settings = _options.ManagedAgents;
        if (string.IsNullOrWhiteSpace(settings.MenuPlannerAgentId) || string.IsNullOrWhiteSpace(settings.EnvironmentId))
        {
            _logger.LogError("Managed Agents のエージェント ID か環境 ID が設定されていません");
            return Result<MenuAgentRunResult>.Error("AI の設定が足りないため、献立を考えられません。「自分で組む」で手で作ってください。");
        }

        if (await _runner.IsDailyLimitReachedAsync())
        {
            return Result<MenuAgentRunResult>.Error("今日は AI を使えません（1日の上限に達しました）。手で編集してください。");
        }

        var isChat = request.Mode == MenuAgentMode.Chat;
        var purpose = isChat ? ClaudePurpose.MenuChat : ClaudePurpose.MenuPlanning;

        string sessionId;
        string model;
        bool continued;
        try
        {
            (sessionId, model, continued) = await OpenSessionAsync(request, settings, cancellationToken);
        }
        catch (AnthropicApiException ex)
        {
            _logger.LogError(ex, "Managed Agents のセッションを開けませんでした");
            return Result<MenuAgentRunResult>.Error("AI の処理でエラーになりました。少し待ってから、もう一度試してください。");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Managed Agents のセッションを開けませんでした（通信）");
            return Result<MenuAgentRunResult>.Error("AI の処理に失敗しました。少し待ってから、もう一度試してください。");
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // 処理全体の期限ではない中断 ＝ 1回の通信のタイムアウト
            _logger.LogWarning(ex, "Managed Agents のセッションを開けませんでした（タイムアウト）");
            return Result<MenuAgentRunResult>.Error("AI の処理に時間がかかりすぎました。少し待ってから、もう一度試してください。");
        }

        _logger.LogInformation("献立づくりのセッション: {SessionId} {Mode} 続き {Continued}", sessionId, request.Mode, continued);

        var turn = new Turn(this, tools, progress, purpose, model, sessionId, isChat);
        Result<MenuAgentRunResult> result;
        try
        {
            result = await turn.RunAsync(MenuAgentToolDispatcher.FirstMessage(request, includeHistory: !continued), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 期限を超えた：セッションを止めて消す（呼び出し側が、それまでに出された最後の案を検証して使う）
            await _cleaner.ForgetAsync(sessionId);
            throw;
        }
        catch (DailyLimitReachedException)
        {
            result = Result<MenuAgentRunResult>.Error("今日は AI を使えません（1日の上限に達しました）。手で編集してください。");
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException)
        {
            _logger.LogError(ex, "献立づくりのセッションでエラーになりました: {SessionId}", sessionId);
            result = Result<MenuAgentRunResult>.Error("AI の処理でエラーになりました。もう一度試しても直らない場合は、手で編集してください。");
        }
        catch (OperationCanceledException ex)
        {
            // 処理全体の期限ではない中断（1回の通信のタイムアウト）。エラーとして返し、呼び出し側でそれまでの最後の案を検証して使う
            _logger.LogWarning(ex, "献立づくりのセッションで通信がタイムアウトしました: {SessionId}", sessionId);
            result = Result<MenuAgentRunResult>.Error("AI の処理に時間がかかりすぎたため、途中で止めました。");
        }
        catch (Exception ex)
        {
            // 想定外の例外：セッションを残さないように消してから投げ直す（職員の文章を残さない。D-94）
            _logger.LogError(ex, "献立づくりのセッションで想定外のエラーになりました: {SessionId}", sessionId);
            await _cleaner.ForgetAsync(sessionId);
            throw;
        }

        // 次のチャットで続けないセッション（失敗・上限で止めた）は、止めてから Anthropic 側から消す（職員の文章を残さない。D-94）
        if (result.HasError || result.Value?.SessionId is null)
        {
            await _cleaner.ForgetAsync(sessionId);
        }

        return result;
    }

    /// <summary>
    /// セッションを開く。チャットで前のセッションがあり、続けられる状態（止まっていて、こちらの返事を待っていない）なら続ける。
    /// それ以外は新しく作る（作成のとき・前のセッションが終わっていたり壊れていたりしたとき）
    /// </summary>
    private async Task<(string SessionId, string Model, bool Continued)> OpenSessionAsync(MenuAgentRequest request, ManagedAgentsOptions settings, CancellationToken cancellationToken)
    {
        if (request.Mode == MenuAgentMode.Chat && !string.IsNullOrWhiteSpace(request.SessionId))
        {
            try
            {
                var previous = await _client.Beta.Sessions.Retrieve(request.SessionId, new SessionRetrieveParams(), cancellationToken);
                if (await CanContinueAsync(previous, cancellationToken))
                {
                    return (previous.ID, ModelOf(previous), true);
                }

                _logger.LogInformation("前のセッションは続けられないので、新しく始めます: {SessionId}", request.SessionId);
            }
            catch (AnthropicNotFoundException)
            {
                _logger.LogInformation("前のセッションが見つからないので、新しく始めます: {SessionId}", request.SessionId);
            }
        }

        var session = await _client.Beta.Sessions.Create(
            new SessionCreateParams
            {
                Agent = new BetaManagedAgentsAgentParams
                {
                    ID = settings.MenuPlannerAgentId,
                    Version = settings.MenuPlannerAgentVersion,
                },
                EnvironmentID = settings.EnvironmentId,
                Title = request.Mode == MenuAgentMode.Chat ? "献立のチャット" : "献立案の作成",
            },
            cancellationToken);
        WarnIfToolsDiffer(session);
        return (session.ID, ModelOf(session), false);
    }

    /// <summary>続けて使えるセッションか（止まっていて、最後に正しく答え終えている。こちらの道具の結果を待っていない）</summary>
    private async Task<bool> CanContinueAsync(BetaManagedAgentsSession session, CancellationToken cancellationToken)
    {
        if (session.ArchivedAt is not null || session.Status.Raw() != "idle")
        {
            return false;
        }

        // セッションは作ったときの版のプロンプト・道具のまま動く。設定の版と違えば続けない
        // （条件の指紋にも版を入れているが、指紋に版を入れる前に作ったセッションもここで止める）
        if (_options.ManagedAgents.MenuPlannerAgentVersion is int pinned && session.Agent.Version != pinned)
        {
            _logger.LogInformation("前のセッションはエージェントの版が違うので続けません: {SessionVersion} / {Pinned}", session.Agent.Version, pinned);
            return false;
        }

        string? lastStopReason = null;
        await foreach (var listed in ListEventsAsync(session.ID, cancellationToken))
        {
            if (listed.Kind == AgentEventKind.Idle)
            {
                lastStopReason = listed.StopReason;
            }
        }

        return lastStopReason == "end_turn";
    }

    /// <summary>セッションのモデル名（記録用）</summary>
    private static string ModelOf(BetaManagedAgentsSession session) => session.Agent.Model.ID;

    /// <summary>エージェントの道具が、このコードの道具と食い違っていないか（agents/ のファイルを直し忘れたときに気づけるように）</summary>
    private void WarnIfToolsDiffer(BetaManagedAgentsSession session)
    {
        var expected = MenuAgentToolDispatcher.Tools.Select(t => t.Name).Order().ToList();
        var actual = session.Agent.Tools
            .Select(t => t.TryPickBetaManagedAgentsCustom(out var custom) ? custom.Name : null)
            .OfType<string>()
            .Order()
            .ToList();
        if (!expected.SequenceEqual(actual))
        {
            _logger.LogWarning("エージェントの道具がコードと違います。agents/oyatsu-menu-planner/agent.md を直して ant apply してください: {Expected} / {Actual}", string.Join(",", expected), string.Join(",", actual));
        }
    }

    /// <summary>流れ（SSE）とイベント一覧の、どちらから来たイベントも同じ形にしたもの</summary>
    private sealed record AgentEvent(
        AgentEventKind Kind,
        string? Id,
        string? Name = null,
        IReadOnlyDictionary<string, JsonElement>? Input = null,
        string? Text = null,
        string? StopReason = null,
        BetaManagedAgentsSpanModelUsage? Usage = null,
        bool IsError = false);

    private enum AgentEventKind
    {
        Other,
        Message,
        UserMessage,
        MessagePreviewStart,
        MessagePreviewDelta,
        CustomToolUse,
        ModelRequestEnd,
        Idle,
        Terminated,
        Error,
    }

    /// <summary>1回の依頼（1つのターン）の進め方</summary>
    private sealed class Turn(
        ManagedMenuPlannerAgent owner,
        IMenuAgentTools tools,
        IJobProgress progress,
        ClaudePurpose purpose,
        string model,
        string sessionId,
        bool isChat)
    {
        /// <summary>処理したイベント（流れと一覧の二重を除く）</summary>
        private readonly HashSet<string> _seen = [];

        /// <summary>最後の道具のあとに書いた文章（画面に流す表示用。残す返答はイベント一覧から作り直す）</summary>
        private readonly StringBuilder _reply = new();

        /// <summary>書いている途中の文章（プレビュー。届いた分だけ）</summary>
        private readonly Dictionary<string, StringBuilder> _previews = [];

        private int _modelRequests;
        private bool _hasSubmitted;
        private bool _hasAskedResubmit;
        private int _nudges;

        public async Task<Result<MenuAgentRunResult>> RunAsync(string firstMessage, CancellationToken cancellationToken)
        {
            // 続きのセッションの、前のターンのイベントは処理しない
            await foreach (var old in owner.ListEventsAsync(sessionId, cancellationToken))
            {
                if (old.Id is not null)
                {
                    _seen.Add(old.Id);
                }
            }

            var events = Channel.CreateUnbounded<AgentEvent>();
            using var streamStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var reconnects = 0;
            var reader = ReadStreamAsync(events.Writer, streamStop.Token);

            await SendMessageAsync(firstMessage, cancellationToken);

            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 流れが切れたら、つなぎ直す（そのあいだの取りこぼしは、一覧で拾う）
                    if (reader.IsCompleted)
                    {
                        if (reconnects >= owner._options.ManagedAgents.MaxStreamReconnects)
                        {
                            owner._logger.LogWarning("イベントの流れを何度つなぎ直しても切れました: {SessionId}", sessionId);
                            return Result<MenuAgentRunResult>.Error("AI との通信が切れました。少し待ってから、もう一度試してください。");
                        }

                        reconnects++;
                        owner._logger.LogInformation("イベントの流れをつなぎ直します: {SessionId} {Count}", sessionId, reconnects);
                        reader = ReadStreamAsync(events.Writer, streamStop.Token);
                        await PollAsync(events.Writer, cancellationToken);
                    }

                    AgentEvent next;
                    using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        quiet.CancelAfter(QuietPollInterval);
                        try
                        {
                            next = await events.Reader.ReadAsync(quiet.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // しばらく何も来ない：流れを開く前に起きたイベントを取りこぼしていないか、一覧で確かめる
                            await PollAsync(events.Writer, cancellationToken);
                            continue;
                        }
                    }

                    if (next.Id is not null && next.Kind is not (AgentEventKind.MessagePreviewStart or AgentEventKind.MessagePreviewDelta) && !_seen.Add(next.Id))
                    {
                        continue;
                    }

                    var finished = await HandleAsync(next, cancellationToken);
                    if (finished is not null)
                    {
                        return finished;
                    }
                }
            }
            finally
            {
                await streamStop.CancelAsync();
                try
                {
                    await reader;
                }
                catch (OperationCanceledException)
                {
                    // 止めただけ
                }
            }
        }

        /// <summary>1つのイベントを処理する。ターンが終わったら結果を返す（続くなら null）</summary>
        private async Task<Result<MenuAgentRunResult>?> HandleAsync(AgentEvent agentEvent, CancellationToken cancellationToken)
        {
            switch (agentEvent.Kind)
            {
                case AgentEventKind.MessagePreviewStart:
                    _previews[agentEvent.Id!] = new StringBuilder();
                    return null;

                case AgentEventKind.MessagePreviewDelta:
                    if (_previews.TryGetValue(agentEvent.Id!, out var preview))
                    {
                        preview.Append(agentEvent.Text);
                        ReportReply(preview.ToString());
                    }

                    return null;

                case AgentEventKind.Message:
                    _previews.Remove(agentEvent.Id!);
                    _reply.Append(agentEvent.Text);
                    ReportReply(string.Empty);
                    return null;

                case AgentEventKind.CustomToolUse:
                    await AnswerToolAsync(agentEvent, cancellationToken);
                    return null;

                case AgentEventKind.ModelRequestEnd:
                    _modelRequests++;
                    var usage = agentEvent.Usage;
                    await owner._runner.RecordUsageAsync(
                        purpose,
                        model,
                        usage?.InputTokens ?? 0,
                        usage?.OutputTokens ?? 0,
                        usage?.CacheCreationInputTokens ?? 0,
                        usage?.CacheReadInputTokens ?? 0,
                        succeeded: !agentEvent.IsError);
                    owner._logger.LogInformation(
                        "Claude を呼び出しました（Managed Agents）: {Purpose} {Model} 入力 {InputTokens} 出力 {OutputTokens} キャッシュ書き込み {CacheCreation} キャッシュ読み込み {CacheRead}",
                        purpose,
                        model,
                        usage?.InputTokens ?? 0,
                        usage?.OutputTokens ?? 0,
                        usage?.CacheCreationInputTokens ?? 0,
                        usage?.CacheReadInputTokens ?? 0);

                    if (_modelRequests >= owner._options.ManagedAgents.MaxModelRequests)
                    {
                        // 上限：途中の文章は、返答として残さない（質問への返答と取り違えないように）
                        owner._logger.LogWarning("献立づくりのモデルの呼び出しが上限に達しました: {Max}", owner._options.ManagedAgents.MaxModelRequests);
                        await owner._cleaner.InterruptAsync(sessionId);
                        var partialReply = tools.IsAccepted && isChat ? await ReplyFromHistoryAsync(cancellationToken) : string.Empty;
                        return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(tools.IsAccepted, _modelRequests, partialReply));
                    }

                    if (await owner._runner.IsDailyLimitReachedAsync())
                    {
                        await owner._cleaner.InterruptAsync(sessionId);
                        return Result<MenuAgentRunResult>.Error("今日は AI を使えません（1日の上限に達しました）。手で編集してください。");
                    }

                    return null;

                case AgentEventKind.Error:
                    // 一時的なエラー（混雑など）は Anthropic 側で再試行される。終わったかどうかは idle の stop_reason で決める
                    owner._logger.LogWarning("献立づくりのセッションでエラーが起きました: {SessionId} {Error}", sessionId, agentEvent.Text);
                    return null;

                case AgentEventKind.Terminated:
                    owner._logger.LogWarning("献立づくりのセッションが終わってしまいました: {SessionId}", sessionId);
                    return Result<MenuAgentRunResult>.Error("AI の処理が途中で終わりました。もう一度試してください。");

                case AgentEventKind.Idle:
                    return await OnIdleAsync(agentEvent.StopReason, cancellationToken);

                default:
                    return null;
            }
        }

        /// <summary>道具を実行して結果を返す</summary>
        private async Task AnswerToolAsync(AgentEvent toolUse, CancellationToken cancellationToken)
        {
            // 道具を使う前の文章は前置きなので捨てる（返答として残すのは、最後の道具のあとの文章だけ）
            _reply.Clear();
            ReportReply(string.Empty);

            string content;
            bool isError;
            if (tools.IsAccepted)
            {
                // 受け付けたあとの道具は実行しない（案はもう受け付けている）
                content = isChat
                    ? "献立案はもう受け付けています。道具は使わずに、何を変えたかを文章で書いてください。"
                    : "献立案はもう受け付けています。これで終わりです。";
                isError = true;
            }
            else
            {
                if (isChat)
                {
                    progress.Report(MenuAgentToolDispatcher.StageFor(toolUse.Name!));
                }

                _hasSubmitted |= toolUse.Name == "submit_menu";
                (content, isError) = MenuAgentToolDispatcher.Dispatch(toolUse.Name!, toolUse.Input ?? new Dictionary<string, JsonElement>(), tools, owner._logger);
                if (tools.IsAccepted && isChat)
                {
                    // チャット：受け付けたら、何を変えたかを職員向けに書かせる
                    progress.Report("変えたところをまとめています");
                    content += Environment.NewLine + "続けて、職員に向けて、何を変えたか・満たせなかった条件とその理由を、Markdown で短く書いてください。道具はもう使わないでください。";
                }
            }

            await owner._client.Beta.Sessions.Events.Send(
                sessionId,
                new EventSendParams
                {
                    Events =
                    [
                        new BetaManagedAgentsUserCustomToolResultEventParams
                        {
                            CustomToolUseID = toolUse.Id!,
                            Content = [new BetaManagedAgentsTextBlock { Text = content }],
                            IsError = isError,
                        },
                    ],
                },
                cancellationToken);
        }

        /// <summary>セッションが止まったとき：終わりか、続きを促すかを決める</summary>
        private async Task<Result<MenuAgentRunResult>?> OnIdleAsync(string? stopReason, CancellationToken cancellationToken)
        {
            switch (stopReason)
            {
                case "requires_action":
                    // 道具の結果を待っている（返したものは届いている途中）
                    return null;
                case "end_turn":
                    break;
                default:
                    owner._logger.LogWarning("献立づくりのセッションが止まりました: {SessionId} {StopReason}", sessionId, stopReason);
                    return Result<MenuAgentRunResult>.Error("AI の処理に失敗しました。少し待ってから、もう一度試してください。");
            }

            // 残す返答は、流れからではなくイベント一覧から作り直す（つなぎ直したあとは、流れのイベントの順番が入れ替わることがあるため）
            var reply = await ReplyFromHistoryAsync(cancellationToken);
            if (tools.IsAccepted)
            {
                return Ok(true, isChat ? reply : string.Empty);
            }

            if (isChat && _hasSubmitted)
            {
                // 却下された案のまま文章で終えた：1回だけ出し直しを促す。それでも出さなければ、Claude の文章は残さない
                // （却下された案の扱いと説明はアプリが決める。Claude の文章と実際の献立が食い違わないように）
                if (_hasAskedResubmit)
                {
                    return Ok(false, string.Empty);
                }

                _hasAskedResubmit = true;
                await SendMessageAsync("出した献立案は受け付けられていません。問題を直した案を submit_menu で出し直してください。", cancellationToken);
                return null;
            }

            if (isChat && reply.Trim().Length > 0)
            {
                // 道具を使わずに文章で終わった ＝ 質問への返答（submit_menu を出していないので、献立は変えない）
                return Ok(false, reply);
            }

            // 道具を使わずに終わった：submit_menu で出すよう促す（2回まで）
            if (_nudges >= 2)
            {
                return Ok(false, string.Empty);
            }

            _nudges++;
            await SendMessageAsync(
                isChat ? "質問なら文章で答え、献立を直す依頼なら submit_menu の道具で出してください。" : "献立案は、必ず submit_menu の道具で出してください。",
                cancellationToken);
            return null;
        }

        /// <summary>
        /// 返答の文章：イベント一覧で、最後の道具の呼び出し・最後の依頼（user.message）より後ろに書かれた文章だけ。
        /// 道具を使う前の前置きや、却下された案の前の文章は入れない
        /// </summary>
        private async Task<string> ReplyFromHistoryAsync(CancellationToken cancellationToken)
        {
            var reply = new StringBuilder();
            await foreach (var listed in owner.ListEventsAsync(sessionId, cancellationToken))
            {
                switch (listed.Kind)
                {
                    case AgentEventKind.UserMessage:
                    case AgentEventKind.CustomToolUse:
                        reply.Clear();
                        break;
                    case AgentEventKind.Message:
                        reply.Append(listed.Text);
                        break;
                }
            }

            return reply.ToString();
        }

        private Result<MenuAgentRunResult> Ok(bool accepted, string reply) =>
            Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(accepted, _modelRequests, reply, sessionId));

        /// <summary>チャットの返答を画面に出す（書き終えた分＋書いている途中の分）</summary>
        private void ReportReply(string preview)
        {
            if (isChat)
            {
                progress.ReportText(_reply + preview);
            }
        }

        private async Task SendMessageAsync(string text, CancellationToken cancellationToken)
        {
            if (await owner._runner.IsDailyLimitReachedAsync())
            {
                throw new DailyLimitReachedException();
            }

            await owner._client.Beta.Sessions.Events.Send(
                sessionId,
                new EventSendParams
                {
                    Events = [new BetaManagedAgentsUserMessageEventParams { Content = [new BetaManagedAgentsTextBlock { Text = text }] }],
                },
                cancellationToken);
        }

        /// <summary>流れ（SSE）を読んで、キューに入れる（切れたら終わる。つなぎ直しは呼び出し側）</summary>
        private Task ReadStreamAsync(ChannelWriter<AgentEvent> writer, CancellationToken cancellationToken) => Task.Run(
            async () =>
            {
                try
                {
                    var stream = owner._client.Beta.Sessions.Events.StreamStreaming(
                        sessionId,
                        new EventStreamParams { EventDeltas = isChat ? ["agent.message"] : null },
                        cancellationToken);
                    await foreach (var streamEvent in stream.WithCancellation(cancellationToken))
                    {
                        await writer.WriteAsync(FromStream(streamEvent), cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or IOException)
                {
                    owner._logger.LogInformation("イベントの流れが切れました: {SessionId} {Error}", sessionId, ex.GetType().Name);
                }
            },
            cancellationToken);

        /// <summary>イベント一覧から、まだ処理していないものをキューに入れる（流れの取りこぼしを拾う）</summary>
        private async Task PollAsync(ChannelWriter<AgentEvent> writer, CancellationToken cancellationToken)
        {
            await foreach (var listed in owner.ListEventsAsync(sessionId, cancellationToken))
            {
                if (listed.Id is not null && !_seen.Contains(listed.Id))
                {
                    await writer.WriteAsync(listed, cancellationToken);
                }
            }
        }
    }

    /// <summary>1日の上限に達した（ターンの途中で気づいたとき）</summary>
    private sealed class DailyLimitReachedException : Exception;

    /// <summary>セッションのイベントを古い順に読む</summary>
    private async IAsyncEnumerable<AgentEvent> ListEventsAsync(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = await _client.Beta.Sessions.Events.List(sessionId, new EventListParams { Order = "asc" }, cancellationToken);
        await foreach (var listed in page.Paginate(cancellationToken))
        {
            yield return FromList(listed);
        }
    }

    private static AgentEvent FromStream(BetaManagedAgentsStreamSessionEvents e)
    {
        if (e.TryPickAgentMessageEvent(out var message))
        {
            return new AgentEvent(AgentEventKind.Message, message.ID, Text: TextOf(message.Content));
        }

        if (e.TryPickStartEvent(out var start))
        {
            return start.Event.Type.Raw() == "agent.message"
                ? new AgentEvent(AgentEventKind.MessagePreviewStart, start.Event.ID)
                : new AgentEvent(AgentEventKind.Other, null);
        }

        if (e.TryPickDeltaEvent(out var delta))
        {
            return new AgentEvent(AgentEventKind.MessagePreviewDelta, delta.EventID, Text: delta.Delta.Content.Text);
        }

        if (e.TryPickAgentCustomToolUseEvent(out var toolUse))
        {
            return new AgentEvent(AgentEventKind.CustomToolUse, toolUse.ID, toolUse.Name, toolUse.Input);
        }

        if (e.TryPickSpanModelRequestEndEvent(out var end))
        {
            return new AgentEvent(AgentEventKind.ModelRequestEnd, end.ID, Usage: end.ModelUsage, IsError: end.IsError ?? false);
        }

        if (e.TryPickSessionStatusIdleEvent(out var idle))
        {
            return new AgentEvent(AgentEventKind.Idle, idle.ID, StopReason: idle.StopReason.Type.Raw());
        }

        if (e.TryPickSessionStatusTerminatedEvent(out var terminated))
        {
            return new AgentEvent(AgentEventKind.Terminated, terminated.ID);
        }

        if (e.TryPickSessionErrorEvent(out var error))
        {
            return new AgentEvent(AgentEventKind.Error, error.ID, Text: error.Error.Json.GetRawText());
        }

        return new AgentEvent(AgentEventKind.Other, null);
    }

    private static AgentEvent FromList(BetaManagedAgentsSessionEvent e)
    {
        if (e.TryPickUserMessage(out var userMessage))
        {
            return new AgentEvent(AgentEventKind.UserMessage, userMessage.ID);
        }

        if (e.TryPickAgentMessage(out var message))
        {
            return new AgentEvent(AgentEventKind.Message, message.ID, Text: TextOf(message.Content));
        }

        if (e.TryPickAgentCustomToolUse(out var toolUse))
        {
            return new AgentEvent(AgentEventKind.CustomToolUse, toolUse.ID, toolUse.Name, toolUse.Input);
        }

        if (e.TryPickSpanModelRequestEnd(out var end))
        {
            return new AgentEvent(AgentEventKind.ModelRequestEnd, end.ID, Usage: end.ModelUsage, IsError: end.IsError ?? false);
        }

        if (e.TryPickStatusIdle(out var idle))
        {
            return new AgentEvent(AgentEventKind.Idle, idle.ID, StopReason: idle.StopReason.Type.Raw());
        }

        if (e.TryPickStatusTerminated(out var terminated))
        {
            return new AgentEvent(AgentEventKind.Terminated, terminated.ID);
        }

        if (e.TryPickError(out var error))
        {
            return new AgentEvent(AgentEventKind.Error, error.ID, Text: error.Error.Json.GetRawText());
        }

        return new AgentEvent(AgentEventKind.Other, null);
    }

    private static string TextOf(IReadOnlyList<BetaManagedAgentsAgentMessageEventContent> content) =>
        string.Concat(content.Select(c => c.TryPickBetaManagedAgentsTextBlock(out var text) ? text.Text : string.Empty));
}
