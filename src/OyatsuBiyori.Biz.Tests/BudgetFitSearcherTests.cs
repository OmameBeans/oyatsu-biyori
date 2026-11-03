using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class BudgetFitSearcherTests
{
    /// <summary>
    /// 50人・入数10（5パック）。月〜金に「基本」（300円）を1品ずつ：実績 7,500円。
    /// 写しには 入れ替え・追加の候補も入れる。
    /// </summary>
    private static (Menu Menu, Product Basic) BaseMenu(int budget, params (Product Product, int? Price)[] extra)
    {
        var basic = Safe("きほんのクッキー");
        var menu = NewMenu(budget, 50, [(basic, 300), .. extra]);
        for (var i = 0; i < 5; i++)
        {
            Put(menu, i, basic);
        }

        return (menu, basic);
    }

    [Fact]
    public void 予算に届いていれば案を出さない()
    {
        var (menu, basic) = BaseMenu(7500);

        var result = BudgetFitSearcher.Search(menu, Context([basic]));

        Assert.False(result.IsUnderBudget);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void 予算を超えず今の差より小さくなる案を差の小さい順に3件()
    {
        // 予算 8,000円・差 500円
        var plus100 = Safe("ちょいたかクッキー");   // 入れ替え：+100×5=+500 → ちょうど
        var plus60 = Safe("すこしたかいクッキー");  // 入れ替え：+60×5=+300
        var cheapAdd = Safe("やすいグミ", pieces: 50); // 追加：1パック 90円
        var (menu, basic) = BaseMenu(8000, (plus100, 400), (plus60, 360), (cheapAdd, 90));

        var result = BudgetFitSearcher.Search(menu, Context([basic, plus100, plus60, cheapAdd]));

        Assert.True(result.IsUnderBudget);
        Assert.Equal(3, result.Options.Count);
        Assert.All(result.Options, o => Assert.True(o.NewTotal <= 8000));
        Assert.Equal(0, result.Options[0].Difference);
        Assert.Equal(BudgetFitKind.Replace, result.Options[0].Kind);
        Assert.Equal(plus100.Id, result.Options[0].NewProductId);
        Assert.Equal(D(0), result.Options[0].Date);
        Assert.Equal(basic.Id, result.Options[0].OldProductId);
        Assert.True(result.Options.Select(o => o.Difference).SequenceEqual(result.Options.Select(o => o.Difference).Order()));
    }

    [Fact]
    public void 予算を超える案は出さない()
    {
        var expensive = Safe("たかいクッキー");     // 入れ替え +200×5=+1000
        var (menu, basic) = BaseMenu(8000, (expensive, 500));

        var result = BudgetFitSearcher.Search(menu, Context([basic, expensive]));

        // パック数 +1（+300）だけが予算内
        Assert.All(result.Options, o => Assert.Equal(BudgetFitKind.PackIncrease, o.Kind));
        Assert.All(result.Options, o => Assert.Equal(7800, o.NewTotal));
        Assert.Equal(3, result.Options.Count);
    }

    [Fact]
    public void 今の差より小さくならない案は出さない()
    {
        var cheaper = Safe("やすいクッキー");
        var (menu, basic) = BaseMenu(7600, (cheaper, 200));

        var result = BudgetFitSearcher.Search(menu, Context([basic, cheaper]));

        // 安くする入れ替え・パック数 −1 は差が広がる。+1 は 7,800円で予算を超える。追加も超える
        Assert.Empty(result.Options);
        Assert.True(result.IsUnderBudget);
    }

    [Fact]
    public void 同じ日にすでにある商品は追加も入れ替え先にもしない()
    {
        var (menu, basic) = BaseMenu(100000);

        var result = BudgetFitSearcher.Search(menu, Context([basic]));

        Assert.All(result.Options, o => Assert.Equal(BudgetFitKind.PackIncrease, o.Kind));
    }

    [Fact]
    public void 手作り_おやつなし_人数0の日では試さない()
    {
        var add = Safe("ついかのグミ", pieces: 50);
        var (menu, basic) = BaseMenu(100000, (add, 100));
        menu.Days[0].DayType = DayType.Handmade;
        menu.Days[1].DayType = DayType.NoSnack;
        menu.Days[2].ChildrenCount = 0;

        var result = BudgetFitSearcher.Search(menu, Context([basic, add]));

        Assert.All(result.Options, o => Assert.Contains(o.Date, new[] { D(3), D(4) }));
    }

    [Fact]
    public void 入れられない商品と候補から外した商品は案に出ない()
    {
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var excluded = Safe("こんしゅうはいらないグミ");
        var noPrice = Safe("ねだんなしクッキー");
        var (menu, basic) = BaseMenu(8000, (egg, 400), (excluded, 400), (noPrice, null));
        menu.ExcludedProductIds.Add(excluded.Id);

        var result = BudgetFitSearcher.Search(menu, Context([basic, egg, excluded, noPrice], Avoid("egg")));

        Assert.All(result.Options, o => Assert.Equal(basic.Id, o.NewProductId));
    }

    [Fact]
    public void 献立に入っている入れられない商品のパック数の案は出さず入れ替えの元にはする()
    {
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var (menu, basic) = BaseMenu(100000, (egg, 300));
        var eggItem = Put(menu, 0, egg);
        var context = Context([basic, egg], Avoid("egg"));

        var options = BudgetFitSearcher.Search(menu, context).Options;

        Assert.DoesNotContain(options, o => o.NewProductId == egg.Id);
        Assert.DoesNotContain(options, o => o.MenuItemId == eggItem.Id && o.Kind != BudgetFitKind.Replace);

        // 前に作った案（入れられない商品のパック数 +1）を持ち込まれても反映しない
        var stale = new BudgetFitOption(BudgetFitKind.PackIncrease, D(0), eggItem.Id, egg.Id, egg.Id, eggItem.PackCount + 1, 0, 100000, false, false)
        {
            CurrentTotal = MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack).Total,
        };
        var result = BudgetFitSearcher.Apply(menu, stale, context);
        Assert.True(result.HasError);
        Assert.Contains("候補に入りません", result.ErrorMessages[0]);
        Assert.Equal(5, eggItem.PackCount);
    }

    [Fact]
    public void 一人あたりの個数の既定が正しくない商品は候補に入らず例外にもならない()
    {
        var broken = Safe("こすうがへんなクッキー", perChild: 0);
        var (menu, basic) = BaseMenu(8000, (broken, 400));

        var result = BudgetFitSearcher.Search(menu, Context([basic, broken]));

        Assert.DoesNotContain(result.Options, o => o.NewProductId == broken.Id);
    }

    [Fact]
    public void 候補の既定の個数が2なら追加のパック数もそれで求める()
    {
        var twoEach = Safe("ふたつずつグミ", pieces: 20, perChild: 2);
        var (menu, basic) = BaseMenu(8000, (twoEach, 100));

        var option = BudgetFitSearcher.Search(menu, Context([basic, twoEach])).Options.First(o => o.Kind == BudgetFitKind.Add);

        Assert.Equal(5, option.NewPackCount); // 50人×2÷20
        Assert.Equal(7500 + 500, option.NewTotal);
    }

    [Fact]
    public void 追加の案を反映すると新しい行が入る()
    {
        var add = Safe("ついかのグミ", pieces: 50);
        var (menu, basic) = BaseMenu(7600, (add, 100));
        var context = Context([basic, add]);
        var options = BudgetFitSearcher.Search(menu, context).Options;
        Assert.Equal(3, options.Count); // 5日とも同じ差。日付の順に3件
        Assert.All(options, o => Assert.Equal(BudgetFitKind.Add, o.Kind));
        var option = options[0];
        Assert.Equal(D(0), option.Date);

        Assert.False(BudgetFitSearcher.Apply(menu, option, context).HasError);

        var added = Assert.Single(menu.Days.Single(d => d.Date == option.Date).Items, i => i.ProductId == add.Id);
        Assert.Equal(1, added.PackCount);
        Assert.Equal(7600, MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack).Total);
    }

    [Theory]
    [InlineData(DayType.Handmade, 50)]
    [InlineData(DayType.NoSnack, 50)]
    [InlineData(DayType.Normal, 0)]
    [InlineData(DayType.Normal, 60)]
    public void 商品のない日の種類や人数が変わったら追加の案を反映しない(DayType dayType, int children)
    {
        var add = Safe("ついかのグミ", pieces: 50);
        var (menu, basic) = BaseMenu(6100, (add, 100));
        menu.Days[0].Items.Clear();
        var context = Context([basic, add]);
        var option = BudgetFitSearcher.Search(menu, context).Options.First(o => o.Kind == BudgetFitKind.Add && o.Date == D(0));

        menu.Days[0].DayType = dayType;
        menu.Days[0].ChildrenCount = children;

        Assert.True(BudgetFitSearcher.Apply(menu, option, context).HasError);
        Assert.Empty(menu.Days[0].Items);
    }

    [Fact]
    public void 写しの価格が変わったら入れ替えの案を反映しない()
    {
        var plus100 = Safe("ちょいたかクッキー");
        var (menu, basic) = BaseMenu(8000, (plus100, 400));
        var context = Context([basic, plus100]);
        var option = BudgetFitSearcher.Search(menu, context).Options[0];
        Assert.Equal(BudgetFitKind.Replace, option.Kind);

        menu.ListedProducts[1] = menu.ListedProducts[1] with { Price = 450 };

        Assert.True(BudgetFitSearcher.Apply(menu, option, context).HasError);
        Assert.Equal(basic.Id, menu.Days[0].Items[0].ProductId);
    }

    [Fact]
    public void 予算か実績が案を作ったときと違えば反映しない()
    {
        var (menu, basic) = BaseMenu(7800);
        var context = Context([basic]);
        var option = BudgetFitSearcher.Search(menu, context).Options[0];

        menu.WeeklyBudget = 9000;
        Assert.True(BudgetFitSearcher.Apply(menu, option, context).HasError);

        menu.WeeklyBudget = 7800;
        MenuItemFactory.SetPackCount(menu.Days[4].Items[0], menu.Days[4], 6);
        Assert.True(BudgetFitSearcher.Apply(menu, option, context).HasError);
        Assert.Equal(5, menu.Days[0].Items[0].PackCount);
    }

    [Fact]
    public void 警告の商品を使う案には印を付ける()
    {
        var reference = ReferenceOnly("ちょいたかゼリー");
        var (menu, basic) = BaseMenu(8000, (reference, 400));

        var result = BudgetFitSearcher.Search(menu, Context([basic, reference], Avoid("milk")));

        var option = result.Options.First();
        Assert.Equal(reference.Id, option.NewProductId);
        Assert.True(option.UsesWarningProduct);
    }

    [Fact]
    public void パック数を減らす案は予算に届かないときは差が広がるので出ない()
    {
        var (menu, basic) = BaseMenu(100000);
        var day = menu.Days[0];
        MenuItemFactory.SetPackCount(day.Items[0], day, 1);

        var options = BudgetFitSearcher.Search(menu, Context([basic])).Options;

        Assert.DoesNotContain(options, o => o.Kind == BudgetFitKind.PackDecrease);
    }

    [Fact]
    public void パック数を1つ足して全員分に足りるようになれば足りない印は付けない()
    {
        var (menu, basic) = BaseMenu(7300);
        MenuItemFactory.SetPackCount(menu.Days[0].Items[0], menu.Days[0], 4);

        var result = BudgetFitSearcher.Search(menu, Context([basic]));

        Assert.Equal(7200, result.CurrentTotal);
        Assert.Empty(result.Options); // +1 は 7,500円で予算を超える

        menu.WeeklyBudget = 7500;
        var increase = BudgetFitSearcher.Search(menu, Context([basic])).Options[0];
        Assert.Equal(BudgetFitKind.PackIncrease, increase.Kind);
        Assert.Equal(D(0), increase.Date);
        Assert.Equal(0, increase.Difference);
        Assert.False(increase.HasShortage);
    }

    [Fact]
    public void パック数の案で全員分に足りないままなら印を付ける()
    {
        var (menu, basic) = BaseMenu(7500);
        var day = menu.Days[0];
        MenuItemFactory.SetPackCount(day.Items[0], day, 2);

        var result = BudgetFitSearcher.Search(menu, Context([basic]));

        Assert.Contains(result.Options, o => o.Kind == BudgetFitKind.PackIncrease && o.Date == D(0) && o.HasShortage);
    }

    [Fact]
    public void 同じ差の案は種類と日付の順で決まる()
    {
        var (menu, basic) = BaseMenu(7800);

        var first = BudgetFitSearcher.Search(menu, Context([basic])).Options;
        var second = BudgetFitSearcher.Search(menu, Context([basic])).Options;

        Assert.Equal(first, second);
        Assert.Equal([D(0), D(1), D(2)], first.Select(o => o.Date));
    }

    [Fact]
    public void 選んだ案を反映すると実績が案のとおりになる()
    {
        var plus100 = Safe("ちょいたかクッキー");
        var add = Safe("ついかのグミ", pieces: 50);
        var (menu, basic) = BaseMenu(8000, (plus100, 400), (add, 90));
        var context = Context([basic, plus100, add]);

        foreach (var option in BudgetFitSearcher.Search(menu, context).Options)
        {
            var copy = BaseMenu(8000, (plus100, 400), (add, 90));
            // 同じ形の献立で、日・商品の行の Id をそろえる
            for (var i = 0; i < 5; i++)
            {
                copy.Menu.Days[i].Items[0].Id = menu.Days[i].Items[0].Id;
            }

            copy.Menu.ListedProducts.Clear();
            copy.Menu.ListedProducts.AddRange(menu.ListedProducts);
            foreach (var item in copy.Menu.Days.SelectMany(d => d.Items))
            {
                item.ProductId = basic.Id;
            }

            Assert.False(BudgetFitSearcher.Apply(copy.Menu, option, context).HasError);
            Assert.Equal(option.NewTotal, MenuAmountCalculator.CalculateWeek(copy.Menu, context.PiecesPerPack).Total);
        }
    }

    [Fact]
    public void 入れ替えを反映すると確認は消え追加は新しい行になる()
    {
        var plus100 = Safe("ちょいたかクッキー");
        var (menu, basic) = BaseMenu(8000, (plus100, 400));
        var context = Context([basic, plus100]);
        var target = menu.Days[0].Items[0];
        target.AcknowledgedAt = NoonUtc(Today);

        var option = BudgetFitSearcher.Search(menu, context).Options[0];
        Assert.False(BudgetFitSearcher.Apply(menu, option, context).HasError);

        Assert.Equal(plus100.Id, target.ProductId);
        Assert.Null(target.AcknowledgedAt);
        Assert.Equal(8000, MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack).Total);
    }

    [Fact]
    public void 献立が変わっていたら案を反映しない()
    {
        var plus100 = Safe("ちょいたかクッキー");
        var (menu, basic) = BaseMenu(8000, (plus100, 400));
        var context = Context([basic, plus100]);
        var option = BudgetFitSearcher.Search(menu, context).Options[0];

        menu.Days[0].Items.Clear();

        Assert.True(BudgetFitSearcher.Apply(menu, option, context).HasError);
    }

    [Fact]
    public void 案の商品が入れられないに変わっていたら反映しない()
    {
        var plus100 = Safe("ちょいたかクッキー");
        var (menu, basic) = BaseMenu(8000, (plus100, 400));
        var option = BudgetFitSearcher.Search(menu, Context([basic, plus100])).Options[0];

        plus100.AllergenChecks.Add(TestData.Reference(Today, "egg"));

        Assert.True(BudgetFitSearcher.Apply(menu, option, Context([basic, plus100], Avoid("egg"))).HasError);
        Assert.Equal(basic.Id, menu.Days[0].Items[0].ProductId);
    }
}
