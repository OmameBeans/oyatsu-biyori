using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Sessions;
using Anthropic.Models.Beta.Sessions.Events;
using Microsoft.Extensions.Logging;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Claude.Services;

/// <summary>
/// 使い終わった献立づくりのセッションを、Anthropic 側から消す（D-94）。動いている途中なら中断を送ってから消す。
/// 失敗しても例外にせず、ログだけ残す（献立の保存・削除は止めない）。
/// </summary>
public sealed class MenuAgentSessionCleaner(AnthropicClient client, ILogger<MenuAgentSessionCleaner> logger) : IMenuAgentSessionCleaner
{
    /// <summary>消すまでに試す回数（中断のあと、止まるまで少しかかることがある）</summary>
    private const int MaxAttempts = 3;

    /// <summary>試し直すまでの間</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>1回の呼び出しの期限</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    private readonly AnthropicClient _client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly ILogger<MenuAgentSessionCleaner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task ForgetAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await InterruptAsync(sessionId);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(CallTimeout);
                await _client.Beta.Sessions.Delete(sessionId, new SessionDeleteParams(), timeout.Token);
                _logger.LogInformation("献立づくりのセッションを消しました: {SessionId}", sessionId);
                return;
            }
            catch (AnthropicNotFoundException)
            {
                // もうない
                return;
            }
            catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    _logger.LogWarning(ex, "献立づくりのセッションを消せませんでした: {SessionId}", sessionId);
                    return;
                }

                await Task.Delay(RetryDelay);
            }
        }
    }

    /// <summary>動いている途中なら止める（止まっていれば何も起きない。失敗は無視する）</summary>
    public async Task InterruptAsync(string sessionId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(CallTimeout);
            await _client.Beta.Sessions.Events.Send(
                sessionId,
                new EventSendParams { Events = [new BetaManagedAgentsUserInterruptEventParams()] },
                timeout.Token);
        }
        catch (Exception ex) when (ex is AnthropicApiException or HttpRequestException or OperationCanceledException)
        {
            _logger.LogInformation("セッションに中断を送れませんでした: {SessionId} {Error}", sessionId, ex.GetType().Name);
        }
    }
}
