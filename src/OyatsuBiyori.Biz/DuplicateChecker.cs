using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 重複チェック（FR-COND-05、FR-MENU-12、U-09、D-48、T1-10）。禁止はせず、警告だけ出す。
/// - 基準は確定献立だけ。その日の日付から数え、|その日 − 出した日| ≤ 期間 なら期間内（N日目までは期間内。再確認期間と同じ数え方）
/// - 期間は商品の今のおやつの分類で決める。設定なしの分類は見ない
/// - 確定献立が後の週にある（先の週を先に確定した）ときも「○日後に出す予定」として警告する（警告は多めに出す側に倒す）
/// - 同じ週（同じ献立）の中で同じ商品を出すときは、分類の期間にかかわらず両方の日に警告する
/// 「同じ商品」は商品 ID の一致（味違い・内容量違いは別の商品。補足ルールは Claude に渡すだけ）。
/// </summary>
public static class DuplicateChecker
{
    /// <summary>
    /// 献立の商品の重複を求める。対象の献立では、おやつなしの日の商品も比べる（日の種類を通常に戻すことがあるため、多めに出す）。
    /// 確定献立の履歴は、実際に出した通常の日の商品だけ（ServedItem.FromMenus）。
    /// </summary>
    /// <param name="menu">対象の献立（自分自身は確定済みでも履歴から外し、同じ週の中として数える）</param>
    /// <param name="context">今の設定と確定献立</param>
    public static IReadOnlyList<DuplicateHit> Check(Menu menu, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        var hits = new List<DuplicateHit>();
        var entries = menu.Days
            .SelectMany(d => d.Items.Select(i => (Day: d, Item: i)))
            .ToList();

        foreach (var (day, item) in entries)
        {
            var product = context.FindProduct(item.ProductId);
            var avoidDays = product is null ? null : context.DuplicateAvoidDays(product.SnackType);

            if (avoidDays is int days)
            {
                var history = context.ConfirmedHistory
                    .Where(s => s.MenuId != menu.Id && s.ProductId == item.ProductId)
                    .Select(s => s.Date)
                    .Distinct()
                    .Where(date => Math.Abs(day.Date.DayNumber - date.DayNumber) <= days);
                foreach (var servedDate in history)
                {
                    hits.Add(new DuplicateHit(item, day.Date, servedDate, day.Date.DayNumber - servedDate.DayNumber, false));
                }
            }

            foreach (var (otherDay, _) in entries.Where(e => !ReferenceEquals(e.Item, item) && e.Item.ProductId == item.ProductId))
            {
                hits.Add(new DuplicateHit(item, day.Date, otherDay.Date, day.Date.DayNumber - otherDay.Date.DayNumber, true));
            }
        }

        return hits
            .OrderBy(h => h.Date)
            .ThenBy(h => h.OtherDate)
            .ToList();
    }

    /// <summary>
    /// 手順2の一覧の「○日前に出した」：週の初日から数えて、期間内に確定献立で出した最も近い日までの日数（なければ null）。
    /// </summary>
    /// <param name="product">商品</param>
    /// <param name="weekStart">献立の週の初日</param>
    /// <param name="menuId">対象の献立（履歴から外す）</param>
    /// <param name="context">今の設定と確定献立</param>
    public static int? DaysSinceServed(Product product, DateOnly weekStart, string? menuId, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(context);

        if (context.DuplicateAvoidDays(product.SnackType) is not int days)
        {
            return null;
        }

        var daysAgo = context.ConfirmedHistory
            .Where(s => s.MenuId != menuId && s.ProductId == product.Id && s.Date < weekStart)
            .Select(s => weekStart.DayNumber - s.Date.DayNumber)
            .Where(d => d <= days)
            .ToList();
        return daysAgo.Count == 0 ? null : daysAgo.Min();
    }

    /// <summary>
    /// Claude に渡す確定献立の範囲（D-48）：週の初日から「最も長い期間＋1週分」前から、週の終わりから最も長い期間の後まで
    /// （後の週の確定献立を渡さないと、警告の出る案を返されるため）。期間の設定がなければ前後1週分。
    /// </summary>
    public static (DateOnly From, DateOnly To) HistoryRange(DateOnly weekStart, IEnumerable<DuplicateAvoidRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var longest = rules.Where(r => r.SnackType != SnackType.NotSnack && r.Days > 0).Select(r => r.Days).DefaultIfEmpty(0).Max();
        var weekEnd = weekStart.AddDays(6);
        return (weekStart.AddDays(-(longest + 7)), weekEnd.AddDays(Math.Max(longest, 7)));
    }
}

/// <summary>
/// 重複の警告。
/// </summary>
/// <param name="Item">警告を付ける献立の商品</param>
/// <param name="Date">その商品の日</param>
/// <param name="OtherDate">同じ商品を出した（出す）日</param>
/// <param name="DaysApart">その日 − 相手の日（正：前に出した、負：後に出す予定、0：同じ日）</param>
/// <param name="SameMenu">同じ週（同じ献立）の中の重複か</param>
public sealed record DuplicateHit(MenuItem Item, DateOnly Date, DateOnly OtherDate, int DaysApart, bool SameMenu)
{
    /// <summary>画面と Claude に返す文面</summary>
    public string Message => (SameMenu, DaysApart) switch
    {
        (_, 0) => "同じ日に同じ商品が2つ入っています",
        (true, > 0) => $"同じ週の {MenuIssueText.DateLabel(OtherDate)} にも入っています（{DaysApart}日前）",
        (true, _) => $"同じ週の {MenuIssueText.DateLabel(OtherDate)} にも入っています（{-DaysApart}日後）",
        (false, > 0) => $"{DaysApart}日前（{MenuIssueText.DateLabel(OtherDate)}）に出しました",
        _ => $"{-DaysApart}日後（{MenuIssueText.DateLabel(OtherDate)}）に出す予定です",
    };
}
