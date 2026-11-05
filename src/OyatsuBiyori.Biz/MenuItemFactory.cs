using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立の商品（menu_items）を作る・直す処理を1か所にまとめる（04 menu_items、FR-FLYER-07、U-04、T1-12）。
/// Claude の案・手動の追加・チャット・入れ替え案はすべてここを通す。
/// 価格・注文番号は掲載品の写しから、1人あたりの個数は商品の既定値から写し、パック数と金額はアプリが計算する
/// （Claude に個数や金額を決めさせない）。
/// </summary>
public static class MenuItemFactory
{
    /// <summary>
    /// 献立の商品を作る（日には足さない）。写しにない・写しの価格がない・入数がない商品は、金額を計算できないため作らない。
    /// 判定が「入れられない」商品は作れる（手で入れたら理由を示し、確定させない。FR-MENU-14）。
    /// </summary>
    /// <param name="day">入れる日</param>
    /// <param name="product">商品</param>
    /// <param name="listed">その週の掲載品の写しの行（なければ null）</param>
    public static Result<MenuItem> Create(MenuDay day, Product product, ListedProduct? listed)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(product);

        var check = CheckInsertable(product, listed);
        if (check.HasError)
        {
            return Result<MenuItem>.Error(check.ErrorMessages[0]);
        }

        var item = new MenuItem
        {
            MenuDayId = day.Id,
            ProductId = product.Id,
        };
        Fill(item, day, product, listed!);
        return Result<MenuItem>.Ok(item);
    }

    /// <summary>
    /// 商品を入れ替える。価格・個数・パック数は新しい商品で作り直し、確定時の確認は消す（商品を入れ替えたら確認し直し。SR-22）。
    /// </summary>
    public static Result ReplaceProduct(MenuItem item, MenuDay day, Product product, ListedProduct? listed)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(product);

        var check = CheckInsertable(product, listed);
        if (check.HasError)
        {
            return check;
        }

        item.ProductId = product.Id;
        Fill(item, day, product, listed!);
        ClearAcknowledgement(item);
        return new Result();
    }

    /// <summary>1人あたりの個数を変え、パック数を式で求め直す（U-04）</summary>
    public static Result SetPerChildCount(MenuItem item, MenuDay day, Product product, int perChildCount)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(product);

        var validation = InputValidator.ValidatePerChildCount(perChildCount);
        if (validation.HasError)
        {
            return validation;
        }

        if (product.PiecesPerPack is not int pieces || pieces < 1)
        {
            return Result.Error($"「{product.Name}」の入数がありません。先に入数を入れてください");
        }

        item.PerChildCount = perChildCount;
        item.PackCount = MenuAmountCalculator.DefaultPackCount(day.ChildrenCount, perChildCount, pieces);
        item.Amount = MenuAmountCalculator.StoredItemAmount(item, day);
        return new Result();
    }

    /// <summary>パック数を上書きする（1以上。全員分に足りなければ警告になるが、止めない）</summary>
    public static Result SetPackCount(MenuItem item, MenuDay day, int packCount)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(day);

        var validation = InputValidator.ValidatePackCount(packCount);
        if (validation.HasError)
        {
            return validation;
        }

        item.PackCount = packCount;
        item.Amount = MenuAmountCalculator.StoredItemAmount(item, day);
        return new Result();
    }

    /// <summary>
    /// その日の人数を変えたあと、商品のパック数を式で求め直す（U-04）。入数が分からない商品はパック数を変えない。
    /// 金額も求め直す。
    /// </summary>
    public static void RecalculatePackCounts(MenuDay day, Func<string, int?> piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(piecesPerPack);

        foreach (var item in day.Items)
        {
            RecalculatePackCount(item, day, piecesPerPack(item.ProductId));
        }

        RefreshAmounts(day);
    }

    /// <summary>
    /// 1つの商品のパック数を、その日の人数と1人あたりの個数から式で求め直す（U-04）。入数が分からない商品はパック数を変えない。
    /// 金額は変えないので、呼んだ側で求め直す。
    /// </summary>
    public static void RecalculatePackCount(MenuItem item, MenuDay day, int? piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(day);

        if (piecesPerPack is int pieces && pieces >= 1 && item.PerChildCount >= 1)
        {
            item.PackCount = MenuAmountCalculator.DefaultPackCount(day.ChildrenCount, item.PerChildCount, pieces);
        }
    }

    /// <summary>日の種類を変えたあと、商品の金額を求め直す（おやつなしの日は0。D-77）</summary>
    public static void RefreshAmounts(MenuDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        foreach (var item in day.Items)
        {
            item.Amount = MenuAmountCalculator.StoredItemAmount(item, day);
        }
    }

    /// <summary>確定時の確認を消す（未確認に戻す）</summary>
    public static void ClearAcknowledgement(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.AcknowledgedAt = null;
        item.AcknowledgedJudgement = null;
        item.AcknowledgedJudgementFingerprint = null;
    }

    /// <summary>献立に入れられる形か（写しにある・写しの税込価格がある・入数がある・1人あたりの個数の既定が1以上）</summary>
    public static Result CheckInsertable(Product product, ListedProduct? listed)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (listed is null || listed.ProductId != product.Id)
        {
            return Result.Error($"「{product.Name}」はその週の掲載品にないため、献立に入れられません");
        }

        if (listed.Price is null or < 1)
        {
            return Result.Error($"「{product.Name}」の税込価格がありません。今週の商品で税込価格を入れてください");
        }

        if (product.PiecesPerPack is null or < 1)
        {
            return Result.Error($"「{product.Name}」の入数がありません。今週の商品で入数を入れてください");
        }

        if (product.DefaultPerChildCount < 1)
        {
            return Result.Error($"「{product.Name}」の1人あたりの個数が正しくありません。商品の編集で1以上にしてください");
        }

        return new Result();
    }

    private static void Fill(MenuItem item, MenuDay day, Product product, ListedProduct listed)
    {
        item.UnitPrice = listed.Price!.Value;
        item.OrderNo = listed.OrderNo;
        item.PerChildCount = product.DefaultPerChildCount;
        item.PackCount = MenuAmountCalculator.DefaultPackCount(day.ChildrenCount, item.PerChildCount, product.PiecesPerPack!.Value);
        item.Amount = MenuAmountCalculator.StoredItemAmount(item, day);
    }
}
