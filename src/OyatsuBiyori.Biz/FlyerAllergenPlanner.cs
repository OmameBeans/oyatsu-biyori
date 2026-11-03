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
    /// </summary>
    /// <param name="labelText">チラシのアレルゲン表記の原文（なければ null か空）</param>
    /// <param name="allergens">マスタのすべての品目（探す言葉つき）</param>
    public static FlyerLabelParseResult ParseLabel(string? labelText, IEnumerable<Allergen> allergens)
    {
        ArgumentNullException.ThrowIfNull(allergens);

        var allergenList = allergens.ToList();
        var mapped = new List<Allergen>();
        var unmapped = new List<string>();

        foreach (var term in (labelText ?? string.Empty).Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = StripLabelPhrases(TextNormalizer.ForKeywordSearch(term));
            if (normalized.Length == 0 || NoLabelTerms.Contains(normalized, StringComparer.Ordinal))
            {
                continue;
            }

            var hits = allergenList.Where(a => MatchesLabelTerm(a, normalized)).ToList();
            if (hits.Count == 0)
            {
                unmapped.Add(term);
                continue;
            }

            mapped.AddRange(hits);
        }

        return new FlyerLabelParseResult
        {
            Allergens = mapped
                .DistinctBy(a => a.Id, StringComparer.Ordinal)
                .OrderBy(a => a.SortOrder)
                .Select(a => new AllergenRef(a.Id, a.Name))
                .ToList(),
            UnmappedTerms = unmapped.Distinct(StringComparer.Ordinal).ToList(),
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
            // 過去に確認済みがある → 追加しない。ただしチラシの品目が「含む」になっていなければ食い違い（SR-20）
            discrepancies = notYetContained
                .Select(a => new FlyerAllergenDiscrepancy(a, avoidedIds.Contains(a.Id), !labelIds.Contains(a.Id)))
                .ToList();
            decision = discrepancies.Count == 0
                ? FlyerReferenceDecision.None
                : discrepancies.Any(d => d.IsAvoided)
                    ? FlyerReferenceDecision.AddForDiscrepancy
                    : FlyerReferenceDecision.StaffChoice;
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
            note += "／説明文で当たった言葉：" + string.Join("・", words);
        }

        return note;
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
}

/// <summary>
/// チラシの1商品のアレルゲンの情報。
/// </summary>
/// <param name="LabelAllergenIds">アレルゲン表記の品目（ParseLabel の結果を職員がチラシと見比べて直したもの）</param>
/// <param name="LabelText">アレルゲン表記の原文（なければ null。source_note に残す）</param>
/// <param name="Description">説明文（探す言葉を探すためだけに使い、保存しない）</param>
/// <param name="UnresolvedLabelTerms">対応づけできず、職員もまだ品目に対応づけていない（「対応する品目なし」とも決めていない）語</param>
public sealed record FlyerAllergenInput(IReadOnlyCollection<string> LabelAllergenIds, string? LabelText, string? Description, IReadOnlyCollection<string> UnresolvedLabelTerms);

/// <summary>「参考」の記録を追加するか（03 §3 の表と SR-20）</summary>
public enum FlyerReferenceDecision
{
    /// <summary>追加しない</summary>
    None,

    /// <summary>追加する（記録がない、または参考だけでチラシの品目が増えた）</summary>
    Add,

    /// <summary>確認済みの商品で、避ける品目の食い違いがあるので必ず追加する（SR-20。献立に入れられなくなる）</summary>
    AddForDiscrepancy,

    /// <summary>確認済みの商品で、避ける品目でない食い違いだけがある。職員が「確認し直す（追加）」か「チラシの誤り」を選ぶ（既定は追加）</summary>
    StaffChoice,
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

    /// <summary>
    /// 「参考」の記録を追加するか。職員の選択は StaffChoice のときだけ効く（避ける品目の食い違いは選べない）。
    /// </summary>
    /// <param name="staffTreatsAsFlyerError">職員が「チラシの誤りとして扱う」を選んだか</param>
    public bool ShouldAddReference(bool staffTreatsAsFlyerError = false) => Decision switch
    {
        FlyerReferenceDecision.Add or FlyerReferenceDecision.AddForDiscrepancy => true,
        FlyerReferenceDecision.StaffChoice => !staffTreatsAsFlyerError,
        _ => false,
    };
}
