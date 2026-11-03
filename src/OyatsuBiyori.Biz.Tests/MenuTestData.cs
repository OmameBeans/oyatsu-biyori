using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using static OyatsuBiyori.Biz.Tests.TestData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// 献立のテスト用のデータ（架空の商品・献立）。
/// </summary>
internal static class MenuTestData
{
    /// <summary>献立の週（月曜）。TestData.Today（11/3）の2週後</summary>
    public static readonly DateOnly WeekStart = new(2026, 11, 16);

    /// <summary>週の n 日目（0＝月曜）</summary>
    public static DateOnly D(int dayIndex) => WeekStart.AddDays(dayIndex);

    /// <summary>確認済み（期限内）で、すべての品目を含まないとした商品</summary>
    public static Product Safe(string name, SnackType snackType = SnackType.Sweet, int? pieces = 10, int perChild = 1)
    {
        return Snack(name, snackType, pieces, perChild, ConfirmedAll(Today.AddDays(-10), []));
    }

    /// <summary>チラシの「参考（表記なし）」だけの商品（警告）</summary>
    public static Product ReferenceOnly(string name, SnackType snackType = SnackType.Sweet, int? pieces = 10, params string[] contains)
    {
        return Snack(name, snackType, pieces, 1, TestData.Reference(Today.AddDays(-3), contains));
    }

    /// <summary>商品</summary>
    public static Product Snack(string name, SnackType snackType, int? pieces, int perChild, params AllergenCheck[] checks)
    {
        var product = Product(name, checks);
        product.SnackType = snackType;
        product.PiecesPerPack = pieces;
        product.DefaultPerChildCount = perChild;
        return product;
    }

    /// <summary>献立（月〜金・通常の日・人数 children）。写しには listed の商品を price で入れる</summary>
    public static Menu NewMenu(int budget, int children, params (Product Product, int? Price)[] listed)
    {
        var menu = new Menu { WeekStart = WeekStart, WeeklyBudget = budget };
        for (var i = 0; i < 5; i++)
        {
            menu.Days.Add(new MenuDay { MenuId = menu.Id, Date = D(i), ChildrenCount = children, DayType = DayType.Normal });
        }

        foreach (var (product, price) in listed)
        {
            menu.ListedProducts.Add(new ListedProduct(product.Id, price, "1234"));
        }

        return menu;
    }

    /// <summary>その日に商品を入れる（MenuItemFactory で作る）</summary>
    public static MenuItem Put(Menu menu, int dayIndex, Product product)
    {
        var day = menu.Days.Single(d => d.Date == D(dayIndex));
        var listed = menu.ListedProducts.Single(l => l.ProductId == product.Id);
        var item = MenuItemFactory.Create(day, product, listed).Value!;
        day.Items.Add(item);
        return item;
    }

    /// <summary>写しを通さずに商品を入れる（掲載品にない・価格がない商品のため）</summary>
    public static MenuItem PutRaw(Menu menu, int dayIndex, string productId, int unitPrice = 300, int packCount = 5)
    {
        var day = menu.Days.Single(d => d.Date == D(dayIndex));
        var item = new MenuItem { MenuDayId = day.Id, ProductId = productId, UnitPrice = unitPrice, PackCount = packCount, PerChildCount = 1 };
        item.Amount = MenuAmountCalculator.StoredItemAmount(item, day);
        day.Items.Add(item);
        return item;
    }

    /// <summary>判定などの入力（既定：しょっぱい・あまい30日、確定献立なし）</summary>
    public static MenuContext Context(IEnumerable<Product> products, IReadOnlyList<Allergen>? avoided = null, IEnumerable<ServedItem>? history = null, IEnumerable<DuplicateAvoidRule>? rules = null, DateOnly? today = null)
    {
        return new MenuContext(
            products,
            avoided ?? [],
            today ?? Today,
            RecheckDays,
            rules ?? [new DuplicateAvoidRule(SnackType.Salty, 30), new DuplicateAvoidRule(SnackType.Sweet, 30)],
            history ?? []);
    }
}
