using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Server.Security;

/// <summary>
/// ログインの Cookie に「パスワードの変更が必要」の印を持たせる（印があるあいだは、パスワード変更以外の API を止める。U-17）。
/// </summary>
public sealed class AppClaimsPrincipalFactory(UserManager<AppUser> userManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(userManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.MustChangePassword)
        {
            identity.AddClaim(new Claim(SecurityServiceCollectionExtensions.MustChangePasswordClaim, "true"));
        }

        return identity;
    }
}

/// <summary>
/// パスワードの規則：12文字以上（見た目の文字数）・ユーザー名と同じものは不可（FR-AUTH-03a。Biz の InputValidator を使う）。
/// </summary>
public sealed class PasswordRulesValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        var result = InputValidator.ValidatePassword(password, user.UserName);
        return Task.FromResult(result.HasError
            ? IdentityResult.Failed(result.ErrorMessages.Select(m => new IdentityError { Code = "PasswordRule", Description = m }).ToArray())
            : IdentityResult.Success);
    }
}

/// <summary>
/// Identity のエラーを日本語にする（使うものだけ）。
/// </summary>
public sealed class JapaneseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "今のパスワードが違います" };

    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = $"パスワードは{length}文字以上にしてください" };

    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = "そのユーザー名はもう使われています" };

    public override IdentityError InvalidUserName(string? userName) => new() { Code = nameof(InvalidUserName), Description = "ユーザー名に使えない文字があります" };

    public override IdentityError UserLockoutNotEnabled() => new() { Code = nameof(UserLockoutNotEnabled), Description = "ロックが有効になっていません" };

    public override IdentityError ConcurrencyFailure() => new() { Code = nameof(ConcurrencyFailure), Description = "別の画面で更新されました。読み込み直してください" };

    public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = "処理に失敗しました。もう一度試してください" };
}

/// <summary>
/// 状態を変える API（GET・HEAD・OPTIONS 以外。multipart を含む）で X-XSRF-TOKEN を必須にする（NFR-SEC-10）。
/// 失敗したときは X-Antiforgery-Error ヘッダーを付けた 400 を返す（画面は1回だけトークンを取り直して送り直す）。
/// </summary>
public sealed class AntiforgeryApiFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    /// <summary>Antiforgery の失敗を画面に知らせるヘッダー</summary>
    public const string ErrorHeader = "X-Antiforgery-Error";

    private readonly IAntiforgery _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.HttpContext.Response.Headers[ErrorHeader] = "true";
            context.Result = new BadRequestObjectResult(Result.Error("画面の情報が古くなりました。画面を読み込み直して、もう一度試してください。"));
        }
    }
}
