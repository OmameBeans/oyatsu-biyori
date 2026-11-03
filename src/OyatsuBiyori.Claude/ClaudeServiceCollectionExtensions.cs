using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            };
        });
        services.AddScoped<ClaudeRequestRunner>();
        return services;
    }
}
