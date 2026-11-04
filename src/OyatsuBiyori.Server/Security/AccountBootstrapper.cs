using Microsoft.AspNetCore.Identity;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Server.Security;

/// <summary>
/// 起動時のアカウントの準備（FR-AUTH-03・05、T2-02）。
/// - アカウントがなければ、設定（Auth:InitialUserName・Auth:InitialPassword。環境変数・user-secrets）から1つ作り、「変更が必要」の印を立てる
/// - すでにあれば作り直さず、パスワードも上書きしない
/// - Auth:UnlockOnStartup が true なら、ロックと失敗回数を消す（ロックの解除の手順。運用メモ）
/// </summary>
public sealed class AccountBootstrapper(
    UserManager<AppUser> userManager,
    IConfiguration configuration,
    ILogger<AccountBootstrapper> logger)
{
    private readonly UserManager<AppUser> _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<AccountBootstrapper> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>準備する</summary>
    public async Task RunAsync()
    {
        var users = _userManager.Users.ToList();
        if (users.Count == 0)
        {
            await CreateInitialAccountAsync();
        }
        else if (_configuration.GetValue<bool>("Auth:UnlockOnStartup"))
        {
            foreach (var user in users)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            _logger.LogWarning("Auth:UnlockOnStartup により、ログインのロックを解除しました。設定を元に戻してください");
        }
    }

    private async Task CreateInitialAccountAsync()
    {
        var userName = _configuration["Auth:InitialUserName"];
        var password = _configuration["Auth:InitialPassword"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("アカウントがありません。Auth:InitialUserName と Auth:InitialPassword を環境変数か user-secrets で設定して、起動し直してください");
            return;
        }

        var user = new AppUser
        {
            UserName = userName.Trim(),
            MustChangePassword = true,
            LockoutEnabled = true,
        };

        // 初期パスワードは変更を求めるため、文字数などの規則は当てずに作る（変更時に規則を当てる）
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            _logger.LogError("初期アカウントを作れませんでした: {Errors}", string.Join(" / ", created.Errors.Select(e => e.Code)));
            return;
        }

        var hash = _userManager.PasswordHasher.HashPassword(user, password);
        user.PasswordHash = hash;
        await _userManager.UpdateSecurityStampAsync(user);
        await _userManager.UpdateAsync(user);
        _logger.LogInformation("初期アカウントを作りました（最初のログインでパスワードの変更を求めます）");
    }
}
