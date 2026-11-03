using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立の検証・確定の判定・入れ替え案・届いた後の影響に共通の入力（T1-10〜17）。
/// 判定は保存しないため（D-15）、要求のたびに作り直す。同じ商品の判定は1回だけ求めて使い回す。
/// </summary>
public sealed class MenuContext
{
    private readonly Dictionary<string, Product> _products;
    private readonly Dictionary<string, AllergenJudgement> _judgements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fingerprints = new(StringComparer.Ordinal);

    /// <summary>
    /// 作る。products には、掲載品の写しの商品だけでなく、献立に入っているすべての商品を入れる
    /// （読み忘れると「掲載品にない」が「存在しない商品」になる）。商品は AllergenChecks → Items と Tags、
    /// 避ける品目は Keywords を読み込んでおくこと。
    /// </summary>
    /// <param name="products">商品</param>
    /// <param name="avoidedAllergens">施設の避ける品目</param>
    /// <param name="today">日本時間の今日</param>
    /// <param name="recheckDays">再確認期間（日）</param>
    /// <param name="duplicateRules">分類ごとの重複させない期間（今の設定）</param>
    /// <param name="confirmedHistory">確定献立で出した商品（ServedItem.FromMenus で作る）</param>
    public MenuContext(
        IEnumerable<Product> products,
        IEnumerable<Allergen> avoidedAllergens,
        DateOnly today,
        int recheckDays,
        IEnumerable<DuplicateAvoidRule> duplicateRules,
        IEnumerable<ServedItem> confirmedHistory)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(avoidedAllergens);
        ArgumentNullException.ThrowIfNull(duplicateRules);
        ArgumentNullException.ThrowIfNull(confirmedHistory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recheckDays);

        _products = products.DistinctBy(p => p.Id, StringComparer.Ordinal).ToDictionary(p => p.Id, StringComparer.Ordinal);
        AvoidedAllergens = avoidedAllergens.DistinctBy(a => a.Id, StringComparer.Ordinal).OrderBy(a => a.SortOrder).ToList();
        Today = today;
        RecheckDays = recheckDays;
        DuplicateRules = duplicateRules.ToList();
        ConfirmedHistory = confirmedHistory.ToList();
    }

    /// <summary>施設の避ける品目</summary>
    public IReadOnlyList<Allergen> AvoidedAllergens { get; }

    /// <summary>日本時間の今日</summary>
    public DateOnly Today { get; }

    /// <summary>再確認期間（日）</summary>
    public int RecheckDays { get; }

    /// <summary>分類ごとの重複させない期間</summary>
    public IReadOnlyList<DuplicateAvoidRule> DuplicateRules { get; }

    /// <summary>確定献立で出した商品</summary>
    public IReadOnlyList<ServedItem> ConfirmedHistory { get; }

    /// <summary>商品（なければ null）</summary>
    public Product? FindProduct(string productId) => _products.GetValueOrDefault(productId);

    /// <summary>商品の入数（商品がなければ null）</summary>
    public int? PiecesPerPack(string productId) => FindProduct(productId)?.PiecesPerPack;

    /// <summary>
    /// 商品の判定（03 §5）。確認済みの前提を満たさない記録などの例外は、捕まえずにそのまま伝える（警告に落とさない）。
    /// </summary>
    public AllergenJudgement Judge(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!_judgements.TryGetValue(product.Id, out var judgement))
        {
            judgement = AllergenJudge.Judge(product, AvoidedAllergens, Today, RecheckDays);
            _judgements[product.Id] = judgement;
        }

        return judgement;
    }

    /// <summary>判定の中身の指紋（確定時の確認と比べる。SR-22）</summary>
    public string Fingerprint(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!_fingerprints.TryGetValue(product.Id, out var fingerprint))
        {
            fingerprint = AllergenJudgementFingerprint.Compute(product.Id, Judge(product), AvoidedAllergens.Select(a => a.Id));
            _fingerprints[product.Id] = fingerprint;
        }

        return fingerprint;
    }

    /// <summary>分類の重複させない期間（日）。設定なし・対象外なら null</summary>
    public int? DuplicateAvoidDays(SnackType snackType)
    {
        if (snackType == SnackType.NotSnack)
        {
            return null;
        }

        var days = DuplicateRules.Where(r => r.SnackType == snackType && r.Days > 0).Select(r => (int?)r.Days).ToList();
        return days.Count == 0 ? null : days.Max();
    }
}

/// <summary>確定献立で出した商品（重複の基準）</summary>
/// <param name="MenuId">献立</param>
/// <param name="Date">出した日（日本時間）</param>
/// <param name="ProductId">商品</param>
public sealed record ServedItem(string MenuId, DateOnly Date, string ProductId)
{
    /// <summary>
    /// 献立から作る。基準にするのは確定献立だけ（FR-MENU-12）。手作り・おやつなしの日の商品は、実際には出さないため数えない。
    /// </summary>
    public static IReadOnlyList<ServedItem> FromMenus(IEnumerable<Menu> menus)
    {
        ArgumentNullException.ThrowIfNull(menus);

        return menus
            .Where(m => m.Status == MenuStatus.Confirmed)
            .SelectMany(m => m.Days
                .Where(d => d.DayType == DayType.Normal)
                .SelectMany(d => d.Items.Select(i => new ServedItem(m.Id, d.Date, i.ProductId))))
            .ToList();
    }
}
