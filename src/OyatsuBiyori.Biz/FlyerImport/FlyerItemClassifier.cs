using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// 取り込みの分類（D-101、09 の 3.2・3.4・3.8、TI-04）。読み取った1商品を 使えない ＞ 再入力 ＞ 取り込める の順で分け、
/// 当てはまった理由をすべて返す。分類を変えない「注意」（内容量の変化・読み取りの検証の指摘・二重の読み取りの食い違いなど）も、ここで求めて返す。
/// 一致した商品のアレルゲンの判定（AllergenJudge）は呼び出し側で求めて渡し、ここでは書き換えない。
/// 分類はアレルゲンの3つの表示とは別のもの（「取り込める」でも献立では判定どおり 🟡 のことがある）。迷ったら確認する側（使えない・再入力）に倒す。
/// 画面の表示と「取り込みを終える」の API の両方で、同じこの関数で分類する（3.1）。
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
            // 判定なしで「取り込める」にしない（確認する側に倒す）
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

        // チラシの品目と、一致した商品の記録との食い違い（03 §3・09 の 3.5）。記録を足すかは「取り込みを終える」の側が決める
        var plan = FlyerAllergenPlanner.Plan(
            new FlyerAllergenInput(labelIds, read.AllergenLabelText, string.Join("\n", names.Append(original.Description ?? string.Empty).Where(t => t.Length > 0)), unresolvedTerms),
            input.AllAllergens,
            avoidedIds,
            product?.AllergenChecks ?? []);

        var piecesPerPack = read.PiecesPerPackCandidate is >= 1
            ? read.PiecesPerPackCandidate
            : product?.PiecesPerPack is >= 1 ? product.PiecesPerPack : null;

        var isLabelAbsent = IsLabelAbsent(labelIds, unresolvedTerms);

        var unusable = new List<FlyerItemReason>();
        AddUnusableReasons(unusable, input, product, piecesPerPack, labelIds, avoided, avoidedIds, allergensById, names);

        var needsInput = new List<FlyerItemReason>();
        AddNeedsInputReasons(needsInput, input, product, isLabelAbsent, unresolvedTerms, allergensById);

        var category = unusable.Count > 0
            ? FlyerItemCategory.Unusable
            : needsInput.Count > 0
                ? FlyerItemCategory.NeedsInput
                : FlyerItemCategory.Ready;

        return new FlyerItemClassification
        {
            Category = category,
            // アレルゲンに関わる理由を先に（同じなら 使えない → 再入力 の順、そのあと見つけた順）
            Reasons = AllergenFirst(unusable.Concat(needsInput)),
            Notes = AllergenFirst(BuildNotes(input, product, plan)),
            FlyerAllergens = plan.FlyerAllergens,
            Discrepancies = plan.Discrepancies,
            UnresolvedTerms = unresolvedTerms,
            PiecesPerPack = piecesPerPack,
            IsLabelAbsent = isLabelAbsent,
            IsNew = product is null,
        };
    }

    /// <summary>
    /// アレルゲンの表記がないか（決めたこと E）。表記の品目（職員が選んだ品目を含む）も、まだ品目を決めていない語も1つもないときだけ「表記なし」。
    /// 読み取りの「表記なし」の印や原文の有無は見ない（「なし」「－」のように文字はあっても品目が1つも出ない表記も「表記なし」になる。品目を入れれば表記なしでなくなる。09 の 5.3）。
    /// 表記なしの商品の記録は品目が空の「参考（表記なし）」で、どの品目も「含まない」にはしない
    /// </summary>
    /// <param name="labelAllergenIds">表記の品目（職員が選んだ品目を含む）</param>
    /// <param name="unresolvedTerms">まだ品目を決めていない表記の語</param>
    public static bool IsLabelAbsent(IReadOnlyCollection<string> labelAllergenIds, IReadOnlyCollection<string> unresolvedTerms)
    {
        ArgumentNullException.ThrowIfNull(labelAllergenIds);
        ArgumentNullException.ThrowIfNull(unresolvedTerms);

        return labelAllergenIds.Count == 0 && unresolvedTerms.Count == 0;
    }

    /// <summary>使えない の理由（09 の 3.2・3.4）。職員の操作では外せない（外せるのは、職員が今回のチラシの表記の品目を直したときだけ）</summary>
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

        // 4. この行で新しく登録して「使わない」に戻した商品（照合からは外す）の記録（決めたこと H。パッケージで確かめた 🔴 を取りこぼさない）
        if (input.OwnCreatedJudgement is { Kind: AllergenJudgementKind.Contains or AllergenJudgementKind.MayContain } own)
        {
            AddJudgementReasons(reasons, own, "OwnCreated", "この取り込みで登録した商品");
        }

        if (product is null)
        {
            return;
        }

        // 5. 商品マスタの記録（03 §5 の判定のうち「含む」「含む可能性あり」だけ。記録がない（不明）ときはチラシの品目だけで決める。09 の 3.2 の注）
        var judgement = input.MatchedJudgement!;
        if (judgement.Kind is AllergenJudgementKind.Contains or AllergenJudgementKind.MayContain)
        {
            AddJudgementReasons(reasons, judgement, null, "商品一覧");
        }

        if (product.IsDisabled)
        {
            reasons.Add(Unusable(FlyerItemReasonCode.Disabled, nameof(FlyerItemReasonCode.Disabled),
                "商品一覧で「使わない」にした商品です", null, isAllergenRelated: false));
        }
    }

    /// <summary>商品の判定（含む / 含む可能性あり）を「使えない」の理由にする</summary>
    private static void AddJudgementReasons(List<FlyerItemReason> reasons, AllergenJudgement judgement, string? keyPrefix, string where)
    {
        var prefix = keyPrefix is null ? $"{FlyerItemReasonCode.AvoidedInMaster}" : $"{FlyerItemReasonCode.AvoidedInMaster}:{keyPrefix}";
        foreach (var allergen in judgement.ContainedAllergens)
        {
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, $"{prefix}:{allergen.Id}",
                keyPrefix is null ? $"{allergen.Name}を含みます（商品一覧の確認の記録）" : $"{where}の記録で{allergen.Name}を含みます",
                null));
        }

        // 原材料の全文は、Claude の読み取りと職員の修正の両方で当たることがあるので1つにまとめる
        foreach (var group in judgement.KeywordHits.GroupBy(h => (h.Match.AllergenId, InName: h.Source == KeywordHitSource.ProductName)))
        {
            var words = string.Join("・", group.Select(h => $"「{h.Match.MatchedText}」").Distinct(StringComparer.Ordinal));
            var place = group.Key.InName ? $"{where}の商品名" : $"{where}の原材料";
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, $"{prefix}:{group.Key.AllergenId}:{(group.Key.InName ? "name" : "ingredients")}",
                $"{place}に{words}があります（{group.First().Match.AllergenName}）", null));
        }

        if (judgement.ContainedAllergens.Count == 0 && judgement.KeywordHits.Count == 0)
        {
            // 判定が入れられないのに理由がない（起きないはず）ときも、使えないにする
            reasons.Add(Unusable(FlyerItemReasonCode.AvoidedInMaster, prefix,
                $"{where}のアレルゲンの判定で、献立に入れられない商品です", null));
        }
    }

    /// <summary>再入力 の理由（09 の 3.1 の表の6つ）。直すと分類し直して消える。「このままでよい」はない（D-101）</summary>
    private static void AddNeedsInputReasons(
        List<FlyerItemReason> reasons,
        FlyerClassificationInput input,
        Product? product,
        bool isLabelAbsent,
        IReadOnlyList<string> unresolvedTerms,
        Dictionary<string, Allergen> allergensById)
    {
        var read = input.Read;

        // 表記の読み落としの疑いを先に見る（アレルゲンの理由）
        foreach (var term in unresolvedTerms)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.UnmappedTerm, $"{FlyerItemReasonCode.UnmappedTerm}:{term}",
                $"アレルゲン表記の「{term}」がどの品目か分かりません。品目を選ぶか「対応する品目なし」を選んでください",
                FlyerItemField.AllergenLabel, isAllergenRelated: true));
        }

        if (product is not null && isLabelAbsent)
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
                    $"アレルゲンの表記なしと読みましたが、商品一覧の記録では{string.Join("・", contained.Select(a => a.Name))}を含みます。"
                        + "チラシで表記を探して品目を入れてください。チラシに本当に表記がなければ「外す」を押してください",
                    FlyerItemField.AllergenLabel, isAllergenRelated: true, contained.Select(a => new AllergenRef(a.Id, a.Name)).ToList()));
            }
        }

        if (string.IsNullOrWhiteSpace(read.Name) || ProductMatcher.NormalizeName(read.Name).Length == 0)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingName, nameof(FlyerItemReasonCode.MissingName),
                "商品名が読めませんでした", FlyerItemField.Name, isAllergenRelated: false));
        }

        if (ProductMatcher.NormalizeMaker(read.Maker).Length == 0)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingMaker, nameof(FlyerItemReasonCode.MissingMaker),
                "メーカーが読めませんでした。メーカーを入れると、登録済みの商品と照合します", FlyerItemField.Maker, isAllergenRelated: false));
        }

        if (read.PriceIncludingTax is not >= 1)
        {
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingPrice, nameof(FlyerItemReasonCode.MissingPrice),
                "税込価格が読めませんでした", FlyerItemField.PriceIncludingTax, isAllergenRelated: false));
        }

        if (product is null && (read.SnackType is not { } snackType || !Enum.IsDefined(snackType)))
        {
            // 新しい商品はおやつの分類がないと登録できない（Product.SnackType は必須。09 の 3.1）
            reasons.Add(NeedsInput(FlyerItemReasonCode.MissingSnackType, nameof(FlyerItemReasonCode.MissingSnackType),
                "おやつの分類が分かりません。分類を選んでください", FlyerItemField.SnackType, isAllergenRelated: false));
        }
    }

    /// <summary>注意（09 の 3.8。行に出すだけで分類は変えない。値は自動で決めて進める）</summary>
    private static List<FlyerItemReason> BuildNotes(FlyerClassificationInput input, Product? product, FlyerAllergenPlan plan)
    {
        var read = input.Read;
        var notes = new List<FlyerItemReason>();
        if (plan.Discrepancies.Count > 0)
        {
            notes.Add(Note(FlyerItemReasonCode.MasterDiscrepancy, nameof(FlyerItemReasonCode.MasterDiscrepancy),
                $"チラシの表記が、パッケージでの確認と食い違っています（{string.Join("・", plan.Discrepancies.Select(d => d.Allergen.Name))}）。取り込みを終えると「参考」の記録を足します",
                FlyerItemField.AllergenLabel, isAllergenRelated: true));
        }

        foreach (var note in input.ReviewNotes)
        {
            // Claude の指摘の文のまま（画面ではテキストとしてだけ出す）。アレルゲンの品目は足すだけで、ここでは減らさない（SR-19）
            notes.Add(Note(FlyerItemReasonCode.ReviewNote, note.Key, note.Message, note.Field,
                isAllergenRelated: note.Field.Contains("アレルゲン", StringComparison.Ordinal)));
        }

        if (product is not null)
        {
            // 一致した商品と内容量か入数の候補が違う（FR-IMPORT-12）。マスタの値が空なら比べない（取り込みで埋めるだけ）
            var volumeChanged = !string.IsNullOrWhiteSpace(read.Volume) && !string.IsNullOrWhiteSpace(product.Volume)
                && TextNormalizer.ForVolume(read.Volume) != TextNormalizer.ForVolume(product.Volume);
            var piecesChanged = read.PiecesPerPackCandidate is >= 1 && product.PiecesPerPack is >= 1
                && read.PiecesPerPackCandidate != product.PiecesPerPack;
            if (volumeChanged || piecesChanged)
            {
                var overwrite = input.OverwritesMaster
                    ? "チラシの値で商品一覧を上書きします"
                    : "過去の週の取り込みなので、商品一覧は上書きしません（空の項目だけ埋めます）";
                var message = volumeChanged
                    ? $"内容量が変わりました（商品一覧 {product.Volume}・{PiecesText(product.PiecesPerPack)} → チラシ {read.Volume}・{PiecesText(read.PiecesPerPackCandidate)}）。{overwrite}。届いたらパッケージの原材料も見てください"
                    : $"入数が変わりました（商品一覧 {PiecesText(product.PiecesPerPack)} → チラシ {PiecesText(read.PiecesPerPackCandidate)}）。{overwrite}";
                notes.Add(Note(FlyerItemReasonCode.MasterVolumeOrPiecesChanged, nameof(FlyerItemReasonCode.MasterVolumeOrPiecesChanged),
                    message, volumeChanged ? FlyerItemField.Volume : FlyerItemField.PiecesPerPack, isAllergenRelated: false));
            }
        }

        foreach (var conflict in input.DuplicateConflicts.Where(c => !input.EditedFields.Contains(c.Field)))
        {
            // 先に読んだ値を使う（FlyerDuplicateMerger がもう先の値にしてある）。職員がその項目を直したら、直した値を使うので出さない
            var used = conflict.Values.Count > 0 ? conflict.Values[0] : string.Empty;
            var message = $"2回読みました。{FieldLabel(conflict.Field)}が {string.Join(" / ", conflict.Values)} と違います。{used} を使います";
            if (conflict.Field == FlyerItemField.OrderNo)
            {
                // 注文番号が違うのは、同じ名前・メーカーで大きさ違いの商品が別々に載っているときが多い（FR-IMPORT-06）
                message += "。同じ名前・メーカーで大きさ違いの商品が載っているかもしれません。後の方を使うときは値を直してください";
            }

            notes.Add(Note(FlyerItemReasonCode.DuplicateConflict, $"{FlyerItemReasonCode.DuplicateConflict}:{conflict.Field}",
                message, conflict.Field, isAllergenRelated: false));
        }

        if (read.PriceIncludingTax is >= 1 && read.PriceExcludingTax is int excluding
            && !FlyerReadingValidator.IsTaxConsistent(excluding, read.PriceIncludingTax.Value))
        {
            notes.Add(Note(FlyerItemReasonCode.TaxMismatch, nameof(FlyerItemReasonCode.TaxMismatch),
                $"税込{read.PriceIncludingTax}円が税抜{excluding}円と合いません。税込{read.PriceIncludingTax}円を使います",
                FlyerItemField.PriceIncludingTax, isAllergenRelated: false));
        }

        if (string.IsNullOrWhiteSpace(read.Volume))
        {
            notes.Add(Note(FlyerItemReasonCode.MissingVolume, nameof(FlyerItemReasonCode.MissingVolume),
                "内容量が読めませんでした", FlyerItemField.Volume, isAllergenRelated: false));
        }
        else if (read.PiecesPerPackCandidate is >= 1
            && !input.IsPiecesEnteredByStaff
            && !input.EditedFields.Contains(FlyerItemField.PiecesPerPack)
            && !FlyerReadingValidator.IsPiecesCandidateSupported(read.Volume, read.PiecesPerPackCandidate))
        {
            // 職員が入数を入れた・直したら、職員の値を使うので出さない
            notes.Add(Note(FlyerItemReasonCode.PiecesMismatch, nameof(FlyerItemReasonCode.PiecesMismatch),
                $"入数の候補（{read.PiecesPerPackCandidate}）が内容量の表記と合いません。{read.PiecesPerPackCandidate} を使います",
                FlyerItemField.PiecesPerPack, isAllergenRelated: false));
        }

        if (string.IsNullOrWhiteSpace(read.OrderNo))
        {
            notes.Add(Note(FlyerItemReasonCode.MissingOrderNo, nameof(FlyerItemReasonCode.MissingOrderNo),
                "注文番号が読めませんでした。注文のときにチラシで確かめてください", FlyerItemField.OrderNo, isAllergenRelated: false));
        }

        if (input.IsManual)
        {
            notes.Add(Note(FlyerItemReasonCode.NotReviewed, nameof(FlyerItemReasonCode.NotReviewed),
                "手で入力した商品です。読み取りの検証を通っていません", null, isAllergenRelated: false));
        }
        else if (input.IsPiecesEnteredByStaff)
        {
            notes.Add(Note(FlyerItemReasonCode.NotReviewed, nameof(FlyerItemReasonCode.NotReviewed),
                "入数は職員が入れました。読み取りの検証を通っていません", FlyerItemField.PiecesPerPack, isAllergenRelated: false));
        }

        return notes;
    }

    /// <summary>アレルゲンに関わるものを先に（そのあとは見つけた順）</summary>
    private static List<FlyerItemReason> AllergenFirst(IEnumerable<FlyerItemReason> reasons) =>
        reasons
            .Select((r, i) => (Reason: r, Order: i))
            .OrderByDescending(x => x.Reason.IsAllergenRelated)
            .ThenBy(x => x.Order)
            .Select(x => x.Reason)
            .ToList();

    private static string PiecesText(int? pieces) => pieces is >= 1 ? $"{pieces}個" : "入数なし";

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
        FlyerItemField.SnackType => "おやつの分類",
        _ => field,
    };

    private static FlyerItemReason Unusable(FlyerItemReasonCode code, string key, string message, string? field, bool isAllergenRelated = true) =>
        new(code, key, FlyerItemReasonLevel.Unusable, message, field, isAllergenRelated, []);

    private static FlyerItemReason NeedsInput(FlyerItemReasonCode code, string key, string message, string? field, bool isAllergenRelated, IReadOnlyList<AllergenRef>? allergens = null) =>
        new(code, key, FlyerItemReasonLevel.NeedsInput, message, field, isAllergenRelated, allergens ?? []);

    private static FlyerItemReason Note(FlyerItemReasonCode code, string key, string message, string? field, bool isAllergenRelated) =>
        new(code, key, FlyerItemReasonLevel.Note, message, field, isAllergenRelated, []);
}

