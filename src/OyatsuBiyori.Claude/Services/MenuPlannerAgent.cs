using System.Text;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Claude.Prompts;

namespace OyatsuBiyori.Claude.Services;

/// <summary>
/// 献立づくりのエージェントの Messages API 版（Sonnet。T4-04・T4-08、D-82・U-56。D-94 で Managed Agents 版と設定で切り替える）。手書きのループ（10回まで。tool_choice は auto・道具は strict）で、
/// Biz の計算を道具として使わせ、submit_menu が Biz の検証を通ったらループを止める。道具の判断ロジックは Biz（IMenuAgentTools）に置き、
/// ここには道具の定義・ループ・振り分けだけを置く（NFR-MNT-05）。ループの1回ごとに記録し、1日の金額の上限に数える（ClaudeRequestRunner。D-95）。
/// </summary>
public sealed class MenuPlannerAgent(ClaudeRequestRunner runner, ILogger<MenuPlannerAgent> logger) : IMenuPlannerAgent
{
    /// <summary>セッションを続けない（毎回、条件と履歴を最初のメッセージで渡す）ので空</summary>
    public string SessionVersion => string.Empty;

    /// <summary>ループの上限</summary>
    public const int MaxIterations = 10;

    private readonly ClaudeRequestRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly ILogger<MenuPlannerAgent> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(progress);

        var isChat = request.Mode == MenuAgentMode.Chat;
        var purpose = isChat ? ClaudePurpose.MenuChat : ClaudePurpose.MenuPlanning;
        var messages = new List<BetaMessageParam> { new BetaUserMessageParam { Content = MenuAgentToolDispatcher.FirstMessage(request, includeHistory: true) } };
        var toolDefinitions = ToolDefinitions();

        // チャットの返答（質問への返答か、何を変えたかの説明）。届いた順に報告して、画面にパラパラ出す
        var reply = new StringBuilder();

        // チャットで submit_menu を受け付けたあと、何を変えたかを書かせる1回（道具は使わせない。ループの上限とは別に1回だけ）
        var isExplaining = false;

        // submit_menu を出したか（却下されたあとに文章で終わっても、質問への返答として扱わない）
        var hasSubmitted = false;
        var hasAskedResubmit = false;

