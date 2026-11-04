namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// リポジトリで取得・追加したエンティティの変更をまとめて保存する。
/// 献立の保存で別の画面の更新と衝突したら ConcurrencyConflictException を投げる（D-46）。
/// </summary>
public interface IUnitOfWork
{
    /// <summary>変更を保存する</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 別の画面（端末）で先に更新されていた（row_version が違う）。
/// </summary>
public sealed class ConcurrencyConflictException(Exception? inner = null)
    : Exception("別の画面で更新されました。読み込み直してください。", inner);
