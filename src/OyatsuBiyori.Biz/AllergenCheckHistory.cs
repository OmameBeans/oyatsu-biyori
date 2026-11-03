using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 商品のアレルゲン確認の記録（追加のみの履歴）から、最新の記録・有効な記録・「含む」品目・現在のステータスを求める（03 §3・§5、T1-05・T1-06）。
/// 判定は保存せず、表示・検証のたびに求める。
/// </summary>
public sealed class AllergenCheckHistory
{
    /// <summary>古い順に並べた記録</summary>
    public IReadOnlyList<AllergenCheck> Checks { get; }

    /// <summary>最新の記録（なければ null ＝「不明」）</summary>
    public AllergenCheck? Latest { get; }

    /// <summary>最新の「確認済み」の記録（なければ null）</summary>
    public AllergenCheck? LatestConfirmed { get; }

    /// <summary>
    /// 有効な記録：最新の「確認済み」と、それより後のすべての記録（「確認済み」がなければ、すべての記録）。
    /// 「確認済み」と同じ日時の記録も含める（「含む」を広く取る。確認する側に倒す）。
    /// </summary>
    public IReadOnlyList<AllergenCheck> ValidChecks { get; }

    /// <summary>有効な記録の「含む」品目の和集合（「参考」の記録で減らない。D-33）</summary>
    public IReadOnlySet<string> ContainedAllergenIds { get; }

    /// <summary>最終確認日（最新の「確認済み」の記録の日本時間の日付。なければ null）</summary>
    public DateOnly? LastConfirmedDate { get; }

    /// <summary>状態を決めている最新の記録の日付（日本時間。参考が最新なら参考の日。なければ null）</summary>
    public DateOnly? LatestDate { get; }

    private AllergenCheckHistory(IReadOnlyList<AllergenCheck> checks)
    {
        Checks = checks;
        Latest = checks.Count > 0 ? checks[^1] : null;
        LatestConfirmed = checks.LastOrDefault(c => c.Status == AllergenCheckStatus.Confirmed);
        ValidChecks = LatestConfirmed is null
            ? checks
            : checks.Where(c => c.CheckedAt >= LatestConfirmed.CheckedAt).ToList();
        ContainedAllergenIds = ValidChecks
            .SelectMany(c => c.Items)
            .Where(i => i.Contains)
            .Select(i => i.AllergenId)
            .ToHashSet(StringComparer.Ordinal);
        LastConfirmedDate = LatestConfirmed is null ? null : JapanDate.FromUtc(LatestConfirmed.CheckedAt);
        LatestDate = Latest is null ? null : JapanDate.FromUtc(Latest.CheckedAt);
    }

    /// <summary>
    /// 1つの商品の記録から作る。記録の Items は読み込んでおくこと。
    /// 並びは確認日時の順。同じ日時の記録が並んだら「参考」を後（新しい側）にする（確認する側に倒す）。
    /// 「確認済み」の前提（パッケージ・原材料の全文・品目の行）を満たさない記録があれば、黙って判定せず止める。
    /// </summary>
    public static AllergenCheckHistory From(IEnumerable<AllergenCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        var list = checks.ToList();
        foreach (var check in list.Where(c => c.Status == AllergenCheckStatus.Confirmed))
        {
            EnsureValidConfirmed(check);
        }

        var ordered = list
            .OrderBy(c => c.CheckedAt)
            .ThenBy(c => c.Status == AllergenCheckStatus.Reference ? 1 : 0)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        return new AllergenCheckHistory(ordered);
    }

    /// <summary>
    /// 「確認済み」の記録の前提を確かめる。満たさない記録は、作る側の誤り（SR-03・SR-12）か、Items の読み込み忘れ。
    /// どちらも全文や「含む」が欠けたまま「安全」になりうるため、例外で止める。
    /// - 情報源はパッケージだけ（SR-03）
    /// - 原材料の全文（読み取り・職員の確認のどちらか）がある（SR-12。全文がないと探す言葉がかからない）
    /// - 品目の行がある（SR-15 で29品目と避ける追加品目をすべて確定するため、0件は読み込み忘れ）
    /// </summary>
    private static void EnsureValidConfirmed(AllergenCheck check)
    {
        if (check.Source != AllergenCheckSource.Package)
        {
            throw new InvalidOperationException($"確認の記録 {check.Id} は情報源がパッケージでないのに「確認済み」になっています。");
        }

        if (string.IsNullOrWhiteSpace(check.RawIngredients) && string.IsNullOrWhiteSpace(check.RawIngredientsOcr))
        {
            throw new InvalidOperationException($"確認の記録 {check.Id} は原材料の全文がないのに「確認済み」になっています。");
        }

        if (check.Items.Count == 0)
        {
            throw new InvalidOperationException($"確認の記録 {check.Id} の品目が読み込まれていません。記録は Items を含めて読み込んでください。");
        }
    }

    /// <summary>
    /// 最新の「確認済み」が再確認期間を過ぎたか（日本時間の日付で `今日 - 確認日 > 期間`。90日なら91日目から）。
    /// 「確認済み」がなければ false。
    /// </summary>
    public bool IsRecheckNeeded(DateOnly today, int recheckDays)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recheckDays);

        if (LastConfirmedDate is not { } confirmedDate)
        {
            return false;
        }

        return today.DayNumber - confirmedDate.DayNumber > recheckDays;
    }

    /// <summary>
    /// 商品の現在のステータス（商品一覧の表示と絞り込み用。T1-06）。判定（AllergenJudge）とは別に求める。
    /// 記録なし → 不明 / 最新が参考 → 参考 / 再確認期間を過ぎた → 要再確認 / それ以外 → 確認済み。
    /// </summary>
    public ProductAllergenStatus GetStatus(DateOnly today, int recheckDays)
    {
        if (Latest is null)
        {
            return ProductAllergenStatus.Unknown;
        }

        if (Latest.Status == AllergenCheckStatus.Reference)
        {
            return ProductAllergenStatus.Reference;
        }

        return IsRecheckNeeded(today, recheckDays) ? ProductAllergenStatus.RecheckNeeded : ProductAllergenStatus.Confirmed;
    }
}