        for (var iteration = 1; iteration <= MaxIterations || isExplaining; iteration++)
        {
            var parameters = ClaudeRequestRunner.WithFallback(new MessageCreateParams
            {
                Model = _runner.ModelFor(purpose),
                MaxTokens = _runner.MaxTokens,
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = ClaudeRequestRunner.SystemPromptWith(PromptStore.Get("MenuPlanner")), CacheControl = new BetaCacheControlEphemeral() },
                },
                Tools = toolDefinitions,
                ToolChoice = isExplaining ? new BetaToolChoiceNone() : new BetaToolChoiceAuto(),
                Messages = messages,
                OutputConfig = new BetaOutputConfig { Effort = _runner.EffortFor(purpose) },
            });

            Result<BetaMessage> sent;
            if (isChat)
            {
                // 残すのは最後の1回の文章だけ（道具を使う前の前置き・却下された回の文章は、次の回で捨てる）。再試行のときも捨てて受け取り直す
                sent = await _runner.SendStreamingAsync(
                    purpose,
                    parameters,
                    () =>
                    {
                        reply.Clear();
                        progress.ReportText(string.Empty);
                    },
                    delta =>
                    {
                        reply.Append(delta);
                        progress.ReportText(reply.ToString());
                    },
                    cancellationToken);
            }
            else
            {
                sent = await _runner.SendAsync(purpose, parameters, cancellationToken);
            }

            var stopReason = sent.HasError ? null : sent.Value!.StopReason?.Raw();
            if (isExplaining && (sent.HasError || stopReason is "refusal" or "max_tokens"))
            {
                // 説明を書けなかったときは、submit_menu の説明を使う（案はもう受け付けている）
                _logger.LogWarning("チャットの説明を書けませんでした: {StopReason}", stopReason);
                return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(true, iteration));
            }

            if (sent.HasError)
            {
                return Result<MenuAgentRunResult>.Error(sent.ErrorMessages[0]);
            }

            var response = sent.Value!;
            if (stopReason is "refusal")
            {
                return Result<MenuAgentRunResult>.Error(isChat
                    ? "AI がこの内容に答えられませんでした。書き方を変えて、もう一度試してください。"
                    : "AI がこの内容に答えられませんでした。要望の文章を見直すか、「自分で組む」で手で作ってください。");
            }

            if (stopReason is "max_tokens")
            {
                return Result<MenuAgentRunResult>.Error("AI の答えが長すぎて途中で切れました。もう一度試してください。");
            }

            var assistant = new List<BetaContentBlockParam>();
            var results = new List<BetaContentBlockParam>();
            foreach (var block in response.Content)
            {
                if (block.TryPickText(out var text))
                {
                    assistant.Add(new BetaTextBlockParam { Text = text.Text });
                }
                else if (block.TryPickThinking(out var thinking))
                {
                    assistant.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                }
                else if (block.TryPickRedactedThinking(out var redacted))
                {
                    assistant.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out var toolUse) && !isExplaining)
                {
                    // 説明を書かせる回（道具なし）では、万一道具の呼び出しが来ても実行しない（案はもう受け付けている）
                    assistant.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                    if (isChat)
                    {
                        progress.Report(MenuAgentToolDispatcher.StageFor(toolUse.Name));
                    }

                    hasSubmitted |= toolUse.Name == "submit_menu";

                    var (content, isError) = MenuAgentToolDispatcher.Dispatch(toolUse.Name, toolUse.Input, tools, _logger);
                    results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = content, IsError = isError });
                }
            }

            if (assistant.Count > 0)
            {
                messages.Add(new BetaAssistantMessageParam { Content = assistant });
            }

            if (isExplaining)
            {
                return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(true, iteration, reply.ToString()));
            }

            if (tools.IsAccepted)
            {
                if (!isChat)
                {
                    return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(true, iteration));
                }

                // チャット：受け付けたら、何を変えたかを職員向けに書かせる（もう道具は使わせない）
                isExplaining = true;
                progress.Report("変えたところをまとめています");
                results.Add(new BetaTextBlockParam { Text = "献立案を受け付けました。職員に向けて、何を変えたか・満たせなかった条件とその理由を、Markdown で短く書いてください。" });
                messages.Add(new BetaUserMessageParam { Content = results });
                continue;
            }

            if (results.Count > 0)
            {
                messages.Add(new BetaUserMessageParam { Content = results });
            }
            else if (isChat && hasSubmitted)
            {
                // 却下された案のまま文章で終えた：1回だけ出し直しを促す。それでも出さなければ、Claude の文章は残さない
                // （却下された案の扱いと説明はアプリが決める。Claude の文章と実際の献立が食い違わないように）
                if (hasAskedResubmit)
                {
                    return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(false, iteration));
                }

                hasAskedResubmit = true;
                messages.Add(new BetaUserMessageParam { Content = "出した献立案は受け付けられていません。問題を直した案を submit_menu で出し直してください。" });
            }
            else if (isChat && reply.ToString().Trim().Length > 0)
            {
                // 道具を使わずに文章で終わった ＝ 質問への返答（submit_menu を出していないので、献立は変えない）
                return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(false, iteration, reply.ToString()));
            }
            else
            {
                // 道具を使わずに終わったときは、submit_menu で出すよう促す
                messages.Add(new BetaUserMessageParam
                {
                    Content = isChat
                        ? "質問なら文章で答え、献立を直す依頼なら submit_menu の道具で出してください。"
                        : "献立案は、必ず submit_menu の道具で出してください。",
                });
            }
        }

        // 上限に達したときの途中の文章は、返答として残さない（質問への返答と取り違えないように）
        _logger.LogWarning("献立づくりのループが上限に達しました: {MaxIterations}", MaxIterations);
        return Result<MenuAgentRunResult>.Ok(new MenuAgentRunResult(false, MaxIterations));
    }

    private static List<BetaToolUnion> ToolDefinitions() =>
        MenuAgentToolDispatcher.Tools
            .Select(tool => (BetaToolUnion)new BetaTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = InputSchema.FromRawUnchecked(MenuAgentToolDispatcher.SchemaFor(tool)),
                Strict = tool.Strict,
            })
            .ToList();
}
