using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// アレルゲンの判定（03 §5 の疑似コード、T1-05）。
/// ステータスより先に「含む」「含む可能性あり」を調べ、どの条件にも当たらないときだけ「安全」にする。
/// 判定は保存せず、表示・検証のたびに求める（避ける品目・探す言葉・再確認期間の変更をすぐ反映するため）。
/// </summary>
public static class AllergenJudge
{
    /// <summary>
    /// 商品を判定する。商品の AllergenChecks（と各記録の Items）、避ける品目の Keywords は読み込んでおくこと。
    /// </summary>
    /// <param name="product">商品</param>
    /// <param name="avoidedAllergens">施設の避ける品目</param>
    /// <param name="today">日本時間の今日（JapanDate.Today で求める）</param>
    /// <param name="recheckDays">再確認期間（日）</param>
    public static AllergenJudgement Judge(Product product, IEnumerable<Allergen> avoidedAllergens, DateOnly today, int recheckDays)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Judge(product.Name, product.AllergenChecks, avoidedAllergens, today, recheckDays);
    }

    /// <summary>
    /// 商品名と確認の記録から判定する。記録の Items、避ける品目の Keywords は読み込んでおくこと。
    /// </summary>
    public static AllergenJudgement Judge(string productName, IEnumerable<AllergenCheck> checks, IEnumerable<Allergen> avoidedAllergens, DateOnly today, int recheckDays)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(avoidedAllergens);
        // 設定の不整合を黙って「毎回要再確認」にしない
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recheckDays);

        var history = AllergenCheckHistory.From(checks);
        var avoided = avoidedAllergens
            .DistinctBy(a => a.Id, StringComparer.Ordinal)
            .OrderBy(a => a.SortOrder)
            .ToList();

        // 理由はすべて集めてから、疑似コードの順で種類を決める（画面で理由をまとめて示すため）
        var contained = avoided
            .Where(a => history.ContainedAllergenIds.Contains(a.Id))
            .Select(ToRef)
            .ToList();
        var keywordHits = FindKeywordHits(productName, history, avoided);
        var unconfirmed = history.LatestConfirmed is null
            ? []
            : avoided
                .Where(a => !history.LatestConfirmed.Items.Any(i => i.AllergenId == a.Id))
                .Select(ToRef)
                .ToList();
        var manufacturerCheck = avoided
            .Where(a => a.LabelNotGuaranteed)
            .Select(ToRef)
            .ToList();

        var (category, kind) = Decide(history, avoided.Count > 0, contained.Count > 0, keywordHits.Count > 0, unconfirmed.Count > 0, today, recheckDays);

        return new AllergenJudgement
        {
            Category = category,
            Kind = kind,
            ContainedAllergens = contained,
            KeywordHits = keywordHits,
            UnconfirmedAllergens = unconfirmed,
            // 入れられない商品には付けない（「献立に入りません」をほかの表示で薄めないため）
            ManufacturerCheckAllergens = category == AllergenJudgementCategory.Blocked ? [] : manufacturerCheck,
            LatestCheckId = history.Latest?.Id,
            LatestConfirmedCheckId = history.LatestConfirmed?.Id,
            LastConfirmedDate = history.LastConfirmedDate,
        };
    }

    /// <summary>03 §5 の疑似コードの順で、区分と細かい種類を決める</summary>
    private static (AllergenJudgementCategory Category, AllergenJudgementKind Kind) Decide(
        AllergenCheckHistory history, bool hasAvoided, bool hasContained, bool hasKeywordHits, bool hasUnconfirmed, DateOnly today, int recheckDays)
    {
        if (hasContained)
        {
            return (AllergenJudgementCategory.Blocked, AllergenJudgementKind.Contains);
        }

        if (hasKeywordHits)
        {
            return (AllergenJudgementCategory.Blocked, AllergenJudgementKind.MayContain);
        }

        if (history.Latest is null)
        {
            return hasAvoided
                ? (AllergenJudgementCategory.Blocked, AllergenJudgementKind.Unknown)
                : (AllergenJudgementCategory.Warning, AllergenJudgementKind.Unknown);
        }

        if (history.Latest.Status == AllergenCheckStatus.Reference)
        {
            return (AllergenJudgementCategory.Warning, AllergenJudgementKind.Reference);
        }

        if (history.IsRecheckNeeded(today, recheckDays))
        {
            return (AllergenJudgementCategory.Warning, AllergenJudgementKind.RecheckNeeded);
        }

        if (hasUnconfirmed)
        {
            return (AllergenJudgementCategory.Warning, AllergenJudgementKind.UnconfirmedItems);
        }

        return (AllergenJudgementCategory.Safe, AllergenJudgementKind.Safe);
    }

    /// <summary>
    /// 商品名と、有効な記録すべての全文（Claude の読み取り・職員の修正の両方）から探す言葉を探す（SR-14）。
    /// 同一工場・コンタミの注意書き（ContaminationNote）は探さない（判定は職員に委ねる。SR-11）。
    /// </summary>
    private static List<AllergenKeywordHit> FindKeywordHits(string? productName, AllergenCheckHistory history, IReadOnlyList<Allergen> avoided)
    {
        var hits = new List<AllergenKeywordHit>();
        if (avoided.Count == 0)
        {
            return hits;
        }

        hits.AddRange(AllergenKeywordFinder.Find(productName, avoided)
            .Select(m => new AllergenKeywordHit(m, KeywordHitSource.ProductName, null)));

        foreach (var check in history.ValidChecks)
        {
            hits.AddRange(AllergenKeywordFinder.Find(check.RawIngredientsOcr, avoided)
                .Select(m => new AllergenKeywordHit(m, KeywordHitSource.RawIngredientsOcr, check.Id)));
            hits.AddRange(AllergenKeywordFinder.Find(check.RawIngredients, avoided)
                .Select(m => new AllergenKeywordHit(m, KeywordHitSource.RawIngredients, check.Id)));
        }

        return hits;
    }

    private static AllergenRef ToRef(Allergen allergen) => new(allergen.Id, allergen.Name);
}

