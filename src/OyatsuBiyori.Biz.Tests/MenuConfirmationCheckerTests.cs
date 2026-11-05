using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class MenuConfirmationCheckerTests
{
    private static readonly DateTime Now = NoonUtc(Today);

    /// <summary>月〜金に1品ずつ。月曜だけ「参考」の商品</summary>
    private static (Menu Menu, List<Product> Products, MenuItem WarningItem) MenuWithReference()
    {
        var products = Enumerable.Range(0, 5).Select(i => i == 0 ? ReferenceOnly("ぷるぷるゼリー") : Safe($"おかし{i}")).ToList();
        var menu = NewMenu(20000, 50, products.Select(p => (p, (int?)300)).ToArray());
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, products[i]);
        }

        return (menu, products, menu.Days[0].Items[0]);
    }

    [Fact]
    public void 安全な商品だけなら確認なしで確定できる()
    {
        var products = Enumerable.Range(0, 5).Select(i => Safe($"おかし{i}")).ToList();
        var menu = NewMenu(20000, 50, products.Select(p => (p, (int?)300)).ToArray());
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, products[i]);
        }

        var result = MenuConfirmationChecker.Check(menu, Context(products, Avoid("milk")));

        Assert.True(result.CanConfirm);
        Assert.Empty(result.Acknowledgements);
    }

    [Fact]
    public void 警告の商品は確認するまで確定できない()
    {
        var (menu, products, item) = MenuWithReference();
        var context = Context(products, Avoid("milk"));

        var before = MenuConfirmationChecker.Check(menu, context);
        Assert.False(before.CanConfirm);
        Assert.Same(item, Assert.Single(before.PendingAcknowledgements).Item);

        Assert.False(MenuConfirmationChecker.Acknowledge(item, context, Now).HasError);

        var after = MenuConfirmationChecker.Check(menu, context);
        Assert.True(after.CanConfirm);
        Assert.Equal(AllergenJudgementKind.Reference, item.AcknowledgedJudgement);
        Assert.Equal(Now, item.AcknowledgedAt);
        Assert.StartsWith("v1:", item.AcknowledgedJudgementFingerprint);
    }

    [Fact]
    public void 入れられない商品は確認しても確定できない()
    {
        var (menu, products, _) = MenuWithReference();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        var eggItem = Put(menu, 1, egg);
        var context = Context([.. products, egg], Avoid("egg"));

        var acknowledge = MenuConfirmationChecker.Acknowledge(eggItem, context, Now);
        Assert.True(acknowledge.HasError);
        Assert.Null(eggItem.AcknowledgedAt);

        // 記録を書き換えて確認したことにしても止まる
        eggItem.AcknowledgedAt = Now;
        eggItem.AcknowledgedJudgement = AllergenJudgementKind.Contains;
        eggItem.AcknowledgedJudgementFingerprint = context.Fingerprint(egg);
        var result = MenuConfirmationChecker.Check(menu, context);
        Assert.False(result.CanConfirm);
        Assert.Contains(result.BlockingIssues, i => i.Kind == MenuIssueKind.AllergenBlocked && i.ProductId == egg.Id);
    }

    [Fact]
    public void おやつなしの日の入れられない商品も確定を止める()
    {
        var (menu, products, item) = MenuWithReference();
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        menu.ListedProducts.Add(new ListedProduct(egg.Id, 198, "2222"));
        Put(menu, 2, egg);
        menu.Days[2].DayType = DayType.NoSnack;
        var context = Context([.. products, egg], Avoid("egg"));
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        Assert.False(MenuConfirmationChecker.Check(menu, context).CanConfirm);
    }

    [Fact]
    public void 価格がない商品と掲載品にない商品は確定を止める()
    {
        var (menu, products, item) = MenuWithReference();
        var gone = Safe("けされたクッキー");
        PutRaw(menu, 1, gone.Id);
        menu.ListedProducts[1] = menu.ListedProducts[1] with { Price = null };
        var context = Context([.. products, gone], Avoid("milk"));
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        var result = MenuConfirmationChecker.Check(menu, context);

        Assert.False(result.CanConfirm);
        Assert.Contains(result.BlockingIssues, i => i.Kind == MenuIssueKind.NotListed);
        Assert.Contains(result.BlockingIssues, i => i.Kind == MenuIssueKind.MissingPriceOrPieces);
    }

    [Fact]
    public void 候補から外した商品と表示だけの警告は確定を止めない()
    {
        var (menu, products, item) = MenuWithReference();
        menu.ExcludedProductIds.Add(products[1].Id);
        products[2].IsDisabled = true;
        menu.WeeklyBudget = 100;
        menu.Days[3].DayType = DayType.NoSnack;
        var context = Context(products, Avoid("milk"));
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        var result = MenuConfirmationChecker.Check(menu, context);

        Assert.True(result.CanConfirm);
        Assert.Empty(result.BlockingIssues);
        Assert.Contains(result.DisplayWarnings, w => w.Kind == MenuIssueKind.ExcludedThisWeek && w.Severity == MenuIssueSeverity.Warning);
        Assert.Contains(result.DisplayWarnings, w => w.Kind == MenuIssueKind.ProductDisabled);
        Assert.Contains(result.DisplayWarnings, w => w.Kind == MenuIssueKind.OverBudget);
        Assert.Contains(result.DisplayWarnings, w => w.Kind == MenuIssueKind.ItemsOnNonNormalDay);
        Assert.DoesNotContain(result.DisplayWarnings, w => w.Kind == MenuIssueKind.AllergenWarning);
    }

    [Fact]
    public void 参考から要再確認に変わったら確認し直し()
    {
        var reference = ReferenceOnly("ぷるぷるゼリー");
        var menu = NewMenu(20000, 50, (reference, 300));
        var item = Put(menu, 0, reference);
        MenuConfirmationChecker.Acknowledge(item, Context([reference], Avoid("milk")), Now);

        // パッケージで確認したが、そのまま90日を過ぎた
        reference.AllergenChecks.Add(ConfirmedAll(Today.AddDays(-1), []));
        var later = Context([reference], Avoid("milk"), today: Today.AddDays(100));

        var result = MenuConfirmationChecker.Check(menu, later);

        var pending = Assert.Single(result.PendingAcknowledgements);
        Assert.Equal(AllergenJudgementKind.RecheckNeeded, pending.Judgement.Kind);
        Assert.False(result.CanConfirm);
    }

    [Fact]
    public void 種類が同じでも参考の記録が増えたら確認し直し()
    {
        var reference = ReferenceOnly("ぷるぷるゼリー");
        var menu = NewMenu(20000, 50, (reference, 300));
        var item = Put(menu, 0, reference);
        MenuConfirmationChecker.Acknowledge(item, Context([reference], Avoid("milk")), Now);

        reference.AllergenChecks.Add(TestData.Reference(Today.AddDays(-1), "soybean"));
        var result = MenuConfirmationChecker.Check(menu, Context([reference], Avoid("milk")));

        Assert.Equal(AllergenJudgementKind.Reference, item.AcknowledgedJudgement);
        Assert.Single(result.PendingAcknowledgements);
    }

    [Fact]
    public void 種類が同じでも避ける品目が増えて未確認の品目が増えたら確認し直し()
    {
        var product = Snack("さくさくクッキー", SnackType.Sweet, 10, 1, Confirmed(Today.AddDays(-5), [], ["milk"]));
        var menu = NewMenu(20000, 50, (product, 300));
        var item = Put(menu, 0, product);
        var first = Context([product], Avoid("milk", "egg"));
        Assert.Equal(AllergenJudgementKind.UnconfirmedItems, first.Judge(product).Kind);
        MenuConfirmationChecker.Acknowledge(item, first, Now);

        var result = MenuConfirmationChecker.Check(menu, Context([product], Avoid("milk", "egg", "soybean")));

        Assert.Single(result.PendingAcknowledgements);
    }

    [Fact]
    public void 判定が同じままなら確認は残る()
    {
        var (menu, products, item) = MenuWithReference();
        MenuConfirmationChecker.Acknowledge(item, Context(products, Avoid("milk")), Now);

        Assert.True(MenuConfirmationChecker.Check(menu, Context(products, Avoid("milk"))).CanConfirm);
    }

    [Fact]
    public void 商品を入れ替えたら確認し直し()
    {
        var (menu, products, item) = MenuWithReference();
        var other = ReferenceOnly("べつのゼリー");
        menu.ListedProducts.Add(new ListedProduct(other.Id, 300, "9999"));
        var context = Context([.. products, other], Avoid("milk"));
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        MenuItemFactory.ReplaceProduct(item, menu.Days[0], other, menu.ListedProducts[^1]);

        Assert.False(MenuConfirmationChecker.Check(menu, context).CanConfirm);
    }

    [Fact]
    public void 安全な商品の確認は受け付けない()
    {
        var safe = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50, (safe, 300));
        var item = Put(menu, 0, safe);

        Assert.True(MenuConfirmationChecker.Acknowledge(item, Context([safe]), Now).HasError);
        Assert.Null(item.AcknowledgedAt);
    }

    [Fact]
    public void 確認日時はUTCで受け取る()
    {
        var (_, products, item) = MenuWithReference();

        Assert.Throws<ArgumentException>(() => MenuConfirmationChecker.Acknowledge(item, Context(products), DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void 指紋は品目の並びに依らず中身で変わる()
    {
        var product = Snack("さくさくクッキー", SnackType.Sweet, 10, 1, Confirmed(Today.AddDays(-5), [], ["milk"]));
        var judgement = AllergenJudge.Judge(product, Avoid("milk", "egg"), Today, RecheckDays);

        var a = AllergenJudgementFingerprint.Compute(product.Id, judgement, [Allergen("milk").Id, Allergen("egg").Id]);
        var b = AllergenJudgementFingerprint.Compute(product.Id, judgement, [Allergen("egg").Id, Allergen("milk").Id]);
        var c = AllergenJudgementFingerprint.Compute(product.Id, judgement, [Allergen("egg").Id]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith(AllergenJudgementFingerprint.Version + ":", a);
    }

    [Fact]
    public void 記録のない商品どうしでも指紋は商品ごとに違う()
    {
        var first = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var second = Snack("べつのなぞのおかし", SnackType.Sweet, 10, 1);
        var context = Context([first, second]);

        Assert.Equal(context.Judge(first).Kind, context.Judge(second).Kind);
        Assert.NotEqual(context.Fingerprint(first), context.Fingerprint(second));
    }

    [Fact]
    public void 確認したあと商品IDだけを書き換えても別の商品の確認にはならない()
    {
        var first = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var second = Snack("べつのなぞのおかし", SnackType.Sweet, 10, 1);
        var menu = NewMenu(20000, 50, (first, 300), (second, 300));
        var item = Put(menu, 0, first);
        var context = Context([first, second]);
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        item.ProductId = second.Id;

        Assert.False(MenuConfirmationChecker.Check(menu, context).CanConfirm);
    }

    [Fact]
    public void 避ける品目を足した直後は確認済みの不明の商品も確定を止める()
    {
        var unknown = Snack("なぞのおかし", SnackType.Sweet, 10, 1);
        var menu = NewMenu(20000, 50, (unknown, 300));
        var item = Put(menu, 0, unknown);
        MenuConfirmationChecker.Acknowledge(item, Context([unknown]), Now);
        Assert.True(MenuConfirmationChecker.Check(menu, Context([unknown])).CanConfirm);

        var result = MenuConfirmationChecker.Check(menu, Context([unknown], Avoid("egg")));

        Assert.False(result.CanConfirm);
        Assert.Contains(result.BlockingIssues, i => i.Kind == MenuIssueKind.AllergenBlocked);
    }

    [Fact]
    public void 存在しない商品の行は確定を止める()
    {
        var (menu, products, item) = MenuWithReference();
        PutRaw(menu, 1, "deleted-product");
        var context = Context(products, Avoid("milk"));
        MenuConfirmationChecker.Acknowledge(item, context, Now);

        var result = MenuConfirmationChecker.Check(menu, context);

        Assert.False(result.CanConfirm);
        Assert.Contains(result.BlockingIssues, i => i.Kind == MenuIssueKind.ProductNotFound);
    }
}
