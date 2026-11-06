using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// Claude の呼び出しの金額（ドル）と、1日の金額の上限（NFR-COST-05・D-95）を確かめる。
/// </summary>
public class ApiCostCalculatorTests
{
    private static readonly IReadOnlyList<ApiModelPrice> Prices =
    [
        new("claude-sonnet-5-5", 2m, 10m, 2.5m, 0.2m),
        new("claude-haiku-5-5", 0.1m, 0.5m, 0.125m, 0.01m),
        new("claude-opus-5-5", 4m, 20m, 5m, 0.2m),
    ];

    private static ApiUsageLog Log(string model, long input = 0, long output = 0, long cacheWrite = 0, long cacheRead = 0) => new()
    {
        Model = model,
        InputTokens = input,
        OutputTokens = output,
        CacheCreationInputTokens = cacheWrite,
        CacheReadInputTokens = cacheRead,
    };

    [Fact]
    public void D_95_入力_出力_キャッシュの書き込みと読み込みを_それぞれの単価で計算する()
    {
        // 入力 100万 × 2 + 出力 50万 × 10 + 書き込み 20万 × 2.5 + 読み込み 100万 × 0.2 = 2 + 5 + 0.5 + 0.2
        var cost = ApiCostCalculator.CostUsd(Log("claude-sonnet-5-5", 1_000_000, 500_000, 200_000, 1_000_000), Prices);

        Assert.Equal(7.7m, cost);
    }

    [Fact]
    public void D_95_モデルごとの単価を使う()
    {
        Assert.Equal(0.1m, ApiCostCalculator.CostUsd(Log("claude-haiku-5-5", input: 1_000_000), Prices));
        Assert.Equal(4m, ApiCostCalculator.CostUsd(Log("claude-opus-5-5", input: 1_000_000), Prices));
    }

    [Fact]
    public void D_95_日付のついたモデル名は_前方一致で単価を当てる()
    {
        Assert.Equal(2m, ApiCostCalculator.CostUsd(Log("claude-sonnet-5-5-20261101", input: 1_000_000), Prices));
    }

    [Fact]
    public void D_95_表にないモデルは_いちばん高い単価で数える()
    {
        var price = ApiCostCalculator.PriceFor("claude-unknown-9", Prices);

        Assert.Equal(4m, price.InputUsdPerMTok);
        Assert.Equal(20m, price.OutputUsdPerMTok);
        Assert.Equal(5m, price.CacheWriteUsdPerMTok);
        Assert.Equal(0.2m, price.CacheReadUsdPerMTok);
    }

    [Fact]
    public void D_95_単価の表が空なら_高めの単価で数える()
    {
        var cost = ApiCostCalculator.CostUsd(Log("claude-sonnet-5-5", input: 1_000_000), []);

        Assert.True(cost >= 4m);
    }

    [Fact]
    public void D_95_失敗した呼び出し_トークン0_は0ドル()
    {
        Assert.Equal(0m, ApiCostCalculator.CostUsd(Log("claude-sonnet-5-5"), Prices));
    }

    [Fact]
    public void NFR_COST_05_上限ちょうどで止め_少しでも下なら止めない()
    {
        // 出力 30万トークン × 10 ドル = 3 ドル
        var atLimit = new[] { Log("claude-sonnet-5-5", output: 300_000) };
        var justBelow = new[] { Log("claude-sonnet-5-5", output: 299_999) };

        Assert.True(ApiCostCalculator.IsLimitReached(atLimit, Prices, 3m));
        Assert.False(ApiCostCalculator.IsLimitReached(justBelow, Prices, 3m));
    }

    [Fact]
    public void NFR_COST_05_その日の呼び出しを合計して比べる()
    {
        var logs = new[]
        {
            Log("claude-sonnet-5-5", output: 200_000),
            Log("claude-haiku-5-5", output: 1_000_000),
            Log("claude-sonnet-5-5", input: 250_000),
        };

        // 2 + 0.5 + 0.5 = 3
        Assert.Equal(3m, ApiCostCalculator.TotalUsd(logs, Prices));
        Assert.True(ApiCostCalculator.IsLimitReached(logs, Prices, 3m));
    }

    [Fact]
    public void NFR_COST_05_上限が0なら金額では止めない()
    {
        var logs = new[] { Log("claude-opus-5-5", output: 10_000_000) };

        Assert.False(ApiCostCalculator.IsLimitReached(logs, Prices, 0m));
    }
}
