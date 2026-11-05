using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Tests;

public class MenuAmountCalculatorTests
{
    [Theory]
    [InlineData(50, 1, 10, 5)]
    [InlineData(51, 1, 10, 6)]
    [InlineData(49, 1, 10, 5)]
    [InlineData(60, 1, 12, 5)]
    [InlineData(61, 1, 12, 6)]
    [InlineData(45, 2, 10, 9)]
    [InlineData(45, 2, 9, 10)]
    [InlineData(1, 1, 24, 1)]
    public void パック数の既定は切り上げ(int children, int perChild, int pieces, int expected)
    {
        Assert.Equal(expected, MenuAmountCalculator.DefaultPackCount(children, perChild, pieces));
    }

    [Fact]
    public void 人数0の日のパック数は0()
    {
        Assert.Equal(0, MenuAmountCalculator.DefaultPackCount(0, 1, 10));
    }

    [Theory]
    [InlineData(50, 1, 0)]
    [InlineData(50, 0, 10)]
    [InlineData(-1, 1, 10)]
    public void 入数や1人あたりの個数が0なら計算の前に拒む(int children, int perChild, int pieces)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuAmountCalculator.DefaultPackCount(children, perChild, pieces));
    }

    [Theory]
    [InlineData(5, 50, 1, 10, false)]
    [InlineData(4, 50, 1, 10, true)]
    [InlineData(5, 51, 1, 10, true)]
    [InlineData(9, 45, 2, 10, false)]
    [InlineData(8, 45, 2, 10, true)]
    [InlineData(0, 0, 1, 10, false)]
    public void パック数が全員分に足りないかの境界(int packs, int children, int perChild, int pieces, bool expected)
    {
        Assert.Equal(expected, MenuAmountCalculator.IsPackShort(packs, children, perChild, pieces));
    }

    [Fact]
    public void 商品の金額は献立に入れた時点の価格で計算する()
    {
        var item = new MenuItem { ProductId = "p", UnitPrice = 398, PackCount = 6 };

        Assert.Equal(2388, MenuAmountCalculator.ItemAmount(item));
    }

    [Fact]
    public void 通常の日の金額と1人あたり金額()
    {
        var day = Day(DayType.Normal, 45, Item("a", 398, 5), Item("b", 250, 3));

        var result = MenuAmountCalculator.CalculateDay(day, _ => 10);

        Assert.Equal(2740, result.Amount);
        Assert.Equal(2740m / 45, result.PerChildAmount);
        Assert.False(result.ExcludedFromAmount);
    }

    [Theory]
    [InlineData(DayType.NoSnack)]
    public void おやつなしの日は商品があっても金額0で金額に含まれていない印(DayType dayType)
    {
        var day = Day(dayType, 45, Item("a", 398, 5));

        var result = MenuAmountCalculator.CalculateDay(day, _ => 10);

        Assert.Equal(0, result.Amount);
        Assert.Null(result.PerChildAmount);
        Assert.True(result.ExcludedFromAmount);
    }

    [Fact]
    public void おやつなしの日に商品がなければ金額に含まれていない印は立たない()
    {
        var result = MenuAmountCalculator.CalculateDay(Day(DayType.NoSnack, 45), _ => 10);

        Assert.False(result.ExcludedFromAmount);
    }

    [Fact]
    public void 人数0の日は金額0で1人あたり金額は出さない()
    {
        var day = Day(DayType.Normal, 0, Item("a", 398, 5));

        var result = MenuAmountCalculator.CalculateDay(day, _ => 10);

        Assert.Equal(0, result.Amount);
        Assert.Null(result.PerChildAmount);
    }

    [Fact]
    public void 全員分に足りない商品を返す()
    {
        var shortItem = Item("a", 398, 4);
        var enoughItem = Item("b", 250, 5);
        var day = Day(DayType.Normal, 50, shortItem, enoughItem);

        var result = MenuAmountCalculator.CalculateDay(day, _ => 10);

        Assert.Equal([shortItem], result.ShortPackItems);
    }

    [Fact]
    public void 入数が分からない商品は足りないかを判定しない()
    {
        var day = Day(DayType.Normal, 50, Item("a", 398, 1));

        var result = MenuAmountCalculator.CalculateDay(day, _ => null);

        Assert.Empty(result.ShortPackItems);
    }

    [Fact]
    public void 週の実績と予算超過()
    {
        var menu = new Menu { WeeklyBudget = 5000 };
        menu.Days.Add(Day(DayType.Normal, 45, Item("a", 398, 5)));
        menu.Days.Add(Day(DayType.Normal, 45, Item("b", 500, 6)));
        menu.Days.Add(Day(DayType.NoSnack, 45, Item("c", 1000, 5)));

        var result = MenuAmountCalculator.CalculateWeek(menu, _ => 10);

        Assert.Equal(1990 + 3000, result.Total);
        Assert.False(result.IsOverBudget);
        Assert.True(result.IsUnderBudget);
        Assert.Equal(10, result.Remaining);

        menu.WeeklyBudget = 4989;
        var over = MenuAmountCalculator.CalculateWeek(menu, _ => 10);
        Assert.True(over.IsOverBudget);
        Assert.Equal(-1, over.Remaining);
    }

    [Fact]
    public void 予算ちょうどは超過でも不足でもない()
    {
        var menu = new Menu { WeeklyBudget = 1990 };
        menu.Days.Add(Day(DayType.Normal, 45, Item("a", 398, 5)));

        var result = MenuAmountCalculator.CalculateWeek(menu, _ => 10);

        Assert.False(result.IsOverBudget);
        Assert.False(result.IsUnderBudget);
    }

    [Theory]
    [InlineData(60.5, 61)]
    [InlineData(61.5, 62)]
    [InlineData(60.49, 60)]
    [InlineData(60.0, 60)]
    public void 表示の1人あたり金額は四捨五入で銀行丸めにしない(double value, int expected)
    {
        Assert.Equal(expected, MenuAmountCalculator.RoundForDisplay((decimal)value));
    }

    private static MenuDay Day(DayType dayType, int children, params MenuItem[] items)
    {
        var day = new MenuDay { Date = new DateOnly(2026, 11, 16), DayType = dayType, ChildrenCount = children };
        day.Items.AddRange(items);
        return day;
    }

    private static MenuItem Item(string productId, int unitPrice, int packCount) => new()
    {
        ProductId = productId,
        UnitPrice = unitPrice,
        PackCount = packCount,
        PerChildCount = 1,
    };
}
