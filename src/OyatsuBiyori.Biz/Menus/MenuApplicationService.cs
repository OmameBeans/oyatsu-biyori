using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.Menus;

/// <summary>
/// 献立の一覧・作成・表示・手動の編集・確定（SC-10・SC-09。T3-01〜03、T4-02・03・06・07・10〜13）。
/// 金額・判定・検証はすべて Biz で計算し、画面には結果だけを返す。
/// 確定済みの献立は、直し始めたら下書きに戻す（U-16）。警告の商品の確認だけなら確定のまま（D-71）。
/// </summary>
public sealed class MenuApplicationService(
    IMenuRepository menuRepository,
    IProductRepository productRepository,
    ISettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    MenuContextFactory contextFactory,
    IUserContext userContext,
    TimeProvider timeProvider,
    IBackgroundJobService backgroundJobs)
{
    private readonly IBackgroundJobService _jobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly IProductRepository _products = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly MenuContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private const string NotFound = "献立が見つかりません。献立一覧から開き直してください";

    private const string Running = "献立を考えている途中です。終わってから直してください";

    /// <summary>献立案の作成・組み直しが動いているか（動いているあいだに手で直すと、どちらかの変更が消えるため止める）</summary>
    private bool IsProposalRunning(string menuId) => MenuProposalApplicationService.IsRunning(_jobs, menuId);

    /// <summary>画面から来た row_version を読む（形が違えば null）</summary>
    private static byte[]? ParseRowVersion(string? rowVersion)
    {
        if (string.IsNullOrEmpty(rowVersion))
        {
            return null;
        }

        var buffer = new byte[rowVersion.Length];
        return Convert.TryFromBase64String(rowVersion, buffer, out var written) ? buffer[..written] : null;
    }

    /// <summary>
    /// 献立一覧（SC-10。T3-01・T4-12）。今週・来週・再来週と、今週から最後の献立の週までの間で空いた週は、未作成でも行を出す。新しい週が上。
    /// 上部に対応が必要なこと（パッケージで確認する商品・未確定の献立・使えなくなった商品を使う献立）を返す。
    /// </summary>
    public async Task<Result<MenuListView>> ListAsync()
    {
        var menus = await _menus.GetAllAsync();
        var setting = await _settings.GetSettingAsync();
        var (context, products) = await _contextFactory.ForAllProductsAsync();
        var today = context.Today;
        var thisWeek = JapanDate.WeekStart(today);

        var rows = new List<MenuListRow>();
        foreach (var menu in menus)
        {
            rows.Add(ToRow(menu, context, today));
        }

        // 今週・来週・再来週と、今週から最後の献立の週までの間で空いた週は、未作成の行を出す
        // （先の週の下書きを削除して間が空いても、その週を開いて作れるように。D-88 で週を選ぶ入口は置かないため）
        var existingWeeks = rows.Select(r => r.WeekStart).ToHashSet();
        var lastWeek = existingWeeks.Append(thisWeek.AddDays(14)).Max();
        for (var weekStart = thisWeek; weekStart <= lastWeek; weekStart = weekStart.AddDays(7))
        {
            if (!existingWeeks.Contains(weekStart))
            {
                rows.Add(new MenuListRow(weekStart, null, null, null, null, null, 0, 0, 0, 0, null, false));
            }
        }

        foreach (var (row, index) in rows.Select((r, i) => (r, i)).ToList())
        {
            var label = row.WeekStart == thisWeek ? "今週" : row.WeekStart == thisWeek.AddDays(7) ? "来週" : row.WeekStart == thisWeek.AddDays(14) ? "再来週" : null;
            rows[index] = row with { RelativeLabel = label };
        }

        // 対応が必要なこと（03 §6.3・SC-10）
        var futureMenus = menus.Where(m => m.Days.Any(d => d.Date >= today)).ToList();
        var futureProductIds = futureMenus.SelectMany(m => m.Days.Where(d => d.Date >= today).SelectMany(d => d.Items)).Select(i => i.ProductId).ToHashSet(StringComparer.Ordinal);
        var needsPackage = products.Where(p => NeedsPackageCheck(context.Judge(p))).ToList();
        var futureCount = needsPackage.Count(p => futureProductIds.Contains(p.Id));
        var otherCount = needsPackage.Count(p => !futureProductIds.Contains(p.Id) && !p.IsDisabled);
        var impact = MenuImpactAnalyzer.Analyze(futureMenus, context);
        var blocked = impact.BlockedUsages
            .Select(u =>
            {
                var product = context.FindProduct(u.Item.ProductId);
                return new BlockedUsageView(u.MenuId, u.WeekStart, u.Date, u.ProductName ?? "（商品が見つかりません）",
                    product is null || u.Judgement is null ? null : AllergenJudgementView.From(product, u.Judgement, today));
            })
            .ToList();

        return Result<MenuListView>.Ok(new MenuListView(
            rows.OrderByDescending(r => r.WeekStart).ToList(),
            futureCount,
            otherCount,
            menus.Count(m => m.Status == MenuStatus.Draft),
            blocked,
            setting.DefaultChildrenCount is > 0 && setting.DefaultWeeklyBudget is > 0,
            thisWeek));
    }

    /// <summary>「パッケージで確認する商品」か（🟡 と、情報がないため 🔴 の商品。03 §6.3）</summary>
    public static bool NeedsPackageCheck(AllergenJudgement judgement) =>
        judgement.Category == AllergenJudgementCategory.Warning
        || (judgement.Category == AllergenJudgementCategory.Blocked && judgement.Kind == AllergenJudgementKind.Unknown);

    private static MenuListRow ToRow(Menu menu, MenuContext context, DateOnly today)
    {
        var week = MenuAmountCalculator.CalculateWeek(menu, context.PiecesPerPack);
        var items = menu.Days.SelectMany(d => d.Items).ToList();
        var blocked = 0;
        var warning = 0;
        var recheck = 0;
        foreach (var item in items)
        {
            var product = context.FindProduct(item.ProductId);
            if (product is null)
            {
                blocked++;
                continue;
            }

            var judgement = context.Judge(product);
            if (judgement.Category == AllergenJudgementCategory.Blocked)
            {
                blocked++;
            }
            else if (judgement.Category == AllergenJudgementCategory.Warning)
            {
                warning++;
                if (menu.Status == MenuStatus.Confirmed && !MenuConfirmationChecker.IsAcknowledged(item, judgement, context.Fingerprint(product)))
                {
                    recheck++;
                }
            }
        }

        return new MenuListRow(menu.WeekStart, menu.Id, menu.Status, week.Total, menu.WeeklyBudget, week.Remaining, blocked, warning, recheck, items.Count, null, week.IsOverBudget);
    }

    /// <summary>
    /// 献立の下書きを作る（T3-02。1週1件）。通常の人数か既定の週の予算が未設定なら作らずに設定へ案内する。
    /// その週の献立がもうあれば、それを返す（D-49）。
    /// </summary>
    public async Task<Result<string>> CreateAsync(DateOnly? date)
    {
        if (date is null)
        {
            return Result<string>.Error("週を選んでください");
        }

        var weekStart = JapanDate.WeekStart(date.Value);
        var existing = await _menus.GetByWeekStartAsync(weekStart);
        if (existing is not null)
        {
            return Result<string>.Ok(existing.Id);
        }

        var setting = await _settings.GetSettingAsync();
        if (setting.DefaultChildrenCount is not > 0 || setting.DefaultWeeklyBudget is not > 0)
        {
            return Result<string>.Error("献立を作る前に、設定の「条件設定」で通常の人数と既定の週の予算を入れてください");
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var menu = new Menu
        {
            WeekStart = weekStart,
            WeeklyBudget = setting.DefaultWeeklyBudget.Value,
            Status = MenuStatus.Draft,
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        };
        for (var offset = 0; offset < 7; offset++)
        {
            var day = weekStart.AddDays(offset);
            if (setting.ServingWeekdays.Contains(day.DayOfWeek))
            {
                menu.Days.Add(new MenuDay
                {
                    MenuId = menu.Id,
                    Date = day,
                    ChildrenCount = setting.DefaultChildrenCount.Value,
                    ChildrenCountSource = ChildrenCountSource.Default,
                    DayType = DayType.Normal,
                    UpdateDateTime = now,
                    UpdateUserId = user?.UserId,
                });
            }
        }

        _menus.Add(menu);
        await _unitOfWork.SaveChangesAsync();
        return Result<string>.Ok(menu.Id);
    }

    /// <summary>献立の中身（SC-09 の手順4・確定済みの閲覧）</summary>
    public async Task<Result<MenuDetailView>> GetAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuDetailView>.Error(NotFound);
        }

        return Result<MenuDetailView>.Ok(await BuildDetailAsync(menu));
    }

    /// <summary>手順2 今週の商品（掲載品の写しの評価。T4-02）</summary>
    public async Task<Result<ListedProductsView>> GetListedProductsAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<ListedProductsView>.Error(NotFound);
        }

        var context = await _contextFactory.ForMenuAsync(menu);
        var evaluations = MenuCandidateFilter.Evaluate(menu, context);
        var items = evaluations
            .Where(e => e.Product is not null)
            .Select(e => new ListedProductView(
                e.ProductId,
                e.Product!.Name,
                e.Product.Maker,
                e.Product.Volume,
                e.Product.SnackType,
                e.Product.Tags.Select(t => new TagView(t.Kind, t.Value)).ToList(),
                e.Listed?.Price,
                e.Product.PiecesPerPack,
                e.Product.DefaultPerChildCount,
                e.Listed?.OrderNo,
                AllergenJudgementView.From(e.Product, e.Judgement!, context.Today),
                DuplicateChecker.DaysSinceServed(e.Product, menu.WeekStart, menu.Id, context),
                menu.ExcludedProductIds.Contains(e.ProductId),
                e.Product.IsDisabled,
                e.Exclusions,
                e.IsCandidate))
            .OrderBy(v => v.SnackType)
            .ThenBy(v => v.Name, StringComparer.Ordinal)
            .ToList();

        return Result<ListedProductsView>.Ok(new ListedProductsView(
            items,
            items.Count(i => i.IsCandidate),
            items.Count(i => i.IsCandidate && i.Judgement.Category == AllergenJudgementCategory.Warning),
            items.Count(i => i.Judgement.Category == AllergenJudgementCategory.Blocked),
            items.Count(i => i.IsExcludedThisWeek),
            context.AvoidedAllergens.Count == 0));
    }

    /// <summary>「今週は使わない」を切り替える（切り替えるたびに保存。T4-02）</summary>
    public async Task<Result> SetExcludedAsync(string? menuId, string? productId, bool excluded)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null || string.IsNullOrEmpty(productId))
        {
            return Result.Error(NotFound);
        }

        if (!menu.ListedProducts.Any(l => l.ProductId == productId))
        {
            return Result.Error("その週の掲載品にない商品です。画面を読み込み直してください");
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result.Error(Running);
        }

        menu.ExcludedProductIds.RemoveAll(id => id == productId);
        if (excluded)
        {
            menu.ExcludedProductIds.Add(productId);
        }

        await TouchAsync(menu);
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>
    /// 手順2で税込価格・入数をその場で入れる（T4-02）。価格は写しと商品の両方に保存（商品は過去の週なら上書きしない。U-33・U-10）。
    /// </summary>
    public async Task<Result<string?>> SetListedValuesAsync(string? menuId, string? productId, int? price, int? piecesPerPack)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        var product = string.IsNullOrEmpty(productId) ? null : await _products.GetAsync(productId);
        if (menu is null || product is null)
        {
            return Result<string?>.Error(NotFound);
        }

        if (!menu.ListedProducts.Any(l => l.ProductId == product.Id))
        {
            return Result<string?>.Error("その週の掲載品にない商品です。画面を読み込み直してください");
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result<string?>.Error(Running);
        }

        string? note = null;
        if (price is not null)
        {
            var listed = ListedProductsUpdater.SetListedPrice(menu, product.Id, price.Value);
            if (listed.HasError)
            {
                return Result<string?>.Error(listed.ErrorMessages[0]);
            }

            if (!ListedProductsUpdater.ApplyManualPrice(product, menu.WeekStart, price.Value))
            {
                note = "この週の献立の価格だけを変えました（商品の価格は、新しい週の値のままにしています）";
            }
        }

        if (piecesPerPack is not null)
        {
            var validation = InputValidator.ValidatePiecesPerPack(piecesPerPack);
            if (validation.HasError)
            {
                return Result<string?>.Error(validation.ErrorMessages[0]);
            }

            product.PiecesPerPack = piecesPerPack;
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        product.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
        product.UpdateUserId = user?.UserId;
        await TouchAsync(menu);
        await _unitOfWork.SaveChangesAsync();
        return Result<string?>.Ok(note);
    }

    /// <summary>要望の文章を保存する（入力欄を離れたとき・手順を移るとき。T4-03）</summary>
    public async Task<Result> SaveRequestAsync(string? menuId, string? requestText)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result.Error(Running);
        }

        var text = string.IsNullOrWhiteSpace(requestText) ? null : requestText.Trim();
        if (text is { Length: > 2000 })
        {
            return Result.Error("要望の文章は2000文字までにしてください");
        }

        if (menu.RequestText != text)
        {
            menu.RequestText = text;
            await TouchAsync(menu);
            await _unitOfWork.SaveChangesAsync();
        }

        return new Result();
    }

    /// <summary>この献立の週の予算を上書きする（T4-06）</summary>
    public async Task<Result<MenuDetailView>> UpdateBudgetAsync(string? menuId, int? budget, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, _) =>
        {
            var validation = InputValidator.ValidateWeeklyBudget(budget);
            if (validation.HasError)
            {
                return Task.FromResult(validation);
            }

            menu.WeeklyBudget = budget!.Value;
            return Task.FromResult(new Result());
        });
    }

    /// <summary>
    /// 日の人数・日の種類・見出しを直す（「こう理解しました」とカードから。職員が直した印を付ける。U-19・T4-06・T4-07）。
    /// </summary>
    public async Task<Result<MenuDetailView>> UpdateDayAsync(string? menuId, DateOnly date, int? childrenCount, DayType? dayType, string? label, bool updateLabel, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, async (menu, context) =>
        {
            var day = menu.Days.FirstOrDefault(d => d.Date == date);
            if (day is null)
            {
                return Result.Error("その日はおやつを出す日ではありません。画面を読み込み直してください");
            }

            if (childrenCount is not null)
            {
                var validation = InputValidator.ValidateDayChildrenCount(childrenCount.Value);
                if (validation.HasError)
                {
                    return validation;
                }

                if (day.ChildrenCount != childrenCount.Value)
                {
                    day.ChildrenCount = childrenCount.Value;
                    day.ChildrenCountSource = ChildrenCountSource.EditedByStaff;
                    MenuItemFactory.RecalculatePackCounts(day, context.PiecesPerPack);
                }
            }

            if (dayType is not null)
            {
                if (!Enum.IsDefined(dayType.Value))
                {
                    return Result.Error("日の種類を選び直してください");
                }

                if (day.DayType != dayType.Value)
                {
                    day.DayType = dayType.Value;
                    day.DayTypeEdited = true;
                    MenuItemFactory.RefreshAmounts(day);
                }
            }

            if (updateLabel)
            {
                var newLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
                if (newLabel is { Length: > 40 })
                {
                    return Result.Error("見出しは40文字までにしてください");
                }

                day.Label = newLabel;
                day.LabelEdited = true;
            }

            day.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
            return await Task.FromResult(new Result());
        });
    }

    /// <summary>
    /// 日に商品を足す（掲載品だけ。T1-12 で作る）。入れられない商品を入れたら、理由を検証で示す（確定は不可。FR-MENU-14）。
    /// </summary>
    public async Task<Result<MenuDetailView>> AddItemAsync(string? menuId, DateOnly date, string? productId, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, context) =>
        {
            var day = menu.Days.FirstOrDefault(d => d.Date == date);
            var product = productId is null ? null : context.FindProduct(productId);
            if (day is null || product is null)
            {
                return Task.FromResult(Result.Error("日か商品が見つかりません。画面を読み込み直してください"));
            }

            if (day.Items.Any(i => i.ProductId == product.Id))
            {
                return Task.FromResult(Result.Error($"「{product.Name}」はこの日にもう入っています。パック数を変えてください"));
            }

            var created = MenuItemFactory.Create(day, product, MenuCandidateFilter.EvaluateProduct(menu, product.Id, context).Listed);
            if (created.HasError)
            {
                return Task.FromResult(Result.Error(created.ErrorMessages[0]));
            }

            created.Value!.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
            day.Items.Add(created.Value);
            return Task.FromResult(new Result());
        });
    }

    /// <summary>商品を入れ替える（確定時の確認は消える。SR-22）</summary>
    public async Task<Result<MenuDetailView>> ReplaceItemAsync(string? menuId, string? itemId, string? productId, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, context) =>
        {
            var (day, item) = FindItem(menu, itemId);
            var product = productId is null ? null : context.FindProduct(productId);
            if (day is null || item is null || product is null)
            {
                return Task.FromResult(Result.Error("商品が見つかりません。画面を読み込み直してください"));
            }

            if (day.Items.Any(i => i.Id != item.Id && i.ProductId == product.Id))
            {
                return Task.FromResult(Result.Error($"「{product.Name}」はこの日にもう入っています"));
            }

            return Task.FromResult(MenuItemFactory.ReplaceProduct(item, day, product, MenuCandidateFilter.EvaluateProduct(menu, product.Id, context).Listed));
        });
    }

    /// <summary>商品を外す</summary>
    public async Task<Result<MenuDetailView>> RemoveItemAsync(string? menuId, string? itemId, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, _) =>
        {
            var (day, item) = FindItem(menu, itemId);
            if (day is null || item is null)
            {
                return Task.FromResult(Result.Error("商品が見つかりません。画面を読み込み直してください"));
            }

            day.Items.Remove(item);
            _menus.RemoveItem(item);
            return Task.FromResult(new Result());
        });
    }

    /// <summary>
    /// 商品を別の日へ動かす（献立案の画面のドラッグ＆ドロップ。FR-MENU-24）。価格は献立に入れた時点のまま、1人あたりの個数も保ち、
    /// パック数は動かした先の日の人数が違えば求め直す（同じなら上書きした値も残す）。日が変わると重複などの判定も変わるので、確定時の確認は消す（確認し直し。SR-22）。
    /// </summary>
    public async Task<Result<MenuDetailView>> MoveItemAsync(string? menuId, string? itemId, DateOnly date, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, context) =>
        {
            var (from, item) = FindItem(menu, itemId);
            var to = menu.Days.FirstOrDefault(d => d.Date == date);
            if (from is null || item is null || to is null)
            {
                return Task.FromResult(Result.Error("日か商品が見つかりません。画面を読み込み直してください"));
            }

            if (from.Id == to.Id)
            {
                return Task.FromResult(Result.Error("同じ日には動かせません。別の日に動かしてください"));
            }

            if (to.Items.Any(i => i.ProductId == item.ProductId))
            {
                var name = context.FindProduct(item.ProductId)?.Name ?? "この商品";
                return Task.FromResult(Result.Error($"「{name}」は動かす先の日にもう入っています。パック数を変えてください"));
            }

            // 人数が同じ日へ動かすときは、パック数（職員や入れ替え案が上書きした値も）をそのまま残す。人数が違えば式で求め直す（U-04）
            var isSameChildren = from.ChildrenCount == to.ChildrenCount;
            // 先に行き先の日へ付け替えてから元の日の一覧から外す（途中で変更の検出が走っても、
            // 「どの日にも属さない商品」として消されないように）
            item.MenuDayId = to.Id;
            item.MenuDay = to;
            to.Items.Add(item);
            from.Items.Remove(item);
            if (!isSameChildren)
            {
                MenuItemFactory.RecalculatePackCount(item, to, context.PiecesPerPack(item.ProductId));
            }

            item.Amount = MenuAmountCalculator.StoredItemAmount(item, to);
            MenuItemFactory.ClearAcknowledgement(item);
            item.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
            return Task.FromResult(new Result());
        });
    }

    /// <summary>1人あたりの個数（パック数を式で求め直す）かパック数（上書き）を変える（U-04）</summary>
    public async Task<Result<MenuDetailView>> UpdateItemAsync(string? menuId, string? itemId, int? perChildCount, int? packCount, string? rowVersion)
    {
        return await EditAsync(menuId, rowVersion, (menu, context) =>
        {
            var (day, item) = FindItem(menu, itemId);
            if (day is null || item is null)
            {
                return Task.FromResult(Result.Error("商品が見つかりません。画面を読み込み直してください"));
            }

            if (perChildCount is not null)
            {
                var product = context.FindProduct(item.ProductId);
                if (product is null)
                {
                    return Task.FromResult(Result.Error("商品が見つかりません。画面を読み込み直してください"));
                }

                var result = MenuItemFactory.SetPerChildCount(item, day, product, perChildCount.Value);
                if (result.HasError)
                {
                    return Task.FromResult(result);
                }
            }

            if (packCount is not null)
            {
                var result = MenuItemFactory.SetPackCount(item, day, packCount.Value);
                if (result.HasError)
                {
                    return Task.FromResult(result);
                }
            }

            return Task.FromResult(new Result());
        });
    }

    /// <summary>確定のダイアログの中身（T4-11）。判定は呼ぶたびに求め直す</summary>
    public async Task<Result<MenuConfirmationView>> GetConfirmationAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuConfirmationView>.Error(NotFound);
        }

        var context = await _contextFactory.ForMenuAsync(menu);
        return Result<MenuConfirmationView>.Ok(ToConfirmationView(MenuConfirmationChecker.Check(menu, context), context));
    }

    /// <summary>
    /// 確定する（T4-11。FR-MENU-32・SR-04・SR-21・SR-22）。画面でチェックした警告の商品を、今の判定のまま確認したとして記録し、
    /// そのあとで確定できるかを判定し直す。止めるエラーがあれば確定しない。
    /// </summary>
    public async Task<Result<MenuDetailView>> ConfirmAsync(string? menuId, IReadOnlyList<AcknowledgementInput> acknowledgements, string? rowVersion)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuDetailView>.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result<MenuDetailView>.Error(Running);
        }

        if (!string.IsNullOrEmpty(rowVersion))
        {
            var expected = ParseRowVersion(rowVersion);
            if (expected is null)
            {
                return Result<MenuDetailView>.Error("画面の情報が古くなりました。読み込み直してください");
            }

            _menus.SetExpectedRowVersion(menu, expected);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result<MenuDetailView>.Error("ログインが必要です。ログインし直してください");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var context = await _contextFactory.ForMenuAsync(menu);
        var before = MenuConfirmationChecker.Check(menu, context);
        foreach (var status in before.Acknowledgements.Where(a => !a.IsAcknowledged))
        {
            var seen = acknowledgements.FirstOrDefault(a => a.MenuItemId == status.Item.Id);
            if (seen is null)
            {
                continue;
            }

            // 画面で読んだ判定と今の判定が違えば、確認済みにしない（読んでいない理由で確認したことにしない。SR-22）
            if (seen.Kind != status.Judgement.Kind || !string.Equals(seen.Fingerprint, status.Fingerprint, StringComparison.Ordinal))
            {
                return Result<MenuDetailView>.Error($"「{status.Product.Name}」（{MenuIssueText.DateLabel(status.Date)}）のアレルゲンの判定が変わりました。確定のダイアログを開き直して、確かめ直してください");
            }

            var acknowledged = MenuConfirmationChecker.Acknowledge(status.Item, context, now);
            if (acknowledged.HasError)
            {
                return Result<MenuDetailView>.Error("アレルゲンの判定が変わりました。確定のダイアログを開き直して、確かめ直してください");
            }
        }

        var check = MenuConfirmationChecker.Check(menu, context);
        if (!check.CanConfirm)
        {
            var error = new Result<MenuDetailView>();
            if (check.BlockingIssues.Count > 0)
            {
                // 理由（アレルゲンの文言）は、ダイアログを開き直したときに画面の共通部品で示す
                error.ErrorMessages.Add($"確定できない商品が{check.BlockingIssues.Count}件あります。確定のダイアログを開き直して、理由を確かめてください");
            }

            if (check.PendingAcknowledgements.Count > 0)
            {
                error.ErrorMessages.Add($"確認のチェックが付いていない商品が{check.PendingAcknowledgements.Count}件あります。1件ずつ確かめてチェックを付けてください");
            }

            return error;
        }

        menu.Status = MenuStatus.Confirmed;
        menu.ConfirmedAt = now;
        menu.ConfirmedBy = user.UserName;
        menu.UpdateDateTime = now;
        menu.UpdateUserId = user.UserId;
        await _unitOfWork.SaveChangesAsync();
        await _menus.RefreshRowVersionAsync(menu);
        return Result<MenuDetailView>.Ok(await BuildDetailAsync(menu));
    }

    /// <summary>
    /// 警告の商品を確認し直す（確定済みのまま。「確認し直し」のバッジを消す。U-34・D-71・T4-13）。
    /// </summary>
    public async Task<Result<MenuDetailView>> AcknowledgeAsync(string? menuId, AcknowledgementInput? input)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuDetailView>.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result<MenuDetailView>.Error(Running);
        }

        var (_, item) = FindItem(menu, input?.MenuItemId);
        if (item is null || input is null)
        {
            return Result<MenuDetailView>.Error("商品が見つかりません。画面を読み込み直してください");
        }

        var context = await _contextFactory.ForMenuAsync(menu);
        var product = context.FindProduct(item.ProductId);
        if (product is null || context.Judge(product).Kind != input.Kind || !string.Equals(context.Fingerprint(product), input.Fingerprint, StringComparison.Ordinal))
        {
            return Result<MenuDetailView>.Error("アレルゲンの判定が変わりました。画面を読み込み直して、確かめ直してください");
        }

        var acknowledged = MenuConfirmationChecker.Acknowledge(item, context, _timeProvider.GetUtcNow().UtcDateTime);
        if (acknowledged.HasError)
        {
            return Result<MenuDetailView>.Error(acknowledged.ErrorMessages[0]);
        }

        // 確認だけなので、献立は確定のまま（下書きに戻さない）。row_version は進める
        menu.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
        await _unitOfWork.SaveChangesAsync();
        await _menus.RefreshRowVersionAsync(menu);
        return Result<MenuDetailView>.Ok(await BuildDetailAsync(menu));
    }

    /// <summary>下書きを削除する（確定済みは削除できない。一度確定してから下書きに戻ったものは削除できる。U-54）</summary>
    public async Task<Result> DeleteAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result.Error(NotFound);
        }

        if (menu.Status == MenuStatus.Confirmed)
        {
            return Result.Error("確定済みの献立は削除できません");
        }

        _menus.Remove(menu);
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>
    /// 手動の編集の共通の手順：row_version を確かめ、確定済みなら下書きに戻し（U-16）、変えたあと献立の行を必ず更新して
    /// row_version を進める（日・商品だけの変更でも。D-46）。
    /// </summary>
    private async Task<Result<MenuDetailView>> EditAsync(string? menuId, string? rowVersion, Func<Menu, MenuContext, Task<Result>> edit)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<MenuDetailView>.Error(NotFound);
        }

        if (IsProposalRunning(menu.Id))
        {
            return Result<MenuDetailView>.Error(Running);
        }

        if (!string.IsNullOrEmpty(rowVersion))
        {
            var expected = ParseRowVersion(rowVersion);
            if (expected is null)
            {
                return Result<MenuDetailView>.Error("画面の情報が古くなりました。読み込み直してください");
            }

            _menus.SetExpectedRowVersion(menu, expected);
        }

        var context = await _contextFactory.ForMenuAsync(menu);
        var result = await edit(menu, context);
        if (result.HasError)
        {
            var error = new Result<MenuDetailView>();
            error.ErrorMessages.AddRange(result.ErrorMessages);
            return error;
        }

        if (menu.Status == MenuStatus.Confirmed)
        {
            menu.Status = MenuStatus.Draft;
        }

        await TouchAsync(menu);
        await _unitOfWork.SaveChangesAsync();
        await _menus.RefreshRowVersionAsync(menu);
        return Result<MenuDetailView>.Ok(await BuildDetailAsync(menu));
    }

    private async Task TouchAsync(Menu menu)
    {
        var user = await _userContext.GetCurrentUserInfoAsync();
        menu.UpdateDateTime = _timeProvider.GetUtcNow().UtcDateTime;
        menu.UpdateUserId = user?.UserId;
    }

    private static (MenuDay? Day, MenuItem? Item) FindItem(Menu menu, string? itemId)
    {
        foreach (var day in menu.Days)
        {
            var item = day.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is not null)
            {
                return (day, item);
            }
        }

        return (null, null);
    }

    /// <summary>献立の中身を組み立てる（金額・判定・検証は Biz で計算する）</summary>
    public async Task<MenuDetailView> BuildDetailAsync(Menu menu)
    {
        var context = await _contextFactory.ForMenuAsync(menu);
        var validation = MenuValidator.Validate(menu, context);
        var setting = await _settings.GetSettingAsync();
        var messages = await _menus.GetMessagesAsync(menu.Id);

        var days = new List<MenuDayView>();
        foreach (var day in menu.Days.OrderBy(d => d.Date))
        {
            var amount = validation.Week.Days.First(d => d.Date == day.Date);
            var items = day.Items
                .OrderBy(i => context.FindProduct(i.ProductId)?.SnackType ?? SnackType.NotSnack)
                .ThenBy(i => i.Id, StringComparer.Ordinal)
                .Select(item =>
                {
                    var product = context.FindProduct(item.ProductId);
                    var judgement = product is null ? null : context.Judge(product);
                    var needsRecheck = product is not null && judgement!.Category == AllergenJudgementCategory.Warning
                        && item.AcknowledgedAt is not null
                        && !MenuConfirmationChecker.IsAcknowledged(item, judgement, context.Fingerprint(product));
                    return new MenuItemView(
                        item.Id,
                        item.ProductId,
                        product?.Name ?? "（商品が見つかりません）",
                        product?.SnackType,
                        product?.Tags.Select(t => new TagView(t.Kind, t.Value)).ToList() ?? [],
                        item.PerChildCount,
                        item.PackCount,
                        item.UnitPrice,
                        item.Amount,
                        item.OrderNo,
                        product is null ? null : AllergenJudgementView.From(product, judgement!, context.Today),
                        product is not null && judgement!.Category == AllergenJudgementCategory.Warning && MenuConfirmationChecker.IsAcknowledged(item, judgement, context.Fingerprint(product)),
                        needsRecheck,
                        product is null ? null : context.Fingerprint(product));
                })
                .ToList();
            days.Add(new MenuDayView(
                day.Id,
                day.Date,
                day.ChildrenCount,
                day.ChildrenCountSource,
                day.DayType,
                day.DayTypeEdited,
                day.Label,
                day.LabelEdited,
                day.Reason,
                amount.Amount,
                amount.PerChildAmount is { } perChild ? MenuAmountCalculator.RoundForDisplay(perChild) : null,
                amount.ExcludedFromAmount,
                items));
        }

        var week = validation.Week;
        return new MenuDetailView(
            menu.Id,
            menu.WeekStart,
            menu.Status,
            menu.RowVersion is null ? string.Empty : Convert.ToBase64String(menu.RowVersion),
            menu.WeeklyBudget,
            menu.RequestText,
            menu.InterpretedRequests,
            menu.ConfirmedAt,
            menu.ConfirmedBy,
            menu.ListedProducts.Count,
            menu.ExcludedProductIds,
            setting.DefaultChildrenCount,
            context.AvoidedAllergens.Select(a => a.Name).ToList(),
            setting.DuplicateAvoidDays,
            days,
            new WeekAmountView(week.Total, week.Budget, week.Remaining, week.IsOverBudget, week.IsUnderBudget),
            validation.Issues.Select(i => new MenuIssueView(i.Severity, i.Kind, i.Date, i.MenuItemId, i.ProductId, i.ProductName, i.Message, i.BlocksConfirmation)).ToList(),
            messages.Select(m => new MenuMessageView(m.Role, m.Content, m.CreatedAt)).ToList());
    }

    /// <summary>
    /// 確定を止める問題の表示。アレルゲンの問題は判定の材料を渡し、文言は画面の共通部品で作る（T4-01）
    /// </summary>
    private static BlockingIssueView ToBlockingView(MenuIssue issue, MenuContext context)
    {
        var product = issue.ProductId is null ? null : context.FindProduct(issue.ProductId);
        var judgement = issue.Kind == MenuIssueKind.AllergenBlocked && product is not null
            ? AllergenJudgementView.From(product, context.Judge(product), context.Today)
            : null;
        return new BlockingIssueView(issue.Date, issue.ProductName, judgement is null ? issue.Message : null, judgement);
    }

    private static MenuConfirmationView ToConfirmationView(MenuConfirmationResult result, MenuContext context)
    {
        var manufacturer = context.AvoidedAllergens.Where(a => a.LabelNotGuaranteed).Select(a => a.Name).ToList();
        return new MenuConfirmationView(
            result.CanConfirm,
            result.BlockingIssues.Select(i => ToBlockingView(i, context)).ToList(),
            result.Acknowledgements.Select(a => new AcknowledgementView(
                a.Item.Id,
                a.Date,
                a.Product.Name,
                AllergenJudgementView.From(a.Product, a.Judgement, context.Today),
                a.IsAcknowledged,
                a.Fingerprint)).ToList(),
            result.DisplayWarnings.Select(i => i.ToLine()).ToList(),
            manufacturer);
    }
}

