using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Server.BackgroundJobs;
using OyatsuBiyori.Server.Security;

namespace OyatsuBiyori.Server;

/// <summary>
/// Server（Web API）の土台の登録。
/// </summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>1回のリクエストで受け取れる大きさの上限（画像はブラウザで縮めてから送る。web.config の maxAllowedContentLength と合わせる）</summary>
    public const long MaxRequestBodySize = 50L * 1024 * 1024;

    /// <summary>Server の土台を登録する</summary>
    public static IServiceCollection AddServerFoundation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<BackgroundUserScope>();
        services.AddScoped<IUserContext, HttpUserContext>();

        services.AddSingleton<InMemoryBackgroundJobService>();
        services.AddSingleton<IBackgroundJobService>(sp => sp.GetRequiredService<InMemoryBackgroundJobService>());
        services.AddHostedService(sp => sp.GetRequiredService<InMemoryBackgroundJobService>());

        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxRequestBodySize);

        // 商品照合のしきい値・候補の件数（D-02。実際のチラシで調整する）
        services.AddOptions<ProductMatchOptions>()
            .BindConfiguration(ProductMatchOptions.SectionName)
            .Validate(o => o.DiceThreshold is > 0 and <= 1 && o.MaxCandidates >= 1, "ProductMatch の設定が正しくありません（DiceThreshold は 0 より大きく 1 以下、MaxCandidates は 1 以上）")
            .ValidateOnStart();

        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            // 入力の形が合わないときも、日本語の Result の形で返す（NFR-UX-03）
            options.InvalidModelStateResponseFactory = context =>
            {
                var result = new Result();
                result.ErrorMessages.Add("入力された値の形が正しくありません。数字の欄に数字以外が入っていないか確かめてください。");
                return new BadRequestObjectResult(result);
            };
        });

        return services;
    }
}
