using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// チラシを取り込んだときのアレルゲンの扱い（03 §3、SR-19・SR-20、FR-FLYER-09、T1-08）。
/// チラシの品目＝アレルゲン表記の品目（マスタの品目に対応づけたもの）∪ 説明文で避ける品目の探す言葉が当たった品目。
/// 書かれていない品目を「含まない」にはしない。「確認済み」にはしない（参考の記録だけを作る）。
/// </summary>
public static class FlyerAllergenPlanner
{
    /// <summary>表記を語に分ける区切り（中黒・読点・括弧・空白など）</summary>
    private static readonly char[] LabelSeparators =
    [
        '・', '･', '、', '､', ',', '，', '/', '／', '|', '｜', ' ', '　', '\t', '\r', '\n',
        '(', ')', '（', '）', '[', ']', '［', '］', '【', '】', '「', '」', '〔', '〕', '<', '>', '＜', '＞',
    ];

    /// <summary>「表記なし」とみなす表記（正規化したあとの値。記号だけの「－」「—」は正規化で空になる）</summary>
    private static readonly string[] NoLabelTerms = ["なし", "無し", "表記なし", "表示なし"];

    /// <summary>
    /// チラシのアレルゲン表記（例：「乳・小麦」「（一部に乳成分・卵を含む）」）を、マスタの品目に対応づける。
    /// 対応づけできない語は捨てずに返す（画面で職員にチラシと見比べて対応づけさせる）。
    /// 対応づけは確認する側に倒す：語が品目名・探す言葉を含めば対応づけ（乳成分・牛乳 → 乳、卵白・鶏卵 → 卵）、
    /// 1つの語が複数の品目に当たればすべて採る（多めに当たっても「参考」の記録として職員の確認に回るだけのため）。
    /// 「なし」「－」だけの表記は「表記なし」として扱い、対応づけできない語にしない（どの品目も「含まない」にはしない）。
    /// チラシの略記（「麦」→小麦、「え」→えび、「カ」→カシューナッツ など。マスタの略記）は、語全体が一致したときだけ当てる
    /// （「くっきー」「えびせん」「カカオ」は略記に当てない。D-96）。略記はかな変換をかけずに比べる（「か」＝かに、「カ」＝カシューナッツ。D-100）。
    /// </summary>
    /// <param name="labelText">チラシのアレルゲン表記の原文（なければ null か空）</param>
    /// <param name="allergens">マスタのすべての品目（探す言葉・略記つき）</param>
    public static FlyerLabelParseResult ParseLabel(string? labelText, IEnumerable<Allergen> allergens)
    {
        ArgumentNullException.ThrowIfNull(allergens);

        var allergenList = allergens.ToList();
        var mapped = new List<Allergen>();
        var unmapped = new List<string>();
        var abbreviationHits = new List<FlyerAbbreviationHit>();

        foreach (var term in (labelText ?? string.Empty).Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = StripLabelPhrases(TextNormalizer.ForKeywordSearch(term));
            if (normalized.Length == 0 || NoLabelTerms.Contains(normalized, StringComparer.Ordinal))
            {
                continue;
            }

            // 品目名・探す言葉はかなの違いを問わずに当て、略記はかな変換をかけずに当てる（「か」＝かに、「カ」＝カシューナッツ。FR-IMPORT-32）
            var abbreviationTerm = NormalizeAbbreviationTerm(term);
            var abbreviationMatches = allergenList.Where(a => MatchesAbbreviation(a, abbreviationTerm)).ToList();
            var hits = allergenList.Where(a => MatchesLabelTerm(a, normalized) || abbreviationMatches.Contains(a)).ToList();
            if (hits.Count == 0)
            {
                unmapped.Add(term);
                continue;
            }

            mapped.AddRange(hits);
            abbreviationHits.AddRange(abbreviationMatches.Select(a => new FlyerAbbreviationHit(term, new AllergenRef(a.Id, a.Name))));
        }

        return new FlyerLabelParseResult
        {
            Allergens = mapped
                .DistinctBy(a => a.Id, StringComparer.Ordinal)
                .OrderBy(a => a.SortOrder)
                .Select(a => new AllergenRef(a.Id, a.Name))
                .ToList(),
            UnmappedTerms = unmapped.Distinct(StringComparer.Ordinal).ToList(),
            AbbreviationHits = abbreviationHits.Distinct().ToList(),
        };
    }

