using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Repositories;

/// <summary>
/// Claude API の呼び出しの記録。
/// </summary>
public sealed class ApiUsageLogRepository(OyatsuBiyoriDbContext context) : IApiUsageLogRepository
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<bool> AddAsync(ApiUsageLog log)
    {
        _context.ApiUsageLogs.Add(log);
        return await _context.SaveChangesAsync() > 0;
    }

    public Task<int> CountSinceAsync(DateTime sinceUtc)
    {
        return _context.ApiUsageLogs.CountAsync(l => l.CalledAt >= sinceUtc);
    }

    public async Task<IReadOnlyList<ApiUsageLog>> GetSinceAsync(DateTime sinceUtc)
    {
        return await _context.ApiUsageLogs.AsNoTracking().Where(l => l.CalledAt >= sinceUtc).ToListAsync();
    }
}
