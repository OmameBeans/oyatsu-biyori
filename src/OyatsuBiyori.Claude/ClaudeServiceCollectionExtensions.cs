using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Claude.Services;

namespace OyatsuBiyori.Claude;

/// <summary>
/// Claude の DI 登録。API キーは環境変数 ANTHROPIC_API_KEY か user-secrets（Claude:ApiKey）から読む。
/// </summary>
public static class ClaudeServiceCollectionExtensions
{
    /// <summary>Claude のサービスを登録する</summary>
    public static IServiceCollection AddClaude(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeOptions>(configuration.GetSection(ClaudeOptions.SectionName));

        // SDK の自動の再試行は切る（アプリで再試行し、1回ごとに数えるため。D-65）
        services.AddSingleton(_ =>
        {
            var apiKey = configuration["Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            return new AnthropicClient
            {
                ApiKey = apiKey,
                MaxRetries = 0,
                // 1回ごとのタイムアウトはアプリ（ClaudeRequestRunner）で用途ごとにかける。SDK 側で先に切られないよう、それより長くしておく
                Timeout = TimeSpan.FromMinutes(10),
            };
        });
        services.AddScoped<ClaudeRequestRunner>();
        services.AddScoped<IFlyerReader, FlyerReader>();
        services.AddScoped<IFlyerReviewer, FlyerReviewer>();

        // 献立づくりのエージェントは、設定で Messages API 版と Managed Agents 版を切り替える（D-94）
        services.AddScoped<MenuPlannerAgent>();
        services.AddScoped<ManagedMenuPlannerAgent>();
        services.AddScoped<MenuAgentSessionCleaner>();
        services.AddScoped<IMenuAgentSessionCleaner>(provider => provider.GetRequiredService<MenuAgentSessionCleaner>());
        services.AddScoped<IMenuPlannerAgent>(provider =>
            provider.GetRequiredService<IOptions<ClaudeOptions>>().Value.MenuPlannerEngine == MenuPlannerEngine.ManagedAgents
                ? provider.GetRequiredService<ManagedMenuPlannerAgent>()
                : provider.GetRequiredService<MenuPlannerAgent>());

        services.AddScoped<IBudgetFitExplainer, BudgetFitExplainer>();
        services.AddScoped<IAllergenExtractor, AllergenExtractor>();
        return services;
    }
}