/// <summary>献立一覧</summary>
public sealed record MenuListView(
    IReadOnlyList<MenuListRow> Rows,
    int FuturePackageCheckCount,
    int OtherPackageCheckCount,
    int DraftCount,
    IReadOnlyList<BlockedUsageView> BlockedUsages,
    bool IsReadyForMenu,
    DateOnly ThisWeekStart);

/// <summary>献立一覧の1週</summary>
public sealed record MenuListRow(
    DateOnly WeekStart,
    string? MenuId,
    MenuStatus? Status,
    int? Total,
    int? Budget,
    int? Remaining,
    int BlockedCount,
    int WarningCount,
    int RecheckCount,
    int ItemCount,
    string? RelativeLabel,
    bool IsOverBudget);

/// <summary>使えなくなった商品を使う献立の日</summary>
public sealed record BlockedUsageView(string MenuId, DateOnly WeekStart, DateOnly Date, string ProductName, AllergenJudgementView? Judgement);

/// <summary>タグ</summary>
public sealed record TagView(ProductTagKind Kind, string Value);

/// <summary>手順2 今週の商品</summary>
public sealed record ListedProductsView(
    IReadOnlyList<ListedProductView> Items,
    int CandidateCount,
    int CandidateWarningCount,
    int BlockedCount,
    int ExcludedCount,
    bool IsAvoidedEmpty);