    /// <summary>
    /// チラシの1商品について、チラシの品目を求め、「参考」の記録を追加するかを 03 §3 の表で決める。
    /// </summary>
    /// <param name="input">チラシの表記（職員が見比べて直したあとの品目）と説明文</param>
    /// <param name="allAllergens">マスタのすべての品目（探す言葉つき）</param>
    /// <param name="avoidedAllergenIds">施設の避ける品目</param>
    /// <param name="existingChecks">その商品の確認の記録（Items つき。新しい商品なら空）</param>
    public static FlyerAllergenPlan Plan(FlyerAllergenInput input, IEnumerable<Allergen> allAllergens, IEnumerable<string> avoidedAllergenIds, IEnumerable<AllergenCheck> existingChecks)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(allAllergens);
        ArgumentNullException.ThrowIfNull(avoidedAllergenIds);
        ArgumentNullException.ThrowIfNull(existingChecks);

        var allergensById = allAllergens.DistinctBy(a => a.Id, StringComparer.Ordinal).ToDictionary(a => a.Id, StringComparer.Ordinal);
        var avoidedIds = avoidedAllergenIds.ToHashSet(StringComparer.Ordinal);

        foreach (var id in input.LabelAllergenIds.Concat(avoidedIds))
        {
            if (!allergensById.ContainsKey(id))
            {
                throw new ArgumentException($"マスタにない品目です: {id}", nameof(input));
            }
        }

        // 説明文は、避ける品目の探す言葉を探すためだけに使う（保存しない。FR-FLYER-03）
        var descriptionHits = AllergenKeywordFinder.Find(input.Description, avoidedIds.Select(id => allergensById[id]));

        var flyerAllergenIds = input.LabelAllergenIds
            .Concat(descriptionHits.Select(h => h.AllergenId))
            .ToHashSet(StringComparer.Ordinal);
        var flyerAllergens = flyerAllergenIds
            .Select(id => allergensById[id])
            .OrderBy(a => a.SortOrder)
            .Select(a => new AllergenRef(a.Id, a.Name))
            .ToList();

        var history = AllergenCheckHistory.From(existingChecks);
        // 有効な記録で「含む」になっていない品目（確認済みで「含まない」の品目も、行がなく未確定の品目も入る）
        var notYetContained = flyerAllergens.Where(a => !history.ContainedAllergenIds.Contains(a.Id)).ToList();
        var labelIds = input.LabelAllergenIds.ToHashSet(StringComparer.Ordinal);
        var hasConfirmed = history.LatestConfirmed is not null;

        FlyerReferenceDecision decision;
        IReadOnlyList<FlyerAllergenDiscrepancy> discrepancies = [];
        if (history.Latest is null)
        {
            // 記録がない → 追加する（品目がなければ「表記なし」）
            decision = FlyerReferenceDecision.Add;
        }
        else if (hasConfirmed)
        {
            // 過去に確認済みがある → 追加しない。ただしチラシの品目が「含む」になっていなければ食い違い（SR-20）。
            // 食い違いがあれば、避ける品目かどうかにかかわらず「参考」の記録を足す（職員に選ばせない。確認する側に倒す。D-100、09 の 3.5）
            discrepancies = notYetContained
                .Select(a => new FlyerAllergenDiscrepancy(a, avoidedIds.Contains(a.Id), !labelIds.Contains(a.Id)))
                .ToList();
            decision = discrepancies.Count == 0
                ? FlyerReferenceDecision.None
                : FlyerReferenceDecision.AddForDiscrepancy;
        }
        else
        {
            // 参考だけ → チラシの品目が増えたときだけ追加する（減っても追加しない）
            decision = notYetContained.Count > 0 ? FlyerReferenceDecision.Add : FlyerReferenceDecision.None;
        }

