using System.Security.Claims;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.Security;

/// <summary>
/// バックグラウンドの処理の中で使う職員（受け付けたときに捕まえた職員。スコープごとに1つ）。
/// </summary>
public sealed class BackgroundUserScope
{
    /// <summary>処理を受け付けた職員</summary>
    public CurrentUserInfo? User { get; set; }
}

/// <summary>
/// ログイン中の職員を返す。バックグラウンドの処理の中では、受け付けたときの職員を返す。
/// </summary>
public sealed class HttpUserContext(IHttpContextAccessor httpContextAccessor, BackgroundUserScope backgroundUserScope) : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    private readonly BackgroundUserScope _backgroundUserScope = backgroundUserScope ?? throw new ArgumentNullException(nameof(backgroundUserScope));

    public Task<CurrentUserInfo?> GetCurrentUserInfoAsync()
    {
        if (_backgroundUserScope.User is { } backgroundUser)
        {
            return Task.FromResult<CurrentUserInfo?>(backgroundUser);
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal?.Identity?.Name;
        if (principal?.Identity?.IsAuthenticated != true || userId is null)
        {
            return Task.FromResult<CurrentUserInfo?>(null);
        }

        return Task.FromResult<CurrentUserInfo?>(new CurrentUserInfo(userId, userName ?? string.Empty));
    }
}
