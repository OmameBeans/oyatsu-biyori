using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 商品照合（D-100、09 の 2、FR-IMPORT-01〜05）。正規化した「商品名＋メーカー」の完全一致だけで、商品マスタの商品と同じかを決める。
/// 内容量・税込価格・注文番号は照合に使わない（内容量の変化は取り込みの分類の「再入力」で扱う）。
/// 似ている商品の候補（Dice 係数）・Claude（Haiku）の判定は使わない。一致しない商品は新しい商品として扱う。
/// </summary>
public static class ProductMatcher
{
    /// <summary>
    /// チラシの1商品を、商品マスタの商品と照合する。使わない（IsDisabled）商品も照合する（同じ商品を二重に登録しないため。
    /// 取り込みの分類では「使えない」になる）。
    /// 保存済みの NormalizedName は使わず、毎回マスタの商品名・メーカーから正規化し直す（正規化を直しても保存値とずれないため。09 の 2）。
    /// </summary>
    /// <param name="name">チラシの商品名（直した値があればその値）</param>
    /// <param name="maker">チラシのメーカー（直した値があればその値）</param>
    /// <param name="products">商品マスタの商品（全件）</param>
    public static ProductMatch Match(string? name, string? maker, IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        var key = KeyOf(name, maker);
        if (key.NotMatchableReason is { } reason)
        {
            // 商品名かメーカーが空なら照合しない（「一致なし」とは分ける。メーカーを入れたら照合し直す。FR-IMPORT-03）
            return new ProductMatch(ProductMatchOutcome.NotMatchable, null, [], reason);
        }

        var matched = products
            .Where(p => KeyOf(p.Name, p.Maker) is { NotMatchableReason: null } productKey
                && string.Equals(productKey.Name, key.Name, StringComparison.Ordinal)
                && string.Equals(productKey.Maker, key.Maker, StringComparison.Ordinal))
            // 2件以上なら、最後に載った週 → 更新日時 → ID の順（商品に登録日時の列がないため。FR-IMPORT-04）
            .OrderByDescending(p => p.ListedWeekStart.HasValue)
            .ThenByDescending(p => p.ListedWeekStart)
            .ThenByDescending(p => p.UpdateDateTime)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return matched.Count == 0
            ? new ProductMatch(ProductMatchOutcome.NotFound, null, [], null)
            : new ProductMatch(ProductMatchOutcome.Matched, matched[0], matched.Skip(1).ToList(), null);
    }

    /// <summary>
    /// 照合に使う鍵（正規化した商品名とメーカー）。同じ取り込みの中の二重の読み取りをまとめるときにも使う（FR-IMPORT-06）。
    /// 商品名かメーカーが正規化で空になるなら、照合できない理由を返す。
    /// </summary>
    public static ProductMatchKey KeyOf(string? name, string? maker)
    {
        var normalizedName = NormalizeName(name);
        var normalizedMaker = NormalizeMaker(maker);
        ProductNotMatchableReason? reason = normalizedName.Length == 0
            ? ProductNotMatchableReason.MissingName
            : normalizedMaker.Length == 0
                ? ProductNotMatchableReason.MissingMaker
                : null;
        return new ProductMatchKey(normalizedName, normalizedMaker, reason);
    }

    /// <summary>商品名の照合用の正規化（NFKC → ひらがな → 「CO・OP」「コープ」の除去 → 記号・空白の除去）</summary>
    public static string NormalizeName(string? name) => TextNormalizer.ForProductMatch(name);

    /// <summary>
    /// メーカーの照合用の正規化（NFKC → ひらがな → 記号・空白の除去）。「コープ」は除かない
    /// （メーカーが「コープ」だけの商品のメーカーが空にならないように。D-100 の 14.1）
    /// </summary>
    public static string NormalizeMaker(string? maker) => TextNormalizer.ForKeywordSearch(maker);
}

/// <summary>照合の結果の種類</summary>
public enum ProductMatchOutcome
{
    /// <summary>商品マスタに一致する商品がある</summary>
    Matched,

    /// <summary>照合したが、一致する商品がない（新しい商品）</summary>
    NotFound,

    /// <summary>商品名かメーカーが空なので照合していない（取り込みの分類では「再入力」）</summary>
    NotMatchable,
}

/// <summary>照合できない理由</summary>
public enum ProductNotMatchableReason
{
    /// <summary>商品名が空</summary>
    MissingName,

    /// <summary>メーカーが空</summary>
    MissingMaker,
}

/// <summary>
/// 照合に使う鍵。
/// </summary>
/// <param name="Name">正規化した商品名</param>
/// <param name="Maker">正規化したメーカー</param>
/// <param name="NotMatchableReason">照合できない理由（照合できるなら null）</param>
public sealed record ProductMatchKey(string Name, string Maker, ProductNotMatchableReason? NotMatchableReason)
{
    /// <summary>照合できるか（商品名とメーカーの両方がある）</summary>
    public bool IsMatchable => NotMatchableReason is null;
}

/// <summary>
/// 照合の結果。
/// </summary>
/// <param name="Outcome">結果の種類</param>
/// <param name="Product">使う商品（一致したときだけ。2件以上なら FR-IMPORT-04 の順で先頭）</param>
/// <param name="OtherMatches">ほかに一致した商品（画面で「ほかに同じ名前の商品が n 件あります」と示す）</param>
/// <param name="NotMatchableReason">照合できない理由（NotMatchable のときだけ）</param>
public sealed record ProductMatch(ProductMatchOutcome Outcome, Product? Product, IReadOnlyList<Product> OtherMatches, ProductNotMatchableReason? NotMatchableReason)
{
    /// <summary>一致する商品があるか</summary>
    public bool IsMatched => Outcome == ProductMatchOutcome.Matched;
}
