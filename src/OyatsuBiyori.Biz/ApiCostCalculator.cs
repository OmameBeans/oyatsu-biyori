using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// モデルの単価（ドル。100万トークンあたり。NFR-COST-05・D-95）
/// </summary>
/// <param name="Model">モデル名（記録のモデル名と完全に一致するか、記録のモデル名がこれで始まるものに当てる）</param>
/// <param name="InputUsdPerMTok">入力</param>
/// <param name="OutputUsdPerMTok">出力</param>
/// <param name="CacheWriteUsdPerMTok">キャッシュの書き込み</param>
/// <param name="CacheReadUsdPerMTok">キャッシュの読み込み</param>
public sealed record ApiModelPrice(
    string Model,
    decimal InputUsdPerMTok,
    decimal OutputUsdPerMTok,
    decimal CacheWriteUsdPerMTok,
    decimal CacheReadUsdPerMTok);

/// <summary>
/// Claude の呼び出しの金額（ドル）を、記録したトークン数とモデルごとの単価から計算する（NFR-COST-05・D-95）。
/// 単価の表にないモデルは、表の中でいちばん高い単価で数える（上限で止める側に倒す）。
/// </summary>
public static class ApiCostCalculator
{
    private const decimal TokensPerUnit = 1_000_000m;

    /// <summary>1回の呼び出しの金額（ドル）</summary>
    public static decimal CostUsd(ApiUsageLog log, IReadOnlyList<ApiModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(prices);

        var price = PriceFor(log.Model, prices);
        return (log.InputTokens * price.InputUsdPerMTok
            + log.OutputTokens * price.OutputUsdPerMTok
            + log.CacheCreationInputTokens * price.CacheWriteUsdPerMTok
            + log.CacheReadInputTokens * price.CacheReadUsdPerMTok) / TokensPerUnit;
    }

    /// <summary>呼び出しの金額の合計（ドル）</summary>
    public static decimal TotalUsd(IEnumerable<ApiUsageLog> logs, IReadOnlyList<ApiModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(logs);
        return logs.Sum(log => CostUsd(log, prices));
    }

    /// <summary>
    /// 1日の上限に達したか（使った金額が上限以上なら達した）。上限が0以下なら、金額では止めない
    /// </summary>
    public static bool IsLimitReached(IEnumerable<ApiUsageLog> todayLogs, IReadOnlyList<ApiModelPrice> prices, decimal dailyLimitUsd)
    {
        if (dailyLimitUsd <= 0)
        {
            return false;
        }

        return TotalUsd(todayLogs, prices) >= dailyLimitUsd;
    }

    /// <summary>モデルの単価（完全一致 → 前方一致 → 表の中でいちばん高い単価）</summary>
    public static ApiModelPrice PriceFor(string model, IReadOnlyList<ApiModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        var exact = prices.FirstOrDefault(p => string.Equals(p.Model, model, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        var prefix = prices
            .Where(p => p.Model.Length > 0 && (model ?? string.Empty).StartsWith(p.Model, StringComparison.Ordinal))
            .OrderByDescending(p => p.Model.Length)
            .FirstOrDefault();
        if (prefix is not null)
        {
            return prefix;
        }

        // 表にないモデル：いちばん高い単価で数える（表が空なら、どのモデルにも当てはまる高い単価がないので止める側にする）
        if (prices.Count == 0)
        {
            return UnknownPrice;
        }

        return new ApiModelPrice(
            model ?? string.Empty,
            prices.Max(p => p.InputUsdPerMTok),
            prices.Max(p => p.OutputUsdPerMTok),
            prices.Max(p => p.CacheWriteUsdPerMTok),
            prices.Max(p => p.CacheReadUsdPerMTok));
    }

    /// <summary>単価の表がないときの単価（いちばん高いモデルの単価を目安にした、高めの値）</summary>
    private static readonly ApiModelPrice UnknownPrice = new(string.Empty, 10m, 50m, 12.5m, 1m);
}
