using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 掲載品の写し（menus.listed_products）と、商品の価格などの上書き（FR-FLYER-02・06c・07、D-24・D-53、U-10・U-15・U-33、T1-18）。
/// - 写しは商品ごとに1行。取り込みの確定のたびに追記し、選び直したら入れ替える
/// - 取り込み直しは、その結果を最初に確定した時点で写しを空にしてから追記する（途中でやめたら前の写しのまま）
/// - 商品の価格・注文番号・最後に載った週は、献立の週が商品の最後に載った週以後のときだけ上書きする（古い値に戻さない）
/// </summary>
public static class ListedProductsUpdater
{
    /// <summary>
    /// 取り込みの確定を写しに反映する。
    /// </summary>
    /// <param name="menu">献立</param>
    /// <param name="entry">確定した商品の行</param>
    /// <param name="clearFirst">取り込み直しの結果を最初に確定するとき true（写しを空にしてから追記する）</param>
    /// <param name="previousProductId">選び直したとき、前に確定していた商品（同じ取り込みの品目。なければ null）</param>
    public static void ApplyConfirmation(Menu menu, ListedProduct entry, bool clearFirst, string? previousProductId = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(entry.ProductId);

        if (clearFirst)
        {
            menu.ListedProducts.Clear();
        }

        if (previousProductId is not null && previousProductId != entry.ProductId)
        {
            menu.ListedProducts.RemoveAll(l => l.ProductId == previousProductId);
        }

        Upsert(menu, entry);
    }

    /// <summary>選び直して「取り込まない」にしたとき、前に確定していた商品の行を外す</summary>
    public static void Remove(Menu menu, string productId)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.ListedProducts.RemoveAll(l => l.ProductId == productId);
    }

    /// <summary>
    /// 手順2で入れた税込価格を写しに保存する（U-33）。掲載品にない商品はエラー。商品側は ApplyManualPrice で別に保存する。
    /// </summary>
    public static Result SetListedPrice(Menu menu, string productId, int price)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var validation = InputValidator.ValidatePrice(price);
        if (validation.HasError)
        {
            return validation;
        }

        var listed = menu.ListedProducts.LastOrDefault(l => l.ProductId == productId);
        if (listed is null)
        {
            return Result.Error("その週の掲載品にない商品です。チラシを取り込み直してください");
        }

        Upsert(menu, listed with { Price = price });
        return new Result();
    }

    /// <summary>
    /// 商品の価格などを上書きしてよいか：商品の最後に載った週がない、または献立の週がそれ以後（U-10）。
    /// </summary>
    public static bool ShouldOverwriteProduct(Product product, DateOnly menuWeekStart)
    {
        ArgumentNullException.ThrowIfNull(product);

        return product.ListedWeekStart is not { } listedWeek || menuWeekStart >= listedWeek;
    }

    /// <summary>
    /// 取り込みの確定で商品の価格・税抜・注文番号・最後に載った週を上書きする。上書きしなかったら false（過去の週の取り込み）。
    /// </summary>
    public static bool ApplyListing(Product product, DateOnly menuWeekStart, int? price, int? priceExcludingTax, string? orderNo)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!ShouldOverwriteProduct(product, menuWeekStart))
        {
            return false;
        }

        // 読めなかった項目（null）で前の値を消さない。献立の価格・注文番号は写しを使うため、商品の値は一覧の表示用
        product.Price = price ?? product.Price;
        product.PriceExcludingTax = priceExcludingTax ?? product.PriceExcludingTax;
        product.OrderNo = orderNo ?? product.OrderNo;
        product.ListedWeekStart = menuWeekStart;
        return true;
    }

    /// <summary>
    /// 手順2で入れた税込価格を商品に保存する（U-33）。取り込みと同じ条件のときだけ、価格だけを上書きする
    /// （税抜・注文番号・最後に載った週は変えない）。上書きしなかったら false（「商品の価格は更新していません」と示す）。
    /// </summary>
    public static bool ApplyManualPrice(Product product, DateOnly menuWeekStart, int price)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentOutOfRangeException.ThrowIfLessThan(price, 1);

        if (!ShouldOverwriteProduct(product, menuWeekStart))
        {
            return false;
        }

        product.Price = price;
        return true;
    }

    private static void Upsert(Menu menu, ListedProduct entry)
    {
        var index = menu.ListedProducts.FindIndex(l => l.ProductId == entry.ProductId);
        if (index < 0)
        {
            menu.ListedProducts.Add(entry);
            return;
        }

        menu.ListedProducts[index] = entry;
        // 万一2行あったら、後ろの行を消して1行にする
        for (var i = menu.ListedProducts.Count - 1; i > index; i--)
        {
            if (menu.ListedProducts[i].ProductId == entry.ProductId)
            {
                menu.ListedProducts.RemoveAt(i);
            }
        }
    }
}
