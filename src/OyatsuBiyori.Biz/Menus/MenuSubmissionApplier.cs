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
        var issues = MenuProposalBuilder.Apply(target, Complete(target, proposal, mode), context);
        return (applied, issues);
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
