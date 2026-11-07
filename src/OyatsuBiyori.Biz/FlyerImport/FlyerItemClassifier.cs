using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// 取り込みの分類（D-100、09 の 3.2・3.4・5.2、TI-04）。読み取った1商品を 使えない ＞ 再入力 ＞ 要確認（新しい商品） ＞ 使える の順で分け、
/// 当てはまった理由をすべて返す。まとめて確定・まとめて登録に入れるか（5.2）も同じところで決める。
/// 一致した商品のアレルゲンの判定（AllergenJudge）は呼び出し側で求めて渡し、ここでは書き換えない。
/// 分類はアレルゲンの3つの表示とは別のもの（「使える」でも献立では判定どおり 🟡 のことがある）。迷ったら確認する側（使えない・再入力）に倒す。
/// 画面の表示と確定の API の両方で、同じこの関数で分類する（3.1）。
/// </summary>
public static class FlyerItemClassifier
{
    /// <summary>1商品を分類する</summary>
    public static FlyerItemClassification Classify(FlyerClassificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Read);
        ArgumentNullException.ThrowIfNull(input.OriginalRead);
        ArgumentNullException.ThrowIfNull(input.Match);
        ArgumentNullException.ThrowIfNull(input.AllAllergens);
        ArgumentNullException.ThrowIfNull(input.AvoidedAllergens);

        var product = input.Match.IsMatched ? input.Match.Product : null;
        if (product is not null && input.MatchedJudgement is null)
        {
            // 判定なしで「使える」にしない（確認する側に倒す）
            throw new ArgumentException("一致した商品のアレルゲンの判定を渡してください", nameof(input));
        }

