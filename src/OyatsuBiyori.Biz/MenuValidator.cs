using System.Text;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立案の検証（FR-MENU-15、SR-18、D-23、T1-13）。Claude の案・手動の編集・チャット・入れ替え案のどれにも同じ検証をかける。
/// エラー：入れられない・掲載品にない・写しの価格か入数がない・存在しない商品・候補から外した商品（今週は使わない・使わない・対象外）
/// 警告：判定が警告・重複・予算超過・通常の日が0品・手作り / おやつなしの日に商品・全員分に足りないパック数
/// 日の種類・人数にかかわらず、すべての日の商品の判定を調べる（手作りの日でも、入れられない商品はエラー）。
/// </summary>
public static class MenuValidator
{
    /// <summary>献立を検証する</summary>
    /// <param name="menu">献立（Days・Items を読み込んでおくこと）</param>
    /// <param name="context">商品・判定・重複の基準</param>
    /// <param name="proposalIssues">Claude の案を反映したときに作れなかった商品（MenuProposalBuilder の結果）</param>
    public static MenuValidationResult Validate(Menu menu, MenuContext context, IEnumerable<MenuIssue>? proposalIssues = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<MenuIssue>(proposalIssues ?? []);
        var days = menu.Days.OrderBy(d => d.Date).ToList();

        foreach (var day in days)
        {
            foreach (var item in day.Items)
            {
                issues.AddRange(ItemIssues(menu, day, item, context));
            }

            if (day.DayType == DayType.Normal && day.ChildrenCount > 0 && day.Items.Count == 0)
            {
                issues.Add(new MenuIssue(MenuIssueSeverity.Warning, MenuIssueKind.EmptyNormalDay, day.Date, null, null, null,
                    "おやつの商品が1つもありません。商品を入れるか、日の種類を「手作り」「おやつなし」にしてください"));
            }
        }

        var week = MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack);
        foreach (var dayAmount in week.Days)
        {
            if (dayAmount.ExcludedFromAmount)
            {
                var day = days.First(d => d.Date == dayAmount.Date);
                foreach (var item in day.Items)
                {
                    issues.Add(Issue(MenuIssueSeverity.Warning, MenuIssueKind.ItemsOnNonNormalDay, day, item, context,
                        $"日の種類が「{DayTypeLabel(day.DayType)}」のため、金額に含まれていません。不要なら外してください"));
                }
            }

            foreach (var item in dayAmount.ShortPackItems)
            {
                var day = days.First(d => d.Date == dayAmount.Date);
                issues.Add(Issue(MenuIssueSeverity.Warning, MenuIssueKind.PackShort, day, item, context,
                    $"パック数（{item.PackCount}）が全員分（{day.ChildrenCount}人 × {item.PerChildCount}個）に足りません"));
            }
        }

        foreach (var hit in DuplicateChecker.Check(menu, context))
        {
            var day = days.First(d => d.Date == hit.Date);
            issues.Add(Issue(MenuIssueSeverity.Warning, MenuIssueKind.Duplicate, day, hit.Item, context, hit.Message));
        }

        if (week.IsOverBudget)
        {
            issues.Add(new MenuIssue(MenuIssueSeverity.Warning, MenuIssueKind.OverBudget, null, null, null, null,
                $"週の実績 {week.Total:N0}円 が週の予算 {week.Budget:N0}円 を {-week.Remaining:N0}円 超えています"));
        }

