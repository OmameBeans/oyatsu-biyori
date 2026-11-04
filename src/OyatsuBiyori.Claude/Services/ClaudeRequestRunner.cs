using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Claude.Prompts;

namespace OyatsuBiyori.Claude.Services;

/// <summary>
/// Claude を1回呼んで、構造化出力（JSON スキーマ）の結果を受け取る共通の処理。
/// - 1日の呼び出し回数の上限を確かめる（失敗・再試行も含めて数える。D-65）
/// - SDK の自動の再試行は切り、ここで再試行する（1回ごとに api_usage_logs に記録する）
/// - refusal・max_tokens・形の崩れた JSON は Result のエラーにする（画面から再試行できるように）
/// - 本文・画像はログに出さない
/// </summary>
public sealed class ClaudeRequestRunner(
    AnthropicClient client,
    IOptions<ClaudeOptions> options,
    IApiUsageLogRepository usageLogs,
    TimeProvider timeProvider,
    ILogger<ClaudeRequestRunner> logger)
{
    /// <summary>サーバー側のフォールバック（拒否されたときに別のモデルで答え直す）のベータ</summary>
    private const string ServerSideFallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ClaudeOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly IApiUsageLogRepository _usageLogs = usageLogs ?? throw new ArgumentNullException(nameof(usageLogs));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<ClaudeRequestRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Claude を呼んで、T の形の結果を受け取る。
    /// </summary>
    /// <param name="purpose">用途（モデル・タイムアウト・記録に使う）</param>
    /// <param name="systemPrompt">用途ごとのプロンプト（共通の前置きの後ろに付ける）</param>
    /// <param name="content">職員のメッセージの中身（画像・データのテキスト）</param>
    /// <param name="cancellationToken">処理全体の期限</param>
    public async Task<Result<T>> RunAsync<T>(
        ClaudePurpose purpose,
        string systemPrompt,
        IReadOnlyList<BetaContentBlockParam> content,
        CancellationToken cancellationToken)
    {
        var model = _options.ModelFor(purpose);
        var request = BuildRequest<T>(model, systemPrompt, content, EffortFor(purpose));
        var response = await SendAsync(purpose, request, cancellationToken);
        if (response.HasError)
        {
            var error = new Result<T>();
            error.ErrorMessages.AddRange(response.ErrorMessages);
            return error;
        }

        return Parse<T>(response.Value!);
    }

    /// <summary>用途のモデル名</summary>
    public string ModelFor(ClaudePurpose purpose) => _options.ModelFor(purpose);

    /// <summary>1回の応答の最大トークン数</summary>
    public int MaxTokens => _options.MaxTokens;

    /// <summary>用途の考える深さ（設定がなければ null）</summary>
    public Effort? EffortFor(ClaudePurpose purpose) => _options.EffortFor(purpose) switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        _ => null,
    };

    /// <summary>
    /// 組み立てたリクエストを送る（エージェントのループで1回ごとに呼ぶ）。1日の上限・再試行・記録はここで行う。
    /// 応答の中身（stop_reason など）は呼び出し側で確かめる。
    /// </summary>
    public async Task<Result<BetaMessage>> SendAsync(ClaudePurpose purpose, MessageCreateParams request, CancellationToken cancellationToken)
    {
        var model = request.Model.Raw();
        var timeout = _options.TimeoutFor(purpose);

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (await IsDailyLimitReachedAsync())
            {
                return Result<BetaMessage>.Error("今日は AI を使えません（1日の上限に達しました）。手で編集してください。");
            }

            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(timeout);

            BetaMessage response;
            try
            {
                response = await _client.Beta.Messages.Create(request, attemptCancellation.Token);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                await RecordAsync(purpose, model, null, succeeded: false);
                _logger.LogWarning("Claude の呼び出しに失敗しました（再試行します）: {Purpose} {Attempt} {Error}", purpose, attempt + 1, ex.GetType().Name);
                if (cancellationToken.IsCancellationRequested || attempt == _options.MaxRetries)
                {
                    return Result<BetaMessage>.Error("AI の処理に失敗しました。少し待ってから、もう一度試してください。");
                }

                continue;
            }
            catch (AnthropicApiException ex)
            {
                await RecordAsync(purpose, model, null, succeeded: false);
                _logger.LogError(ex, "Claude の呼び出しでエラーになりました: {Purpose}", purpose);
                return Result<BetaMessage>.Error("AI の処理でエラーになりました。もう一度試しても直らない場合は、手で編集してください。");
            }

            var stopReason = response.StopReason?.Raw();
            await RecordAsync(purpose, model, response.Usage, succeeded: stopReason is "end_turn" or "tool_use");
            _logger.LogInformation(
                "Claude を呼び出しました: {Purpose} {Model} 入力 {InputTokens} 出力 {OutputTokens} キャッシュ書き込み {CacheCreation} キャッシュ読み込み {CacheRead} 終了理由 {StopReason}",
                purpose,
                model,
                response.Usage.InputTokens,
                response.Usage.OutputTokens,
                response.Usage.CacheCreationInputTokens ?? 0,
                response.Usage.CacheReadInputTokens ?? 0,
                stopReason);
            return Result<BetaMessage>.Ok(response);
        }

        return Result<BetaMessage>.Error("AI の処理に失敗しました。少し待ってから、もう一度試してください。");
    }

    /// <summary>共通の前置きを付けたシステムプロンプト</summary>
    public static string SystemPromptWith(string systemPrompt) => PromptStore.Common + Environment.NewLine + Environment.NewLine + systemPrompt;

    /// <summary>拒否されたときに、サーバー側で別のモデルが答え直すようにする（Haiku にはない）</summary>
    public static MessageCreateParams WithFallback(MessageCreateParams request)
    {
        var model = request.Model.Raw() ?? string.Empty;
        if (model.StartsWith("claude-haiku", StringComparison.Ordinal))
        {
            return request;
        }

        return request with
        {
            Betas = [ServerSideFallbackBeta],
            Fallbacks = new Default(),
        };
    }

    /// <summary>リクエストを作る</summary>
    private MessageCreateParams BuildRequest<T>(string model, string systemPrompt, IReadOnlyList<BetaContentBlockParam> content, Effort? effort)
    {
        var request = new MessageCreateParams
        {
            Model = model,
            MaxTokens = _options.MaxTokens,
            System = new List<BetaTextBlockParam>
            {
                new() { Text = PromptStore.Common + "\n\n" + systemPrompt },
            },
            Messages = [new BetaUserMessageParam { Content = content.ToList() }],
            OutputConfig = new BetaOutputConfig
            {
                Format = new BetaJsonOutputFormat { Schema = JsonSchemas.For<T>() },
                Effort = effort,
            },
        };

        // 拒否されたときは、サーバー側で別のモデルが答え直す（Haiku にはサーバー側のフォールバックがない）
        if (!model.StartsWith("claude-haiku", StringComparison.Ordinal))
        {
            request = request with
            {
                Betas = [ServerSideFallbackBeta],
                Fallbacks = new Default(),
            };
        }

        return request;
    }

    /// <summary>応答を T の形に読む。拒否・途中で切れた・JSON の形が違う場合はエラー</summary>
    private Result<T> Parse<T>(BetaMessage response)
    {
        // 最後まで答え終わった（end_turn）とき以外は、JSON として読まずにエラーにする
        var stopReason = response.StopReason?.Raw();
        switch (stopReason)
        {
            case "end_turn":
                break;
            case "refusal":
                _logger.LogWarning("Claude が答えませんでした（refusal）");
                return Result<T>.Error("AI がこの内容に答えられませんでした。内容を見直すか、手で編集してください。");
            case "max_tokens":
                return Result<T>.Error("AI の答えが長すぎて途中で切れました。取り込む枚数を減らして、もう一度試してください。");
            default:
                _logger.LogWarning("Claude の答えが途中で終わりました: {StopReason}", stopReason);
                return Result<T>.Error("AI の答えが途中で終わりました。もう一度試してください。");
        }

        var text = string.Concat(response.Content.Select(block => block.TryPickText(out var t) ? t.Text : string.Empty));
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, JsonSchemas.SerializerOptions);
            return value is null
                ? Result<T>.Error("AI の答えを読み取れませんでした。もう一度試してください。")
                : Result<T>.Ok(value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Claude の答えが決めた形ではありませんでした");
            return Result<T>.Error("AI の答えを読み取れませんでした。もう一度試してください。");
        }
    }

    /// <summary>再試行してよい失敗か（タイムアウト・429・5xx・通信のエラー）</summary>
    private static bool IsRetryable(Exception ex, CancellationToken overall)
    {
        if (overall.IsCancellationRequested)
        {
            return ex is OperationCanceledException;
        }

        return ex is OperationCanceledException
            or AnthropicRateLimitException
            or Anthropic5xxException
            or HttpRequestException;
    }

    /// <summary>今日（日本時間）の呼び出し回数が上限に達したか</summary>
    private async Task<bool> IsDailyLimitReachedAsync()
    {
        var todayStart = JapanDate.StartOfDayUtc(JapanDate.Today(_timeProvider));
        return await _usageLogs.CountSinceAsync(todayStart) >= _options.DailyCallLimit;
    }

    /// <summary>呼び出しを記録する（本文・画像は記録しない）</summary>
    private async Task RecordAsync(ClaudePurpose purpose, string model, BetaUsage? usage, bool succeeded)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await _usageLogs.AddAsync(new ApiUsageLog
        {
            CalledAt = now,
            Purpose = purpose,
            Model = model,
            InputTokens = usage?.InputTokens ?? 0,
            OutputTokens = usage?.OutputTokens ?? 0,
            CacheCreationInputTokens = usage?.CacheCreationInputTokens ?? 0,
            CacheReadInputTokens = usage?.CacheReadInputTokens ?? 0,
            Succeeded = succeeded,
            UpdateDateTime = now,
        });
    }
}
