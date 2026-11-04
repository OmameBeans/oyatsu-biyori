using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;
using OyatsuBiyori.Data.Managers;
using OyatsuBiyori.Data.Repositories;

namespace OyatsuBiyori.Data;

/// <summary>
/// Data の DI 登録と、DB の準備。
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>マイグレーションを置くアセンブリ（D-40）</summary>
    public const string MigrationsAssembly = "OyatsuBiyori.Migrations";

    /// <summary>Data のサービスを登録する。接続文字列は ConnectionStrings:SQLite から読む</summary>
    public static IServiceCollection AddData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SQLite");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("接続文字列 ConnectionStrings:SQLite を設定してください");
        }

        services.AddDbContext<OyatsuBiyoriDbContext>(options =>
            options.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly(MigrationsAssembly)));
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IApiUsageLogRepository, ApiUsageLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        return services;
    }

    /// <summary>
    /// DB を準備する。WAL にして同時の読み書きで止まりにくくする。
    /// マイグレーションは開発時だけ自動で当てる（本番は手元で作った SQL を当てる。D-42）。
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, bool applyMigrations)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<OyatsuBiyoriDbContext>();
        if (applyMigrations)
        {
            await context.Database.MigrateAsync();
        }

        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