        var allergensById = input.AllAllergens.DistinctBy(a => a.Id, StringComparer.Ordinal).ToDictionary(a => a.Id, StringComparer.Ordinal);
        var avoided = input.AvoidedAllergens.DistinctBy(a => a.Id, StringComparer.Ordinal).OrderBy(a => a.SortOrder).ToList();
        var avoidedIds = avoided.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        // 表記の品目＝読み取り・略記の補正・職員の直し（LabelAllergenIds）∪ 品目の分からない語に職員が選んだ品目
        var labelIds = input.LabelAllergenIds
            .Concat(input.ResolvedTerms.Values.SelectMany(ids => ids))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var id in labelIds)
        {
            if (!allergensById.ContainsKey(id))
            {
                throw new ArgumentException($"マスタにない品目です: {id}", nameof(input));
            }
        }

        var unresolvedTerms = input.UnmappedTerms
            .Where(t => !string.IsNullOrWhiteSpace(t) && !input.ResolvedTerms.ContainsKey(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var read = input.Read;
        var original = input.OriginalRead;

        // 商品名は読んだ名前と直した名前の両方で探す（直して探す言葉を消せないように。SR-14、09 の 3.6）
        var names = new[] { original.Name, read.Name }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // チラシの品目と、一致した商品の記録との食い違い（03 §3・09 の 3.5）。記録を足すかは確定の側が決める
        var plan = FlyerAllergenPlanner.Plan(
            new FlyerAllergenInput(labelIds, read.AllergenLabelText, string.Join("\n", names.Append(original.Description ?? string.Empty).Where(t => t.Length > 0)), unresolvedTerms),
            input.AllAllergens,
            avoidedIds,
            product?.AllergenChecks ?? []);

        var piecesPerPack = read.PiecesPerPackCandidate is >= 1
            ? read.PiecesPerPackCandidate
            : product?.PiecesPerPack is >= 1 ? product.PiecesPerPack : null;

        var unusable = new List<FlyerItemReason>();
        AddUnusableReasons(unusable, input, product, piecesPerPack, labelIds, avoided, avoidedIds, allergensById, names);

        var needsInput = new List<FlyerItemReason>();
        AddNeedsInputReasons(needsInput, input, product, labelIds, unresolvedTerms, allergensById);
        needsInput.RemoveAll(r => IsResolved(r, input));

        var category = unusable.Count > 0
            ? FlyerItemCategory.Unusable
            : needsInput.Count > 0
                ? FlyerItemCategory.NeedsInput
                : product is null ? FlyerItemCategory.New : FlyerItemCategory.Usable;

        // アレルゲンに関わる理由を先に（同じなら 使えない → 再入力 の順、そのあと見つけた順）
        var reasons = unusable.Concat(needsInput)
            .Select((r, i) => (Reason: r, Order: i))
            .OrderByDescending(x => x.Reason.IsAllergenRelated)
            .ThenBy(x => x.Order)
            .Select(x => x.Reason)
            .ToList();

        var notices = new List<FlyerItemReason>();
        if (plan.Discrepancies.Count > 0)
        {
            notices.Add(new FlyerItemReason(
                FlyerItemReasonCode.MasterDiscrepancy,
                nameof(FlyerItemReasonCode.MasterDiscrepancy),
                FlyerItemReasonLevel.Notice,
                $"チラシの表記が、パッケージでの確認と食い違っています（{string.Join("・", plan.Discrepancies.Select(d => d.Allergen.Name))}）。確定すると「参考」の記録を足します",
                FlyerItemField.AllergenLabel,
                IsAllergenRelated: true,
                CanAccept: false));
        }

        var isLabelAbsent = IsLabelAbsent(read, labelIds, input.UnmappedTerms);
        var bulkExclusions = category is FlyerItemCategory.Usable or FlyerItemCategory.New
            ? BulkExclusions(input, category, isLabelAbsent)
            : [];

        return new FlyerItemClassification
        {
            Category = category,
            Reasons = reasons,
            Notices = notices,
            FlyerAllergens = plan.FlyerAllergens,
            Discrepancies = plan.Discrepancies,
            UnresolvedTerms = unresolvedTerms,
            PiecesPerPack = piecesPerPack,
            IsLabelAbsent = isLabelAbsent,
            CanMarkLabelAbsentCompared = isLabelAbsent && !input.ReviewFailed,
            CanBulk = (category is FlyerItemCategory.Usable or FlyerItemCategory.New) && bulkExclusions.Count == 0,
            BulkExclusions = bulkExclusions,
        };
    }

    /// <summary>
    /// アレルゲンの表記がないと読んだか（「表記なし」と読まれた・表記が空・品目も分からない語も1つもない）。
    /// 「なし」「－」のように文字はあっても品目が1つも出ない表記も、確認する側に倒して「表記なし」とする（FR-FLYER-04a）
    /// </summary>
    public static bool IsLabelAbsent(FlyerReadProduct read, IReadOnlyCollection<string> labelAllergenIds, IReadOnlyCollection<string> unmappedTerms)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(labelAllergenIds);
        ArgumentNullException.ThrowIfNull(unmappedTerms);

        return read.AllergenLabelAbsent
            || string.IsNullOrWhiteSpace(read.AllergenLabelText)
            || (labelAllergenIds.Count == 0 && unmappedTerms.Count == 0);
    }

    /// <summary>使えない の理由（09 の 3.2・3.4）。職員の「このままでよい」では外せない</summary>
    private static void AddUnusableReasons(
        List<FlyerItemReason> reasons,
        FlyerClassificationInput input,
        Product? product,
        int? piecesPerPack,
        IReadOnlyList<string> labelIds,
        IReadOnlyList<Allergen> avoided,
        HashSet<string> avoidedIds,
        Dictionary<string, Allergen> allergensById,
        IReadOnlyList<string> names)
    {
        if (piecesPerPack is null)
        {
            reasons.Add(Unusable(FlyerItemReasonCode.PiecesUnknown, nameof(FlyerItemReasonCode.PiecesUnknown),
                "入数（1パックで何人分か）が分かりません。個包装の数が分かれば、入数を入れてください",
                FlyerItemField.PiecesPerPack, isAllergenRelated: false));
        }

        // 1. チラシのアレルゲン表記
        foreach (var allergen in labelIds.Where(avoidedIds.Contains).Select(id => allergensById[id]).OrderBy(a => a.SortOrder))
        {
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInLabel, $"{FlyerItemReasonCode.AvoidedInLabel}:{allergen.Id}",
                $"{allergen.Name}を含みます（チラシの表記）", FlyerItemField.AllergenLabel));
        }

        // 2. チラシの説明文（読み取ったままの説明文。説明文は直せない）
        foreach (var group in AllergenKeywordFinder.Find(input.OriginalRead.Description, avoided).GroupBy(m => m.AllergenId, StringComparer.Ordinal))
        {
            var words = string.Join("・", group.Select(m => $"「{m.MatchedText}」").Distinct(StringComparer.Ordinal));
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInDescription, $"{FlyerItemReasonCode.AvoidedInDescription}:{group.Key}",
                $"{group.First().AllergenName}を含みます（チラシの説明文の{words}）", null));
        }

        // 3. 商品名（SR-14）
        var nameHits = names.SelectMany(n => AllergenKeywordFinder.Find(n, avoided)).ToList();
        foreach (var group in nameHits.GroupBy(m => m.AllergenId, StringComparer.Ordinal))
        {
            var words = string.Join("・", group.Select(m => $"「{m.MatchedText}」").Distinct(StringComparer.Ordinal));
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInName, $"{FlyerItemReasonCode.AvoidedInName}:{group.Key}",
                $"商品名に{words}があります（{group.First().AllergenName}）", FlyerItemField.Name));
        }

        if (product is null)
        {
            return;
        }

        // 4. 商品マスタの記録（03 §5 の判定のうち「含む」「含む可能性あり」だけ。記録がない（不明）ときはチラシの品目だけで決める。09 の 3.2 の注）
        var judgement = input.MatchedJudgement!;
        if (judgement.Kind is AllergenJudgementKind.Contains or AllergenJudgementKind.MayContain)
        {
            foreach (var allergen in judgement.ContainedAllergens)
            {
                reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, $"{FlyerItemReasonCode.AvoidedInMaster}:{allergen.Id}",
                    $"{allergen.Name}を含みます（商品一覧の確認の記録）", null));
            }

            // 原材料の全文は、Claude の読み取りと職員の修正の両方で当たることがあるので1つにまとめる
            foreach (var group in judgement.KeywordHits.GroupBy(h => (h.Match.AllergenId, InName: h.Source == KeywordHitSource.ProductName)))
            {
                var words = string.Join("・", group.Select(h => $"「{h.Match.MatchedText}」").Distinct(StringComparer.Ordinal));
                var where = group.Key.InName ? "商品一覧の商品名" : "商品一覧の原材料";
                reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, $"{FlyerItemReasonCode.AvoidedInMaster}:{group.Key.AllergenId}:{(group.Key.InName ? "name" : "ingredients")}",
                    $"{where}に{words}があります（{group.First().Match.AllergenName}）", null));
            }

            if (judgement.ContainedAllergens.Count == 0 && judgement.KeywordHits.Count == 0)
            {
                // 判定が入れられないのに理由がない（起きないはず）ときも、使えないにする
                reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, nameof(FlyerItemReasonCode.AvoidedInMaster),
                    "商品一覧のアレルゲンの判定で、献立に入れられない商品です", null));
            }
        }

        if (product.IsDisabled)
        {
            reasons.Add(Unusable(FlyerItemReasonCode.Disabled, nameof(FlyerItemReasonCode.Disabled),
                "商品一覧で「使わない」にした商品です", null, isAllergenRelated: false));
        }
    }

    /// <summary>再入力 の理由（09 の 3.1 の表）。直した・「このままでよい」を押したものはあとで除く</summary>
    private static void AddNeedsInputReasons(
        List<FlyerItemReason> reasons,
        FlyerClassificationInput input,
        Product? product,
        IReadOnlyList<string> labelIds,
        IReadOnlyList<string> unresolvedTerms,
        Dictionary<string, Allergen> allergensById)
    {
        var read = input.Read;

        // 表記の読み落としの疑いを先に見る（アレルゲンの理由）
        foreach (var term in unresolvedTerms)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.UnmappedTerm, $"{FlyerItemReasonCode.UnmappedTerm}:{term}",
                $"アレルゲン表記の「{term}」がどの品目か分かりません。品目を選ぶか「対応する品目なし」を選んでください",
                FlyerItemField.AllergenLabel, isAllergenRelated: true, canAccept: false));
        }

        if (product is not null && IsLabelAbsent(read, labelIds, input.UnmappedTerms))
        {
            // 表記なしと読んだのに、一致した商品の有効な記録で特定原材料を含む（読み落としの疑い。D-97 から引き継ぐ）
            var contained = AllergenCheckHistory.From(product.AllergenChecks).ContainedAllergenIds
                .Select(id => allergensById.GetValueOrDefault(id))
                .OfType<Allergen>()
                .Where(a => a.Category == AllergenCategory.Specified)
                .OrderBy(a => a.SortOrder)
                .ToList();
            if (contained.Count > 0)
            {
                reasons.Add(NeedsInput(FlyerItemReasonCode.LabelAbsentButMasterContains, nameof(FlyerItemReasonCode.LabelAbsentButMasterContains),
                    $"アレルゲンの表記なしと読みましたが、商品一覧の記録では{string.Join("・", contained.Select(a => a.Name))}を含みます。チラシの表記を見比べてください",
                    FlyerItemField.AllergenLabel, isAllergenRelated: true, canAccept: true));
            }
        }

        foreach (var note in input.ReviewNotes)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.ReviewNote, note.Key, note.Message, note.Field,
                isAllergenRelated: note.Field.Contains("アレルゲン", StringComparison.Ordinal), canAccept: true));
        }

        if (string.IsNullOrWhiteSpace(read.Name) || ProductMatcher.NormalizeName(read.Name).Length == 0)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingName, nameof(FlyerItemReasonCode.MissingName),
                "商品名が読めませんでした", FlyerItemField.Name, isAllergenRelated: false, canAccept: false));
        }

        if (ProductMatcher.NormalizeMaker(read.Maker).Length == 0)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingMaker, nameof(FlyerItemReasonCode.MissingMaker),
                "メーカーが読めませんでした。メーカーを入れると、登録済みの商品と照合します", FlyerItemField.Maker, isAllergenRelated: false, canAccept: false));
        }

        if (read.PriceIncludingTax is not >= 1)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingPrice, nameof(FlyerItemReasonCode.MissingPrice),
                "税込価格が読めませんでした", FlyerItemField.PriceIncludingTax, isAllergenRelated: false, canAccept: false));
        }
        else if (read.PriceExcludingTax is int excluding && !FlyerReadingValidator.IsTaxConsistent(excluding, read.PriceIncludingTax.Value))
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.TaxMismatch, nameof(FlyerItemReasonCode.TaxMismatch),
                $"税込{read.PriceIncludingTax}円が、税抜{excluding}円と合いません", FlyerItemField.PriceIncludingTax, isAllergenRelated: false, canAccept: true));
        }

        if (string.IsNullOrWhiteSpace(read.Volume))
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingVolume, nameof(FlyerItemReasonCode.MissingVolume),
                "内容量が読めませんでした", FlyerItemField.Volume, isAllergenRelated: false, canAccept: false));
        }
        else if (read.PiecesPerPackCandidate is >= 1 && !FlyerReadingValidator.IsPiecesCandidateSupported(read.Volume, read.PiecesPerPackCandidate))
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.PiecesMismatch, nameof(FlyerItemReasonCode.PiecesMismatch),
                $"入数の候補{read.PiecesPerPackCandidate}個が、内容量の表記（{read.Volume}）と合いません。1パックで何人分かを確かめてください",
                FlyerItemField.PiecesPerPack, isAllergenRelated: false, canAccept: true));
        }

        if (string.IsNullOrWhiteSpace(read.OrderNo))
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingOrderNo, nameof(FlyerItemReasonCode.MissingOrderNo),
                "注文番号が読めませんでした", FlyerItemField.OrderNo, isAllergenRelated: false, canAccept: false));
        }

        if (product is not null)
        {
            // 一致した商品と内容量か入数の候補が違う（FR-IMPORT-12）。マスタの値が空なら比べない（確定で埋めるだけ）
            var volumeChanged = !string.IsNullOrWhiteSpace(read.Volume) && !string.IsNullOrWhiteSpace(product.Volume)
                && TextNormalizer.ForVolume(read.Volume) != TextNormalizer.ForVolume(product.Volume);
            var piecesChanged = read.PiecesPerPackCandidate is >= 1 && product.PiecesPerPack is >= 1
                && read.PiecesPerPackCandidate != product.PiecesPerPack;
            if (volumeChanged || piecesChanged)
            {
                var master = $"{product.Volume ?? "内容量なし"}・{(product.PiecesPerPack is int mp ? $"{mp}個" : "入数なし")}";
                var flyer = $"{read.Volume ?? "内容量なし"}・{(read.PiecesPerPackCandidate is >= 1 ? $"{read.PiecesPerPackCandidate}個" : "入数なし")}";
                var message = $"商品一覧は{master}、チラシは{flyer}です。入数を確かめてください";
                if (volumeChanged)
                {
                    message += "。内容量が変わりました。届いたらパッケージの原材料も見てください";
                }

                reasons.Add(NeedsInput(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, nameof(FlyerItemReasonCode.MasterVolumeOrPiecesChanged),
                    message, FlyerItemField.PiecesPerPack, isAllergenRelated: false, canAccept: true));
            }
        }

        foreach (var conflict in input.DuplicateConflicts)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.DuplicateConflict, $"{FlyerItemReasonCode.DuplicateConflict}:{conflict.Field}",
                $"同じ商品を2回以上読み、{FieldLabel(conflict.Field)}が食い違いました（{string.Join("／", conflict.Values)}）。チラシで確かめてください",
                conflict.Field, isAllergenRelated: false, canAccept: true));
        }
    }

    /// <summary>直した・「このままでよい」を押したので、もう再入力の理由にしないか（09 の 5.3）</summary>
    private static bool IsResolved(FlyerItemReason reason, FlyerClassificationInput input)
    {
        if (reason.CanAccept && input.AcceptedReasonKeys.Contains(reason.Key))
        {
            return true;
        }

        return reason.Code switch
        {
            // 職員が入数を入れた・直したら、入数は確かめたものとする
            FlyerItemReasonCode.PiecesMismatch => input.IsPiecesEnteredByStaff || input.EditedFields.Contains(FlyerItemField.PiecesPerPack),
            // 一致した商品との内容量・入数の違いは、「このままでよい」を押したときだけ確かめたものとする。
            // 入数を入れた・直しただけで消すと、あとで商品名・メーカーを直して別の確認済みの商品に一致したときも、確かめずに引き継いでしまう（FR-IMPORT-12）
            // 食い違った項目を職員が直したら、その値を使う
            FlyerItemReasonCode.DuplicateConflict => reason.Field is { } field && input.EditedFields.Contains(field),
            _ => false,
        };
    }

    /// <summary>まとめて確定・まとめて登録に入れない理由（09 の 5.2）</summary>
    private static List<FlyerBulkExclusion> BulkExclusions(FlyerClassificationInput input, FlyerItemCategory category, bool isLabelAbsent)
    {
        var exclusions = new List<FlyerBulkExclusion>();
        if (isLabelAbsent && !input.IsLabelAbsentCompared)
        {
            exclusions.Add(FlyerBulkExclusion.LabelAbsentNotCompared);
        }

        if (input.IsManual)
        {
            exclusions.Add(FlyerBulkExclusion.Manual);
        }

        if (input.IsPiecesEnteredByStaff)
        {
            exclusions.Add(FlyerBulkExclusion.PiecesEnteredByStaff);
        }

        if (input.ReviewFailed)
        {
            exclusions.Add(FlyerBulkExclusion.ReviewFailed);
        }

        if (input.IsEditing)
        {
            exclusions.Add(FlyerBulkExclusion.Editing);
        }

        if (category == FlyerItemCategory.New
            && (input.Read.SnackType is not { } snackType || !Enum.IsDefined(snackType) || input.Read.PiecesPerPackCandidate is not >= 1))
        {
            exclusions.Add(FlyerBulkExclusion.MissingNewProductFields);
        }

        return exclusions;
    }

    private static string FieldLabel(string field) => field switch
    {
        FlyerItemField.Name => "商品名",
        FlyerItemField.Maker => "メーカー",
        FlyerItemField.Volume => "内容量",
        FlyerItemField.PriceIncludingTax => "税込価格",
        FlyerItemField.PriceExcludingTax => "税抜価格",
        FlyerItemField.OrderNo => "注文番号",
        FlyerItemField.PiecesPerPack => "入数",
        FlyerItemField.AllergenLabel => "アレルゲン表記",
        _ => field,
    };

    private static FlyerItemReason Unusable(FlyerItemReasonCode code, string key, string message, string? field, bool isAllergenRelated = true) =>
        new(code, key, FlyerItemReasonLevel.Unusable, message, field, isAllergenRelated, CanAccept: false);

    private static FlyerItemReason NeedsInput(FlyerItemReasonCode code, string key, string message, string? field, bool isAllergenRelated, bool canAccept) =>
        new(code, key, FlyerItemReasonLevel.NeedsInput, message, field, isAllergenRelated, canAccept);
}

