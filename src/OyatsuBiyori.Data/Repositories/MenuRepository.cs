using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Repositories;

/// <summary>
/// 献立。日ごとの献立と商品を必ず読み込む。
/// </summary>
public sealed class MenuRepository(OyatsuBiyoriDbContext context) : IMenuRepository
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private IQueryable<Menu> WithDays => _context.Menus
        .Include(m => m.Days)
        .ThenInclude(d => d.Items)
        .AsSplitQuery();

    public async Task<Menu?> GetAsync(string menuId)
    {
        return await WithDays.FirstOrDefaultAsync(m => m.Id == menuId);
    }

    public async Task<Menu?> GetByWeekStartAsync(DateOnly weekStart)
    {
        return await WithDays.FirstOrDefaultAsync(m => m.WeekStart == weekStart);
    }

    public async Task<IReadOnlyList<Menu>> GetAllAsync()
    {
        return await WithDays.OrderByDescending(m => m.WeekStart).ToListAsync();
    }

    public async Task<IReadOnlyList<Menu>> GetConfirmedInRangeAsync(DateOnly from, DateOnly to)
    {
        return await WithDays
            .Where(m => m.Status == MenuStatus.Confirmed && m.Days.Any(d => d.Date >= from && d.Date <= to))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Menu>> GetFromDateAsync(DateOnly from)
    {
        return await WithDays
            .Where(m => m.Days.Any(d => d.Date >= from))
            .OrderBy(m => m.WeekStart)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<MenuMessage>> GetMessagesAsync(string menuId)
    {
        return await _context.MenuMessages
            .Where(m => m.MenuId == menuId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public void SetExpectedRowVersion(Menu menu, byte[] rowVersion)
    {
        _context.Entry(menu).Property(m => m.RowVersion).OriginalValue = rowVersion;
    }

    public async Task RefreshRowVersionAsync(Menu menu)
    {
        var current = await _context.Menus.AsNoTracking().Where(m => m.Id == menu.Id).Select(m => m.RowVersion).FirstOrDefaultAsync();
        var entry = _context.Entry(menu);
        entry.Property(m => m.RowVersion).CurrentValue = current;
        entry.Property(m => m.RowVersion).OriginalValue = current;
        entry.Property(m => m.RowVersion).IsModified = false;
    }

    public void Add(Menu menu) => _context.Menus.Add(menu);

    public void Remove(Menu menu) => _context.Menus.Remove(menu);

    public void RemoveItem(MenuItem item) => _context.MenuItems.Remove(item);

    public void AddMessage(MenuMessage message) => _context.MenuMessages.Add(message);

    public void RemoveMessages(IEnumerable<MenuMessage> messages) => _context.MenuMessages.RemoveRange(messages);
}