        return new MenuValidationResult(issues, week);
    }

    /// <summary>
    /// 1つの商品の問題（候補の絞り込みと同じ基準。T1-11）。
    /// </summary>
    private static IEnumerable<MenuIssue> ItemIssues(Menu menu, MenuDay day, MenuItem item, MenuContext context)
    {
        var evaluation = MenuCandidateFilter.EvaluateProduct(menu, item.ProductId, context);
        foreach (var exclusion in evaluation.Exclusions)
        {
            yield return exclusion switch
            {
                CandidateExclusion.ProductNotFound => Issue(MenuIssueSeverity.Error, MenuIssueKind.ProductNotFound, day, item, context,
                    "この商品はありません。献立から外してください"),
                CandidateExclusion.AllergenBlocked => Issue(MenuIssueSeverity.Error, MenuIssueKind.AllergenBlocked, day, item, context,
                    MenuIssueText.BlockedReason(evaluation.Judgement!)),
                CandidateExclusion.NotListed => Issue(MenuIssueSeverity.Error, MenuIssueKind.NotListed, day, item, context,
                    "その週の掲載品にないため、注文できません。別の商品にしてください"),
                CandidateExclusion.MissingPrice => Issue(MenuIssueSeverity.Error, MenuIssueKind.MissingPriceOrPieces, day, item, context,
                    "その週の税込価格がありません。今週の商品で税込価格を入れてください"),
                CandidateExclusion.MissingPieces => Issue(MenuIssueSeverity.Error, MenuIssueKind.MissingPriceOrPieces, day, item, context,
                    "入数がありません。今週の商品で入数を入れてください"),
                CandidateExclusion.InvalidPerChildCount => Issue(MenuIssueSeverity.Error, MenuIssueKind.MissingPriceOrPieces, day, item, context,
                    "1人あたりの個数が正しくありません。商品の編集で1以上にしてください"),
                CandidateExclusion.NotSnack => Issue(MenuIssueSeverity.Error, MenuIssueKind.NotSnack, day, item, context,
                    "おやつの分類が「対象外」の商品です。別の商品にしてください"),
                CandidateExclusion.ProductDisabled => Issue(MenuIssueSeverity.Error, MenuIssueKind.ProductDisabled, day, item, context,
                    "「使わない」にした商品です。別の商品にしてください"),
                CandidateExclusion.ExcludedThisWeek => Issue(MenuIssueSeverity.Error, MenuIssueKind.ExcludedThisWeek, day, item, context,
                    "「今週は使わない」にした商品です。別の商品にしてください"),
                _ => throw new InvalidOperationException($"知らない除外の理由です: {exclusion}"),
            };
        }

        if (evaluation.Judgement is { Category: AllergenJudgementCategory.Warning } judgement)
        {
            yield return Issue(MenuIssueSeverity.Warning, MenuIssueKind.AllergenWarning, day, item, context, MenuIssueText.WarningReason(judgement));
        }
    }

    private static MenuIssue Issue(MenuIssueSeverity severity, MenuIssueKind kind, MenuDay day, MenuItem item, MenuContext context, string message)
    {
        return new MenuIssue(severity, kind, day.Date, item.Id, item.ProductId, context.FindProduct(item.ProductId)?.Name, message);
    }

    private static string DayTypeLabel(DayType dayType) => dayType switch
    {
        DayType.Handmade => "手作り",
        DayType.NoSnack => "おやつなし",
        _ => "通常",
    };

    /// <summary>
    /// 直しきれなかったエラーの商品を献立から外す（FR-MENU-15）。外したあとは検証し直すこと（0品の日の警告などが変わる）。
    /// </summary>
    /// <returns>外した商品と、その日</returns>
    public static IReadOnlyList<(DateOnly Date, MenuItem Item)> RemoveErrorItems(Menu menu, MenuValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(result);

        var errorItemIds = result.Errors.Where(i => i.MenuItemId is not null).Select(i => i.MenuItemId!).ToHashSet(StringComparer.Ordinal);
        var removed = new List<(DateOnly, MenuItem)>();
        foreach (var day in menu.Days)
        {
            foreach (var item in day.Items.Where(i => errorItemIds.Contains(i.Id)).ToList())
            {
                day.Items.Remove(item);
                removed.Add((day.Date, item));
            }
        }

        return removed;
    }
}

/// <summary>
/// 献立案の検証の結果。
/// </summary>
public sealed class MenuValidationResult(IReadOnlyList<MenuIssue> issues, WeekAmount week)
{
    /// <summary>すべての問題（日付の順）</summary>
    public IReadOnlyList<MenuIssue> Issues { get; } = issues
        .OrderBy(i => i.Date ?? DateOnly.MaxValue)
        .ThenBy(i => i.Severity)
        .ThenBy(i => i.Kind)
        .ToList();

    /// <summary>週の金額</summary>
    public WeekAmount Week { get; } = week;

    /// <summary>エラー（Claude に返して直させる）</summary>
    public IReadOnlyList<MenuIssue> Errors => Issues.Where(i => i.Severity == MenuIssueSeverity.Error).ToList();

    /// <summary>警告（表示だけ）</summary>
    public IReadOnlyList<MenuIssue> Warnings => Issues.Where(i => i.Severity == MenuIssueSeverity.Warning).ToList();

    /// <summary>エラーがあるか（submit_menu で受け付けない）</summary>
    public bool HasError => Issues.Any(i => i.Severity == MenuIssueSeverity.Error);

    /// <summary>
    /// Claude に返す文面：エラーだけを、日付・商品・理由の形で並べる。警告は返さない（予算超過などは許すため）。
    /// アレルゲンは品目名だけを伝え、判定の根拠を Claude に委ねない（SR-18）。
    /// </summary>
    public string ToClaudeFeedback()
    {
        var errors = Errors;
        if (errors.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.AppendLine("次の問題があるため、この献立は受け付けられません。問題の商品を別の商品に替えて、もう一度出してください。アプリの判定は変わりません。");
        foreach (var error in errors)
        {
            var id = error.ProductId is null || error.ProductName is null ? string.Empty : $"（商品ID: {error.ProductId}）";
            text.AppendLine("- " + error.ToLine() + id);
        }

        return text.ToString().TrimEnd();
    }
}
