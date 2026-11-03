using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 予算に合わせる入れ替え案（FR-MENU-26〜30a、D-25・D-67、T1-14）。金額の組み合わせはアプリが全通り試し、Claude には計算させない。
/// - 試すのは「1か所の商品の入れ替え」「1日に1品の追加」「1か所のパック数 ±1」だけ（2か所以上の組み合わせは作らない）
/// - 試すのは通常の日で人数が1以上の日だけ。同じ日にすでにある商品の追加・入れ替えは試さない
/// - 目標額（週の予算）を超えず、今の差より小さくなる案を、差の小さい順に上位3件
/// - 候補は T1-11 の候補だけ（入れられない商品は出ない）。警告の商品を使う案は印を付ける
/// </summary>
public static class BudgetFitSearcher
{
    /// <summary>出す案の数</summary>
    public const int MaxOptions = 3;

    /// <summary>入れ替え案を探す</summary>
    /// <param name="menu">献立（Days・Items を読み込んでおくこと）</param>
    /// <param name="context">商品と判定</param>
    public static BudgetFitResult Search(Menu menu, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        var week = MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack);
        if (!week.IsUnderBudget)
        {
            return new BudgetFitResult(week.Total, week.Budget, []);
        }

        var candidates = MenuCandidateFilter.Candidates(menu, context);
        var candidateIds = candidates.Select(c => c.ProductId).ToHashSet(StringComparer.Ordinal);
        var gap = week.Remaining;
        var options = new List<BudgetFitOption>();

        foreach (var day in menu.Days.Where(MenuAmountCalculator.IsCountedDay).OrderBy(d => d.Date))
        {
            var productsOnDay = day.Items.Select(i => i.ProductId).ToHashSet(StringComparer.Ordinal);

            foreach (var item in day.Items)
            {
                var currentAmount = MenuAmountCalculator.ItemAmount(item);

                // 1か所の入れ替え（新しい商品の個数は MenuItemFactory と同じ：既定の1人あたりの個数・式のパック数）
                foreach (var candidate in candidates.Where(c => !productsOnDay.Contains(c.ProductId)))
                {
                    var packs = DefaultPacks(day, candidate);
                    var delta = packs * candidate.Price!.Value - currentAmount;
                    options.Add(Option(BudgetFitKind.Replace, day, item, item.ProductId, candidate, packs, week.Total + delta, week.Budget, false));
                }

                // パック数 ±1（候補の商品の行だけ。−1 はパック数が2以上のときだけ）。
                // 案を出すのは予算に届かないときだけなので −1 は差が広がり選ばれないが、FR-MENU-28 の「±1」の形のとおり試す
                if (candidateIds.Contains(item.ProductId))
                {
                    var evaluation = candidates.First(c => c.ProductId == item.ProductId);
                    var pieces = evaluation.Product!.PiecesPerPack!.Value;
                    options.Add(Option(BudgetFitKind.PackIncrease, day, item, item.ProductId, evaluation, item.PackCount + 1, week.Total + item.UnitPrice, week.Budget,
                        MenuAmountCalculator.IsPackShort(item.PackCount + 1, day.ChildrenCount, item.PerChildCount, pieces)));
                    if (item.PackCount >= 2)
                    {
                        options.Add(Option(BudgetFitKind.PackDecrease, day, item, item.ProductId, evaluation, item.PackCount - 1, week.Total - item.UnitPrice, week.Budget,
                            MenuAmountCalculator.IsPackShort(item.PackCount - 1, day.ChildrenCount, item.PerChildCount, pieces)));
                    }
                }
            }

            // 1日に1品の追加
            foreach (var candidate in candidates.Where(c => !productsOnDay.Contains(c.ProductId)))
            {
                var packs = DefaultPacks(day, candidate);
                options.Add(Option(BudgetFitKind.Add, day, null, null, candidate, packs, week.Total + packs * candidate.Price!.Value, week.Budget, false));
            }
        }

        var best = options
            .Where(o => o.NewTotal <= week.Budget && o.Difference < gap)
            .OrderBy(o => o.Difference)
            .ThenBy(o => o.Kind)
            .ThenBy(o => o.Date)
            .ThenBy(o => o.NewProductId, StringComparer.Ordinal)
            .ThenBy(o => o.MenuItemId, StringComparer.Ordinal)
            .ThenBy(o => o.NewPackCount)
            .Take(MaxOptions)
            .Select(o => o with { CurrentTotal = week.Total })
            .ToList();