/// <summary>取り込みの分類（09 の 3.1。画面では取り込みの画面の中だけで使い、アレルゲンの表示とは別の形で出す）</summary>
public enum FlyerItemCategory
{
    /// <summary>🔴 使えない（今週の商品に入れない）</summary>
    Unusable,

    /// <summary>🟠 再入力（職員が直すまで取り込まない）</summary>
    NeedsInput,

    /// <summary>🟢 取り込める（「取り込みを終える」で、新しい商品は登録し、登録済みの商品は上書きする）</summary>
    Ready,
}

/// <summary>理由の重さ</summary>
public enum FlyerItemReasonLevel
{
    /// <summary>使えない の理由</summary>
    Unusable,

    /// <summary>再入力 の理由</summary>
    NeedsInput,

    /// <summary>注意（行に出すだけで分類は変えない。09 の 3.8）</summary>
    Note,
}

/// <summary>理由・注意の種類</summary>
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

    /// <summary>使えない：一致した商品（またはこの行で登録して照合から外した商品）の判定が「含む」「含む可能性あり」</summary>
    AvoidedInMaster,

    /// <summary>使えない：一致した商品を「使わない」にしている</summary>
    Disabled,

    /// <summary>再入力：商品名が読めない</summary>
    MissingName,

    /// <summary>再入力：メーカーが読めない（照合できない。FR-IMPORT-03）</summary>
    MissingMaker,

    /// <summary>再入力：税込価格が読めない</summary>
    MissingPrice,

    /// <summary>再入力：アレルゲン表記に、品目にも略記にも当たらない語がある</summary>
    UnmappedTerm,

    /// <summary>再入力：表記なしと読んだのに、一致した商品の有効な記録で特定原材料を含む</summary>
    LabelAbsentButMasterContains,

    /// <summary>再入力：新しい商品で、おやつの分類が分からない</summary>
    MissingSnackType,

    /// <summary>注意：確認済みの商品とチラシの表記が食い違う（09 の 3.5）</summary>
    MasterDiscrepancy,

    /// <summary>注意：読み取りの検証（FR-FLYER-03g）の指摘</summary>
    ReviewNote,

    /// <summary>注意：一致した商品と内容量か入数の候補が違う（FR-IMPORT-12。チラシの値で上書きする）</summary>
    MasterVolumeOrPiecesChanged,

    /// <summary>注意：二重に読んだ商品で値が食い違う（FR-IMPORT-06。先に読んだ値を使う）</summary>
    DuplicateConflict,

    /// <summary>注意：税込価格が税抜価格と合わない（税込を使う）</summary>
    TaxMismatch,

    /// <summary>注意：内容量が読めない</summary>
    MissingVolume,

    /// <summary>注意：注文番号が読めない</summary>
    MissingOrderNo,

    /// <summary>注意：入数の候補が内容量の表記と合わない（候補を使う）</summary>
    PiecesMismatch,

    /// <summary>注意：手入力・入数を入れ直した（読み取りの検証を通っていない）</summary>
    NotReviewed,
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

    /// <summary>おやつの分類</summary>
    public const string SnackType = "snackType";
}

