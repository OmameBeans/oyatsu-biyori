using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 届いた後の影響（FR-MENU-34a・36・37、SR-17・SR-22、D-15・D-37、T1-17）。保存せず、表示のたびに求める。
/// - 入れられない：今日以降の日の商品（下書き・確定とも）で、今の判定が「入れられない」もの。日の種類にかかわらず出す
/// - 確認し直し：確定済みの献立の今日以降の日の商品で、今の判定が「警告」なのに、確認したときの判定（種類と中身）と違うもの
/// </summary>
public static class MenuImpactAnalyzer
{
    /// <summary>影響を求める</summary>
    /// <param name="menus">献立（Days・Items を読み込んでおくこと。今日以降の日を持つものを渡す）</param>
    /// <param name="context">今の判定に使うもの（menus のすべての商品を読み込んでおくこと）</param>
    public static MenuImpact Analyze(IEnumerable<Menu> menus, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menus);
        ArgumentNullException.ThrowIfNull(context);

        var blocked = new List<ProductUsage>();
        var recheck = new List<ProductUsage>();
        foreach (var usage in Usages(menus, context.Today))
        {
            var product = context.FindProduct(usage.Item.ProductId);
            if (product is null)
            {
                // 商品が読めないものは、確認する側に倒して「入れられない」として示す
                blocked.Add(usage);
                continue;
            }

            var judgement = context.Judge(product);
            var withJudgement = usage with { ProductName = product.Name, Judgement = judgement };
            if (judgement.Category == AllergenJudgementCategory.Blocked)
            {
                blocked.Add(withJudgement);
            }
            else if (usage.Status == MenuStatus.Confirmed
                && judgement.Category == AllergenJudgementCategory.Warning
                && !MenuConfirmationChecker.IsAcknowledged(usage.Item, judgement, context.Fingerprint(product)))
            {
                recheck.Add(withJudgement);
            }
        }

        return new MenuImpact(blocked, recheck);
    }

    /// <summary>
    /// 商品を使う今日以降の献立と日付（FR-MENU-37・FR-PROD-11）。判定にかかわらず返す。
    /// </summary>
    public static IReadOnlyList<ProductUsage> FindUsages(string productId, IEnumerable<Menu> menus, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(menus);

        return Usages(menus, today).Where(u => u.Item.ProductId == productId).ToList();
    }

    private static IEnumerable<ProductUsage> Usages(IEnumerable<Menu> menus, DateOnly today)
    {
        return menus
            .SelectMany(m => m.Days
                .Where(d => d.Date >= today)
                .SelectMany(d => d.Items.Select(i => new ProductUsage(m.Id, m.WeekStart, m.Status, d.Date, i, null, null))))
            .OrderBy(u => u.Date)
            .ThenBy(u => u.MenuId, StringComparer.Ordinal);
    }
}

/// <summary>商品が使われている献立の日</summary>
/// <param name="MenuId">献立</param>
/// <param name="WeekStart">献立の週</param>
/// <param name="Status">献立の状態</param>
/// <param name="Date">日</param>
/// <param name="Item">献立の商品</param>
/// <param name="ProductName">商品名（判定を求めたときだけ）</param>
/// <param name="Judgement">今の判定（判定を求めたときだけ）</param>
public sealed record ProductUsage(string MenuId, DateOnly WeekStart, MenuStatus Status, DateOnly Date, MenuItem Item, string? ProductName, AllergenJudgement? Judgement);

/// <summary>届いた後の影響</summary>
/// <param name="BlockedUsages">入れられない商品を使う今日以降の日（献立一覧の最上部と日ごとのカード。FR-MENU-36）</param>
/// <param name="RecheckUsages">確定済みで確認し直しの商品（バッジ「確認し直し n件」。FR-MENU-34a）</param>
public sealed record MenuImpact(IReadOnlyList<ProductUsage> BlockedUsages, IReadOnlyList<ProductUsage> RecheckUsages);
