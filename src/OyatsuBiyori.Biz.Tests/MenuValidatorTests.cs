using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class MenuValidatorTests
{
    /// <summary>月〜金に1品ずつ入れた、問題のない献立</summary>
    private static (Menu Menu, List<Product> Products) CleanMenu(int budget = 20000)
    {
        var products = Enumerable.Range(0, 5).Select(i => Safe($"おかし{i}")).ToList();
        var menu = NewMenu(budget, 50, products.Select(p => (p, (int?)300)).ToArray());
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, products[i]);
        }

        return (menu, products);
    }

    [Fact]
    public void 問題のない献立は問題なし()
    {
        var (menu, products) = CleanMenu();

        var result = MenuValidator.Validate(menu, Context(products, Avoid("milk")));

        Assert.Empty(result.Issues);
        Assert.False(result.HasError);
        Assert.Equal(5 * 5 * 300, result.Week.Total);
        Assert.Equal(string.Empty, result.ToClaudeFeedback());
    }

    [Fact]
    public void 入れられない商品はエラーで理由に品目名を出す()
    {
        var (menu, products) = CleanMenu();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        var item = Put(menu, 2, egg);

        var result = MenuValidator.Validate(menu, Context([.. products, egg], Avoid("egg")));

        var error = Assert.Single(result.Errors);
        Assert.Equal(MenuIssueKind.AllergenBlocked, error.Kind);
        Assert.Equal(item.Id, error.MenuItemId);
        Assert.Contains("卵", error.Message);
        Assert.True(error.BlocksConfirmation);
    }

    [Fact]
    public void 探す言葉が当たった商品と不明の商品もエラー()
    {
        var (menu, products) = CleanMenu();
        var hazelnut = ReferenceOnly("ヘーゼルナッツクッキー");
        var unknown = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        menu.ListedProducts.Add(new ListedProduct(hazelnut.Id, 198, "2222"));
        menu.ListedProducts.Add(new ListedProduct(unknown.Id, 198, "3333"));
        Put(menu, 0, hazelnut);
        Put(menu, 1, unknown);

        var result = MenuValidator.Validate(menu, Context([.. products, hazelnut, unknown], Nuts()));

        Assert.Equal(2, result.Errors.Count(e => e.Kind == MenuIssueKind.AllergenBlocked));
        Assert.Contains(result.Errors, e => e.Message.Contains("探す言葉"));
        Assert.Contains(result.Errors, e => e.Message.Contains("アレルゲンの情報がない"));
    }

    [Fact]
    public void 手作りの日や人数0の日でも入れられない商品はエラー()
    {
        var (menu, products) = CleanMenu();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        Put(menu, 3, egg);
        Put(menu, 4, egg);
        menu.Days[3].DayType = DayType.Handmade;
        menu.Days[4].ChildrenCount = 0;

        var result = MenuValidator.Validate(menu, Context([.. products, egg], Avoid("egg")));

        Assert.Equal(2, result.Errors.Count(e => e.Kind == MenuIssueKind.AllergenBlocked));
    }

    [Fact]
    public void 掲載品にない商品と価格がない商品と存在しない商品はエラー()
    {
        var (menu, products) = CleanMenu();
        var notListed = Safe("のってないクッキー");
        var noPrice = Safe("ねだんなしクッキー");
        menu.ListedProducts.Add(new ListedProduct(noPrice.Id, null, null));
        PutRaw(menu, 0, notListed.Id);
        PutRaw(menu, 1, noPrice.Id);
        PutRaw(menu, 2, "deleted-product");

        var result = MenuValidator.Validate(menu, Context([.. products, notListed, noPrice]));

        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.NotListed && e.ProductId == notListed.Id);
        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.MissingPriceOrPieces && e.ProductId == noPrice.Id);
        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.ProductNotFound && e.ProductId == "deleted-product");
    }

    [Fact]
    public void 候補から外した商品はエラー()
    {
        var (menu, products) = CleanMenu();
        menu.ExcludedProductIds.Add(products[0].Id);
        products[1].IsDisabled = true;
        products[2].SnackType = SnackType.NotSnack;

        var result = MenuValidator.Validate(menu, Context(products));

        Assert.Equal(
            [MenuIssueKind.ExcludedThisWeek, MenuIssueKind.ProductDisabled, MenuIssueKind.NotSnack],
            result.Errors.Select(e => e.Kind));
        Assert.All(result.Errors, e => Assert.False(e.BlocksConfirmation));
    }

    [Fact]
    public void 警告の商品は警告でエラーにしない()
    {
        var (menu, products) = CleanMenu();
        var jelly = ReferenceOnly("ぷるぷるゼリー");
        menu.ListedProducts.Add(new ListedProduct(jelly.Id, 198, "2222"));
        Put(menu, 0, jelly);

        var result = MenuValidator.Validate(menu, Context([.. products, jelly], Avoid("milk")));

        Assert.False(result.HasError);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(MenuIssueKind.AllergenWarning, warning.Kind);
        Assert.Contains("パッケージ", warning.Message);
    }

    [Fact]
    public void 予算超過は警告()
    {
        var (menu, products) = CleanMenu(budget: 7499);

        var result = MenuValidator.Validate(menu, Context(products));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(MenuIssueKind.OverBudget, warning.Kind);
        Assert.Null(warning.Date);
        Assert.Contains("1円", warning.Message);
    }

    [Fact]
    public void 予算ちょうどは警告しない()
    {
        var (menu, products) = CleanMenu(budget: 7500);

        Assert.Empty(MenuValidator.Validate(menu, Context(products)).Issues);
    }

    [Fact]
    public void 通常の日が0品なら警告し手作りの日が0品なら警告しない()
    {
        var (menu, products) = CleanMenu();
        menu.Days[0].Items.Clear();
        menu.Days[1].Items.Clear();
        menu.Days[1].DayType = DayType.Handmade;

        var result = MenuValidator.Validate(menu, Context(products));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(MenuIssueKind.EmptyNormalDay, warning.Kind);
        Assert.Equal(D(0), warning.Date);
    }

    [Fact]
    public void 手作りやおやつなしの日に商品があれば金額に含まれていないと警告する()
    {
        var (menu, products) = CleanMenu();
        menu.Days[2].DayType = DayType.NoSnack;
        MenuItemFactory.RefreshAmounts(menu.Days[2]);

        var result = MenuValidator.Validate(menu, Context(products));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(MenuIssueKind.ItemsOnNonNormalDay, warning.Kind);
        Assert.Contains("金額に含まれていません", warning.Message);
        Assert.Equal(4 * 5 * 300, result.Week.Total);
    }

    [Fact]
    public void 手作りの日の商品には足りないパック数の警告を重ねない()
    {
        var (menu, products) = CleanMenu();
        MenuItemFactory.SetPackCount(menu.Days[0].Items[0], menu.Days[0], 1);
        menu.Days[0].DayType = DayType.Handmade;

        var warning = Assert.Single(MenuValidator.Validate(menu, Context(products)).Warnings);

        Assert.Equal(MenuIssueKind.ItemsOnNonNormalDay, warning.Kind);
    }

    [Fact]
    public void 人数0の通常の日は商品がなくても警告しない()
    {
        var (menu, products) = CleanMenu();
        menu.Days[0].Items.Clear();
        menu.Days[0].ChildrenCount = 0;

        Assert.Empty(MenuValidator.Validate(menu, Context(products)).Issues);
    }

    [Fact]
    public void パック数が全員分に足りなければ警告()
    {
        var (menu, products) = CleanMenu();
        MenuItemFactory.SetPackCount(menu.Days[0].Items[0], menu.Days[0], 4);

        var warning = Assert.Single(MenuValidator.Validate(menu, Context(products)).Warnings);

        Assert.Equal(MenuIssueKind.PackShort, warning.Kind);
    }

    [Fact]
    public void 重複は警告()
    {
        var (menu, products) = CleanMenu();
        var history = new[] { new ServedItem("past", D(0).AddDays(-10), products[0].Id) };

        var warning = Assert.Single(MenuValidator.Validate(menu, Context(products, history: history)).Warnings);

        Assert.Equal(MenuIssueKind.Duplicate, warning.Kind);
        Assert.Contains("10日前", warning.Message);
    }

    [Fact]
    public void Claudeに返す文面はエラーだけで日付と商品と理由を並べる()
    {
        var (menu, products) = CleanMenu(budget: 100);
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        Put(menu, 0, egg);

        var feedback = MenuValidator.Validate(menu, Context([.. products, egg], Avoid("egg"))).ToClaudeFeedback();

        Assert.Contains("11/16（月） 「たまごボーロ」", feedback);
        Assert.Contains("卵", feedback);
        Assert.Contains($"商品ID: {egg.Id}", feedback);
        Assert.DoesNotContain("予算", feedback);
    }

    [Fact]
    public void 直しきれなかったエラーの商品を外す()
    {
        var (menu, products) = CleanMenu();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        var item = Put(menu, 0, egg);
        var context = Context([.. products, egg], Avoid("egg"));

        var removed = MenuValidator.RemoveErrorItems(menu, MenuValidator.Validate(menu, context));

        Assert.Equal([(D(0), item)], removed);
        Assert.DoesNotContain(item, menu.Days[0].Items);
        Assert.False(MenuValidator.Validate(menu, context).HasError);
    }

    [Fact]
    public void Claudeの案を反映すると存在しない商品と掲載品にない商品はエラーで作らない()
    {
        var (menu, products) = CleanMenu();
        var notListed = Safe("のってないクッキー");
        var context = Context([.. products, notListed]);
        var proposal = new MenuProposal(
        [
            new MenuProposalDay(D(0), [products[0].Id, "made-up-id"], "定番のおかし"),
            new MenuProposalDay(D(1), [notListed.Id], null),
            new MenuProposalDay(D(2), [products[2].Id], null),
            new MenuProposalDay(D(3), [products[3].Id], null),
            new MenuProposalDay(D(4), [products[4].Id, products[1].Id], null),
            new MenuProposalDay(D(5), [products[0].Id], null),
        ]);

        var proposalIssues = MenuProposalBuilder.Apply(menu, proposal, context);
        var result = MenuValidator.Validate(menu, context, proposalIssues);

        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.ProductNotFound && e.ProductId == "made-up-id");
        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.NotListed && e.ProductId == notListed.Id);
        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.UnknownDate && e.Date == D(5));
        Assert.Single(menu.Days[0].Items);
        Assert.Empty(menu.Days[1].Items);
        Assert.Equal(2, menu.Days[4].Items.Count);
        Assert.Equal("定番のおかし", menu.Days[0].Reason);
        Assert.Contains(result.Warnings, w => w.Kind == MenuIssueKind.EmptyNormalDay && w.Date == D(1));
    }

    [Fact]
    public void Claudeの案で同じ日の同じ商品は今の行を残す()
    {
        var (menu, products) = CleanMenu();
        var kept = menu.Days[0].Items[0];
        MenuItemFactory.SetPackCount(kept, menu.Days[0], 7);
        kept.AcknowledgedAt = NoonUtc(Today);

        MenuProposalBuilder.Apply(menu, new MenuProposal([new MenuProposalDay(D(0), [products[0].Id, products[0].Id], null)]), Context(products));

        Assert.Same(kept, Assert.Single(menu.Days[0].Items));
        Assert.Equal(7, kept.PackCount);
        Assert.NotNull(kept.AcknowledgedAt);
        Assert.Empty(menu.Days[1].Items);
    }

    [Fact]
    public void Claudeの案に候補から外した商品があればエラーにする()
    {
        var (menu, products) = CleanMenu();
        menu.ExcludedProductIds.Add(products[0].Id);
        products[1].IsDisabled = true;
        products[2].SnackType = SnackType.NotSnack;
        var context = Context(products);
        var proposal = new MenuProposal(Enumerable.Range(0, 5).Select(i => new MenuProposalDay(D(i), [products[i].Id], null)).ToList());

        var proposalIssues = MenuProposalBuilder.Apply(menu, proposal, context);
        var result = MenuValidator.Validate(menu, context, proposalIssues);

        Assert.Equal(
            [MenuIssueKind.ExcludedThisWeek, MenuIssueKind.ProductDisabled, MenuIssueKind.NotSnack],
            result.Errors.Select(e => e.Kind));
        Assert.Contains("今週は使わない", result.ToClaudeFeedback());
    }

    [Fact]
    public void Claudeの案の入れられない商品は作ったうえで検証のエラーになる()
    {
        var (menu, products) = CleanMenu();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        var context = Context([.. products, egg], Avoid("egg"));

        var proposalIssues = MenuProposalBuilder.Apply(menu, new MenuProposal([new MenuProposalDay(D(0), [egg.Id], null)]), context);
        var result = MenuValidator.Validate(menu, context, proposalIssues);

        Assert.Empty(proposalIssues);
        Assert.Contains(result.Errors, e => e.Kind == MenuIssueKind.AllergenBlocked && e.ProductId == egg.Id);
    }
}
