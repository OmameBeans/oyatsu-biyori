using Microsoft.Extensions.DependencyInjection;

namespace OyatsuBiyori.Biz.Extensions;

/// <summary>
/// Biz の DI 登録。
/// </summary>
public static class BizServiceCollectionExtensions
{
    /// <summary>Biz のサービスを登録する</summary>
    public static IServiceCollection AddBiz(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