/// <summary>取り込みの分類（09 の 3.1。画面では取り込みの画面の中だけで使い、アレルゲンの表示とは別の形で出す）</summary>
public enum FlyerItemCategory
{
    /// <summary>🔴 使えない（今週の商品に入れない）</summary>
    Unusable,

    /// <summary>🟠 再入力（職員が直すまで確定できない）</summary>
    NeedsInput,

    /// <summary>🔵 要確認（商品マスタに一致する商品がない。新しい商品として登録する）</summary>
    New,

    /// <summary>🟢 使える（商品マスタの商品と同じで、避ける品目が入っていない）</summary>
    Usable,
}

/// <summary>理由の重さ</summary>
public enum FlyerItemReasonLevel
{
    /// <summary>使えない の理由</summary>
    Unusable,

    /// <summary>再入力 の理由</summary>
    NeedsInput,

    /// <summary>知らせるだけ（分類は変えない。確認済みとの食い違いなど）</summary>
    Notice,
}

/// <summary>理由の種類</summary>
public enum FlyerItemReasonCode
{
    /// <summary>使えない：入数が分からない（FR-IMPORT-10）</summary>
    PiecesUnknown,

    /// <summary>使えない：チラシのアレルゲン表記に避ける品目がある</summary>
    AvoidedInLabel,

