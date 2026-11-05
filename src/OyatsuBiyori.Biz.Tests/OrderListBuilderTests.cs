using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>注文リスト（FR-MENU-35・T9-02）</summary>
public class OrderListBuilderTests
{
    private static Func<string, string?> Names(params Product[] products) =>
        id => products.FirstOrDefault(p => p.Id == id)?.Name;

    private static bool NotBlocked(string productId) => false;

    [Fact]
    public void 同じ商品は週の中でまとめてパック数と金額を足す()
    {
        var cookie = Safe("さくさくクッキー", pieces: 12);
        var menu = NewMenu(20000, 50, (cookie, 298));
        var monday = Put(menu, 0, cookie);
        var wednesday = Put(menu, 2, cookie);

        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        var line = Assert.Single(list.Lines);
        Assert.Equal("1234", line.OrderNo);
        Assert.Equal("さくさくクッキー", line.ProductName);
        Assert.Equal(monday.PackCount + wednesday.PackCount, line.PackCount);
        Assert.Equal(10, line.PackCount); // 50人×1個÷12個＝4.17 → 5パック × 2日
        Assert.Equal(10 * 298, line.Amount);
        Assert.Equal([D(0), D(2)], line.Dates);
        Assert.Equal(10, list.TotalPackCount);
        Assert.Equal(10 * 298, list.TotalAmount);
        Assert.Equal(0, list.MissingOrderNoCount);
    }

    [Fact]
    public void 注文番号の順に並べ注文番号のない商品は最後にする()
    {
        var cookie = Safe("さくさくクッキー");
        var senbei = Safe("ぱりぱりせんべい", SnackType.Salty);
        var jelly = Safe("ぷるぷるゼリー");
        var menu = NewMenu(20000, 40, (cookie, 298), (senbei, 198), (jelly, 258));
        Put(menu, 0, cookie).OrderNo = "8120";
        Put(menu, 1, senbei).OrderNo = "8101";
        Put(menu, 2, jelly).OrderNo = " ";

        var list = OrderListBuilder.Build(menu, Names(cookie, senbei, jelly), NotBlocked);

        Assert.Equal(["8101", "8120", null], list.Lines.Select(l => l.OrderNo));
        Assert.Equal(1, list.MissingOrderNoCount);
    }

