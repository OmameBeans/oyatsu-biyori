using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Claude;

/// <summary>
/// Claude の呼び出しの設定（appsettings の "Claude" 節）。モデル名はコードに直書きせず、用途ごとにここから読む。
/// </summary>
public sealed class ClaudeOptions
{
    /// <summary>設定の節の名前</summary>
    public const string SectionName = "Claude";

    /// <summary>既定のモデル（用途ごとの指定がないとき）</summary>
    public string DefaultModel { get; set; } = "claude-sonnet-5-5";

    /// <summary>用途ごとのモデル（例：ProductMatch は claude-haiku-5-5）</summary>
    public Dictionary<ClaudePurpose, string> Models { get; set; } = [];

    /// <summary>1回の呼び出しのタイムアウト（秒。画像の読み取りと献立）</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>照合の判定の1回の呼び出しのタイムアウト（秒）</summary>
    public int ProductMatchTimeoutSeconds { get; set; } = 30;

    /// <summary>失敗したときに再試行する回数（SDK の自動の再試行は切り、アプリで数える。D-64・D-65）</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>1日の呼び出し回数の上限（日本時間の日付で数える。失敗・再試行も含む。NFR-COST-05）</summary>
    public int DailyCallLimit { get; set; } = 200;

    /// <summary>1回のチラシの取り込みでの照合の判定の呼び出しの上限</summary>
    public int ProductMatchCallLimitPerImport { get; set; } = 30;

    /// <summary>1回の応答の最大トークン数</summary>
    public int MaxTokens { get; set; } = 16000;

    /// <summary>用途のモデル名</summary>
    public string ModelFor(ClaudePurpose purpose)
    {
        return Models.TryGetValue(purpose, out var model) && !string.IsNullOrWhiteSpace(model) ? model : DefaultModel;
    }

    /// <summary>用途のタイムアウト</summary>
    public TimeSpan TimeoutFor(ClaudePurpose purpose)
    {
        return TimeSpan.FromSeconds(purpose == ClaudePurpose.ProductMatch ? ProductMatchTimeoutSeconds : TimeoutSeconds);
    }
}
