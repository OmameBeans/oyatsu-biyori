namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// ログイン中の職員の情報を取得する。
/// リクエストの外（バックグラウンドの処理）では HttpContext がないため、受け付けたときの職員を渡して使う。
/// </summary>
public interface IUserContext
{
    /// <summary>ログイン中の職員（未ログインなら null）</summary>
    Task<CurrentUserInfo?> GetCurrentUserInfoAsync();
}

/// <summary>ログイン中の職員</summary>
/// <param name="UserId">職員の ID（Identity の Id）</param>
/// <param name="UserName">ログイン名</param>
public sealed record CurrentUserInfo(string UserId, string UserName);