    [Fact]
    public void 同じ商品でも献立に入れた時点の注文番号が違えば別の行にする()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie).OrderNo = "8101";
        Put(menu, 1, cookie).OrderNo = "8102";

        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        Assert.Equal(2, list.Lines.Count);
    }

    [Fact]
    public void おやつなしの日の商品とパック数0の商品は入れない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie);
        Put(menu, 1, cookie);
        Put(menu, 2, cookie);
        menu.Days[1].DayType = DayType.NoSnack;
        menu.Days[2].DayType = DayType.NoSnack;
        PutRaw(menu, 3, cookie.Id, packCount: 0);

        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        var line = Assert.Single(list.Lines);
        Assert.Equal([D(0)], line.Dates);
        Assert.Equal(4, line.PackCount); // 40人×1個÷10個＝4パック（月曜だけ）
    }

    private static readonly DateTime Now = new(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void 注文したとチェックするとそのときのパック数を記録して注文済みにする()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie).OrderNo = "8101";

        var result = OrderListBuilder.SetOrdered(menu, cookie.Id, " 8101 ", true, Now, "staff", NotBlocked);

        Assert.False(result.HasError);
        var check = Assert.Single(menu.OrderChecks);
        Assert.Equal("8101", check.OrderNo);
        Assert.Equal(4, check.PackCount);
        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);
        Assert.Equal(OrderLineState.Ordered, list.Lines[0].State);
        Assert.Equal(Now, list.Lines[0].OrderedAt);
        Assert.Equal(1, list.OrderedCount);
    }

    [Fact]
    public void チェックしたあとでパック数が変わると数量が変わったにして注文済みに数えない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        var item = Put(menu, 0, cookie);
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        item.PackCount = 6;
        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        Assert.Equal(OrderLineState.QuantityChanged, list.Lines[0].State);
        Assert.Equal(4, list.Lines[0].OrderedPackCount);
        Assert.Equal(0, list.OrderedCount);
        Assert.Equal(1, list.QuantityChangedCount);

        // チェックし直すと、今のパック数で記録し直す（記録は1件のまま）
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);
        Assert.Equal(6, Assert.Single(menu.OrderChecks).PackCount);
        Assert.Equal(OrderLineState.Ordered, OrderListBuilder.Build(menu, Names(cookie), NotBlocked).Lines[0].State);
    }

    [Fact]
    public void チェックを外すと注文していないに戻る()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie);
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", false, Now, "staff", NotBlocked);

        Assert.Empty(menu.OrderChecks);
        Assert.Equal(OrderLineState.NotOrdered, OrderListBuilder.Build(menu, Names(cookie), NotBlocked).Lines[0].State);
    }

    [Fact]
    public void 注文リストにない商品や注文番号にはチェックを付けない()
    {
        var cookie = Safe("さくさくクッキー");
        var senbei = Safe("ぱりぱりせんべい", SnackType.Salty);
        var menu = NewMenu(20000, 40, (cookie, 298), (senbei, 198));
        Put(menu, 0, cookie);

        Assert.True(OrderListBuilder.SetOrdered(menu, senbei.Id, "1234", true, Now, "staff", NotBlocked).HasError);
        Assert.True(OrderListBuilder.SetOrdered(menu, cookie.Id, "9999", true, Now, "staff", NotBlocked).HasError);
        Assert.True(OrderListBuilder.SetOrdered(menu, null, "1234", true, Now, "staff", NotBlocked).HasError);
        Assert.Empty(menu.OrderChecks);
    }

    [Fact]
    public void 商品が献立から外れるとチェックは注文リストに出ない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        var item = Put(menu, 0, cookie);
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        menu.Days[0].Items.Remove(item);
        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        Assert.Empty(list.Lines);
        Assert.Equal(0, list.OrderedCount);
    }

    [Fact]
    public void 商品のない献立は空のリストにする()
    {
        var menu = NewMenu(20000, 40);

        var list = OrderListBuilder.Build(menu, _ => null, NotBlocked);

        Assert.Empty(list.Lines);
        Assert.Equal(0, list.TotalPackCount);
        Assert.Equal(0, list.TotalAmount);
    }

    [Fact]
    public void 人数0の日の商品は金額に数えないので入れない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie);
        Put(menu, 1, cookie).PackCount = 2;
        menu.Days[1].ChildrenCount = 0;

        var list = OrderListBuilder.Build(menu, Names(cookie), NotBlocked);

        var line = Assert.Single(list.Lines);
        Assert.Equal([D(0)], line.Dates);
        Assert.Equal(4, line.PackCount);
    }

    [Fact]
    public void SR_17_入れられない商品の行は印を付けて数える()
    {
        var cookie = Safe("さくさくクッキー");
        var senbei = Safe("ぱりぱりせんべい", SnackType.Salty);
        var menu = NewMenu(20000, 40, (cookie, 298), (senbei, 198));
        Put(menu, 0, cookie);
        Put(menu, 1, senbei).OrderNo = "8101";

        var list = OrderListBuilder.Build(menu, Names(cookie, senbei), id => id == senbei.Id);

        Assert.True(list.Lines.Single(l => l.ProductId == senbei.Id).IsBlocked);
        Assert.False(list.Lines.Single(l => l.ProductId == cookie.Id).IsBlocked);
        Assert.Equal(1, list.BlockedCount);
    }

    [Fact]
    public void SR_17_入れられない商品の行には注文したのチェックを付けられない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie);

        var result = OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", _ => true);

        Assert.True(result.HasError);
        Assert.Empty(menu.OrderChecks);
    }

    [Fact]
    public void SR_17_あとで入れられなくなった商品のチェックは外せる()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        Put(menu, 0, cookie);
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        var result = OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", false, Now, "staff", _ => true);

        Assert.False(result.HasError);
        Assert.Empty(menu.OrderChecks);
    }

    [Fact]
    public void チェックを付けられないときは前のチェックを消さない()
    {
        var cookie = Safe("さくさくクッキー");
        var menu = NewMenu(20000, 40, (cookie, 298));
        var item = Put(menu, 0, cookie);
        OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        // 商品が献立から外れたあとで、古い画面からチェックを付け直そうとした
        menu.Days[0].Items.Remove(item);
        var result = OrderListBuilder.SetOrdered(menu, cookie.Id, "1234", true, Now, "staff", NotBlocked);

        Assert.True(result.HasError);
        Assert.Single(menu.OrderChecks);
    }
}
