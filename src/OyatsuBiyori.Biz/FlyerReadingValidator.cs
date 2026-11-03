using System.Text;
using System.Text.RegularExpressions;

namespace OyatsuBiyori.Biz;

/// <summary>
/// チラシの読み取り結果をアプリで確かめる（FR-FLYER-03f、D-17、T1-15。Claude は呼ばない）。
/// 見つけた点は抽出結果の一覧で印と理由を付けて示し、直すのは職員（読み取り結果は書き換えない）。
/// </summary>
public static partial class FlyerReadingValidator
{
    /// <summary>消費税率（軽減税率と標準税率）</summary>
    private static readonly decimal[] TaxRates = [1.08m, 1.10m];

    /// <summary>
    /// 読み取った商品の一覧を確かめる。
    /// </summary>
    /// <param name="items">読み取った商品（並び順の番号で結果を返す）</param>
    public static IReadOnlyList<FlyerReadingIssue> Validate(IReadOnlyList<FlyerReadItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var issues = new List<FlyerReadingIssue>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                issues.Add(new FlyerReadingIssue(i, FlyerReadingIssueKind.MissingName, "商品名が読み取れていません。チラシを見て入れてください"));
            }

            if (string.IsNullOrWhiteSpace(item.Volume))
            {
                issues.Add(new FlyerReadingIssue(i, FlyerReadingIssueKind.MissingVolume, "内容量が読み取れていません。チラシを見て入れてください"));
            }

            if (item.PriceIncludingTax is null or < 1)
            {
                issues.Add(new FlyerReadingIssue(i, FlyerReadingIssueKind.MissingPrice, "税込価格が読み取れていません。チラシの括弧内の税込価格を入れてください"));
            }
            else if (item.PriceExcludingTax is int excluding && !IsTaxConsistent(excluding, item.PriceIncludingTax.Value))
            {
                issues.Add(new FlyerReadingIssue(i, FlyerReadingIssueKind.TaxMismatch,
                    $"税抜 {excluding}円 と税込 {item.PriceIncludingTax}円 が合いません。どちらかの読み違いかもしれません。チラシで確かめてください"));
            }

            if (!string.IsNullOrWhiteSpace(item.Volume) && !IsPiecesCandidateSupported(item.Volume, item.PiecesPerPackCandidate))
            {
                issues.Add(new FlyerReadingIssue(i, FlyerReadingIssueKind.PiecesUnclear, item.PiecesPerPackCandidate is null
                    ? "内容量の表記から入数の候補が出せません。1パックで何人分かを入れてください"
                    : $"入数の候補 {item.PiecesPerPackCandidate} が内容量の表記と合いません。1パックで何人分かを確かめてください"));
            }
        }

        issues.AddRange(FindDuplicates(items));
        return issues.OrderBy(x => x.Index).ThenBy(x => x.Kind).ToList();
    }

    /// <summary>
    /// 税込が、税抜 × 1.08 か × 1.10 を 切り捨て・四捨五入・切り上げ したもののどれかに合えば整合（D-59）。
    /// </summary>
    public static bool IsTaxConsistent(int priceExcludingTax, int priceIncludingTax)
    {
        foreach (var rate in TaxRates)
        {
            var exact = priceExcludingTax * rate;
            if (Math.Floor(exact) == priceIncludingTax
                || Math.Round(exact, 0, MidpointRounding.AwayFromZero) == priceIncludingTax
                || Math.Ceiling(exact) == priceIncludingTax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 入数の候補が内容量の表記から出せるか：表記の数のどれか、または2つの数の積に合えば出せる
    /// （例：「40g（2袋×12個包装）」は 12 か 24、「4g×3袋」は 3、「10袋入」は 10）。
    /// 候補がない・1未満なら出せない。
    /// </summary>
    public static bool IsPiecesCandidateSupported(string? volume, int? piecesCandidate)
    {
        if (piecesCandidate is not int candidate || candidate < 1)
        {
            return false;
        }

        var numbers = NumberPattern().Matches((volume ?? string.Empty).Normalize(NormalizationForm.FormKC))
            .Select(m => decimal.TryParse(m.Value, out var n) ? n : -1m)
            .Where(n => n > 0)
            .ToList();

        if (numbers.Contains(candidate))
        {
            return true;
        }

        for (var a = 0; a < numbers.Count; a++)
        {
            for (var b = a + 1; b < numbers.Count; b++)
            {
                if (numbers[a] * numbers[b] == candidate)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>同じチラシの中の二重（正規化名と正規化した内容量が同じ）</summary>
    private static IEnumerable<FlyerReadingIssue> FindDuplicates(IReadOnlyList<FlyerReadItem> items)
    {
        var groups = items
            .Select((item, index) => (Index: index, Name: TextNormalizer.ForProductMatch(item.Name), Volume: TextNormalizer.ForVolume(item.Volume)))
            .Where(x => x.Name.Length > 0)
            .GroupBy(x => (x.Name, x.Volume))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var indexes = group.Select(x => x.Index).ToList();
            foreach (var index in indexes)
            {
                var others = string.Join("・", indexes.Where(x => x != index).Select(x => $"{x + 1}件目"));
                yield return new FlyerReadingIssue(index, FlyerReadingIssueKind.Duplicate,
                    $"{others}と同じ商品を二重に読み取ったかもしれません。チラシで確かめ、不要なほうを外してください");
            }
        }
    }

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex NumberPattern();
}

/// <summary>チラシから読み取った商品（確かめるのに要る項目だけ）</summary>
/// <param name="Name">商品名</param>
/// <param name="Volume">内容量の表記</param>
/// <param name="PriceIncludingTax">税込価格</param>
/// <param name="PriceExcludingTax">税抜価格</param>
/// <param name="PiecesPerPackCandidate">Claude が出した入数の候補</param>
public sealed record FlyerReadItem(string? Name, string? Volume, int? PriceIncludingTax, int? PriceExcludingTax, int? PiecesPerPackCandidate);

/// <summary>読み取り結果で見つけた点の種類</summary>
public enum FlyerReadingIssueKind
{
    /// <summary>商品名がない</summary>
    MissingName,

    /// <summary>内容量がない</summary>
    MissingVolume,

    /// <summary>税込価格がない</summary>
    MissingPrice,

    /// <summary>税抜と税込が合わない</summary>
    TaxMismatch,

    /// <summary>入数の候補が出せない・内容量と合わない</summary>
    PiecesUnclear,

    /// <summary>同じチラシの中の二重</summary>
    Duplicate,
}

/// <summary>読み取り結果で見つけた点</summary>
/// <param name="Index">商品の番号（0から）</param>
/// <param name="Kind">種類</param>
/// <param name="Message">理由と次にすること</param>
public sealed record FlyerReadingIssue(int Index, FlyerReadingIssueKind Kind, string Message);