    /// <summary>使えない：チラシの説明文に避ける品目の探す言葉がある</summary>
    AvoidedInDescription,

    /// <summary>使えない：商品名に避ける品目の探す言葉がある（SR-14）</summary>
    AvoidedInName,

    /// <summary>使えない：一致した商品の判定が「含む」「含む可能性あり」</summary>
    AvoidedInMaster,

    /// <summary>使えない：一致した商品を「使わない」にしている</summary>
    Disabled,

    /// <summary>再入力：アレルゲン表記に、品目にも略記にも当たらない語がある</summary>
    UnmappedTerm,

    /// <summary>再入力：表記なしと読んだのに、一致した商品の有効な記録で特定原材料を含む</summary>
    LabelAbsentButMasterContains,

    /// <summary>再入力：読み取りの検証（FR-FLYER-03g）の指摘</summary>
    ReviewNote,

    /// <summary>再入力：商品名が読めない</summary>
    MissingName,

    /// <summary>再入力：メーカーが読めない（照合できない。FR-IMPORT-03）</summary>
    MissingMaker,

    /// <summary>再入力：税込価格が読めない</summary>
    MissingPrice,

    /// <summary>再入力：税込価格が税抜価格と合わない</summary>
    TaxMismatch,

    /// <summary>再入力：内容量が読めない</summary>
    MissingVolume,