/// <summary>今週の商品の1件</summary>
public sealed record ListedProductView(
    string ProductId,
    string Name,
    string? Maker,
    string? Volume,
    SnackType SnackType,
    IReadOnlyList<TagView> Tags,
    int? Price,
    int? PiecesPerPack,
    int PerChildCount,
    string? OrderNo,
    AllergenJudgementView Judgement,
    int? DaysSinceServed,
    bool IsExcludedThisWeek,
    bool IsDisabled,
    IReadOnlyList<CandidateExclusion> Exclusions,
    bool IsCandidate);

/// <summary>献立の中身</summary>
public sealed record MenuDetailView(
    string Id,
    DateOnly WeekStart,
    MenuStatus Status,
    string RowVersion,
    int WeeklyBudget,
    string? RequestText,
    InterpretedRequests? InterpretedRequests,
    DateTime? ConfirmedAt,
    string? ConfirmedBy,
    int ListedCount,
    IReadOnlyList<string> ExcludedProductIds,
    int? DefaultChildrenCount,
    IReadOnlyList<string> AvoidedAllergenNames,
    IReadOnlyList<DuplicateAvoidRule> DuplicateRules,
    IReadOnlyList<MenuDayView> Days,
    WeekAmountView Week,
    IReadOnlyList<MenuIssueView> Issues,
    IReadOnlyList<MenuMessageView> Messages);

