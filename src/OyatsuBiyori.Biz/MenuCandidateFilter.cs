using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立の候補の絞り込み（FR-MENU-07b・07c・29、FR-PROD-08、SR-09、T1-11）。
/// Claude に渡す候補・search_products・入れ替え案で同じものを使う。候補は掲載品の写し（menus.listed_products）から選び、
/// 判定が「入れられない」・価格か入数がない・対象外・今週は使わない・使わない の商品を外す。警告の商品は印を付けて残す。
/// </summary>
public static class MenuCandidateFilter
{
    /// <summary>掲載品の写しの商品を1つずつ評価する（手順2の一覧にも使う）</summary>
    public static IReadOnlyList<ListedProductEvaluation> Evaluate(Menu menu, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        EnsureUniqueListing(menu);
        return menu.ListedProducts.Select(listed => EvaluateListed(menu, listed, context)).ToList();
    }

    /// <summary>候補（外す理由がないもの）だけ</summary>
    public static IReadOnlyList<ListedProductEvaluation> Candidates(Menu menu, MenuContext context)
    {
        return Evaluate(menu, context).Where(e => e.IsCandidate).ToList();
    }

    /// <summary>
    /// 商品を、その献立に入れる商品として調べる（献立の検証・確定の判定でも使う）。外す理由をすべて返す。
    /// </summary>
    /// <param name="menu">献立（写し・今週は使わない）</param>
    /// <param name="productId">商品</param>
    /// <param name="context">判定に使うもの</param>
    public static ListedProductEvaluation EvaluateProduct(Menu menu, string productId, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        EnsureUniqueListing(menu);
        var listed = menu.ListedProducts.FirstOrDefault(l => l.ProductId == productId);
        return listed is null
            ? Build(menu, productId, null, context)
            : EvaluateListed(menu, listed, context);
    }

    /// <summary>写しは商品ごとに1行（T1-18）。2行あるとどちらの価格を使うか決められないため止める</summary>
    private static void EnsureUniqueListing(Menu menu)
    {
        var duplicate = menu.ListedProducts.GroupBy(l => l.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"掲載品の写しに同じ商品が2行あります: {duplicate.Key}");
        }
    }

    private static ListedProductEvaluation EvaluateListed(Menu menu, ListedProduct listed, MenuContext context) => Build(menu, listed.ProductId, listed, context);

    private static ListedProductEvaluation Build(Menu menu, string productId, ListedProduct? listed, MenuContext context)
    {
        var product = context.FindProduct(productId);
        var exclusions = new List<CandidateExclusion>();
        AllergenJudgement? judgement = null;

        if (product is null)
        {
            exclusions.Add(CandidateExclusion.ProductNotFound);
        }
        else
        {
            judgement = context.Judge(product);
            if (judgement.Category == AllergenJudgementCategory.Blocked)
            {
                exclusions.Add(CandidateExclusion.AllergenBlocked);
            }
        }

        if (listed is null)
        {
            exclusions.Add(CandidateExclusion.NotListed);
        }
        else if (listed.Price is null or < 1)
        {
            exclusions.Add(CandidateExclusion.MissingPrice);
        }

        if (product is not null)
        {
            if (product.PiecesPerPack is null or < 1)
            {
                exclusions.Add(CandidateExclusion.MissingPieces);
            }

            if (product.DefaultPerChildCount < 1)
            {
                // MenuItemFactory と同じ基準（パック数を計算できないため）
                exclusions.Add(CandidateExclusion.InvalidPerChildCount);
            }

            if (product.SnackType == SnackType.NotSnack)
            {
                exclusions.Add(CandidateExclusion.NotSnack);
            }

            if (product.IsDisabled)
            {
                exclusions.Add(CandidateExclusion.ProductDisabled);
            }
        }

        if (menu.ExcludedProductIds.Contains(productId))
        {
            exclusions.Add(CandidateExclusion.ExcludedThisWeek);
        }

        return new ListedProductEvaluation
        {
            ProductId = productId,
            Product = product,
            Listed = listed,
            Judgement = judgement,
            Exclusions = exclusions,
        };
    }
}

/// <summary>候補から外す理由</summary>
public enum CandidateExclusion
{
    /// <summary>存在しない商品</summary>
    ProductNotFound,

    /// <summary>判定が「入れられない」</summary>
    AllergenBlocked,

    /// <summary>その週の掲載品にない</summary>
    NotListed,

    /// <summary>写しの税込価格がない</summary>
    MissingPrice,

    /// <summary>商品の入数がない</summary>
    MissingPieces,

    /// <summary>商品の1人あたりの個数の既定が1未満</summary>
    InvalidPerChildCount,

    /// <summary>おやつの分類が「対象外」</summary>
    NotSnack,

    /// <summary>「使わない」商品</summary>
    ProductDisabled,

    /// <summary>「今週は使わない」</summary>
    ExcludedThisWeek,
}

/// <summary>
/// 掲載品の商品の評価。
/// </summary>
public sealed class ListedProductEvaluation
{
    /// <summary>商品 ID</summary>
    public string ProductId { get; init; } = string.Empty;

    /// <summary>商品（存在しなければ null）</summary>
    public Product? Product { get; init; }

    /// <summary>掲載品の写し（掲載品になければ null）</summary>
    public ListedProduct? Listed { get; init; }

    /// <summary>アレルゲンの判定（商品がなければ null）</summary>
    public AllergenJudgement? Judgement { get; init; }

    /// <summary>候補から外す理由（空なら候補）</summary>
    public IReadOnlyList<CandidateExclusion> Exclusions { get; init; } = [];

    /// <summary>候補か</summary>
    public bool IsCandidate => Exclusions.Count == 0;

    /// <summary>警告の印（判定が「警告」。候補にしてよいが、確定時に確認が要る）</summary>
    public bool IsWarning => Judgement?.Category == AllergenJudgementCategory.Warning;

    /// <summary>写しの税込価格（候補なら必ずある）</summary>
    public int? Price => Listed?.Price;

    /// <summary>職員のメモ（献立提案の参考。FR-PROD-08）</summary>
    public string? Memo => Product?.Memo;

    /// <summary>タグ（FR-MENU-06a）</summary>
    public IReadOnlyList<ProductTag> Tags => Product?.Tags ?? [];
}
