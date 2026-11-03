using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立の金額の計算（02 §5「金額計算の定義」、FR-MENU-08a〜10、T1-09）。
/// 金額は Claude の出力を信用せず、表示・検証のたびにここで求める。
/// </summary>
public static class MenuAmountCalculator
{
    /// <summary>
    /// 必要パック数の既定：切り上げ（その日の人数 × 1人あたりの個数 ÷ 入数）。人数0の日は0。
    /// 入数・1人あたりの個数が1未満、人数が負なら計算の前に拒む（0で割らないため）。
    /// </summary>
    public static int DefaultPackCount(int childrenCount, int perChildCount, int piecesPerPack)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(childrenCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(perChildCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(piecesPerPack, 1);

        var needed = (long)childrenCount * perChildCount;
        return checked((int)((needed + piecesPerPack - 1) / piecesPerPack));
    }

    /// <summary>パック数が全員分（その日の人数 × 1人あたりの個数）に足りないか（警告。禁止はしない）</summary>
    public static bool IsPackShort(int packCount, int childrenCount, int perChildCount, int piecesPerPack)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packCount);
        ArgumentOutOfRangeException.ThrowIfNegative(childrenCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(perChildCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(piecesPerPack, 1);

        return (long)packCount * piecesPerPack < (long)childrenCount * perChildCount;
    }

    /// <summary>商品の金額 = パック数（保存した値） × 献立に入れた時点の税込価格（商品の最新の価格は使わない）</summary>
    public static int ItemAmount(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return checked(item.PackCount * item.UnitPrice);
    }

    /// <summary>
    /// 保存する商品の金額（menu_items.amount）：金額を数える日（通常の日で人数1以上）は パック数 × 価格。
    /// 手作り・おやつなし・人数0の日は、商品があっても0（D-77。その日の金額と合わせる）。
    /// </summary>
    public static int StoredItemAmount(MenuItem item, MenuDay day)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(day);
        return IsCountedDay(day) ? ItemAmount(item) : 0;
    }

    /// <summary>その日の金額が数えられる日か（通常の日で、人数が1以上）</summary>
    public static bool IsCountedDay(MenuDay day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day.DayType == DayType.Normal && day.ChildrenCount > 0;
    }

    /// <summary>
    /// その日の金額。手作り・おやつなし・人数0の日は0で、1人あたり金額は null（「—」と表示）。
    /// 手作り・おやつなしの日に商品があっても0のまま、「金額に含まれていません」の印を立てる（D-77）。
    /// </summary>
    /// <param name="day">日ごとの献立（Items を読み込んでおくこと）</param>
    /// <param name="piecesPerPack">商品 ID から入数を返す（全員分に足りないかを見るため。分からなければ null）</param>
    public static DayAmount CalculateDay(MenuDay day, Func<string, int?> piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(piecesPerPack);

        var counted = IsCountedDay(day);
        var amount = counted ? day.Items.Sum(ItemAmount) : 0;
        decimal? perChild = counted ? (decimal)amount / day.ChildrenCount : null;

        // 手作り・おやつなしの日の商品は出さないため、足りるかは見ない（「金額に含まれていません」で示す）
        var shortItems = day.Items
            .Where(i => day.DayType == DayType.Normal
                && piecesPerPack(i.ProductId) is int pieces && pieces >= 1
                && i.PerChildCount >= 1
                && IsPackShort(i.PackCount, day.ChildrenCount, i.PerChildCount, pieces))
            .ToList();

        return new DayAmount
        {
            Date = day.Date,
            Amount = amount,
            PerChildAmount = perChild,
            ExcludedFromAmount = day.DayType != DayType.Normal && day.Items.Count > 0,
            ShortPackItems = shortItems,
        };
    }

    /// <summary>週の実績と週の予算（予算を超えても禁止しない。警告だけ）</summary>
    public static WeekAmount CalculateWeek(Menu menu, Func<string, int?> piecesPerPack)
    {
        ArgumentNullException.ThrowIfNull(menu);

        var days = menu.Days.OrderBy(d => d.Date).Select(d => CalculateDay(d, piecesPerPack)).ToList();
        return new WeekAmount
        {
            Days = days,
            Total = days.Sum(d => d.Amount),
            Budget = menu.WeeklyBudget,
        };
    }

    /// <summary>1人あたり金額を表示用に円未満で四捨五入する（銀行丸めにしない。計算は小数のまま）</summary>
    public static decimal RoundForDisplay(decimal amount) => Math.Round(amount, 0, MidpointRounding.AwayFromZero);
}

/// <summary>その日の金額</summary>
public sealed class DayAmount
{
    /// <summary>日付</summary>
    public DateOnly Date { get; init; }

    /// <summary>その日の金額（手作り・おやつなし・人数0の日は0）</summary>
    public int Amount { get; init; }

    /// <summary>1人あたり金額（小数のまま。手作り・おやつなし・人数0の日は null ＝「—」）</summary>
    public decimal? PerChildAmount { get; init; }

    /// <summary>手作り・おやつなしの日に商品があり、金額に含めていない（「金額に含まれていません」）</summary>
    public bool ExcludedFromAmount { get; init; }

    /// <summary>パック数が全員分に足りない商品</summary>
    public IReadOnlyList<MenuItem> ShortPackItems { get; init; } = [];
}

/// <summary>週の実績と予算</summary>
public sealed class WeekAmount
{
    /// <summary>日ごとの金額（日付の順）</summary>
    public IReadOnlyList<DayAmount> Days { get; init; } = [];

    /// <summary>週の実績</summary>
    public int Total { get; init; }

    /// <summary>週の予算</summary>
    public int Budget { get; init; }

    /// <summary>週の実績が予算を超えたか（警告）</summary>
    public bool IsOverBudget => Total > Budget;

    /// <summary>予算に届かないか（「予算に合わせる」を出す条件。D-67）</summary>
    public bool IsUnderBudget => Total < Budget;

    /// <summary>予算 − 実績（超えたら負）</summary>
    public int Remaining => Budget - Total;
}