/// <summary>日ごとの献立</summary>
public sealed record MenuDayView(
    string Id,
    DateOnly Date,
    int ChildrenCount,
    ChildrenCountSource ChildrenCountSource,
    DayType DayType,
    bool DayTypeEdited,
    string? Label,
    bool LabelEdited,
    string? Reason,
    int Amount,
    decimal? PerChildAmount,
    bool ExcludedFromAmount,
    IReadOnlyList<MenuItemView> Items);

/// <summary>日ごとの商品</summary>
public sealed record MenuItemView(
    string Id,
    string ProductId,
    string ProductName,
    SnackType? SnackType,
    IReadOnlyList<TagView> Tags,
    int PerChildCount,
    int PackCount,
    int UnitPrice,
    int Amount,
    string? OrderNo,
    AllergenJudgementView? Judgement,
    bool IsAcknowledged,
    bool NeedsRecheck,
    string? JudgementFingerprint);

/// <summary>週の金額</summary>
public sealed record WeekAmountView(int Total, int Budget, int Remaining, bool IsOverBudget, bool IsUnderBudget);

/// <summary>献立の問題</summary>
public sealed record MenuIssueView(
    MenuIssueSeverity Severity,
    MenuIssueKind Kind,
    DateOnly? Date,
    string? MenuItemId,
    string? ProductId,
    string? ProductName,
    string Message,
    bool BlocksConfirmation);

