using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class MenuCandidateFilterTests
{
    [Fact]
    public void 確認済みの商品は候補で警告の印がない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50, (cookie, 298));

        var candidate = Assert.Single(MenuCandidateFilter.Candidates(menu, Context([cookie], Avoid("milk"))));

        Assert.False(candidate.IsWarning);
        Assert.Equal(298, candidate.Price);
    }

    [Fact]
    public void 参考の商品は警告の印を付けて候補に残す()
    {
        var jelly = ReferenceOnly("ぷるぷるゼリー");
        var menu = NewMenu(20000, 50, (jelly, 298));

        var candidate = Assert.Single(MenuCandidateFilter.Candidates(menu, Context([jelly], Avoid("milk"))));

        Assert.True(candidate.IsWarning);
    }

    [Fact]
    public void 避ける品目がないとき不明の商品は警告の印を付けて候補に残す()
    {
        var unknown = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var menu = NewMenu(20000, 50, (unknown, 298));

        var candidate = Assert.Single(MenuCandidateFilter.Candidates(menu, Context([unknown])));

        Assert.True(candidate.IsWarning);
    }

    [Fact]
    public void 入れられない商品は候補から外す_含む_含む可能性あり_不明()
    {
        var contains = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var mayContain = ReferenceOnly("ヘーゼルナッツクッキー");
        var unknown = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var menu = NewMenu(20000, 50, (contains, 198), (mayContain, 298), (unknown, 398));

        var evaluations = MenuCandidateFilter.Evaluate(menu, Context([contains, mayContain, unknown], [.. Avoid("egg"), .. Nuts()]));

        Assert.All(evaluations, e => Assert.Contains(CandidateExclusion.AllergenBlocked, e.Exclusions));
        Assert.Empty(MenuCandidateFilter.Candidates(menu, Context([contains, mayContain, unknown], [.. Avoid("egg"), .. Nuts()])));
    }

    [Fact]
    public void 価格か入数がない商品は候補から外す()
    {
        var noPrice = Safe("ねだんなしクッキー");
        var noPieces = Safe("いりすうなしせんべい", pieces: null);
        var menu = NewMenu(20000, 50, (noPrice, null), (noPieces, 298));

        var evaluations = MenuCandidateFilter.Evaluate(menu, Context([noPrice, noPieces]));

        Assert.Equal([CandidateExclusion.MissingPrice], evaluations.Single(e => e.ProductId == noPrice.Id).Exclusions);
        Assert.Equal([CandidateExclusion.MissingPieces], evaluations.Single(e => e.ProductId == noPieces.Id).Exclusions);
    }

    [Fact]
    public void 対象外_今週は使わない_使わない_の商品は候補から外す()
    {
        var notSnack = Safe("台所スポンジ", SnackType.NotSnack);
        var excluded = Safe("こんしゅうはいらないグミ");
        var disabled = Safe("ふにんきキャンディ");
        disabled.IsDisabled = true;
        var menu = NewMenu(20000, 50, (notSnack, 198), (excluded, 298), (disabled, 398));
        menu.ExcludedProductIds.Add(excluded.Id);

        var evaluations = MenuCandidateFilter.Evaluate(menu, Context([notSnack, excluded, disabled]));

        Assert.Equal([CandidateExclusion.NotSnack], evaluations.Single(e => e.ProductId == notSnack.Id).Exclusions);
        Assert.Equal([CandidateExclusion.ExcludedThisWeek], evaluations.Single(e => e.ProductId == excluded.Id).Exclusions);
        Assert.Equal([CandidateExclusion.ProductDisabled], evaluations.Single(e => e.ProductId == disabled.Id).Exclusions);
    }

    [Fact]
    public void 写しにあるが商品がなければ候補から外す()
    {
        var ghost = Safe("きえたおかし");
        var menu = NewMenu(20000, 50, (ghost, 298));

        var evaluation = Assert.Single(MenuCandidateFilter.Evaluate(menu, Context([])));

        Assert.Equal([CandidateExclusion.ProductNotFound], evaluation.Exclusions);
    }

    [Fact]
    public void 候補は写しの商品だけでメモとタグを含める()
    {
        var listed = Safe("さくさくクッキー");
        listed.Memo = "子どもに人気";
        listed.Tags.Add(new ProductTag { ProductId = listed.Id, Kind = ProductTagKind.Flavor, Value = "焼き菓子" });
        var notListed = Safe("のってないクッキー");
        var menu = NewMenu(20000, 50, (listed, 298));

        var candidate = Assert.Single(MenuCandidateFilter.Candidates(menu, Context([listed, notListed])));

        Assert.Equal(listed.Id, candidate.ProductId);
        Assert.Equal("子どもに人気", candidate.Memo);
        Assert.Equal("焼き菓子", Assert.Single(candidate.Tags).Value);
    }

    [Fact]
    public void 写しにない商品を調べると掲載品にないになる()
    {
        var notListed = Safe("のってないクッキー");
        var menu = NewMenu(20000, 50);

        var evaluation = MenuCandidateFilter.EvaluateProduct(menu, notListed.Id, Context([notListed]));

        Assert.Equal([CandidateExclusion.NotListed], evaluation.Exclusions);
    }

    [Fact]
    public void 写しに同じ商品が2行あれば止める()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50, (cookie, 298), (cookie, 398));

        Assert.Throws<InvalidOperationException>(() => MenuCandidateFilter.Evaluate(menu, Context([cookie])));
        Assert.Throws<InvalidOperationException>(() => MenuCandidateFilter.EvaluateProduct(menu, cookie.Id, Context([cookie])));
    }

    [Fact]
    public void 確認済みの前提を満たさない記録の例外は警告に落とさずそのまま伝える()
    {
        var broken = Safe("こわれたきろくのクッキー");
        broken.AllergenChecks[0].Items.Clear();
        var menu = NewMenu(20000, 50, (broken, 298));

        Assert.Throws<InvalidOperationException>(() => MenuCandidateFilter.Evaluate(menu, Context([broken], Avoid("milk"))));
    }
}
