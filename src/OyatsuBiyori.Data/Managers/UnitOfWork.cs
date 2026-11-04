using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Managers;

/// <summary>
/// DbContext の変更をまとめて保存する。row_version の衝突は ConcurrencyConflictException にする（D-46）。
/// </summary>
public sealed class UnitOfWork(OyatsuBiyoriDbContext context) : IUnitOfWork
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var savedMenus = _context.ChangeTracker.Entries<Menu>()
            .Where(e => e.State is EntityState.Modified or EntityState.Added)
            .Select(e => e.Entity)
            .ToList();
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }

        // row_version は SQLite のトリガーで保存のあとに変わる。画面に返す値と次の保存の比較に使うため、必ず読み直す（D-46）
        foreach (var menu in savedMenus)
        {
            var current = await _context.Menus.AsNoTracking().Where(m => m.Id == menu.Id).Select(m => m.RowVersion).FirstOrDefaultAsync(cancellationToken);
            var entry = _context.Entry(menu);
            entry.Property(m => m.RowVersion).CurrentValue = current;
            entry.Property(m => m.RowVersion).OriginalValue = current;
            entry.Property(m => m.RowVersion).IsModified = false;
        }
    }
}
