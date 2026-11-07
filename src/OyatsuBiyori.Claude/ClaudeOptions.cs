using OyatsuBiyori.Biz;
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

    /// <summary>用途ごとのモデル（指定がなければ DefaultModel）</summary>
    public Dictionary<ClaudePurpose, string> Models { get; set; } = [];

    /// <summary>1回の呼び出しのタイムアウト（秒。画像の読み取りと献立）</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>用途ごとのタイムアウト（秒。指定がなければ TimeoutSeconds。画像の読み取りは長めにする）</summary>
    public Dictionary<ClaudePurpose, int> TimeoutSecondsByPurpose { get; set; } = [];

    /// <summary>用途ごとの考える深さ（low / medium / high。指定がなければモデルの既定）。費用と待ち時間を抑えるため設定で変える</summary>
    public Dictionary<ClaudePurpose, string> Efforts { get; set; } = [];

    /// <summary>失敗したときに再試行する回数（SDK の自動の再試行は切り、アプリで数える。D-64・D-65）</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>1日の呼び出し回数の上限（日本時間の日付で数える。失敗・再試行も含む。0 なら回数では止めない。NFR-COST-05・D-95 で金額の上限に替えた）</summary>
    public int DailyCallLimit { get; set; }

    /// <summary>1日に使ってよい金額（ドル。日本時間の日付で、記録したトークン数とモデルの単価から計算する。0 なら金額では止めない。NFR-COST-05・D-95）</summary>
    public decimal DailyCostLimitUsd { get; set; } = 3m;

    /// <summary>モデルごとの単価（ドル。100万トークンあたり）。表にないモデルは、表の中でいちばん高い単価で数える</summary>
    public Dictionary<string, ModelPriceOptions> Prices { get; set; } = [];

    /// <summary>単価の表（Biz の計算に渡す形）</summary>
    public IReadOnlyList<ApiModelPrice> PriceList() =>
        Prices.Select(p => new ApiModelPrice(p.Key, p.Value.Input, p.Value.Output, p.Value.CacheWrite, p.Value.CacheRead)).ToList();

    /// <summary>1回の応答の最大トークン数</summary>
    public int MaxTokens { get; set; } = 16000;

    /// <summary>用途ごとの1回の応答の最大トークン数（指定がなければ MaxTokens。チラシの読み取りは商品が多く、考える分も数えるので大きくする）</summary>
    public Dictionary<ClaudePurpose, int> MaxTokensByPurpose { get; set; } = [];

    /// <summary>献立づくりのエージェントの動かし方（D-94。Managed Agents がうまく動かないときに、Messages API の手書きのループへ戻せるようにする）</summary>
    public MenuPlannerEngine MenuPlannerEngine { get; set; } = MenuPlannerEngine.MessagesApi;

    /// <summary>Managed Agents の設定（エージェントと環境は agents/ のファイルから ant apply で作り、ID をここに入れる）</summary>
    public ManagedAgentsOptions ManagedAgents { get; set; } = new();

    /// <summary>用途のモデル名</summary>
    public string ModelFor(ClaudePurpose purpose)
    {
        return Models.TryGetValue(purpose, out var model) && !string.IsNullOrWhiteSpace(model) ? model : DefaultModel;
    }

    /// <summary>用途のタイムアウト</summary>
    public TimeSpan TimeoutFor(ClaudePurpose purpose)
    {
        if (TimeoutSecondsByPurpose.TryGetValue(purpose, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(TimeoutSeconds);
    }

    /// <summary>用途の1回の応答の最大トークン数</summary>
    public int MaxTokensFor(ClaudePurpose purpose) =>
        MaxTokensByPurpose.TryGetValue(purpose, out var maxTokens) && maxTokens > 0 ? maxTokens : MaxTokens;

    /// <summary>用途の考える深さ（設定がなければ null＝モデルの既定）</summary>
    public string? EffortFor(ClaudePurpose purpose) =>
        Efforts.TryGetValue(purpose, out var effort) && !string.IsNullOrWhiteSpace(effort) ? effort.Trim().ToLowerInvariant() : null;
}

/// <summary>献立づくりのエージェントの動かし方（D-94）</summary>
public enum MenuPlannerEngine
{
    /// <summary>Messages API の上の手書きのループ（MenuPlannerAgent）</summary>
    MessagesApi,

    /// <summary>Claude Managed Agents（ManagedMenuPlannerAgent）。ループと会話は Anthropic 側が持ち、道具はカスタムツールとしてサーバーで実行する</summary>
    ManagedAgents,
}

/// <summary>Managed Agents の設定（appsettings の "Claude:ManagedAgents" 節。D-94）</summary>
public sealed class ManagedAgentsOptions
{
    /// <summary>献立づくりのエージェントの ID（agent_...。claude-lock.json の agents/oyatsu-menu-planner/agent.md）</summary>
    public string MenuPlannerAgentId { get; set; } = string.Empty;

    /// <summary>使うエージェントの版（null なら最新。プロンプトを変えたあと、前の版に戻すときに使う）</summary>
    public int? MenuPlannerAgentVersion { get; set; }

    /// <summary>環境の ID（env_...。claude-lock.json の agents/oyatsu-menu-planner/environment.yaml）</summary>
    public string EnvironmentId { get; set; } = string.Empty;

    /// <summary>1回の依頼でのモデルの呼び出しの上限（Messages API 版のループの上限10回と、チャットの説明の1回と、差し戻しの余裕）</summary>
    public int MaxModelRequests { get; set; } = 12;

    /// <summary>イベントの流れ（SSE）が切れたときに、つなぎ直す回数の上限</summary>
    public int MaxStreamReconnects { get; set; } = 3;
}

/// <summary>モデルの単価（ドル。100万トークンあたり。appsettings の "Claude:Prices" の1件）</summary>
public sealed class ModelPriceOptions
{
    /// <summary>入力</summary>
    public decimal Input { get; set; }

    /// <summary>出力</summary>
    public decimal Output { get; set; }

    /// <summary>キャッシュの書き込み</summary>
    public decimal CacheWrite { get; set; }

    /// <summary>キャッシュの読み込み</summary>
    public decimal CacheRead { get; set; }
}
