using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Server.Security;

/// <summary>
/// ログイン・ログアウト・パスワード変更・状態の確認（FR-AUTH、T2-01〜04）。
/// </summary>
public sealed class AuthService(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> userManager,
    IAntiforgery antiforgery,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthService> logger)
{
    private readonly SignInManager<AppUser> _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly IAntiforgery _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    private readonly ILogger<AuthService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private HttpContext HttpContext => _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("リクエストの外では使えません");

    /// <summary>ログインの状態</summary>
    public async Task<Result<AuthStatus>> GetStatusAsync()
    {
        var principal = HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Result<AuthStatus>.Ok(AuthStatus.Anonymous);
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Result<AuthStatus>.Ok(AuthStatus.Anonymous);
        }

        return Result<AuthStatus>.Ok(new AuthStatus(true, user.UserName, user.MustChangePassword));
    }

    /// <summary>
    /// Antiforgery のトークンを発行する。トークンはユーザーに結びつくため、ログイン・パスワード変更・ログアウトの直後に取り直す。
    /// </summary>
    public Result<AntiforgeryToken> IssueAntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Result<AntiforgeryToken>.Ok(new AntiforgeryToken(tokens.RequestToken ?? string.Empty));
    }

    /// <summary>ログインする（10回続けて失敗したら15分ロック。FR-AUTH-05）</summary>
    public async Task<Result<AuthStatus>> LoginAsync(string? userName, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return Result<AuthStatus>.Error("ユーザー名とパスワードを入れてください");
        }

        var user = await _userManager.FindByNameAsync(userName.Trim());
        if (user is null)
        {
            return Result<AuthStatus>.Error("ユーザー名かパスワードが違います");
        }

        var signIn = await _signInManager.PasswordSignInAsync(user, password, isPersistent: true, lockoutOnFailure: true);
        if (signIn.IsLockedOut)
        {
            _logger.LogWarning("ログインがロックされています");
            return Result<AuthStatus>.Error("ログインに続けて失敗したため、15分ロックしています。時間をおいてから、もう一度試してください。");
        }

        if (!signIn.Succeeded)
        {
            return Result<AuthStatus>.Error("ユーザー名かパスワードが違います");
        }

        return Result<AuthStatus>.Ok(new AuthStatus(true, user.UserName, user.MustChangePassword));
    }

    /// <summary>ログアウトする</summary>
    public async Task<Result> LogoutAsync()
    {
        await _signInManager.SignOutAsync();
        return new Result();
    }

    /// <summary>
    /// パスワードを変える（今のパスワードを確かめる。12文字以上・ユーザー名と同じは不可）。
    /// 成功したら「変更が必要」の印を消し、Cookie を発行し直す（FR-AUTH-03a・03b）。
    /// </summary>
    public async Task<Result<AuthStatus>> ChangePasswordAsync(string? currentPassword, string? newPassword)
    {
        var user = await _userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            return Result<AuthStatus>.Error("ログインが必要です。ログインし直してください。");
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            return Result<AuthStatus>.Error("今のパスワードを入れてください");
        }

        var rule = InputValidator.ValidatePassword(newPassword, user.UserName);
        if (rule.HasError)
        {
            var error = new Result<AuthStatus>();
            error.ErrorMessages.AddRange(rule.ErrorMessages);
            return error;
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return Result<AuthStatus>.Error("今と同じパスワードは使えません。別のパスワードにしてください");
        }

        var changed = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword!);
        if (!changed.Succeeded)
        {
            var error = new Result<AuthStatus>();
            error.ErrorMessages.AddRange(changed.Errors.Select(e => e.Description).Distinct());
            return error;
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _signInManager.RefreshSignInAsync(user);
        return Result<AuthStatus>.Ok(new AuthStatus(true, user.UserName, false));
    }
}

/// <summary>ログインの状態</summary>
/// <param name="IsAuthenticated">ログインしているか</param>
/// <param name="UserName">ユーザー名</param>
/// <param name="MustChangePassword">パスワードの変更が必要か</param>
public sealed record AuthStatus(bool IsAuthenticated, string? UserName, bool MustChangePassword)
{
    /// <summary>ログインしていない</summary>
    public static AuthStatus Anonymous { get; } = new(false, null, false);
}

/// <summary>Antiforgery のトークン（X-XSRF-TOKEN ヘッダーで送る）</summary>
public sealed record AntiforgeryToken(string Token);
