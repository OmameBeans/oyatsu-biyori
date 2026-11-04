using System.Collections.Concurrent;
using OyatsuBiyori.Biz.FlyerImport;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Server.BackgroundJobs;

/// <summary>
/// 確定前のチラシの読み取り結果をメモリに持つ（献立ごと・24時間。再起動したら消える。FR-FLYER-10・D-36）。
/// 説明文はここにだけ持ち、DB には保存しない。
/// </summary>
public sealed class InMemoryFlyerSessionStore(TimeProvider timeProvider) : IFlyerSessionStore
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ConcurrentDictionary<string, FlyerImportSession> _sessions = new();

    public FlyerImportSession? Get(string menuId)
    {
        RemoveExpired();
        return _sessions.TryGetValue(menuId, out var session) ? session : null;
    }

    public void Set(FlyerImportSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RemoveExpired();
        _sessions[session.MenuId] = session;
    }

    public void Remove(string menuId) => _sessions.TryRemove(menuId, out _);

    private void RemoveExpired()
    {
        var threshold = _timeProvider.GetUtcNow().UtcDateTime - Retention;
        foreach (var (key, session) in _sessions)
        {
            if (session.CreatedAt < threshold)
            {
                _sessions.TryRemove(key, out _);
            }
        }
    }
}