    /// <summary>再入力：入数の候補が内容量の表記と合わない</summary>
    PiecesMismatch,

    /// <summary>再入力：注文番号が読めない</summary>
    MissingOrderNo,

    /// <summary>再入力：一致した商品と内容量か入数の候補が違う（FR-IMPORT-12）</summary>
    MasterVolumeOrPiecesChanged,

    /// <summary>再入力：二重に読んだ商品で値が食い違う（FR-IMPORT-06）</summary>
    DuplicateConflict,

    /// <summary>知らせるだけ：確認済みの商品とチラシの表記が食い違う（09 の 3.5）</summary>
    MasterDiscrepancy,
}

/// <summary>まとめて確定・まとめて登録に入れない理由（09 の 5.2）</summary>
public enum FlyerBulkExclusion
{
    /// <summary>表記なしで、チラシと見比べた印をまだ付けていない（FR-FLYER-04a・D-97）</summary>
    LabelAbsentNotCompared,

    /// <summary>取り込みの画面で手入力した（D-73）</summary>
    Manual,

    /// <summary>入数を入れて「使えない」から戻した（FR-IMPORT-11）</summary>
    PiecesEnteredByStaff,

    /// <summary>読み取りの検証ができなかった取り込み</summary>
    ReviewFailed,

    /// <summary>表で行を開いて、確定のフォームで直している途中</summary>
    Editing,

