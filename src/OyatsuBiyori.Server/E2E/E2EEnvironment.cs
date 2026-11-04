using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;
using OyatsuBiyori.Server.DemoData;

namespace OyatsuBiyori.Server.E2E;

/// <summary>
/// 画面の E2E（T0-15・D-87）とダミーデータの環境。偽の Claude とダミーデータの投入は、ここで登録したときだけ動く。
/// 本番で動かないよう、環境名・接続文字列・IIS の3つで止める（DemoDataGuard）。
/// </summary>
public static class E2EEnvironment
{
    /// <summary>E2E の環境名</summary>
    public const string EnvironmentName = "E2E";

    /// <summary>E2E の環境か</summary>
    public static bool IsE2E(IHostEnvironment environment) => environment.IsEnvironment(EnvironmentName);

    /// <summary>E2E の環境なら、Claude を偽物に差し替え、ダミーデータの投入を登録する</summary>
    public static IServiceCollection AddE2EOverrides(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        // ダミーデータの投入は開発と E2E で使う（本番では DemoDataGuard が止める）
        services.AddScoped<DemoDataSeeder>();

        if (!IsE2E(environment))
        {
            return services;
        }

        DemoDataGuard.EnsureAllowed(environment, configuration);
        services.Replace(ServiceDescriptor.Scoped<IFlyerReader, FakeFlyerReader>());
        services.Replace(ServiceDescriptor.Scoped<IFlyerReviewer, FakeFlyerReviewer>());
        services.Replace(ServiceDescriptor.Scoped<IProductMatchJudge, FakeProductMatchJudge>());
        services.Replace(ServiceDescriptor.Scoped<IMenuPlannerAgent, FakeMenuPlannerAgent>());
        services.Replace(ServiceDescriptor.Scoped<IBudgetFitExplainer, FakeBudgetFitExplainer>());
        services.Replace(ServiceDescriptor.Scoped<IAllergenExtractor, FakeAllergenExtractor>());
        return services;
    }

    /// <summary>E2E の環境なら、DB を消して作り直す（毎回同じ状態から始める）</summary>
    public static async Task ResetE2EDatabaseIfNeededAsync(this IServiceProvider services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (!IsE2E(environment))
        {
            return;
        }

        DemoDataGuard.EnsureAllowed(environment, configuration);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OyatsuBiyoriDbContext>();
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>
    /// デモ環境の作り直し（開発の環境だけ。`dotnet run --project src/OyatsuBiyori.Server -- --reset-demo`）：
    /// DB を消してマイグレーションを当て、初期アカウントを作り、ダミーデータを入れる（TD-02）。
    /// </summary>
    public static async Task ResetDemoAsync(this IServiceProvider services, IHostEnvironment environment, IConfiguration configuration)
    {
        DemoDataGuard.EnsureAllowed(environment, configuration);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OyatsuBiyoriDbContext>();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<Security.AccountBootstrapper>().RunAsync();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(includeE2EDraft: false);
    }

    /// <summary>E2E の環境なら、ダミーデータを入れ、アカウントのパスワード変更の印を消す</summary>
    public static async Task SeedE2EDataIfNeededAsync(this IServiceProvider services, IHostEnvironment environment, IConfiguration configuration)
    {
        if (!IsE2E(environment))
        {
            return;
        }

        DemoDataGuard.EnsureAllowed(environment, configuration);
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var user in await userManager.Users.ToListAsync())
        {
            user.MustChangePassword = false;
            await userManager.UpdateAsync(user);
        }

        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(includeE2EDraft: true);
    }
}

/// <summary>
/// ダミーデータ・偽の Claude を本番で動かさない歯止め（TD-02・T0-15）。
/// - 環境名が Development か E2E であること
/// - 接続文字列のファイル名に「dev」か「e2e」を含むこと（本番の接続文字列で動かさない）
/// - IIS の中（APP_POOL_ID がある）で動いていないこと
/// </summary>
public static class DemoDataGuard
{
    /// <summary>許されていなければ例外で止める</summary>
    public static void EnsureAllowed(IHostEnvironment environment, IConfiguration configuration)
    {
        if (!environment.IsDevelopment() && !E2EEnvironment.IsE2E(environment))
        {
            throw new InvalidOperationException("ダミーデータ・偽の Claude は、開発と E2E の環境でしか使えません");
        }

        var connectionString = configuration.GetConnectionString("SQLite") ?? string.Empty;
        if (!connectionString.Contains(".dev.", StringComparison.OrdinalIgnoreCase) && !connectionString.Contains(".e2e.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ダミーデータは、ファイル名に .dev. か .e2e. を含む DB にしか入れられません");
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")))
        {
            throw new InvalidOperationException("IIS の中では、ダミーデータ・偽の Claude を使えません");
        }
    }
}
