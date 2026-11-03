using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.MenuTestData;

namespace OyatsuBiyori.Biz.Tests;

public class ListedProductsUpdaterTests
{
    [Fact]
    public void 確定のたびに写しへ追記し同じ商品は1行に置き換える()
    {
        var menu = NewMenu(20000, 50);

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("a", 298, "1111"), clearFirst: false);
        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("b", 398, "2222"), clearFirst: false);
        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("a", 278, "1112"), clearFirst: false);

        Assert.Equal([new ListedProduct("a", 278, "1112"), new ListedProduct("b", 398, "2222")], menu.ListedProducts);
    }

    [Fact]
    public void 取り込み直しは最初の確定で写しを空にしてから追記する()
    {
        var menu = NewMenu(20000, 50);
        menu.ListedProducts.Add(new ListedProduct("old", 298, "1111"));

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("new1", 198, "3333"), clearFirst: true);
        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("new2", 198, "4444"), clearFirst: false);

        Assert.Equal(["new1", "new2"], menu.ListedProducts.Select(l => l.ProductId));
    }

    [Fact]
    public void 選び直したら前の商品の行を新しい商品の行に入れ替える()
    {
        var menu = NewMenu(20000, 50);
        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("same", 298, "1111"), clearFirst: false);

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("new-product", 298, "1111"), clearFirst: false, previousProductId: "same");

        Assert.Equal(["new-product"], menu.ListedProducts.Select(l => l.ProductId));
    }

    [Fact]
    public void 取り込まないに選び直したら行を外す()
    {
        var menu = NewMenu(20000, 50);
        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("a", 298, "1111"), clearFirst: false);

        ListedProductsUpdater.Remove(menu, "a");

        Assert.Empty(menu.ListedProducts);
    }

    [Fact]
    public void 手順2で入れた価格を写しに保存する()
    {
        var menu = NewMenu(20000, 50);
        menu.ListedProducts.Add(new ListedProduct("a", null, "1111"));

        Assert.False(ListedProductsUpdater.SetListedPrice(menu, "a", 324).HasError);
        Assert.Equal(new ListedProduct("a", 324, "1111"), Assert.Single(menu.ListedProducts));

        Assert.True(ListedProductsUpdater.SetListedPrice(menu, "a", 0).HasError);
        Assert.True(ListedProductsUpdater.SetListedPrice(menu, "not-listed", 324).HasError);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(-7, true)]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public void 商品を上書きするのは献立の週が最後に載った週以後のときだけ(int? listedOffsetDays, bool expected)
    {
        var product = new Product { Name = "さくさくクッキー", ListedWeekStart = listedOffsetDays is int d ? WeekStart.AddDays(d) : null };

        Assert.Equal(expected, ListedProductsUpdater.ShouldOverwriteProduct(product, WeekStart));
    }

    [Fact]
    public void 新しい週の取り込みで価格と注文番号と最後に載った週を上書きする()
    {
        var product = new Product { Name = "さくさくクッキー", Price = 298, PriceExcludingTax = 276, OrderNo = "1111", ListedWeekStart = WeekStart.AddDays(-7) };

        Assert.True(ListedProductsUpdater.ApplyListing(product, WeekStart, 324, 300, "2222"));

        Assert.Equal(324, product.Price);
        Assert.Equal(300, product.PriceExcludingTax);
        Assert.Equal("2222", product.OrderNo);
        Assert.Equal(WeekStart, product.ListedWeekStart);
    }

    [Fact]
    public void 取り込み直しの最初の確定で選び直しても写しは1行()
    {
        var menu = NewMenu(20000, 50);
        menu.ListedProducts.Add(new ListedProduct("old", 298, "1111"));

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct("new", 198, "3333"), clearFirst: true, previousProductId: "old");

        Assert.Equal([new ListedProduct("new", 198, "3333")], menu.ListedProducts);
    }

    [Fact]
    public void 読めなかった項目で商品の前の値を消さない()
    {
        var product = new Product { Name = "さくさくクッキー", Price = 298, PriceExcludingTax = 276, OrderNo = "1111", ListedWeekStart = WeekStart.AddDays(-7) };

        Assert.True(ListedProductsUpdater.ApplyListing(product, WeekStart, null, null, null));

        Assert.Equal(298, product.Price);
        Assert.Equal(276, product.PriceExcludingTax);
        Assert.Equal("1111", product.OrderNo);
        Assert.Equal(WeekStart, product.ListedWeekStart);
    }

    [Fact]
    public void 過去の週の取り込みでは古い値に戻さない()
    {
        var product = new Product { Name = "さくさくクッキー", Price = 324, OrderNo = "2222", ListedWeekStart = WeekStart };

        Assert.False(ListedProductsUpdater.ApplyListing(product, WeekStart.AddDays(-14), 298, 276, "1111"));

        Assert.Equal(324, product.Price);
        Assert.Equal("2222", product.OrderNo);
        Assert.Equal(WeekStart, product.ListedWeekStart);
    }

    [Fact]
    public void 手順2の価格は商品の価格だけを同じ条件で上書きする()
    {
        var product = new Product { Name = "さくさくクッキー", Price = null, PriceExcludingTax = 276, OrderNo = "1111", ListedWeekStart = WeekStart };

        Assert.True(ListedProductsUpdater.ApplyManualPrice(product, WeekStart, 298));
        Assert.Equal(298, product.Price);
        Assert.Equal(276, product.PriceExcludingTax);
        Assert.Equal("1111", product.OrderNo);

        Assert.False(ListedProductsUpdater.ApplyManualPrice(product, WeekStart.AddDays(-7), 250));
        Assert.Equal(298, product.Price);
    }
}
