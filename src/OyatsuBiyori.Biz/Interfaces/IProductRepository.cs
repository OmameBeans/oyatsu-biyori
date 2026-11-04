using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// 商品とアレルゲン確認の記録（取得したものは追跡され、IUnitOfWork で保存する）。
/// 商品は、確認の記録（Items つき）とタグを読み込んで返す（判定に必要なため）。
/// </summary>
public interface IProductRepository
{
    /// <summary>商品（なければ null）</summary>
    Task<Product?> GetAsync(string productId);

    /// <summary>複数の商品（見つかったものだけ）</summary>
    Task<IReadOnlyList<Product>> GetManyAsync(IEnumerable<string> productIds);

    /// <summary>すべての商品（使わない商品も含む）</summary>
    Task<IReadOnlyList<Product>> GetAllAsync();

    /// <summary>JAN コードの商品（なければ null）</summary>
    Task<Product?> FindByJanAsync(string janCode);

    /// <summary>商品を足す</summary>
    void Add(Product product);

    /// <summary>確認の記録を足す（追加のみ。更新・削除はしない。SR-07）</summary>
    void AddCheck(AllergenCheck check);

    /// <summary>タグを足す</summary>
    void AddTag(ProductTag tag);

    /// <summary>タグを消す</summary>
    void RemoveTag(ProductTag tag);
}
