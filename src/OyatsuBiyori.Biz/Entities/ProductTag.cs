using System.ComponentModel.DataAnnotations.Schema;
using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 商品のタグ（キャラクター・味・種類）。
/// </summary>
public sealed class ProductTag : EntityBase
{
    /// <summary>商品</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>商品</summary>
    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    /// <summary>種類</summary>
    public ProductTagKind Kind { get; set; }

    /// <summary>値（味・種類は ProductTagVocabulary の語だけ）</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>出どころ</summary>
    public ProductTagSource Source { get; set; }
}