        return new BudgetFitResult(week.Total, week.Budget, best);
    }

    /// <summary>
    /// 選んだ案を献立に反映する（FR-MENU-30a）。献立が案を作ったときから変わっていれば反映しない。
    /// 反映したあとの金額・判定・検証は、呼び出し側で求め直す。
    /// </summary>
    public static Result Apply(Menu menu, BudgetFitOption option, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(context);

        // 週の予算・実績が案を作ったときと違えば、示した「差」が古いため反映しない
        var week = MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack);
        var day = menu.Days.FirstOrDefault(d => d.Date == option.Date);
        if (day is null || week.Budget != option.Budget || week.Total != option.CurrentTotal)
        {
            return Stale();
        }

        // 商品のない日の種類・人数を変えても実績は変わらないため、日そのものも確かめる（試すのは通常の日で人数1以上の日だけ）
        if (!MenuAmountCalculator.IsCountedDay(day))
        {
            return Stale();
        }

        var item = option.MenuItemId is null ? null : day.Items.FirstOrDefault(i => i.Id == option.MenuItemId);
        if (option.Kind != BudgetFitKind.Add && (item is null || item.ProductId != option.OldProductId))
        {
            return Stale();
        }

        var evaluation = MenuCandidateFilter.EvaluateProduct(menu, option.NewProductId, context);
        if (!evaluation.IsCandidate)
        {
            return Result.Error("案の商品が今は候補に入りません（判定や掲載品が変わりました）。もう一度「予算に合わせる」を押してください");
        }

        // 入れる商品のパック数・価格が案を作ったときと同じか（人数・写しの価格・既定の個数が変わっていれば、示した額にならない）
        if (option.Kind is BudgetFitKind.Add or BudgetFitKind.Replace)
        {
            var packs = DefaultPacks(day, evaluation);
            var removed = option.Kind == BudgetFitKind.Replace ? MenuAmountCalculator.ItemAmount(item!) : 0;
            if (packs != option.NewPackCount || week.Total - removed + packs * evaluation.Price!.Value != option.NewTotal)
            {
                return Stale();
            }
        }

        switch (option.Kind)
        {
            case BudgetFitKind.Add:
                if (day.Items.Any(i => i.ProductId == option.NewProductId))
                {
                    return Stale();
                }

                var created = MenuItemFactory.Create(day, evaluation.Product!, evaluation.Listed);
                if (created.HasError)
                {
                    return created;
                }

                day.Items.Add(created.Value!);
                return new Result();
            case BudgetFitKind.Replace:
                if (day.Items.Any(i => i.ProductId == option.NewProductId))
                {
                    return Stale();
                }

                return MenuItemFactory.ReplaceProduct(item!, day, evaluation.Product!, evaluation.Listed);
            case BudgetFitKind.PackIncrease:
            case BudgetFitKind.PackDecrease:
                var expected = option.Kind == BudgetFitKind.PackIncrease ? option.NewPackCount - 1 : option.NewPackCount + 1;
                if (item!.PackCount != expected)
                {
                    return Stale();
                }

                return MenuItemFactory.SetPackCount(item, day, option.NewPackCount);
            default:
                throw new ArgumentOutOfRangeException(nameof(option), option.Kind, null);
        }
    }

    private static Result Stale() => Result.Error("献立が変わったため、この案は使えません。もう一度「予算に合わせる」を押してください");

    private static int DefaultPacks(MenuDay day, ListedProductEvaluation candidate)
    {
        return MenuAmountCalculator.DefaultPackCount(day.ChildrenCount, candidate.Product!.DefaultPerChildCount, candidate.Product.PiecesPerPack!.Value);
    }

    private static BudgetFitOption Option(BudgetFitKind kind, MenuDay day, MenuItem? item, string? oldProductId, ListedProductEvaluation newProduct, int packs, int newTotal, int budget, bool hasShortage)
    {
        return new BudgetFitOption(kind, day.Date, item?.Id, oldProductId, newProduct.ProductId, packs, newTotal, budget, newProduct.IsWarning, hasShortage);
    }
}

/// <summary>入れ替え案の種類（同じ差なら、この順に並べる）</summary>
public enum BudgetFitKind
{
    /// <summary>1か所の商品の入れ替え</summary>
    Replace,

    /// <summary>1日に1品の追加</summary>
    Add,

    /// <summary>パック数 +1</summary>
    PackIncrease,

    /// <summary>パック数 −1</summary>
    PackDecrease,
}

/// <summary>
/// 入れ替え案。
/// </summary>
/// <param name="Kind">種類</param>
/// <param name="Date">日</param>
/// <param name="MenuItemId">変える献立の商品（追加なら null）</param>
/// <param name="OldProductId">今の商品（追加なら null）</param>
/// <param name="NewProductId">入れる商品（パック数の案なら今の商品）</param>
/// <param name="NewPackCount">入れる商品のパック数</param>
/// <param name="NewTotal">案を反映したときの週の実績</param>
/// <param name="Budget">週の予算（目標額）</param>
/// <param name="UsesWarningProduct">判定が「警告」の商品を使う（確定時に確認が要る）</param>
/// <param name="HasShortage">パック数が全員分に足りなくなる（警告）</param>
public sealed record BudgetFitOption(
    BudgetFitKind Kind,
    DateOnly Date,
    string? MenuItemId,
    string? OldProductId,
    string NewProductId,
    int NewPackCount,
    int NewTotal,
    int Budget,
    bool UsesWarningProduct,
    bool HasShortage)
{
    /// <summary>案を作ったときの週の実績（反映の前に、献立が変わっていないかを確かめる）</summary>
    public int CurrentTotal { get; init; }

    /// <summary>予算との差（予算 − 案の実績。0 ならちょうど）</summary>
    public int Difference => Budget - NewTotal;
}

/// <summary>入れ替え案の結果</summary>
public sealed class BudgetFitResult
{
    /// <summary>作る</summary>
    public BudgetFitResult(int currentTotal, int budget, IReadOnlyList<BudgetFitOption> options)
    {
        CurrentTotal = currentTotal;
        Budget = budget;
        Options = options;
    }

    /// <summary>今の週の実績</summary>
    public int CurrentTotal { get; }

    /// <summary>週の予算（目標額）</summary>
    public int Budget { get; }

    /// <summary>案（差の小さい順に最大3件。予算に届いていれば空）</summary>
    public IReadOnlyList<BudgetFitOption> Options { get; }

    /// <summary>予算に届かないか（「予算に合わせる」を出す条件）</summary>
    public bool IsUnderBudget => CurrentTotal < Budget;
}