    /// <summary>要確認で、おやつの分類か入数の候補がそろわない</summary>
    MissingNewProductFields,
}

/// <summary>取り込みの商品の項目（理由の欄・直した項目・二重の読み取りの食い違いで使う。JSON でもこの文字のまま）</summary>
public static class FlyerItemField
{
    /// <summary>商品名</summary>
    public const string Name = "name";

    /// <summary>メーカー</summary>
    public const string Maker = "maker";

    /// <summary>内容量</summary>
    public const string Volume = "volume";

    /// <summary>税込価格</summary>
    public const string PriceIncludingTax = "priceIncludingTax";

    /// <summary>税抜価格</summary>
    public const string PriceExcludingTax = "priceExcludingTax";

    /// <summary>注文番号</summary>
    public const string OrderNo = "orderNo";

    /// <summary>入数</summary>
    public const string PiecesPerPack = "piecesPerPack";

    /// <summary>アレルゲン表記</summary>
    public const string AllergenLabel = "allergenLabel";
}

/// <summary>
/// 分類の理由。
/// </summary>
/// <param name="Code">種類</param>
/// <param name="Key">理由ごとの鍵（「このままでよい」を押した理由を覚えるため。同じ商品の中で一意）</param>
/// <param name="Level">使えない / 再入力 / 知らせるだけ</param>
/// <param name="Message">画面に出す文</param>
/// <param name="Field">直す欄（FlyerItemField の値。読み取りの検証の指摘は Claude が返した項目名のまま。欄がなければ null）</param>
/// <param name="IsAllergenRelated">アレルゲンに関わる理由か（画面で先に並べる）</param>
/// <param name="CanAccept">「このままでよい」で外せるか</param>
public sealed record FlyerItemReason(
    FlyerItemReasonCode Code,
    string Key,
    FlyerItemReasonLevel Level,
    string Message,
    string? Field,
    bool IsAllergenRelated,
    bool CanAccept);

