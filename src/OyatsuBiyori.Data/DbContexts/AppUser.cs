using Microsoft.AspNetCore.Identity;

namespace OyatsuBiyori.Data.DbContexts;

/// <summary>
/// 職員のアカウント（1件だけ。ASP.NET Core Identity のテーブルを使う）。
/// </summary>
public sealed class AppUser : IdentityUser
{
    /// <summary>パスワードの変更が必要か（初期パスワードのあいだ true。D-55）</summary>
    public bool MustChangePassword { get; set; }
}
