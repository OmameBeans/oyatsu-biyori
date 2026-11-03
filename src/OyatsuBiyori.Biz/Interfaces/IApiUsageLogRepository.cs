using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// Claude API の呼び出しの記録（費用の計算と1日の上限に使う）。
/// </summary>
public interface IApiUsageLogRepository
{
    /// <summary>記録を追加する</summary>
    Task<bool> AddAsync(ApiUsageLog log);

    /// <summary>指定した日時（UTC）以降の呼び出しの回数（失敗・再試行も含む）</summary>
    Task<int> CountSinceAsync(DateTime sinceUtc);
}
