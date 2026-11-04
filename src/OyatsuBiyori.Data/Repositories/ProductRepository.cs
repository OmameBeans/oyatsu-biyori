using Microsoft.EntityFrameworkCore;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Data.DbContexts;

namespace OyatsuBiyori.Data.Repositories;

/// <summary>
/// 商品とアレルゲン確認の記録。判定に要るため、確認の記録（Items つき）とタグを必ず読み込む。
/// </summary>
public sealed class ProductRepository(OyatsuBiyoriDbContext context) : IProductRepository
{
    private readonly OyatsuBiyoriDbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    private IQueryable<Product> WithDetails => _context.Products
        .Include(p => p.Tags)
        .Include(p => p.AllergenChecks)
        .ThenInclude(c => c.Items)
        .AsSplitQuery();

    public async Task<Product?> GetAsync(string productId)
    {
        return await WithDetails.FirstOrDefaultAsync(p => p.Id == productId);
    }

    public async Task<IReadOnlyList<Product>> GetManyAsync(IEnumerable<string> productIds)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await WithDetails.Where(p => ids.Contains(p.Id)).ToListAsync();
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync()
    {
        return await WithDetails.OrderBy(p => p.NormalizedName).ToListAsync();
    }

    public async Task<Product?> FindByJanAsync(string janCode)
    {
        return await _context.Products.FirstOrDefaultAsync(p => p.JanCode == janCode);
    }

    public void Add(Product product) => _context.Products.Add(product);

    public void AddCheck(AllergenCheck check) => _context.AllergenChecks.Add(check);

    public void AddTag(ProductTag tag) => _context.ProductTags.Add(tag);

    public void RemoveTag(ProductTag tag) => _context.ProductTags.Remove(tag);
}
