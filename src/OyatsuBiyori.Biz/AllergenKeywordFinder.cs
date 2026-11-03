using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 探す言葉の検索（T1-07、SR-14、03 §5・§6）。
/// 全文と言葉の両方を TextNormalizer.ForKeywordSearch で正規化してから部分一致で探し、
/// 当たった位置を元の文字列の範囲に戻して返す（全文の上で強調表示するため）。
/// </summary>
public static class AllergenKeywordFinder
{
    /// <summary>
    /// 全文の中で、品目の探す言葉に当たったものをすべて返す（同じ言葉の重なった出現も含める）。
    /// 品目の Keywords は読み込んでおくこと。保存済みの NormalizedKeyword は信用せず、Keyword を毎回正規化し直す
    /// （正規化を直したときに、古い値で見落とさないため）。
    /// </summary>
    /// <param name="text">全文（保存前の編集中のものでもよい）</param>
    /// <param name="allergens">探す品目（判定では避ける品目）</param>
    /// <returns>元の全文での開始位置の順</returns>
    public static IReadOnlyList<KeywordMatch> Find(string? text, IEnumerable<Allergen> allergens)
    {
        ArgumentNullException.ThrowIfNull(allergens);

        var allergenList = allergens.ToList();
        EnsureKeywordsLoaded(allergenList);

        var normalized = TextNormalizer.ForKeywordSearchWithMap(text);
        if (normalized.Text.Length == 0)
        {
            return [];
        }

        var matches = new List<KeywordMatch>();
        foreach (var allergen in allergenList)
        {
            foreach (var keyword in allergen.Keywords)
            {
                var normalizedKeyword = TextNormalizer.ForKeywordSearch(keyword.Keyword);
                if (normalizedKeyword.Length == 0)
                {
                    continue;
                }

                var index = normalized.Text.IndexOf(normalizedKeyword, StringComparison.Ordinal);
                while (index >= 0)
                {
                    var (start, length) = normalized.ToOriginalRange(index, normalizedKeyword.Length);
                    matches.Add(new KeywordMatch(allergen.Id, allergen.Name, keyword.Keyword, start, length, text!.Substring(start, length)));
                    index = normalized.Text.IndexOf(normalizedKeyword, index + 1, StringComparison.Ordinal);
                }
            }
        }

        return matches
            .OrderBy(m => m.Start)
            .ThenByDescending(m => m.Length)
            .ToList();
    }

    /// <summary>初期データで探す言葉を持つ品目（初期データの言葉は削除できない。D-50）</summary>
    private static readonly HashSet<string> SeedKeywordAllergenIds = MasterSeed.Keywords().Select(k => k.AllergenId).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 探す言葉が読み込まれているかを確かめる。施設で追加した品目（登録時に品目名を入れ、最後の1語は消せない。T1-20）と、
    /// 初期データで言葉を持つ品目（くるみ・落花生など。D-50）は必ず探す言葉を持つため、0件なら読み込み忘れとみなして止める
    /// （黙って探さないと SR-14 が効かず、ヘーゼルナッツの事例が起きるため）。
    /// </summary>
    private static void EnsureKeywordsLoaded(IEnumerable<Allergen> allergens)
    {
        var missing = allergens.FirstOrDefault(a =>
            a.Keywords.Count == 0 && (a.Category == AllergenCategory.FacilityAdded || SeedKeywordAllergenIds.Contains(a.Id)));
        if (missing is not null)
        {
            throw new InvalidOperationException($"品目「{missing.Name}」の探す言葉が読み込まれていません。品目は Keywords を含めて読み込んでください。");
        }
    }
}

/// <summary>
/// 探す言葉に当たったもの。
/// </summary>
/// <param name="AllergenId">品目</param>
/// <param name="AllergenName">品目の表示名</param>
/// <param name="Keyword">当たった探す言葉（登録された表記）</param>
/// <param name="Start">元の全文での開始位置</param>
/// <param name="Length">元の全文での長さ（途中の改行・記号を含む）</param>
/// <param name="MatchedText">元の全文で当たった部分</param>
public sealed record KeywordMatch(string AllergenId, string AllergenName, string Keyword, int Start, int Length, string MatchedText);
