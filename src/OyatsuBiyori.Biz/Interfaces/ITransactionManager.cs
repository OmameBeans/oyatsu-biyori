namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// 複数の書き込みを1つのトランザクションにまとめる。失敗時と例外時は RollbackAsync を呼ぶ。
/// </summary>
public interface ITransactionManager
{
    /// <summary>トランザクションを始める</summary>
    Task BeginTransactionAsync();

    /// <summary>コミットする</summary>
    Task CommitAsync();

    /// <summary>ロールバックする</summary>
    Task RollbackAsync();
}
