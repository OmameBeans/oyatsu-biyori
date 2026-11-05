using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Server.Security;

namespace OyatsuBiyori.Server.BackgroundJobs;

/// <summary>
/// バックグラウンドの処理を、メモリ上の待ち行列で動かす（NFR-PERF-05・D-26）。
/// 結果はメモリに24時間持ち、どの端末からでも開ける（D-36）。アプリが再起動したら消える。
/// </summary>
public sealed class InMemoryBackgroundJobService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<InMemoryBackgroundJobService> logger) : BackgroundService, IBackgroundJobService
{
    /// <summary>同時に動かす処理の数</summary>
    private const int WorkerCount = 2;

    /// <summary>終わった処理を持っておく時間</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>流すときに、変わった分をためる時間</summary>
    private static readonly TimeSpan StreamBatchDelay = TimeSpan.FromMilliseconds(50);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<InMemoryBackgroundJobService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Channel<JobEntry> _queue = Channel.CreateUnbounded<JobEntry>();
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();

    public string Enqueue(string kind, string? ownerKey, CurrentUserInfo? user, TimeSpan deadline, BackgroundWork work)
    {
        ArgumentNullException.ThrowIfNull(work);
        RemoveExpired();

        var entry = new JobEntry(Guid.NewGuid().ToString(), kind, ownerKey, user, deadline, work, _timeProvider.GetUtcNow().UtcDateTime);
        _jobs[entry.Id] = entry;
        if (!_queue.Writer.TryWrite(entry))
        {
            entry.Fail(["処理を受け付けられませんでした。もう一度試してください。"], _timeProvider.GetUtcNow().UtcDateTime);
        }

        return entry.Id;
    }

    public BackgroundJobStatus? Get(string id)
    {
        return _jobs.TryGetValue(id, out var entry) ? entry.ToStatus() : null;
    }