/// <summary>チャットの発言</summary>
public sealed record MenuMessageView(MenuMessageRole Role, string Content, DateTime CreatedAt);

/// <summary>確定のダイアログ</summary>
/// <param name="CanConfirm">今の状態で確定できるか（チェックを付ければ確定できるかは Acknowledgements で分かる）</param>
/// <param name="BlockingIssues">確定を止めるエラー</param>
/// <param name="Acknowledgements">確認が要る警告の商品</param>
/// <param name="DisplayWarnings">表示だけの警告</param>
/// <param name="ManufacturerCheckAllergens">避ける品目のうち表示が確実でないもの（末尾に「メーカーへの確認が必要」の1行）</param>
public sealed record MenuConfirmationView(
    bool CanConfirm,
    IReadOnlyList<BlockingIssueView> BlockingIssues,
    IReadOnlyList<AcknowledgementView> Acknowledgements,
    IReadOnlyList<string> DisplayWarnings,
    IReadOnlyList<string> ManufacturerCheckAllergens);

/// <summary>確認が要る警告の商品</summary>
public sealed record AcknowledgementView(string MenuItemId, DateOnly Date, string ProductName, AllergenJudgementView Judgement, bool IsAcknowledged, string Fingerprint);

/// <summary>確定を止める問題（アレルゲンなら判定の材料、それ以外なら文言）</summary>
public sealed record BlockingIssueView(DateOnly? Date, string? ProductName, string? Message, AllergenJudgementView? Judgement);

/// <summary>職員が確かめた警告の商品（画面で読んだ判定の種類と中身）</summary>
public sealed record AcknowledgementInput(string? MenuItemId, AllergenJudgementKind Kind, string? Fingerprint);
