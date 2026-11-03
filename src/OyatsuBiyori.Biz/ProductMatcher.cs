using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 商品照合（02 §3.1、T1-04）。ルールで決められるもの（完全一致・内容量違い）はここで機械的に決め、
/// それ以外は文字バイグラムの Dice 係数で候補を出して、迷うものだけ Claude（Haiku）に回す（D-63）。
/// 決めるのは候補まで。確定は職員が行い、迷う場合は別商品として扱う。
/// </summary>
public static class ProductMatcher
{
    /// <summary>
    /// チラシの1商品を、既存の商品と照合する。使わない（IsDisabled）商品も照合する（同じ商品を二重に登録しないため）。
    /// </summary>
    public static ProductMatchResult Match(ProductMatchInput input, IEnumerable<Product> products, ProductMatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(options);

        // 保存済みの NormalizedName は使わず、毎回同じ関数で正規化し直す（正規化を直したときに古い値でずれないため）
        var name = TextNormalizer.ForProductMatch(input.Name);
        var volume = TextNormalizer.ForVolume(input.Volume);
        var maker = NormalizeMaker(input.Maker);

        var sameProducts = new List<ProductRuleMatch>();
        var volumeVariants = new List<ProductRuleMatch>();
        var scored = new List<ProductMatchCandidate>();

        foreach (var product in products)
        {
            var productName = TextNormalizer.ForProductMatch(product.Name);
            var productMaker = NormalizeMaker(product.Maker);
            var makerCompared = maker.Length > 0 && productMaker.Length > 0;
            var sameMaker = !makerCompared || maker == productMaker;

            // 内容量が片方か両方にないときは、同じとも違うとも決めず Claude に回す
            // （空どうしを「同じ商品」にすると、根拠なくアレルゲン確認を引き継ぐことになるため。SR-05）
            var productVolume = TextNormalizer.ForVolume(product.Volume);
            if (name.Length > 0 && name == productName && sameMaker && volume.Length > 0 && productVolume.Length > 0)
            {
                if (volume == productVolume)
                {
                    sameProducts.Add(new ProductRuleMatch(product, makerCompared));
                }
                else
                {
                    volumeVariants.Add(new ProductRuleMatch(product, makerCompared));
                }

                continue;
            }

            var score = DiceCoefficient(name, productName);
            if (score >= options.DiceThreshold)
            {
                scored.Add(new ProductMatchCandidate(product, score));
            }
        }

        var candidates = scored
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Product.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Product.Id, StringComparer.Ordinal)
            .Take(options.MaxCandidates)
            .ToList();

        return new ProductMatchResult
        {
            SameProducts = sameProducts,
            VolumeVariants = volumeVariants,
            Candidates = candidates,
        };
    }

    /// <summary>
    /// 文字バイグラム（重複を数える）の Dice 係数。2 × 共通のバイグラム数 ÷ (A のバイグラム数 + B のバイグラム数)。
    /// 1文字の文字列は、その1文字を1つの単位として比べる。どちらかが空なら 0。
    /// </summary>
    public static double DiceCoefficient(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        var gramsA = Bigrams(a);
        var gramsB = Bigrams(b);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var gram in gramsA)
        {
            counts[gram] = counts.GetValueOrDefault(gram) + 1;
        }

        var common = 0;
        foreach (var gram in gramsB)
        {
            if (counts.TryGetValue(gram, out var count) && count > 0)
            {
                counts[gram] = count - 1;
                common++;
            }
        }

        return 2.0 * common / (gramsA.Count + gramsB.Count);
    }

    private static List<string> Bigrams(string text)
    {
        if (text.Length < 2)
        {
            return [text];
        }

        var grams = new List<string>(text.Length - 1);
        for (var i = 0; i < text.Length - 1; i++)
        {
            grams.Add(text.Substring(i, 2));
        }

        return grams;
    }

    /// <summary>メーカーの比較用の正規化（「コープ」は除かない。メーカー名そのものが消えないように）</summary>
    private static string NormalizeMaker(string? maker)
    {
        return TextNormalizer.ForKeywordSearch(maker);
    }
}

/// <summary>
/// 照合するチラシの商品。
/// </summary>
/// <param name="Name">商品名</param>
/// <param name="Volume">内容量</param>
/// <param name="Maker">メーカー（チラシでは空のことが多い）</param>
public sealed record ProductMatchInput(string Name, string? Volume, string? Maker);

/// <summary>
/// 照合の設定（appsettings の「ProductMatch」で変える）。
/// </summary>
public sealed class ProductMatchOptions
{
    /// <summary>設定のセクション名</summary>
    public const string SectionName = "ProductMatch";

    /// <summary>候補にする Dice 係数のしきい値（D-02。実際のチラシで調整する）</summary>
    public double DiceThreshold { get; set; } = 0.3;

    /// <summary>候補の最大件数</summary>
    public int MaxCandidates { get; set; } = 5;
}

/// <summary>
/// Dice 係数で出した候補。
/// </summary>
/// <param name="Product">既存の商品</param>
/// <param name="Score">Dice 係数（0〜1）</param>
public sealed record ProductMatchCandidate(Product Product, double Score);

/// <summary>
/// ルールで決まった候補。
/// </summary>
/// <param name="Product">既存の商品（IsDisabled なら「使わない」商品として画面で示す）</param>
/// <param name="MakerCompared">メーカーを比べたか（片方が空で比べていなければ、画面で「メーカー未確認」と示す）</param>
public sealed record ProductRuleMatch(Product Product, bool MakerCompared);

/// <summary>
/// 照合の結果（候補まで。確定は職員）。
/// </summary>
public sealed class ProductMatchResult
{
    /// <summary>「同じ商品」の候補（正規化名・内容量が一致し、メーカーは両方にあるときだけ一致。内容量は両方にあるときだけ）</summary>
    public IReadOnlyList<ProductRuleMatch> SameProducts { get; init; } = [];

    /// <summary>「内容量違い」の候補（正規化名が同じで、内容量が両方にあって違う）。アレルゲン確認は引き継がない（SR-05）</summary>
    public IReadOnlyList<ProductRuleMatch> VolumeVariants { get; init; } = [];

    /// <summary>Dice 係数がしきい値以上の上位の候補（ルールで決まった商品は除く）</summary>
    public IReadOnlyList<ProductMatchCandidate> Candidates { get; init; } = [];

    /// <summary>ルールで決まったか（決まったものは Claude を呼ばない）</summary>
    public bool IsDecidedByRule => SameProducts.Count > 0 || VolumeVariants.Count > 0;

    /// <summary>
    /// 「同じ商品」としてまとめて確定できるか（FR-FLYER-06a）。同じ商品の候補がちょうど1件のときだけ。
    /// 2件以上（既存の商品の二重）なら、まとめて確定せず職員に選ばせる（既定は「新しい商品」）。
    /// </summary>
    public bool CanBulkConfirmAsSame => SameProducts.Count == 1;

    /// <summary>Claude（Haiku）に回すか（ルールで決まらず、似ている候補があるとき）</summary>
    public bool NeedsJudgement => !IsDecidedByRule && Candidates.Count > 0;
}