/// <summary>
/// アレルゲンの判定の結果。
/// </summary>
public sealed class AllergenJudgement
{
    /// <summary>区分（入れられない / 警告 / 安全）</summary>
    public AllergenJudgementCategory Category { get; init; }

    /// <summary>細かい判定の種類（確定時に確認した判定として保存し、変わったら確認し直す。SR-22）</summary>
    public AllergenJudgementKind Kind { get; init; }

    /// <summary>避ける品目のうち、有効な記録で「含む」の品目</summary>
    public IReadOnlyList<AllergenRef> ContainedAllergens { get; init; } = [];

    /// <summary>商品名・原材料の全文で当たった、避ける品目の探す言葉</summary>
    public IReadOnlyList<AllergenKeywordHit> KeywordHits { get; init; } = [];

    /// <summary>避ける品目のうち、最新の「確認済み」の記録で含む / 含まないを確定していない品目（SR-16）</summary>
    public IReadOnlyList<AllergenRef> UnconfirmedAllergens { get; init; } = [];

    /// <summary>「メーカーへの確認が必要」の品目（避ける品目のうち表示が確実でないもの。SR-06。入れられないときは空）</summary>
    public IReadOnlyList<AllergenRef> ManufacturerCheckAllergens { get; init; } = [];

    /// <summary>最新の記録（なければ null）</summary>
    public string? LatestCheckId { get; init; }

    /// <summary>最新の「確認済み」の記録（なければ null）</summary>
    public string? LatestConfirmedCheckId { get; init; }

    /// <summary>最終確認日（日本時間）</summary>
    public DateOnly? LastConfirmedDate { get; init; }

    /// <summary>「メーカーへの確認が必要」を添えるか</summary>
    public bool NeedsManufacturerCheck => ManufacturerCheckAllergens.Count > 0;
}

/// <summary>品目の参照（Id と表示名）</summary>
public sealed record AllergenRef(string Id, string Name);

/// <summary>
/// 判定で当たった探す言葉。
/// </summary>
/// <param name="Match">当たった言葉と範囲</param>
/// <param name="Source">当たった場所</param>
/// <param name="CheckId">全文で当たったときの確認の記録（商品名なら null）</param>
public sealed record AllergenKeywordHit(KeywordMatch Match, KeywordHitSource Source, string? CheckId);