/// <summary>読み取りの検証の指摘（Key は呼び出し側が商品の中で一意に振る。例：review:0）</summary>
/// <param name="Key">鍵</param>
/// <param name="Field">Claude が返した項目名</param>
/// <param name="Message">指摘の文</param>
public sealed record FlyerReviewNoteInput(string Key, string Field, string Message);

/// <summary>分類の入力</summary>
public sealed class FlyerClassificationInput
{
    /// <summary>Claude が読み取ったままの内容（二重の読み取りをまとめたあと。説明文と、商品名の探す言葉はここから探す）</summary>
    public required FlyerReadProduct OriginalRead { get; init; }

    /// <summary>今の内容（職員が直した値・入れた入数を反映したもの。直していなければ OriginalRead と同じ）</summary>
    public required FlyerReadProduct Read { get; init; }

    /// <summary>職員が直した項目（FlyerItemField の値）</summary>
    public IReadOnlySet<string> EditedFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>「使えない（入数が分からない）」から、職員が入数を入れて戻したか（FR-IMPORT-11）</summary>
    public bool IsPiecesEnteredByStaff { get; init; }

    /// <summary>アレルゲン表記の品目（Claude の対応づけ・アプリの対応づけと略記の補正・職員の直しのあと。品目 ID）</summary>
    public IReadOnlyCollection<string> LabelAllergenIds { get; init; } = [];

