using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Menus;

/// <summary>
/// エージェントが出した案（submit_menu）を献立に当てる処理を1か所にまとめる（道具での試しと、本物の献立への反映の両方で使う）。
/// - 作成：要望の読み取り（ApplyRequest）を当て、案で1週間分を置き換える（案にない日は商品なし）
/// - チャット：頼まれた日ごとの値（ApplyChat）を当て、案に出てこない日は今の商品と理由のまま残す（依頼と関係のない日を消さない）
/// </summary>
public static class MenuSubmissionApplier
{
    /// <summary>案を献立に当てる。反映できなかった商品（存在しない ID など）は問題として返す（残りは MenuValidator で検証する）</summary>
    public static (InterpretedRequestApplyResult Applied, IReadOnlyList<MenuIssue> ProposalIssues) Apply(
        Menu target,
        MenuAgentProposal proposal,
        InterpretedRequests interpreted,
        IReadOnlyList<DateOnly> explicitDates,
        MenuAgentMode mode,
        int defaultChildrenCount,
        MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(interpreted);
        ArgumentNullException.ThrowIfNull(explicitDates);
        ArgumentNullException.ThrowIfNull(context);

        var applied = mode == MenuAgentMode.Create
            ? InterpretedRequestApplier.ApplyRequest(target, interpreted, defaultChildrenCount, context.PiecesPerPack)
            : InterpretedRequestApplier.ApplyChat(target, interpreted.Days, explicitDates.ToHashSet(), context.PiecesPerPack);
        var issues = MenuProposalBuilder.Apply(target, Complete(target, proposal, mode), context).ToList();
        if (mode == MenuAgentMode.Create)
        {
            RestoreShortPacks(target, context);
        }

        issues.AddRange(ApplyServings(target, proposal, context));
        return (applied, issues);
    }

    /// <summary>
    /// 作成（作り直し）では、前の案から残した商品のうち、全員分に足りないパック数を全員分に戻す。
    /// 前のチャットで「○人分」に減らした数を、新しい案に黙って持ち込まないため（作成ではエージェントに今の献立を見せない）。
    /// 全員分に足りているパック数（予備を多めにした上書き）は残す。今回の案の servings は、このあとに当てる
    /// </summary>
    private static void RestoreShortPacks(Menu target, MenuContext context)
    {
        foreach (var day in target.Days)
        {
            foreach (var item in day.Items)
            {
                if (context.PiecesPerPack(item.ProductId) is int pieces && pieces >= 1 && item.PerChildCount >= 1
                    && MenuAmountCalculator.IsPackShort(item.PackCount, day.ChildrenCount, item.PerChildCount, pieces))
                {
                    MenuItemFactory.SetPackCount(item, day, MenuAmountCalculator.DefaultPackCount(day.ChildrenCount, item.PerChildCount, pieces));
                }
            }
        }
    }

    /// <summary>
    /// 商品ごとの「何人分買うか」（在庫で補う日など）を、パック数に直して当てる（作成・チャットの両方。FR-MENU-08b・D-102）。
    /// パック数 = 切り上げ（何人分 × 1人あたりの個数 ÷ 入数）。エージェントにはパック数・金額を決めさせない。
    /// 日の人数と商品を当てたあとに当てる（人数を変えた日の、式での求め直しで消さないため）。
    /// 使えない指定（その日にない商品・範囲外の人数・同じ商品への二重の指定）は当てずにエラーとして返し、直させる（商品は外さない）。
    /// </summary>
    public static IReadOnlyList<MenuIssue> ApplyServings(Menu target, MenuAgentProposal proposal, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<MenuIssue>();
        // 同じ日付の案が2つに分かれていても、MenuProposalBuilder は1日にまとめるので、指定も日付ごとにまとめて見る
        var servingsByDate = proposal.Days
            .Where(d => d.Servings is { Count: > 0 })
            .GroupBy(d => d.Date)
            .Select(g => (Date: g.Key, Servings: g.SelectMany(d => d.Servings!).ToList(), ProductIds: g.SelectMany(d => d.ProductIds).ToHashSet(StringComparer.Ordinal)));
        foreach (var proposed in servingsByDate)
        {
            var day = target.Days.FirstOrDefault(d => d.Date == proposed.Date);
            if (day is null)
            {
                // 献立にない日は MenuProposalBuilder が UnknownDate として返している
                continue;
            }

            var duplicated = proposed.Servings
                .GroupBy(s => s.ProductId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var productId in duplicated)
            {
                issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.InvalidServing, day.Date, null, productId, context.FindProduct(productId)?.Name,
                    "同じ商品に、何人分買うかの指定（servings）が2つ以上あります。1つにしてください（この指定は反映していません）"));
            }

            foreach (var serving in proposed.Servings.Where(s => !duplicated.Contains(s.ProductId)))
            {
                var product = context.FindProduct(serving.ProductId);
                var item = day.Items.FirstOrDefault(i => i.ProductId == serving.ProductId);
                if (item is null && proposed.ProductIds.Contains(serving.ProductId))
                {
                    // productIds に入れたのに商品を作れなかった（掲載品にない・価格がないなど）。その理由は MenuProposalBuilder が返しているので、重ねて言わない
                    continue;
                }

                if (item is null)
                {
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.InvalidServing, day.Date, null, serving.ProductId, product?.Name,
                        "何人分買うかの指定（servings）は、その日の productIds に入れた商品にだけ付けられます（この指定は反映していません）"));
                    continue;
                }

                if (serving.ChildrenCount < 1 || serving.ChildrenCount > InputValidator.MaxChildrenCount)
                {
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.InvalidServing, day.Date, null, serving.ProductId, product?.Name,
                        $"何人分買うかの指定（servings）は1〜{InputValidator.MaxChildrenCount}人で入れてください（この指定は反映していません）"));
                    continue;
                }

                if (context.PiecesPerPack(serving.ProductId) is not int pieces || pieces < 1)
                {
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.InvalidServing, day.Date, null, serving.ProductId, product?.Name,
                        "入数がないため、何人分買うかからパック数を求められません（この指定は反映していません）"));
                    continue;
                }

                var set = MenuItemFactory.SetPackCount(item, day, MenuAmountCalculator.DefaultPackCount(serving.ChildrenCount, item.PerChildCount, pieces));
                if (set.HasError)
                {
                    issues.Add(new MenuIssue(MenuIssueSeverity.Error, MenuIssueKind.InvalidServing, day.Date, null, serving.ProductId, product?.Name,
                        $"{set.ErrorMessages[0]}（この指定は反映していません）"));
                }
            }
        }

        return issues;
    }

    /// <summary>チャットでは、案に出てこない日を今の商品と理由で埋める（MenuProposalBuilder は案にない日を空にするため）</summary>
    public static MenuProposal Complete(Menu target, MenuAgentProposal proposal, MenuAgentMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(proposal);

        var days = proposal.Days.Select(d => new MenuProposalDay(d.Date, d.ProductIds, d.Reason)).ToList();
        if (mode == MenuAgentMode.Chat)
        {
            var mentioned = proposal.Days.Select(d => d.Date).ToHashSet();
            days.AddRange(target.Days
                .Where(d => !mentioned.Contains(d.Date))
                .Select(d => new MenuProposalDay(d.Date, d.Items.Select(i => i.ProductId).ToList(), d.Reason)));
        }

        return new MenuProposal(days);
    }
}
