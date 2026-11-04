using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Server.Security;

/// <summary>
/// ログイン・認可・Antiforgery・ログインの回数制限の登録（FR-AUTH、NFR-SEC-01〜03・10、T2-01〜04）。
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>パスワードの変更が済んでいなくても使える API のポリシー（状態の確認・パスワード変更・ログアウト）</summary>
    public const string AllowPasswordChangePolicy = "AllowPasswordChange";

    /// <summary>ログインの API の回数制限のポリシー</summary>
    public const string LoginRateLimitPolicy = "login";

    /// <summary>「パスワードの変更が必要」を Cookie に持たせるクレーム</summary>
    public const string MustChangePasswordClaim = "oyatsu:must_change_password";

    /// <summary>Antiforgery のトークンを受け取るヘッダー</summary>
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    /// <summary>ログイン・認可などを登録する</summary>
    public static IServiceCollection AddServerSecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddIdentityCore<AppUser>(options =>
            {
                // パスワードは12文字以上・文字の種類は問わない（FR-AUTH-03a。文字数は PasswordRulesValidator で見た目の文字数で数える）
                options.Password.RequiredLength = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                // 10回続けて失敗したら15分ロックする（FR-AUTH-05）
                options.Lockout.MaxFailedAccessAttempts = 10;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<OyatsuBiyoriDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddErrorDescriber<JapaneseIdentityErrorDescriber>()
            .AddPasswordValidator<PasswordRulesValidator>();

        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "oyatsu-biyori.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;

            // スマホで使うため30日保持し、使うたびに延ばす（FR-AUTH-06）
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // /api はログイン画面へのリダイレクトではなく 401 / 403 を返す（NFR-SEC-10）
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(Result.Error("ログインが必要です。ログインし直してください。"));
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(Result.Error("パスワードの変更が必要です。設定のパスワード変更で、新しいパスワードにしてください。"));
            };
        });

        // Data Protection のキーはファイルに保存する（再起動してもログインが切れないように）。置き場所はリポジトリの外
        var keysPath = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath))
        {
            keysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "oyatsu-biyori", "keys", environment.EnvironmentName);
        }

        // 本番は DataProtection:KeysPath（環境変数）で Web から見えない場所を必ず指定する（publish で消えないように）。Windows では DPAPI で暗号化する
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("oyatsu-biyori")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi();
        }

        services.AddAuthorizationBuilder()
            // すべての API でログインと、パスワードの変更が済んでいることを求める（NFR-SEC-02、FR-AUTH-03b）
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == MustChangePasswordClaim))
                .Build())
            .AddPolicy(AllowPasswordChangePolicy, policy => policy.RequireAuthenticatedUser());

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = "oyatsu-biyori.af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
        });
        services.Configure<MvcOptions>(options => options.Filters.Add<AntiforgeryApiFilter>());

        // ログインの API は1分に10回まで（FR-AUTH-05）
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await context.HttpContext.Response.WriteAsJsonAsync(
                    Result.Error("ログインの操作が続いたため、少し止めています。1分ほど待ってから、もう一度試してください。"),
                    cancellationToken);
            };
            options.AddPolicy(LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });

        services.AddScoped<AuthService>();
        services.AddScoped<AccountBootstrapper>();
        return services;
    }
}