/// <summary>
/// 分類の理由・注意。
/// </summary>
/// <param name="Code">種類</param>
/// <param name="Key">理由ごとの鍵（同じ商品の中で一意。画面の key に使う）</param>
/// <param name="Level">使えない / 再入力 / 注意</param>
/// <param name="Message">画面に出す文（ReviewNote は Claude の文。テキストとしてだけ出す）</param>
/// <param name="Field">直す欄（FlyerItemField の値。読み取りの検証の指摘は Claude が返した項目名のまま。欄がなければ null）</param>
/// <param name="IsAllergenRelated">アレルゲンに関わる理由か（画面で先に並べる）</param>
/// <param name="Allergens">LabelAbsentButMasterContains で、記録で含む特定原材料（ほかは空）</param>
public sealed record FlyerItemReason(
    FlyerItemReasonCode Code,
    string Key,
    FlyerItemReasonLevel Level,
    string Message,
    string? Field,
    bool IsAllergenRelated,
    IReadOnlyList<AllergenRef> Allergens);

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

    /// <summary>「使えない（入数が分からない）」から、職員が入数を入れて戻したか（FR-IMPORT-11。注意 NotReviewed を出す）</summary>
    public bool IsPiecesEnteredByStaff { get; init; }

    /// <summary>アレルゲン表記の品目（Claude の対応づけ・アプリの対応づけと略記の補正・職員の直しのあと。品目 ID）</summary>
    public IReadOnlyCollection<string> LabelAllergenIds { get; init; } = [];

    /// <summary>品目に対応づけできなかった表記の語</summary>
    public IReadOnlyCollection<string> UnmappedTerms { get; init; } = [];

    /// <summary>職員が品目を選んだ語（語 → 品目 ID。空なら「対応する品目なし」）</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ResolvedTerms { get; init; } = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

    /// <summary>読み取りの検証の指摘（直した指摘は呼び出し側が外す）</summary>
    public IReadOnlyList<FlyerReviewNoteInput> ReviewNotes { get; init; } = [];

    /// <summary>二重の読み取りで食い違った値</summary>
    public IReadOnlyList<FlyerDuplicateConflict> DuplicateConflicts { get; init; } = [];

    /// <summary>照合の結果（今の商品名・メーカーで ProductMatcher.Match したもの）</summary>
    public required ProductMatch Match { get; init; }

    /// <summary>一致した商品の判定（AllergenJudge.Judge。一致したときは必ず渡す。記録と避ける品目の探す言葉を読み込んでおくこと）</summary>
    public AllergenJudgement? MatchedJudgement { get; init; }

    /// <summary>
    /// この行で新しく登録して「使わない」に戻し、照合から外した商品の判定（決めたこと H。なければ null）。
    /// 含む / 含む可能性あり なら「使えない」にする
    /// </summary>
    public AllergenJudgement? OwnCreatedJudgement { get; init; }

    /// <summary>マスタのすべての品目（探す言葉つき）</summary>
    public required IReadOnlyList<Allergen> AllAllergens { get; init; }

    /// <summary>施設の避ける品目（探す言葉つき。グループは展開しておく）</summary>
    public required IReadOnlyList<Allergen> AvoidedAllergens { get; init; }

    /// <summary>手入力した商品か（D-73。注意 NotReviewed を出す）</summary>
    public bool IsManual { get; init; }

    /// <summary>
    /// 一致した商品をチラシの値で上書きする週か（ListedProductsUpdater.ShouldOverwriteProduct。一致なしなら true）。
    /// 注意の文の切り替えだけに使う
    /// </summary>
    public bool OverwritesMaster { get; init; } = true;
}

