namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// Claude を呼ぶ処理をバックグラウンドで受け付ける（NFR-PERF-05）。受け付けたら ID を返し、画面は2秒ごとに状態を取りに行く。
/// 状態はメモリに持つため、アプリが再起動したら消える（見つからない ID は「失敗（再起動）」として扱う）。
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>
    /// 処理を受け付ける。
    /// </summary>
    /// <param name="kind">処理の種類（例：flyer-reading）</param>
    /// <param name="ownerKey">結びつける対象（例：献立の ID）。あとで別の端末から探すために使う</param>
    /// <param name="user">受け付けた職員（処理の中では HttpContext がないため、ここで捕まえて渡す）</param>
    /// <param name="deadline">処理全体の期限</param>
    /// <param name="work">処理の本体。スコープつきのサービス・進捗の報告・期限を受け取る</param>
    /// <returns>処理の ID</returns>
    string Enqueue(string kind, string? ownerKey, CurrentUserInfo? user, TimeSpan deadline, BackgroundWork work);

    /// <summary>処理の状態（見つからなければ null）</summary>
    BackgroundJobStatus? Get(string id);

    /// <summary>対象に結びついた、いちばん新しい処理の状態（見つからなければ null）</summary>
    BackgroundJobStatus? FindLatest(string kind, string ownerKey);
}

/// <summary>バックグラウンドの処理の本体</summary>
/// <param name="services">処理ごとのスコープのサービス（DbContext などはここから取る）</param>
/// <param name="progress">段階名を報告する</param>
/// <param name="cancellationToken">処理全体の期限</param>
public delegate Task<Result<object?>> BackgroundWork(IServiceProvider services, IJobProgress progress, CancellationToken cancellationToken);

/// <summary>進捗の報告</summary>
public interface IJobProgress
{
    /// <summary>今の段階名を報告する（例：「チラシを読み取っています」）</summary>
    void Report(string stage);
}

/// <summary>処理の状態</summary>
public enum BackgroundJobState
{
    /// <summary>待っている</summary>
    Queued,

    /// <summary>処理中</summary>
    Running,

    /// <summary>終わった</summary>
    Succeeded,

    /// <summary>失敗した（画面から再試行できる）</summary>
    Failed,
}

/// <summary>
/// 処理の状態と結果。
/// </summary>
public sealed record BackgroundJobStatus(
    string Id,
    string Kind,
    string? OwnerKey,
    BackgroundJobState State,
    string? Stage,
    object? Result,
    IReadOnlyList<string> ErrorMessages,
    DateTime CreatedAt,
    DateTime? FinishedAt)
{
    /// <summary>見つからない ID の状態（アプリが再起動して消えた。NFR-PERF-05）</summary>
    public static BackgroundJobStatus Lost(string id) => new(
        id,
        string.Empty,
        null,
        BackgroundJobState.Failed,
        null,
        null,
        ["処理が見つかりませんでした（サーバーが再起動した可能性があります）。もう一度試してください。"],
        DateTime.MinValue,
        null);
}