        return new FlyerAllergenPlan
        {
            FlyerAllergens = flyerAllergens,
            DescriptionHits = descriptionHits,
            Decision = decision,
            Discrepancies = discrepancies,
            UnresolvedLabelTerms = input.UnresolvedLabelTerms,
            SourceNote = BuildSourceNote(input.LabelText, descriptionHits),
        };
    }

    /// <summary>
    /// チラシの品目を「含む」とした「参考」の記録を作る（情報源はチラシ）。追加するかは呼び出し側が plan.ShouldAddReference で決める。
    /// 書かれていない品目の行は作らない（「含まない」にしない）。
    /// 対応づけできていない表記の語が残っていれば作らない（その語の品目が「含む」に入らないまま保存されるため）。
    /// </summary>
    public static AllergenCheck CreateReferenceCheck(string productId, FlyerAllergenPlan plan, DateTime checkedAtUtc, string checkedBy, string? updateUserId)
    {
        ArgumentException.ThrowIfNullOrEmpty(productId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkedBy);
        if (!plan.CanConfirm)
        {
            throw new InvalidOperationException("対応づけできていないアレルゲン表記の語があります。品目に対応づけるか「対応する品目なし」と決めてから確定してください。");
        }

        if (checkedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("確認日時は UTC で渡してください", nameof(checkedAtUtc));
        }

        var check = new AllergenCheck
        {
            ProductId = productId,
            Status = AllergenCheckStatus.Reference,
            Source = AllergenCheckSource.Flyer,
            SourceNote = plan.SourceNote,
            CheckedAt = checkedAtUtc,
            CheckedBy = checkedBy,
            UpdateDateTime = checkedAtUtc,
            UpdateUserId = updateUserId,
        };
        check.Items = plan.FlyerAllergens
            .Select(a => new AllergenCheckItem
            {
                AllergenCheckId = check.Id,
                AllergenId = a.Id,
                Contains = true,
                UpdateDateTime = checkedAtUtc,
                UpdateUserId = updateUserId,
            })
            .ToList();
        return check;
    }

    /// <summary>表記の語が品目に当たるか（語が、品目名・括弧の中・括弧を除いた名前・探す言葉のどれかを含む）</summary>
    private static bool MatchesLabelTerm(Allergen allergen, string normalizedTerm)
    {
        return NameVariants(allergen.Name)
            .Concat(allergen.Keywords.Select(k => k.Keyword))
            .Select(TextNormalizer.ForKeywordSearch)
            .Any(n => n.Length > 0 && normalizedTerm.Contains(n, StringComparison.Ordinal));
    }

    /// <summary>
    /// Claude が品目に対応づけられなかった語を、マスタの略記に当てる（D-96）。品目を足す方向にだけ使う。
    /// 語を区切りで分けた部分（「麦・え」なら「麦」「え」）のどれかが略記と語全体で一致すれば、その品目を返す。
    /// IsFullyMapped は、空でない部分がすべて略記に当たったときだけ true（そのときだけ未対応の語から外してよい）。
    /// 正規化で空になる部分（記号だけの部分）は飛ばす。「なし」は ParseLabel と違って除かず、当たらない部分として残す（確認する側に倒す）。
    /// </summary>
    /// <param name="term">Claude が対応づけられなかった語</param>
    /// <param name="allergens">マスタのすべての品目（略記つき）</param>
    public static FlyerAbbreviationMatch MatchAbbreviations(string term, IEnumerable<Allergen> allergens)
    {
        ArgumentNullException.ThrowIfNull(allergens);

        var allergenList = allergens.ToList();
        var mapped = new List<Allergen>();
        var abbreviationHits = new List<FlyerAbbreviationHit>();
        var hasPart = false;
        var allMapped = true;
        foreach (var part in (term ?? string.Empty).Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = StripLabelPhrases(TextNormalizer.ForKeywordSearch(part));
            if (normalized.Length == 0)
            {
                continue;
            }

            hasPart = true;
            var hits = allergenList.Where(a => MatchesAbbreviation(a, NormalizeAbbreviationTerm(part))).ToList();
            if (hits.Count == 0)
            {
                allMapped = false;
                continue;
            }

            mapped.AddRange(hits);
            abbreviationHits.AddRange(hits.Select(a => new FlyerAbbreviationHit(part, new AllergenRef(a.Id, a.Name))));
        }

        var result = mapped
            .DistinctBy(a => a.Id, StringComparer.Ordinal)
            .OrderBy(a => a.SortOrder)
            .Select(a => new AllergenRef(a.Id, a.Name))
            .ToList();
        return new FlyerAbbreviationMatch(result, hasPart && allMapped && result.Count > 0, abbreviationHits.Distinct().ToList());
    }

    /// <summary>
    /// 表記の語が品目の略記と語全体で一致するか（部分一致では当てない。D-96、FR-IMPORT-31）。
    /// 語も略記も TextNormalizer.ForLabelAbbreviation（カタカナをひらがなにしない）で正規化して比べる（D-100、FR-IMPORT-32）。
    /// そのため、ひらがなの「か」はかに、カタカナの「カ」・半角の「ｶ」はカシューナッツにだけ当たる。
    /// 「エ」「ソ」「ク」（カタカナ）は、マスタの略記（ひらがな）と字が違うので当てない（「品目不明」として職員に選んでもらう。確認する側に倒す）。
    /// 保存済みの NormalizedAbbreviation は信用せず、略記を毎回正規化し直す（正規化を直したときに古い値でずれないため）
    /// </summary>
    private static bool MatchesAbbreviation(Allergen allergen, string abbreviationTerm)
    {
        return abbreviationTerm.Length > 0
            && allergen.LabelAbbreviations.Any(a => string.Equals(TextNormalizer.ForLabelAbbreviation(a.Abbreviation), abbreviationTerm, StringComparison.Ordinal));
    }

    /// <summary>略記と比べる語（かな変換をかけない正規化のあと、「一部に」「を含む」を除く）</summary>
    private static string NormalizeAbbreviationTerm(string term) => StripLabelPhrases(TextNormalizer.ForLabelAbbreviation(term));

    /// <summary>品目名の書き方（「落花生（ピーナッツ）」→「落花生（ピーナッツ）」「落花生」「ピーナッツ」）</summary>
    private static IEnumerable<string> NameVariants(string name)
    {
        yield return name;

        var open = name.IndexOfAny(['（', '(']);
        var close = name.IndexOfAny(['）', ')']);
        if (open > 0 && close > open)
        {
            yield return name[..open];
            yield return name[(open + 1)..close];
        }
    }

    /// <summary>表記の決まり文句（「一部に」「を含む」）を除く</summary>
    private static string StripLabelPhrases(string normalizedTerm)
    {
        var term = normalizedTerm;
        if (term.StartsWith("一部に", StringComparison.Ordinal))
        {
            term = term["一部に".Length..];
        }

        foreach (var suffix in new[] { "を含む", "含む" })
        {
            if (term.EndsWith(suffix, StringComparison.Ordinal))
            {
                term = term[..^suffix.Length];
                break;
            }
        }

        return term;
    }

    /// <summary>source_note：表記の原文（なければ「表記なし」。職員が「対応する品目なし」とした語も原文に残る）と、説明文で当たった言葉</summary>
    private static string BuildSourceNote(string? labelText, IReadOnlyList<KeywordMatch> descriptionHits)
    {
        var note = IsNoLabel(labelText) ? "表記なし" : "表記：" + labelText!.Trim();
        var words = descriptionHits.Select(h => h.MatchedText).Distinct(StringComparer.Ordinal).ToList();
        if (words.Count > 0)
        {
            note += "／商品名・説明文で当たった言葉：" + string.Join("・", words);
        }

        return note;
    }

    /// <summary>
    /// 掲載品の写しに入れる表記の原文（D-101・FR-IMPORT-40。献立の確定で見比べるための表示だけに使う）。
    /// 読み取った表記の原文と、読み取りの検証で見つけた表記を「／」でつなぐ。どれも表記なしなら「表記なし」。
    /// 職員が品目を直したとき（editedAllergenNames が null でない）は「（品目を直しました：…）」を後ろに足す
    /// （表記なしなら「（品目を入れました：…）」、品目が空なら「（品目なし）」）
    /// </summary>
    /// <param name="labelText">読み取った表記の原文（二重に読んだ商品はつないだもの）</param>
    /// <param name="foundLabelTexts">読み取りの検証で見つけた表記の原文</param>
    /// <param name="editedAllergenNames">職員が品目を直したときの今の品目の名前（直していなければ null）</param>
    public static string ListingLabelText(string? labelText, IReadOnlyList<string> foundLabelTexts, IReadOnlyList<string>? editedAllergenNames)
    {
        ArgumentNullException.ThrowIfNull(foundLabelTexts);

        var texts = new[] { labelText }
            .Concat(foundLabelTexts)
            .Where(t => !IsNoLabel(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var isAbsent = texts.Count == 0;
        var text = isAbsent ? "表記なし" : string.Join("／", texts);
        if (editedAllergenNames is null)
        {
            return text;
        }

        if (editedAllergenNames.Count == 0)
        {
            return text + "（品目なし）";
        }

        var names = string.Join("・", editedAllergenNames.Distinct(StringComparer.Ordinal));
        return text + (isAbsent ? $"（品目を入れました：{names}）" : $"（品目を直しました：{names}）");
    }

    /// <summary>表記がない（空・「なし」・記号だけ）か</summary>
    private static bool IsNoLabel(string? labelText)
    {
        return (labelText ?? string.Empty)
            .Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => TextNormalizer.ForKeywordSearch(t))
            .All(t => t.Length == 0 || NoLabelTerms.Contains(t, StringComparer.Ordinal));
    }
}

