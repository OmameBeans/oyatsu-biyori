using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

public class MenuItemFactoryTests
{
    [Fact]
    public void 価格と注文番号は写しから個数は商品の既定値から写しパック数と金額を計算する()
    {
        var cookie = Safe("さくさくクッキー", pieces: 12, perChild: 2);
        cookie.Price = 999; // 商品の最新の価格は使わない
        var menu = NewMenu(20000, 50, (cookie, 298));
        var day = menu.Days[0];

        var item = MenuItemFactory.Create(day, cookie, menu.ListedProducts[0]).Value!;

        Assert.Equal(298, item.UnitPrice);
        Assert.Equal("1234", item.OrderNo);
        Assert.Equal(2, item.PerChildCount);
        Assert.Equal(9, item.PackCount); // 50×2÷12=8.33 → 9
        Assert.Equal(9 * 298, item.Amount);
        Assert.Equal(day.Id, item.MenuDayId);
        Assert.Null(item.AcknowledgedAt);
    }

    [Fact]
    public void 写しにない商品は作らない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50);

        Assert.True(MenuItemFactory.Create(menu.Days[0], cookie, null).HasError);
    }

    [Fact]
    public void 別の商品の写しの行では作らない()
    {
        var cookie = Safe("さくさくクッキー");
        var other = Safe("べつのクッキー");
        var menu = NewMenu(20000, 50, (other, 298));

        Assert.True(MenuItemFactory.Create(menu.Days[0], cookie, menu.ListedProducts[0]).HasError);
    }

    [Fact]
    public void 価格か入数がない商品は作らない()
    {
        var noPrice = Safe("ねだんなしクッキー");
        var noPieces = Safe("いりすうなしせんべい", pieces: null);
        var menu = NewMenu(20000, 50, (noPrice, null), (noPieces, 298));

        Assert.True(MenuItemFactory.Create(menu.Days[0], noPrice, menu.ListedProducts[0]).HasError);
        Assert.True(MenuItemFactory.Create(menu.Days[0], noPieces, menu.ListedProducts[1]).HasError);
    }

    [Fact]
    public void 入れられない商品も手で入れられるように作れる()
    {
        var egg = ReferenceOnly("たまごボーロ", SnackType.Sweet, 10, "egg");
        var menu = NewMenu(20000, 50, (egg, 198));

        Assert.False(MenuItemFactory.Create(menu.Days[0], egg, menu.ListedProducts[0]).HasError);
    }

    [Fact]
    public void 人数0の日に入れた商品はパック数0で金額0()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 0, (cookie, 298));

        var result = MenuItemFactory.Create(menu.Days[0], cookie, menu.ListedProducts[0]);

        Assert.False(result.HasError);
        Assert.Equal(0, result.Value!.PackCount);
        Assert.Equal(0, result.Value.Amount);
    }

    [Fact]
    public void 人数0の日はパック数を上書きしても金額0()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 0, (cookie, 298));
        var item = Put(menu, 0, cookie);

        MenuItemFactory.SetPackCount(item, menu.Days[0], 3);

        Assert.Equal(0, item.Amount);
        Assert.Equal(0, MenuAmountCalculator.CalculateDay(menu.Days[0], _ => 10).Amount);
    }

    [Fact]
    public void 入数がない商品の一人あたりの個数は変えられない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);
        cookie.PiecesPerPack = null;

        Assert.True(MenuItemFactory.SetPerChildCount(item, menu.Days[0], cookie, 2).HasError);
        Assert.Equal(1, item.PerChildCount);
    }

    [Fact]
    public void 手作りの日に入れた商品の金額は0()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 50, (cookie, 298));
        menu.Days[0].DayType = DayType.Handmade;

        var item = MenuItemFactory.Create(menu.Days[0], cookie, menu.ListedProducts[0]).Value!;

        Assert.Equal(5, item.PackCount);
        Assert.Equal(0, item.Amount);
    }

    [Fact]
    public void 商品を入れ替えたら確定時の確認を消す()
    {
        var cookie = ReferenceOnly("さくさくクッキー");
        var jelly = Safe("ぷるぷるゼリー", pieces: 6);
        var menu = NewMenu(20000, 50, (cookie, 298), (jelly, 498));
        var item = Put(menu, 0, cookie);
        item.AcknowledgedAt = NoonUtc(Today);
        item.AcknowledgedJudgement = AllergenJudgementKind.Reference;
        item.AcknowledgedJudgementFingerprint = "v1:x";

        var result = MenuItemFactory.ReplaceProduct(item, menu.Days[0], jelly, menu.ListedProducts[1]);

        Assert.False(result.HasError);
        Assert.Equal(jelly.Id, item.ProductId);
        Assert.Equal(498, item.UnitPrice);
        Assert.Equal(9, item.PackCount);
        Assert.Null(item.AcknowledgedAt);
        Assert.Null(item.AcknowledgedJudgement);
        Assert.Null(item.AcknowledgedJudgementFingerprint);
    }

    [Fact]
    public void 一人あたりの個数を変えたらパック数を式で求め直す()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);
        MenuItemFactory.SetPackCount(item, menu.Days[0], 20);

        MenuItemFactory.SetPerChildCount(item, menu.Days[0], cookie, 2);

        Assert.Equal(9, item.PackCount);
        Assert.Equal(9 * 298, item.Amount);
    }

    [Fact]
    public void 一人あたりの個数とパック数は1以上()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);

        Assert.True(MenuItemFactory.SetPerChildCount(item, menu.Days[0], cookie, 0).HasError);
        Assert.True(MenuItemFactory.SetPackCount(item, menu.Days[0], 0).HasError);
        Assert.Equal(5, item.PackCount);
    }

    [Fact]
    public void パック数を上書きすると金額も変わる()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);

        MenuItemFactory.SetPackCount(item, menu.Days[0], 4);

        Assert.Equal(4, item.PackCount);
        Assert.Equal(4 * 298, item.Amount);
    }

    [Fact]
    public void 人数を変えたらその日の商品のパック数を求め直す()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);
        MenuItemFactory.SetPackCount(item, menu.Days[0], 20);
        menu.Days[0].ChildrenCount = 61;

        MenuItemFactory.RecalculatePackCounts(menu.Days[0], id => id == cookie.Id ? 10 : null);

        Assert.Equal(7, item.PackCount);
        Assert.Equal(7 * 298, item.Amount);
    }

    [Fact]
    public void 日の種類を変えたら金額を求め直す()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 45, (cookie, 298));
        var item = Put(menu, 0, cookie);

        menu.Days[0].DayType = DayType.NoSnack;
        MenuItemFactory.RefreshAmounts(menu.Days[0]);
        Assert.Equal(0, item.Amount);

        menu.Days[0].DayType = DayType.Normal;
        MenuItemFactory.RefreshAmounts(menu.Days[0]);
        Assert.Equal(5 * 298, item.Amount);
    }
}