/// <summary>分類の結果</summary>
public sealed class FlyerItemClassification
{
    /// <summary>分類</summary>
    public FlyerItemCategory Category { get; init; }

    /// <summary>当てはまった理由（使えない・再入力。アレルゲンに関わる理由が先）</summary>
    public IReadOnlyList<FlyerItemReason> Reasons { get; init; } = [];

    /// <summary>注意（分類は変えない。アレルゲンに関わるものが先。09 の 3.8）</summary>
    public IReadOnlyList<FlyerItemReason> Notes { get; init; } = [];

    /// <summary>チラシの品目（表記の品目 ∪ 職員が選んだ品目 ∪ 説明文・商品名で避ける品目の探す言葉が当たった品目）</summary>
    public IReadOnlyList<AllergenRef> FlyerAllergens { get; init; } = [];

    /// <summary>確認済みの商品で、有効な記録で「含む」になっていないチラシの品目（09 の 3.5）</summary>
    public IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies { get; init; } = [];

    /// <summary>まだ品目を決めていない表記の語</summary>
    public IReadOnlyList<string> UnresolvedTerms { get; init; } = [];

    /// <summary>使う入数（読み取りの候補、なければ一致した商品の入数。分からなければ null）</summary>
    public int? PiecesPerPack { get; init; }

    /// <summary>アレルゲンの表記がないか（決めたこと E）</summary>
    public bool IsLabelAbsent { get; init; }

    /// <summary>新しい商品か（一致する商品がない。取り込みを終えると新しく登録する）</summary>
    public bool IsNew { get; init; }
}