/// <summary>
/// アレルゲン表記の読み取り結果。
/// </summary>
public sealed class FlyerLabelParseResult
{
    /// <summary>マスタの品目に対応づけた品目</summary>
    public IReadOnlyList<AllergenRef> Allergens { get; init; } = [];

    /// <summary>対応づけできなかった語（捨てずに職員に確かめさせる）</summary>
    public IReadOnlyList<string> UnmappedTerms { get; init; } = [];

    /// <summary>略記で当てた語と品目（画面で「麦→小麦」のように示すため。品目は Allergens にも入っている）</summary>
    public IReadOnlyList<FlyerAbbreviationHit> AbbreviationHits { get; init; } = [];
}

/// <summary>略記で当てた語と品目（D-96・D-100）</summary>
/// <param name="Term">表記の語（原文のまま。例：麦、カ）</param>
/// <param name="Allergen">当てた品目</param>
public sealed record FlyerAbbreviationHit(string Term, AllergenRef Allergen);

/// <summary>
/// Claude が対応づけられなかった語を略記に当てた結果。
/// </summary>
/// <param name="Allergens">当てた品目</param>
/// <param name="IsFullyMapped">空でない部分がすべて略記に当たったか（そのときだけ未対応の語から外してよい）</param>
/// <param name="Hits">略記で当てた部分と品目</param>
public sealed record FlyerAbbreviationMatch(IReadOnlyList<AllergenRef> Allergens, bool IsFullyMapped, IReadOnlyList<FlyerAbbreviationHit> Hits)
{
    /// <summary>これまでの書き方（var (allergens, isFullyMapped) = ...）のための分解</summary>
    public void Deconstruct(out IReadOnlyList<AllergenRef> allergens, out bool isFullyMapped)
    {
        allergens = Allergens;
        isFullyMapped = IsFullyMapped;
    }
}

