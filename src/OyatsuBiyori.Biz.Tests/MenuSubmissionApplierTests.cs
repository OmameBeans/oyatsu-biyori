using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// エージェントの案を献立に当てる処理（道具の試しと本物の反映で共通）。
/// レビューで見つかったバグ：チャットで一部の日だけ（または日ごとの値だけ）を出すと、ほかの日の商品がすべて消えていた。
/// </summary>
public class MenuSubmissionApplierTests
{
    private static (Menu Menu, MenuContext Context, Product A, Product B, Product C) FullMenu()
    {
        var a = Safe("さくさくビスケット");
        var b = Safe("もぐもぐせんべい", SnackType.Salty);
        var c = Safe("ぷるぷるゼリー", SnackType.Smooth);
        var menu = NewMenu(20000, 50, (a, 300), (b, 250), (c, 280));
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, a);
            menu.Days[i].Reason = $"前の理由{i}";
        }

        return (menu, Context([a, b, c]), a, b, c);
    }

    [Fact]
    public void チャットで日ごとの値だけを出しても_商品は消えない()
    {
        var (menu, context, a, _, _) = FullMenu();
        var interpreted = new InterpretedRequests { Days = [new InterpretedDay(D(4), 30, null, null)] };

        MenuSubmissionApplier.Apply(menu, new MenuAgentProposal([], []), interpreted, [D(4)], MenuAgentMode.Chat, 50, context);

        Assert.All(menu.Days, d => Assert.Equal(a.Id, Assert.Single(d.Items).ProductId));
        Assert.Equal(30, menu.Days[4].ChildrenCount);
        // 人数を変えた日はパック数を式で求め直す（30人 ÷ 入数10 = 3パック）
        Assert.Equal(3, menu.Days[4].Items[0].PackCount);
    }

    [Fact]
    public void チャットで1日だけを出したら_ほかの日は商品と理由のまま残る()
    {
        var (menu, context, a, b, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(2), [b.Id], "せんべいにしました")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Equal(b.Id, Assert.Single(menu.Days[2].Items).ProductId);
        Assert.Equal("せんべいにしました", menu.Days[2].Reason);
        foreach (var index in new[] { 0, 1, 3, 4 })
        {
            Assert.Equal(a.Id, Assert.Single(menu.Days[index].Items).ProductId);
            Assert.Equal($"前の理由{index}", menu.Days[index].Reason);
        }
    }

    [Fact]
    public void チャットで日を空のリストで出したら_その日だけ空にする()
    {
        var (menu, context, _, _, _) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(1), [], "この日はなしにしました")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        Assert.Empty(menu.Days[1].Items);
        Assert.All(menu.Days.Where((_, i) => i != 1), d => Assert.Single(d.Items));
    }

    [Fact]
    public void 作成では案で1週間分を置き換え_案にない日は空にする()
    {
        var (menu, context, _, b, c) = FullMenu();
        var proposal = new MenuAgentProposal([new MenuAgentProposalDay(D(0), [b.Id, c.Id], "理由")], []);

        MenuSubmissionApplier.Apply(menu, proposal, new InterpretedRequests(), [], MenuAgentMode.Create, 50, context);

        Assert.Equal(2, menu.Days[0].Items.Count);
        Assert.All(menu.Days.Skip(1), d => Assert.Empty(d.Items));
    }

    [Fact]
    public void 同じ商品を残した日は_確定時の確認とパック数の上書きを残す()
    {
        var (menu, context, a, _, _) = FullMenu();
        var item = menu.Days[0].Items[0];
        item.PackCount = 9;
        item.AcknowledgedAt = NoonUtc(Today);
        item.AcknowledgedJudgement = AllergenJudgementKind.Reference;

        MenuSubmissionApplier.Apply(menu, new MenuAgentProposal([new MenuAgentProposalDay(D(0), [a.Id], "理由")], []), new InterpretedRequests(), [], MenuAgentMode.Chat, 50, context);

        var kept = Assert.Single(menu.Days[0].Items);
        Assert.Same(item, kept);
        Assert.Equal(9, kept.PackCount);
        Assert.NotNull(kept.AcknowledgedAt);
    }

    [Fact]
    public void 写しは確定時の確認も写す()
    {
        var (menu, _, _, _, _) = FullMenu();
        var item = menu.Days[0].Items[0];
        item.AcknowledgedAt = NoonUtc(Today);
        item.AcknowledgedJudgement = AllergenJudgementKind.Reference;
        item.AcknowledgedJudgementFingerprint = "指紋";

        var clone = MenuCloner.Clone(menu).Days[0].Items[0];

        Assert.Equal(item.AcknowledgedAt, clone.AcknowledgedAt);
        Assert.Equal(item.AcknowledgedJudgement, clone.AcknowledgedJudgement);
        Assert.Equal("指紋", clone.AcknowledgedJudgementFingerprint);
        Assert.NotSame(item, clone);
    }
}
