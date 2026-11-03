using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// Claude の献立案を献立の商品に反映する（FR-MENU-15、T1-12・T1-13）。
/// Claude から受け取るのは日ごとの商品 ID と理由だけ。個数・パック数・金額は MenuItemFactory で計算する。
/// 存在しない商品・掲載品にない商品・価格か入数がない商品・献立にない日付は、商品を作らずにエラーとして返す
/// （残りは MenuValidator で検証する）。
/// </summary>
public static class MenuProposalBuilder
{
    /// <summary>
    /// 案で献立の日ごとの商品を置き換える。案にない日は商品を空にする（案は1週間分を出させる）。
    /// 同じ日に同じ商品がもう入っていれば、その行（1人あたりの個数・パック数の上書き・確定時の確認）をそのまま残す。
    /// </summary>
    /// <param name="menu">反映する献立（Days・Items を読み込んでおくこと）</param>
    /// <param name="proposal">Claude の案</param>
    /// <param name="context">商品と判定</param>
    /// <returns>反映できなかった商品（エラー）</returns>
    public static IReadOnlyList<MenuIssue> Apply(Menu menu, MenuProposal proposal, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<MenuIssue>();
        var daysByDate = menu.Days.ToDictionary(d => d.Date);

        foreach (var unknown in proposal.Days.Where(d => !daysByDate.ContainsKey(d.Date)))
        {
            issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.UnknownDate, unknown.Date, null, null, null,
                "この日は献立にありません（おやつを出す日ではありません）。献立にある日だけに商品を入れてください"));
        }

        var proposedByDate = proposal.Days
            .Where(d => daysByDate.ContainsKey(d.Date))
            .GroupBy(d => d.Date)
            .ToDictionary(g => g.Key, g => (ProductIds: g.SelectMany(d => d.ProductIds).ToList(), Reason: g.Select(d => d.Reason).LastOrDefault(r => !string.IsNullOrWhiteSpace(r))));

        foreach (var day in menu.Days)
        {
            var proposed = proposedByDate.GetValueOrDefault(day.Date);
            var productIds = proposed.ProductIds ?? [];
            var kept = new List<MenuItem>();
            var remaining = day.Items.ToList();

            foreach (var productId in productIds.Distinct(StringComparer.Ordinal))
            {
                var existing = remaining.FirstOrDefault(i => i.ProductId == productId);
                if (existing is not null)
                {
                    remaining.Remove(existing);
                    kept.Add(existing);
                    continue;
                }

                var product = context.FindProduct(productId);
                if (product is null)
                {
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.ProductNotFound, day.Date, null, productId, null,
                        "この商品 ID の商品はありません。search_products で返った商品から選んでください"));
                    continue;
                }

                var listed = MenuCandidateFilter.EvaluateProduct(menu, productId, context).Listed;
                var created = MenuItemFactory.Create(day, product, listed);
                if (created.HasError)
                {
                    var kind = listed is null ? MenuIssueKind.NotListed : MenuIssueKind.MissingPriceOrPieces;
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, kind, day.Date, null, productId, product.Name, created.ErrorMessages[0]));
                    continue;
                }

                kept.Add(created.Value!);
            }

            day.Items.Clear();
            day.Items.AddRange(kept);
            day.Reason = proposed.Reason;
        }

        return issues;
    }
}

/// <summary>Claude の献立案（Biz 側の形。1週間分）</summary>
/// <param name="Days">日ごとの案</param>
public sealed record MenuProposal(IReadOnlyList<MenuProposalDay> Days);

/// <summary>日ごとの案</summary>
/// <param name="Date">日付</param>
/// <param name="ProductIds">商品 ID（個数は決めさせない）</param>
/// <param name="Reason">理由（予算・アレルギー・要望をどう満たしたか）</param>
public sealed record MenuProposalDay(DateOnly Date, IReadOnlyList<string> ProductIds, string? Reason);