    public BackgroundJobStatus? FindLatest(string kind, string ownerKey)
    {
        return _jobs.Values
            .Where(e => e.Kind == kind && e.OwnerKey == ownerKey)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.ToStatus())
            .FirstOrDefault();
    }

    public BackgroundJobStatus? FindLatestSucceeded(string kind, string ownerKey)
    {
        return _jobs.Values
            .Where(e => e.Kind == kind && e.OwnerKey == ownerKey)
            .Select(e => e.ToStatus())
            .Where(s => s.State == BackgroundJobState.Succeeded)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    public async IAsyncEnumerable<BackgroundJobStreamEvent> StreamAsync(string id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(id, out var entry))
        {
            var lost = BackgroundJobStatus.Lost(id);
            yield return new BackgroundJobStreamEvent(lost.State, null, 0, string.Empty, lost.ErrorMessages);
            yield break;
        }

        // 送った文章と段階名を覚えておき、変わった分だけ流す（最初の1回は今の全文）
        var sentText = string.Empty;
        string? sentStage = null;
        var isFirst = true;
        while (true)
        {
            var (snapshot, changed) = entry.Snapshot();
            if (isFirst || snapshot.Text != sentText || snapshot.Stage != sentStage || snapshot.IsFinished)
            {
                // 前に送った文章の続きなら足りない分だけ、書き直されていれば違うところから後ろを送る
                var offset = CommonPrefixLength(sentText, snapshot.Text);
                yield return new BackgroundJobStreamEvent(snapshot.State, snapshot.Stage, offset, snapshot.Text[offset..], snapshot.ErrorMessages);
                sentText = snapshot.Text;
                sentStage = snapshot.Stage;
                isFirst = false;
            }

            if (snapshot.IsFinished)
            {
                yield break;
            }

            await changed.WaitAsync(cancellationToken);

            // 1文字ずつ送らず、少しためてから送る
            await Task.Delay(StreamBatchDelay, cancellationToken);
        }
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < length && a[i] == b[i])
        {
            i++;
        }

        return i;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, WorkerCount).Select(_ => RunWorkerAsync(stoppingToken));
        return Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunJobAsync(entry, stoppingToken);
        }
    }

    private async Task RunJobAsync(JobEntry entry, CancellationToken stoppingToken)
    {
        entry.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(entry.Deadline);

        try
        {
            // 処理ごとにスコープを作る（リクエストのスコープは使い回せないため）
            await using var scope = _scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundUserScope>().User = entry.User;

            var result = await entry.Work(scope.ServiceProvider, entry, deadline.Token);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (result.HasError)
            {
                entry.Fail(result.ErrorMessages, now);
            }
            else
            {
                entry.Succeed(result.Value, now);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            entry.Fail(["時間がかかりすぎたため、途中で止めました。もう一度試してください。"], _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バックグラウンドの処理に失敗しました: {Kind} {JobId}", entry.Kind, entry.Id);
            entry.Fail(["処理に失敗しました。もう一度試してください。"], _timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    /// <summary>終わってから24時間たった処理を消す</summary>
    private void RemoveExpired()
    {
        var threshold = _timeProvider.GetUtcNow().UtcDateTime - Retention;
        foreach (var (id, entry) in _jobs)
        {
            if (entry.FinishedAt is { } finished && finished < threshold)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    /// <summary>1つの処理</summary>
    private sealed class JobEntry(string id, string kind, string? ownerKey, CurrentUserInfo? user, TimeSpan deadline, BackgroundWork work, DateTime createdAt) : IJobProgress
    {
        private readonly object _lock = new();
        private BackgroundJobState _state = BackgroundJobState.Queued;
        private string? _stage;
        private object? _result;
        private IReadOnlyList<string> _errors = [];
        private string _text = string.Empty;

        // 変わったら完了させて作り直す（流している側は、これを待って次を送る）
        private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id { get; } = id;

        public string Kind { get; } = kind;

        public string? OwnerKey { get; } = ownerKey;

        public CurrentUserInfo? User { get; } = user;

        public TimeSpan Deadline { get; } = deadline;

        public BackgroundWork Work { get; } = work;

        public DateTime CreatedAt { get; } = createdAt;

        public DateTime? FinishedAt { get; private set; }

        public void Report(string stage)
        {
            lock (_lock)
            {
                _stage = stage;
                NotifyChanged();
            }
        }

        public void ReportText(string text)
        {
            lock (_lock)
            {
                _text = text ?? string.Empty;
                NotifyChanged();
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                _state = BackgroundJobState.Running;
                NotifyChanged();
            }
        }

        public void Succeed(object? result, DateTime now)
        {
            lock (_lock)
            {
                _state = BackgroundJobState.Succeeded;
                _result = result;
                FinishedAt = now;
                NotifyChanged();
            }
        }

        public void Fail(IEnumerable<string> errors, DateTime now)
        {
            lock (_lock)
            {
                _state = BackgroundJobState.Failed;
                _errors = errors.ToList();
                FinishedAt = now;
                NotifyChanged();
            }
        }

        /// <summary>流すための今の状態と、次に変わったら完了する Task</summary>
        public (StreamSnapshot Snapshot, Task Changed) Snapshot()
        {
            lock (_lock)
            {
                var isFinished = _state is BackgroundJobState.Succeeded or BackgroundJobState.Failed;
                return (new StreamSnapshot(_state, _stage, _text, _errors, isFinished), _changed.Task);
            }
        }

        // _lock の中で呼ぶ
        private void NotifyChanged()
        {
            var changed = _changed;
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult();
        }

        public BackgroundJobStatus ToStatus()
        {
            lock (_lock)
            {
                return new BackgroundJobStatus(Id, Kind, OwnerKey, _state, _stage, _result, _errors, CreatedAt, FinishedAt);
            }
        }
    }

    /// <summary>流すための、ある時点の状態</summary>
    private sealed record StreamSnapshot(BackgroundJobState State, string? Stage, string Text, IReadOnlyList<string> ErrorMessages, bool IsFinished);
}