    /// <summary>品目に対応づけできなかった表記の語</summary>
    public IReadOnlyCollection<string> UnmappedTerms { get; init; } = [];

    /// <summary>職員が品目を選んだ語（語 → 品目 ID。空なら「対応する品目なし」）</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ResolvedTerms { get; init; } = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

    /// <summary>読み取りの検証の指摘（直した・取り消した指摘は呼び出し側が外す）</summary>
    public IReadOnlyList<FlyerReviewNoteInput> ReviewNotes { get; init; } = [];

    /// <summary>二重の読み取りで食い違った値</summary>
    public IReadOnlyList<FlyerDuplicateConflict> DuplicateConflicts { get; init; } = [];

    /// <summary>「このままでよい」を押した理由の鍵</summary>
    public IReadOnlySet<string> AcceptedReasonKeys { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>照合の結果（今の商品名・メーカーで ProductMatcher.Match したもの）</summary>
    public required ProductMatch Match { get; init; }

    /// <summary>一致した商品の判定（AllergenJudge.Judge。一致したときは必ず渡す。記録と避ける品目の探す言葉を読み込んでおくこと）</summary>
    public AllergenJudgement? MatchedJudgement { get; init; }

    /// <summary>マスタのすべての品目（探す言葉つき）</summary>
    public required IReadOnlyList<Allergen> AllAllergens { get; init; }

    /// <summary>施設の避ける品目（探す言葉つき。グループは展開しておく）</summary>
    public required IReadOnlyList<Allergen> AvoidedAllergens { get; init; }

    /// <summary>手入力した商品か（D-73）</summary>
    public bool IsManual { get; init; }

    /// <summary>その取り込みで、読み取りの検証ができなかったか</summary>
    public bool ReviewFailed { get; init; }

    /// <summary>表記なしを、職員がチラシと見比べたか（D-97）</summary>
    public bool IsLabelAbsentCompared { get; init; }

    /// <summary>画面で行を開いて、確定のフォームで直している途中か（まとめの確定の API で渡す）</summary>
    public bool IsEditing { get; init; }
}

/// <summary>分類の結果</summary>
public sealed class FlyerItemClassification
{
    /// <summary>分類</summary>
    public FlyerItemCategory Category { get; init; }

    /// <summary>当てはまった理由（使えない・再入力。アレルゲンに関わる理由が先）</summary>
    public IReadOnlyList<FlyerItemReason> Reasons { get; init; } = [];

    /// <summary>知らせるだけのこと（確認済みとの食い違いなど。分類は変えない）</summary>
    public IReadOnlyList<FlyerItemReason> Notices { get; init; } = [];

    /// <summary>チラシの品目（表記の品目 ∪ 職員が選んだ品目 ∪ 説明文・商品名で避ける品目の探す言葉が当たった品目）</summary>
    public IReadOnlyList<AllergenRef> FlyerAllergens { get; init; } = [];

    /// <summary>確認済みの商品で、有効な記録で「含む」になっていないチラシの品目（09 の 3.5）</summary>
    public IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies { get; init; } = [];

    /// <summary>まだ品目を決めていない表記の語</summary>
    public IReadOnlyList<string> UnresolvedTerms { get; init; } = [];

    /// <summary>使う入数（読み取りの候補、なければ一致した商品の入数。分からなければ null）</summary>
    public int? PiecesPerPack { get; init; }

    /// <summary>アレルゲンの表記がないと読んだか</summary>
    public bool IsLabelAbsent { get; init; }

    /// <summary>表記なしのまとめての見比べ（「チラシで見比べました」）に並べてよいか</summary>
    public bool CanMarkLabelAbsentCompared { get; init; }

    /// <summary>まとめて確定（使える）・まとめて登録（要確認）に入れてよいか</summary>
    public bool CanBulk { get; init; }

    /// <summary>まとめに入れない理由（分類が使える・要確認のときだけ）</summary>
    public IReadOnlyList<FlyerBulkExclusion> BulkExclusions { get; init; } = [];
}
