using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// 同じ取り込みの中で2回以上読んだ商品（半分ずつ撮った写真の重なりなど）を1件にまとめる（FR-IMPORT-06、TI-03）。
/// まとめる鍵は、照合と同じ正規化をした「商品名＋メーカー」（ProductMatcher.KeyOf）。商品名かメーカーが空の行はまとめない（再入力で直してもらう）。
/// アレルゲン表記の品目・品目の分からない語・説明文は和集合にする（確認する側に倒す）。
/// 税込価格・税抜価格・内容量・注文番号が食い違えば、先に読んだ値を使い、食い違った値を両方持つ（取り込みの分類で「注意」になる。D-101）。値が全部同じならまとめるだけ。
/// </summary>
public static class FlyerDuplicateMerger
{
    /// <summary>表記の原文をつなぐ区切り（FlyerAllergenPlanner.ParseLabel の区切りの1つ）</summary>
    private const string LabelJoiner = "／";

    /// <summary>
    /// 読み取った商品をまとめる。並びは、まとめた商品が最初に出てきた順。
    /// </summary>
    /// <param name="items">Claude が読み取った商品（読み取りの順）</param>
    public static IReadOnlyList<FlyerMergedRead> Merge(IReadOnlyList<FlyerReadProduct> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var groups = new List<List<int>>();
        var groupByKey = new Dictionary<(string Name, string Maker), List<int>>();
        for (var i = 0; i < items.Count; i++)
        {
            var key = ProductMatcher.KeyOf(items[i].Name, items[i].Maker);
            if (!key.IsMatchable)
            {
                groups.Add([i]);
                continue;
            }

            if (groupByKey.TryGetValue((key.Name, key.Maker), out var group))
            {
                group.Add(i);
                continue;
            }

            group = [i];
            groupByKey[(key.Name, key.Maker)] = group;
            groups.Add(group);
        }

        return groups.Select(g => MergeGroup(items, g)).ToList();
    }

    private static FlyerMergedRead MergeGroup(IReadOnlyList<FlyerReadProduct> items, List<int> indexes)
    {
        var reads = indexes.Select(i => items[i]).ToList();
        if (reads.Count == 1)
        {
            return new FlyerMergedRead(reads[0], indexes, []);
        }

        var first = reads[0];
        var conflicts = new List<FlyerDuplicateConflict>();
        var priceIncludingTax = MergeValue(reads, r => r.PriceIncludingTax?.ToString(), FlyerItemField.PriceIncludingTax, conflicts, r => r.PriceIncludingTax);
        var priceExcludingTax = MergeValue(reads, r => r.PriceExcludingTax?.ToString(), FlyerItemField.PriceExcludingTax, conflicts, r => r.PriceExcludingTax);
        var volume = MergeText(reads, r => r.Volume, TextNormalizer.ForVolume, FlyerItemField.Volume, conflicts);
        var orderNo = MergeText(reads, r => r.OrderNo, TextNormalizer.ForKeywordSearch, FlyerItemField.OrderNo, conflicts);
        // 入数の候補も、読めた値が分かれたら食い違い（パック数・金額の計算に使うので、黙って片方を選ばない）
        var pieces = MergeValue(reads, r => r.PiecesPerPackCandidate is >= 1 ? r.PiecesPerPackCandidate.ToString() : null, FlyerItemField.PiecesPerPack, conflicts,
            r => r.PiecesPerPackCandidate is >= 1 ? r.PiecesPerPackCandidate : null);

        // 表記は、表記のあるものをすべてつなぐ。「表記なし」は、どれも表記なしと読んだときだけ（表記の品目を消さない）
        var labelTexts = reads
            .Select(r => r.AllergenLabelText?.Trim())
            .OfType<string>()
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var merged = first with
        {
            Volume = volume,
            PriceIncludingTax = priceIncludingTax,
            PriceExcludingTax = priceExcludingTax,
            OrderNo = orderNo,
            TargetAgeLabel = FirstText(reads, r => r.TargetAgeLabel),
            AllergenLabelText = labelTexts.Count == 0 ? first.AllergenLabelText : string.Join(LabelJoiner, labelTexts),
            AllergenLabelAbsent = reads.All(r => r.AllergenLabelAbsent),
            AllergenCodes = Union(reads.SelectMany(r => r.AllergenCodes)),
            UnmappedAllergenTerms = Union(reads.SelectMany(r => r.UnmappedAllergenTerms)),
            Description = JoinTexts(reads.Select(r => r.Description)),
            BulkNote = FirstText(reads, r => r.BulkNote),
            PiecesPerPackCandidate = pieces ?? first.PiecesPerPackCandidate,
            PiecesPerPackReason = reads.FirstOrDefault(r => r.PiecesPerPackCandidate is >= 1)?.PiecesPerPackReason ?? first.PiecesPerPackReason,
            SnackType = reads.Select(r => r.SnackType).FirstOrDefault(t => t is not null),
            CharacterTags = Union(reads.SelectMany(r => r.CharacterTags)),
            FlavorTags = Union(reads.SelectMany(r => r.FlavorTags)),
        };

        return new FlyerMergedRead(merged, indexes, conflicts);
    }

