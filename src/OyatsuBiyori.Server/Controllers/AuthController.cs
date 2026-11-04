using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OyatsuBiyori.Server.Security;

namespace OyatsuBiyori.Server.Controllers;

/// <summary>
/// ログイン（SC-01）・パスワード変更（SC-12）・ログアウト・状態の確認・Antiforgery のトークン。
/// 匿名で通すのは、ログイン・トークンの発行・状態の確認だけ（T2-01）。
/// </summary>
[Route("api/[controller]/[action]")]
[ApiController]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    private readonly AuthService _authService = authService ?? throw new ArgumentNullException(nameof(authService));

    /// <summary>ログインの状態（ログイン後の枠とログイン画面でだけ呼ぶ。ランディングでは呼ばない）</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Status() => new JsonResult(await _authService.GetStatusAsync());

    /// <summary>Antiforgery のトークン</summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Antiforgery() => new JsonResult(_authService.IssueAntiforgeryToken());

    /// <summary>ログイン</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(SecurityServiceCollectionExtensions.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) => new JsonResult(await _authService.LoginAsync(request.UserName, request.Password));

    /// <summary>ログアウト</summary>
    [HttpPost]
    [Authorize(Policy = SecurityServiceCollectionExtensions.AllowPasswordChangePolicy)]
    public async Task<IActionResult> Logout() => new JsonResult(await _authService.LogoutAsync());

    /// <summary>パスワード変更</summary>
    [HttpPost]
    [Authorize(Policy = SecurityServiceCollectionExtensions.AllowPasswordChangePolicy)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        => new JsonResult(await _authService.ChangePasswordAsync(request.CurrentPassword, request.NewPassword));

    /// <summary>ログインの入力</summary>
    public sealed class LoginRequest
    {
        public string? UserName { get; set; }

        public string? Password { get; set; }
    }

    /// <summary>パスワード変更の入力</summary>
    public sealed class ChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }

        public string? NewPassword { get; set; }
    }
}
