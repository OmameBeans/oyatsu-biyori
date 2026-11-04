using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Menus;

/// <summary>
/// 献立の検証・判定に使う MenuContext を、リポジトリから組み立てる（判定は保存しないため、要求のたびに作る。D-15）。
/// </summary>
public sealed class MenuContextFactory(
    IProductRepository productRepository,
    ISettingRepository settingRepository,
    IMenuRepository menuRepository,
    TimeProvider timeProvider)
{
    private readonly IProductRepository _products = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>日本時間の今日</summary>
    public DateOnly Today => JapanDate.Today(_timeProvider);

    /// <summary>
    /// 1つの献立のための MenuContext（掲載品の写しと献立に入っている商品、重複の基準の確定献立を読む）。
    /// </summary>
    public async Task<MenuContext> ForMenuAsync(Menu menu, IEnumerable<string>? extraProductIds = null)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var setting = await _settings.GetSettingAsync();
        var (from, to) = DuplicateChecker.HistoryRange(menu.WeekStart, setting.DuplicateAvoidDays);
        var history = await _menus.GetConfirmedInRangeAsync(from, to);
        var productIds = menu.ListedProducts.Select(l => l.ProductId)
            .Concat(menu.Days.SelectMany(d => d.Items).Select(i => i.ProductId))
            .Concat(extraProductIds ?? []);
        var products = await _products.GetManyAsync(productIds);
        return new MenuContext(
            products,
            await _settings.GetAvoidedAllergensAsync(),
            Today,
            setting.RecheckDays,
            setting.DuplicateAvoidDays,
            ServedItem.FromMenus(history));
    }

    /// <summary>指定した商品だけの判定に使う MenuContext（商品詳細。重複は見ない）</summary>
    public async Task<MenuContext> ForProductsAsync(IEnumerable<Product> products)
    {
        var setting = await _settings.GetSettingAsync();
        return new MenuContext(products, await _settings.GetAvoidedAllergensAsync(), Today, setting.RecheckDays, setting.DuplicateAvoidDays, []);
    }

    /// <summary>すべての商品の判定に使う MenuContext（献立一覧・商品一覧。重複は見ない）</summary>
    public async Task<(MenuContext Context, IReadOnlyList<Product> Products)> ForAllProductsAsync()
    {
        var setting = await _settings.GetSettingAsync();
        var products = await _products.GetAllAsync();
        var context = new MenuContext(products, await _settings.GetAvoidedAllergensAsync(), Today, setting.RecheckDays, setting.DuplicateAvoidDays, []);
        return (context, products);
    }
}

/// <summary>
/// アレルゲンの判定の表示用（T4-01 の共通部品に渡す）。文言は画面の共通部品で作り、ここでは材料だけを返す。
/// </summary>
/// <param name="Category">区分（色を決める）</param>
/// <param name="Kind">細かい判定の種類</param>
/// <param name="ContainedAllergens">含む避ける品目</param>
/// <param name="KeywordHits">当たった探す言葉</param>
/// <param name="UnconfirmedAllergens">確認していない避ける品目</param>
/// <param name="ManufacturerCheckAllergens">メーカーへの確認が必要な品目</param>
/// <param name="ReferenceSource">参考のとき、最新の記録の情報源</param>
/// <param name="ReferenceHasNoLabel">参考のとき、チラシに表記がなかったか</param>
/// <param name="ReferenceAfterConfirmed">参考のとき、確認済みのあとに足された記録か（SR-20 の食い違い）</param>
/// <param name="ContainsFromReference">「含む」がパッケージ以外の記録だけから来ているか（パッケージで確認し直せば変わることがある）</param>
/// <param name="DaysSinceConfirmed">最後にパッケージで確認してからの日数</param>
/// <param name="LastConfirmedDate">最終確認日</param>
public sealed record AllergenJudgementView(
    AllergenJudgementCategory Category,
    AllergenJudgementKind Kind,
    IReadOnlyList<string> ContainedAllergens,
    IReadOnlyList<KeywordHitView> KeywordHits,
    IReadOnlyList<string> UnconfirmedAllergens,
    IReadOnlyList<string> ManufacturerCheckAllergens,
    AllergenCheckSource? ReferenceSource,
    bool ReferenceHasNoLabel,
    bool ReferenceAfterConfirmed,
    bool ContainsFromReference,
    int? DaysSinceConfirmed,
    DateOnly? LastConfirmedDate)
{
    /// <summary>判定から作る</summary>
    public static AllergenJudgementView From(Product product, AllergenJudgement judgement, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(judgement);

        var history = AllergenCheckHistory.From(product.AllergenChecks);
        var latest = history.Latest;
        var isReference = latest?.Status == AllergenCheckStatus.Reference;
        var confirmedContains = history.LatestConfirmed?.Items.Where(i => i.Contains).Select(i => i.AllergenId).ToHashSet(StringComparer.Ordinal) ?? [];
        var containsFromReference = judgement.ContainedAllergens.Count > 0
            && judgement.ContainedAllergens.All(a => !confirmedContains.Contains(a.Id));

        return new AllergenJudgementView(
            judgement.Category,
            judgement.Kind,
            judgement.ContainedAllergens.Select(a => a.Name).ToList(),
            judgement.KeywordHits
                .Select(h => new KeywordHitView(h.Match.AllergenName, h.Match.Keyword, h.Match.MatchedText, h.Source))
                .DistinctBy(h => (h.AllergenName, h.MatchedText, h.Source))
                .ToList(),
            judgement.UnconfirmedAllergens.Select(a => a.Name).ToList(),
            judgement.ManufacturerCheckAllergens.Select(a => a.Name).ToList(),
            isReference ? latest!.Source : null,
            isReference && latest!.Source == AllergenCheckSource.Flyer && (latest.SourceNote ?? string.Empty).StartsWith("表記なし", StringComparison.Ordinal),
            isReference && history.LatestConfirmed is not null,
            containsFromReference,
            judgement.LastConfirmedDate is { } date ? today.DayNumber - date.DayNumber : null,
            judgement.LastConfirmedDate);
    }
}

/// <summary>当たった探す言葉（表示用）</summary>
public sealed record KeywordHitView(string AllergenName, string Keyword, string MatchedText, KeywordHitSource Source);
