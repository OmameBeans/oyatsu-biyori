using System.Globalization;
using System.Text;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 文字列の正規化。保存するときと探すときで同じ関数を使う。
/// - ForProductMatch：商品照合用（NFKC → ひらがな → 「CO・OP」「コープ」の除去 → 記号・空白の除去）
/// - ForKeywordSearch：探す言葉の検索用（NFKC → ひらがな → 空白・改行・記号の除去。コープの除去はしない。03 §5）
/// - ForVolume：内容量の比較用（NFKC → 空白の除去 → ×・x・＊と括弧の書き方をそろえる）
/// どれも英字は小文字にそろえる（表記ゆれで見落とさないため）。
/// </summary>
public static class TextNormalizer
{
    /// <summary>商品照合用の正規化</summary>
    public static string ForProductMatch(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // 記号を除く前に「CO・OP」「コープ」を除く（中黒で区切られた表記も1語として扱うため）
        var kana = Build(text, keepSymbols: true).Text;
        kana = kana.Replace("co・op", string.Empty, StringComparison.Ordinal)
                   .Replace("co-op", string.Empty, StringComparison.Ordinal)
                   .Replace("こーぷ", string.Empty, StringComparison.Ordinal);
        return Build(kana, keepSymbols: false).Text;
    }

    /// <summary>探す言葉の検索用の正規化</summary>
    public static string ForKeywordSearch(string? text)
    {
        return ForKeywordSearchWithMap(text).Text;
    }

    /// <summary>
    /// 探す言葉の検索用の正規化。正規化後の各文字が、元の文字列のどこから来たかの対応も返す
    /// （当たった言葉を全文の上で強調表示するため）。
    /// </summary>
    public static NormalizedText ForKeywordSearchWithMap(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return NormalizedText.Empty;
        }

        return Build(text, keepSymbols: false);
    }

    /// <summary>内容量の比較用の正規化</summary>
    public static string ForVolume(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(c switch
            {
                '×' or '*' or '＊' or '✕' or '✖' => 'x',
                '[' or '【' or '「' or '〔' => '(',
                ']' or '】' or '」' or '〕' => ')',
                _ => c,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// 元の文字列を「基になる文字 + 後ろに続く濁点・半濁点・結合文字」の単位に分けて正規化し、
    /// 正規化後の各文字に元の範囲を記録する。単位ごとに処理するので、全文と言葉で同じ結果になる。
    /// </summary>
    private static NormalizedText Build(string text, bool keepSymbols)
    {
        var builder = new StringBuilder(text.Length);
        var starts = new List<int>(text.Length);
        var lengths = new List<int>(text.Length);

        // 直前の文字（空白・改行は飛ばし、取り除く記号は含める）。長音に似た文字を「ー」にそろえるかの判断に使う
        var previous = '\0';

        var index = 0;
        while (index < text.Length)
        {
            var length = SegmentLength(text, index);
            // 離れて書かれた全角の濁点・半濁点（゛゜）は、結合用の濁点・半濁点にしてから NFKC で前の文字とまとめる
            var segment = text.Substring(index, length)
                .Replace('゛', '゙')
                .Replace('゜', '゚')
                .Normalize(NormalizationForm.FormKC);

            foreach (var c in segment)
            {
                var converted = ToHiragana(char.ToLowerInvariant(c));

                // かなの直後の、長音に似た文字（全角ハイフン・ダッシュ・漢数字の「一」など）は「ー」にそろえる
                // （「ヘ－ゼル」「ヘ一ゼル」と読み取られても「へーぜる」に当たるように。確認する側に倒す）。
                // 読点などの記号を挟んだら、そろえない（「ソース、一味」の「一味」を、言葉「一味」と同じにするため）
                if (IsLongVowelLookalike(converted) && IsKanaOrLongVowel(previous))
                {
                    converted = 'ー';
                }

                if (!char.IsWhiteSpace(converted))
                {
                    previous = converted;
                }

                if (!keepSymbols && IsRemoved(converted))
                {
                    continue;
                }

                builder.Append(converted);
                starts.Add(index);
                lengths.Add(length);
            }

            index += length;
        }

        return new NormalizedText(builder.ToString(), starts, lengths);
    }

    /// <summary>1つの単位の長さ（基になる文字と、後ろに続く濁点・半濁点・結合文字・サロゲートの後半）</summary>
    private static int SegmentLength(string text, int start)
    {
        var length = char.IsHighSurrogate(text[start]) && start + 1 < text.Length ? 2 : 1;
        while (start + length < text.Length && IsAttached(text[start + length]))
        {
            length++;
        }

        return length;
    }

    /// <summary>前の文字にくっつく文字か（半角・全角の濁点・半濁点、結合文字）</summary>
    private static bool IsAttached(char c)
    {
        if (c is 'ﾞ' or 'ﾟ' or '゛' or '゜')
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    /// <summary>カタカナをひらがなにする（長音「ー」はそのまま）</summary>
    private static char ToHiragana(char c)
    {
        if (c is >= 'ァ' and <= 'ヶ')
        {
            return (char)(c - 0x60);
        }

        if (c is 'ヽ' or 'ヾ')
        {
            return (char)(c - 0x60);
        }

        return c;
    }

    /// <summary>長音に似た文字か（読み取りで「ー」と取り違えやすいもの）</summary>
    private static bool IsLongVowelLookalike(char c)
    {
        return c is '-' or '‐' or '‑' or '‒' or '–' or '—' or '―'
            or '−' or '─' or '━' or '~' or '〜' or '～' or '一';
    }

    /// <summary>ひらがな・カタカナ・長音か</summary>
    private static bool IsKanaOrLongVowel(char c)
    {
        return c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';
    }

    /// <summary>取り除く文字か（空白・改行・記号・句読点・制御文字）。文字・数字・長音は残す</summary>
    private static bool IsRemoved(char c)
    {
        if (char.IsLetterOrDigit(c))
        {
            return false;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark);
    }
}

/// <summary>
/// 正規化した文字列と、正規化後の各文字の元の範囲。
/// </summary>
public sealed class NormalizedText
{
    private readonly IReadOnlyList<int> _starts;
    private readonly IReadOnlyList<int> _lengths;

    /// <summary>空の結果</summary>
    public static NormalizedText Empty { get; } = new(string.Empty, [], []);

    /// <summary>正規化した文字列</summary>
    public string Text { get; }

    internal NormalizedText(string text, IReadOnlyList<int> starts, IReadOnlyList<int> lengths)
    {
        Text = text;
        _starts = starts;
        _lengths = lengths;
    }

    /// <summary>
    /// 正規化後の範囲（開始位置・長さ）を、元の文字列の範囲に戻す。
    /// 途中の改行や記号も含めて、元の文字列でつながった範囲を返す。
    /// </summary>
    public (int Start, int Length) ToOriginalRange(int normalizedStart, int normalizedLength)
    {
        if (normalizedLength <= 0 || normalizedStart < 0 || normalizedStart + normalizedLength > Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedStart));
        }

        var last = normalizedStart + normalizedLength - 1;
        var start = _starts[normalizedStart];
        var end = _starts[last] + _lengths[last];
        return (start, end - start);
    }
}