/// <summary>
/// チラシの1商品のアレルゲンの情報。
/// </summary>
/// <param name="LabelAllergenIds">アレルゲン表記の品目（ParseLabel の結果を職員がチラシと見比べて直したもの）</param>
/// <param name="LabelText">アレルゲン表記の原文（なければ null。source_note に残す）</param>
/// <param name="Description">Claude が読んだ商品名と説明文（探す言葉を探すためだけに使い、説明文は保存しない）</param>
/// <param name="UnresolvedLabelTerms">対応づけできず、職員もまだ品目に対応づけていない（「対応する品目なし」とも決めていない）語</param>
public sealed record FlyerAllergenInput(IReadOnlyCollection<string> LabelAllergenIds, string? LabelText, string? Description, IReadOnlyCollection<string> UnresolvedLabelTerms);

/// <summary>「参考」の記録を追加するか（03 §3 の表と SR-20）</summary>
public enum FlyerReferenceDecision
{
    /// <summary>追加しない</summary>
    None,

    /// <summary>追加する（記録がない、または参考だけでチラシの品目が増えた）</summary>
    Add,

    /// <summary>
    /// 確認済みの商品で、チラシの品目が有効な記録で「含む」になっていないので必ず追加する（SR-20 の見直し。D-100、09 の 3.5）。
    /// 避ける品目なら献立に入れられなくなり、避ける品目でなければ 🟡（参考）になる。職員に「チラシの誤り」を選ばせることはしない
    /// </summary>
    AddForDiscrepancy,
}

