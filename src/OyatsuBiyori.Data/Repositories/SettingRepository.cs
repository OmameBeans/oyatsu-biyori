using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Repositories;

/// <summary>
/// 施設の条件・避ける品目・アレルゲンのマスタ。
/// </summary>
public sealed class SettingRepository(OyatsuBiyoriDbContext context) : ISettingRepository
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<Setting> GetSettingAsync()
    {
        return await _context.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("施設の条件（settings）がありません。マイグレーションを当ててください");
    }

    public async Task<IReadOnlyList<Allergen>> GetAllergensAsync()
    {
        return await _context.Allergens
            .Include(a => a.Keywords)
            .OrderBy(a => a.SortOrder)
            .ToListAsync();
    }

    public async Task<Allergen?> GetAllergenAsync(string allergenId)
    {
        return await _context.Allergens
            .Include(a => a.Keywords)
            .FirstOrDefaultAsync(a => a.Id == allergenId);
    }

    public async Task<IReadOnlyList<AllergenGroup>> GetGroupsAsync()
    {
        return await _context.AllergenGroups
            .Include(g => g.Members)
            .OrderBy(g => g.SortOrder)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AvoidedAllergen>> GetAvoidedAsync()
    {
        return await _context.AvoidedAllergens
            .Include(a => a.Allergen)
            .ThenInclude(a => a.Keywords)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Allergen>> GetAvoidedAllergensAsync()
    {
        var avoided = await GetAvoidedAsync();
        return avoided.Select(a => a.Allergen).OrderBy(a => a.SortOrder).ToList();
    }

    public async Task<AllergenKeyword?> GetKeywordAsync(string keywordId)
    {
        return await _context.AllergenKeywords.FirstOrDefaultAsync(k => k.Id == keywordId);
    }

    public async Task<bool> IsUsedByChecksAsync(string allergenId)
    {
        return await _context.AllergenCheckItems.AnyAsync(i => i.AllergenId == allergenId);
    }

    public async Task<bool> IsInGroupAsync(string allergenId)
    {
        return await _context.AllergenGroupMembers.AnyAsync(m => m.AllergenId == allergenId);
    }

    public void AddAvoided(AvoidedAllergen avoided) => _context.AvoidedAllergens.Add(avoided);

    public void RemoveAvoided(AvoidedAllergen avoided) => _context.AvoidedAllergens.Remove(avoided);

    public void AddAllergen(Allergen allergen) => _context.Allergens.Add(allergen);

    public void RemoveAllergen(Allergen allergen) => _context.Allergens.Remove(allergen);

    public void AddKeyword(AllergenKeyword keyword) => _context.AllergenKeywords.Add(keyword);

    public void RemoveKeyword(AllergenKeyword keyword) => _context.AllergenKeywords.Remove(keyword);

    public void AddSafetyLog(SafetySettingLog log) => _context.SafetySettingLogs.Add(log);
}