    /// <summary>数の項目：読めた値が2つ以上に分かれたら食い違い（読めなかった行は数えない）。値は最初に読めたもの</summary>
    private static int? MergeValue(List<FlyerReadProduct> reads, Func<FlyerReadProduct, string?> display, string field, List<FlyerDuplicateConflict> conflicts, Func<FlyerReadProduct, int?> value)
    {
        var values = reads.Select(value).OfType<int>().Distinct().ToList();
        if (values.Count > 1)
        {
            conflicts.Add(new FlyerDuplicateConflict(field, reads.Select(display).OfType<string>().Distinct(StringComparer.Ordinal).ToList()));
        }

        return values.Count == 0 ? null : values[0];
    }

    /// <summary>文字の項目：正規化して2つ以上に分かれたら食い違い（空の行は数えない）。値は最初に読めたもの</summary>
    private static string? MergeText(List<FlyerReadProduct> reads, Func<FlyerReadProduct, string?> value, Func<string?, string> normalize, string field, List<FlyerDuplicateConflict> conflicts)
    {
        var texts = reads.Select(value).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).ToList();
        if (texts.Select(normalize).Distinct(StringComparer.Ordinal).Count() > 1)
        {
            conflicts.Add(new FlyerDuplicateConflict(field, texts.Distinct(StringComparer.Ordinal).ToList()));
        }

        return texts.Count == 0 ? value(reads[0]) : texts[0];
    }

    private static string? FirstText(List<FlyerReadProduct> reads, Func<FlyerReadProduct, string?> value) =>
        reads.Select(value).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? value(reads[0]);

    private static string? JoinTexts(IEnumerable<string?> texts)
    {
        var list = texts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? null : string.Join("\n", list);
    }

    private static List<string> Union(IEnumerable<string> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>
/// まとめた読み取り。
/// </summary>
/// <param name="Read">まとめた読み取り（1件だけ読んだ商品はそのまま）</param>
/// <param name="SourceIndexes">まとめた元の読み取りの番号（0から。読み取りの検証の指摘の番号を、まとめた商品に付け替えるため）</param>
/// <param name="Conflicts">食い違った項目と値（なければ空）</param>
public sealed record FlyerMergedRead(FlyerReadProduct Read, IReadOnlyList<int> SourceIndexes, IReadOnlyList<FlyerDuplicateConflict> Conflicts)
{
    /// <summary>2回以上読んだ商品をまとめたか</summary>
    public bool IsMerged => SourceIndexes.Count > 1;
}

/// <summary>
/// 二重の読み取りで食い違った項目（取り込みの分類で「注意」になる。先に読んだ値を使う）。
/// </summary>
/// <param name="Field">項目（FlyerItemField の値）</param>
/// <param name="Values">食い違った値（読み取りの表記のまま。読めなかった行の値は入れない）</param>
public sealed record FlyerDuplicateConflict(string Field, IReadOnlyList<string> Values);