/// <summary>
/// 確認済みの商品で、チラシの品目が有効な記録で「含む」になっていないもの（SR-20、FR-FLYER-09）。
/// </summary>
/// <param name="Allergen">品目</param>
/// <param name="IsAvoided">施設の避ける品目か</param>
/// <param name="OnlyFromDescription">表記ではなく、説明文の探す言葉だけで当たった品目か（画面で根拠を示すため）</param>
public sealed record FlyerAllergenDiscrepancy(AllergenRef Allergen, bool IsAvoided, bool OnlyFromDescription);

/// <summary>
/// チラシを取り込んだときのアレルゲンの扱い。
/// </summary>
public sealed class FlyerAllergenPlan
{
    /// <summary>チラシの品目（表記の品目 ∪ 説明文で探す言葉が当たった品目）</summary>
    public IReadOnlyList<AllergenRef> FlyerAllergens { get; init; } = [];

    /// <summary>説明文で当たった探す言葉</summary>
    public IReadOnlyList<KeywordMatch> DescriptionHits { get; init; } = [];

    /// <summary>「参考」の記録を追加するか</summary>
    public FlyerReferenceDecision Decision { get; init; }

    /// <summary>確認済みの商品での食い違い（取り込みの画面で示す）</summary>
    public IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies { get; init; } = [];

    /// <summary>対応づけできていない表記の語（残っていれば、職員が対応づけるか「対応する品目なし」と決めるまで取り込みを確定させない）</summary>
    public IReadOnlyCollection<string> UnresolvedLabelTerms { get; init; } = [];

    /// <summary>対応づけできていない表記の語があるか</summary>
    public bool HasUnresolvedLabelTerms => UnresolvedLabelTerms.Count > 0;

    /// <summary>
    /// この商品の取り込みを確定できるか。対応づけできていない表記の語が残っていれば、記録を足すかどうかにかかわらず確定させない
    /// （「参考」だけで品目が増えないときも、未対応の語が卵などを指していれば見落とすため）。
    /// </summary>
    public bool CanConfirm => !HasUnresolvedLabelTerms;

    /// <summary>記録に残す情報源の補足（表記の原文か「表記なし」と、説明文で当たった言葉）</summary>
    public string SourceNote { get; init; } = string.Empty;

    /// <summary>「参考」の記録を追加するか（職員の選択では変わらない。D-100）</summary>
    public bool ShouldAddReference() => Decision is FlyerReferenceDecision.Add or FlyerReferenceDecision.AddForDiscrepancy;
}
