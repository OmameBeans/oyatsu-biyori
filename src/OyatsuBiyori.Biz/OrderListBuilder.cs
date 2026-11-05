using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 注文リスト（FR-MENU-35・T9-02）。献立の商品を、注文番号・商品ごとにまとめて必要なパック数と金額を足す。
/// - 数えるのは、金額を数える日（通常の日で人数が1以上）の商品だけ（おやつなし・人数0の日の商品は週の金額に含めないため、注文にも入れない）
/// - パック数が0の商品は入れない
/// - 同じ商品でも、献立に入れた時点の注文番号が違えば別の行にする
/// - 並びは注文番号の順。注文番号のない商品は最後に、商品名の順で並べる
/// - 判定が「入れられない」の商品と見つからない商品の行は IsBlocked にする（SR-17。注文させないため、チェックも付けさせない）
/// - 「注文した」のチェック（menus.order_checks）を行に重ねる。チェックしたあとでパック数が変わったら「数量が変わった」にする（注文済みとはしない）
/// </summary>
public static class OrderListBuilder
{
    /// <summary>注文リストを作る</summary>
    /// <param name="menu">献立</param>
    /// <param name="productName">商品 ID から商品名を引く（見つからなければ null）</param>
    /// <param name="isBlocked">商品 ID から、判定が「入れられない」か商品が見つからないかを返す（MenuConfirmationChecker.ClassifyIssue の Blocked）</param>
    public static OrderList Build(Menu menu, Func<string, string?> productName, Func<string, bool> isBlocked)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(productName);
        ArgumentNullException.ThrowIfNull(isBlocked);

        var lines = menu.Days
            .Where(MenuAmountCalculator.IsCountedDay)
            .SelectMany(d => d.Items.Where(i => i.PackCount > 0).Select(i => (Day: d, Item: i)))
            .GroupBy(x => (x.Item.ProductId, OrderNo: NormalizeOrderNo(x.Item.OrderNo)))
            .Select(g =>
            {
                var packCount = g.Sum(x => x.Item.PackCount);
                var check = FindCheck(menu, g.Key.ProductId, g.Key.OrderNo);
                var state = check is null
                    ? OrderLineState.NotOrdered
                    : check.PackCount == packCount ? OrderLineState.Ordered : OrderLineState.QuantityChanged;
                return new OrderListLine(
                    g.Key.OrderNo,
                    g.Key.ProductId,
                    productName(g.Key.ProductId) ?? "（商品が見つかりません）",
                    packCount,
                    checked(g.Sum(x => x.Item.Amount)),
                    g.Select(x => x.Day.Date).Distinct().Order().ToList(),
                    state,
                    check?.PackCount,
                    check?.CheckedAt,
                    isBlocked(g.Key.ProductId));
            })
            .OrderBy(l => l.OrderNo is null)
            .ThenBy(l => l.OrderNo, StringComparer.Ordinal)
            .ThenBy(l => l.ProductName, StringComparer.Ordinal)
            .ThenBy(l => l.ProductId, StringComparer.Ordinal)
            .ToList();

        return new OrderList(
            lines,
            lines.Sum(l => l.PackCount),
            checked(lines.Sum(l => l.Amount)),
            lines.Count(l => l.OrderNo is null),
            lines.Count(l => l.State == OrderLineState.Ordered),
            lines.Count(l => l.State == OrderLineState.QuantityChanged),
            lines.Count(l => l.IsBlocked));
    }

    /// <summary>
    /// 「注文した」のチェックを付ける・外す。付けるのは今の注文リストにある行だけで、そのときのパック数を記録する
    /// （付け直すと記録し直す）。「入れられない」の行には付けない（SR-17）。外すのは、リストから消えた行でもできる。
    /// エラーのときは献立のチェックを変えない。
    /// </summary>
    public static Result SetOrdered(Menu menu, string? productId, string? orderNo, bool isOrdered, DateTime now, string? userId, Func<string, bool> isBlocked)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(isBlocked);

        if (string.IsNullOrEmpty(productId))
        {
            return Result.Error("商品が指定されていません。画面を読み込み直してください");
        }

        var key = NormalizeOrderNo(orderNo);
        OrderListLine? line = null;
        if (isOrdered)
        {
            line = Build(menu, _ => null, isBlocked).Lines.FirstOrDefault(l => l.ProductId == productId && l.OrderNo == key);
            if (line is null)
            {
                return Result.Error("注文リストにない商品です。画面を読み込み直してください");
            }

            if (line.IsBlocked)
            {
                return Result.Error("この商品は献立に入れられない商品です。注文せず、献立で別の商品に替えてください");
            }
        }

        menu.OrderChecks.RemoveAll(c => c.ProductId == productId && NormalizeOrderNo(c.OrderNo) == key);
        if (line is not null)
        {
            menu.OrderChecks.Add(new OrderCheck(productId, key, line.PackCount, now, userId));
        }

        return new Result();
    }

    private static OrderCheck? FindCheck(Menu menu, string productId, string? orderNo) =>
        menu.OrderChecks.LastOrDefault(c => c.ProductId == productId && NormalizeOrderNo(c.OrderNo) == orderNo);

    /// <summary>注文番号をそろえる（前後の空白を除き、空なら null）</summary>
    private static string? NormalizeOrderNo(string? orderNo) => string.IsNullOrWhiteSpace(orderNo) ? null : orderNo.Trim();
}

/// <summary>注文リストの行の状態</summary>
public enum OrderLineState
{
    /// <summary>まだ注文していない</summary>
    NotOrdered,

    /// <summary>注文した（チェックしたときと同じパック数）</summary>
    Ordered,

    /// <summary>チェックしたあとでパック数が変わった（注文し直すか確かめる）</summary>
    QuantityChanged,
}

/// <summary>注文リスト</summary>
/// <param name="Lines">行（注文番号の順）</param>
/// <param name="TotalPackCount">パック数の合計</param>
/// <param name="TotalAmount">金額の合計</param>
/// <param name="MissingOrderNoCount">注文番号のない行の数</param>
/// <param name="OrderedCount">注文した行の数</param>
/// <param name="QuantityChangedCount">注文したあとで数量が変わった行の数</param>
/// <param name="BlockedCount">「入れられない」の行の数（注文してはいけない）</param>
public sealed record OrderList(IReadOnlyList<OrderListLine> Lines, int TotalPackCount, int TotalAmount, int MissingOrderNoCount, int OrderedCount, int QuantityChangedCount, int BlockedCount);

/// <summary>注文リストの1行</summary>
/// <param name="OrderNo">注文番号（献立に入れた時点の値。なければ null）</param>
/// <param name="ProductId">商品 ID</param>
/// <param name="ProductName">商品名</param>
/// <param name="PackCount">必要なパック数（その週の合計）</param>
/// <param name="Amount">金額（その週の合計）</param>
/// <param name="Dates">使う日</param>
/// <param name="State">注文したか</param>
/// <param name="OrderedPackCount">チェックしたときのパック数（チェックがなければ null）</param>
/// <param name="OrderedAt">チェックした日時（UTC。チェックがなければ null）</param>
/// <param name="IsBlocked">判定が「入れられない」か商品が見つからない（注文してはいけない）</param>
public sealed record OrderListLine(
    string? OrderNo,
    string ProductId,
    string ProductName,
    int PackCount,
    int Amount,
    IReadOnlyList<DateOnly> Dates,
    OrderLineState State,
    int? OrderedPackCount,
    DateTime? OrderedAt,
    bool IsBlocked);
