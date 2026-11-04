using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// チラシの取り込み（SC-09 手順1。T3-04〜T3-09）。
/// 読み取り（Claude）→ アプリの検証（T1-15）→ 読み取りの検証（Claude）→ 画像を破棄 → チラシの品目（T1-08）→ 照合（T1-04・Haiku）
/// → 結果をメモリに24時間。職員が商品ごとに確定し、確定のたびに商品・「参考」の記録・掲載品の写しに反映する。
/// 「確認済み」にはしない（参考の記録だけを作る。SR-03）。
/// </summary>
public sealed class FlyerImportApplicationService(
    IMenuRepository menuRepository,
    IProductRepository productRepository,
    ISettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    IFlyerSessionStore sessionStore,
    IBackgroundJobService backgroundJobs,
    IFlyerReader flyerReader,
    IFlyerReviewer flyerReviewer,
    IProductMatchJudge productMatchJudge,
    IUserContext userContext,
    TimeProvider timeProvider,
    ProductMatchOptions matchOptions,
    FlyerImportOptions importOptions)
{
    /// <summary>バックグラウンドの処理の種類</summary>
    public const string JobKind = "flyer-reading";

    /// <summary>1回に取り込める画像の数（FR-FLYER-01）</summary>
    public const int MaxImages = 10;

    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly IProductRepository _products = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IFlyerSessionStore _sessions = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly IBackgroundJobService _jobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
    private readonly IFlyerReader _reader = flyerReader ?? throw new ArgumentNullException(nameof(flyerReader));
    private readonly IFlyerReviewer _reviewer = flyerReviewer ?? throw new ArgumentNullException(nameof(flyerReviewer));
    private readonly IProductMatchJudge _judge = productMatchJudge ?? throw new ArgumentNullException(nameof(productMatchJudge));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ProductMatchOptions _matchOptions = matchOptions ?? throw new ArgumentNullException(nameof(matchOptions));
    private readonly FlyerImportOptions _importOptions = importOptions ?? throw new ArgumentNullException(nameof(importOptions));

    /// <summary>
    /// 読み取りを受け付ける（バックグラウンド）。画面を閉じても続き、戻ったら結果を出す（FR-FLYER-10）。
    /// </summary>
    public async Task<Result<string>> StartReadingAsync(string? menuId, IReadOnlyList<ClaudeImage> images)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<string>.Error("献立が見つかりません。献立一覧から開き直してください");
        }

        if (images.Count == 0)
        {
            return Result<string>.Error("チラシの画像を選んでください");
        }

        if (images.Count > MaxImages)
        {
            return Result<string>.Error($"1回に読み取れるのは{MaxImages}枚までです。いらない画像を外してください");
        }

        var running = _jobs.FindLatest(JobKind, menu.Id);
        if (running is { State: BackgroundJobState.Queued or BackgroundJobState.Running })
        {
            return Result<string>.Ok(running.Id);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var menuKey = menu.Id;
        // 画像はこの入れ物にだけ持ち、読み取りの検証が終わったら空にする（NFR-SEC-06・FR-FLYER-08）
        var holder = images.ToList();
        var jobId = _jobs.Enqueue(JobKind, menuKey, user, _importOptions.JobDeadline, async (services, progress, cancellationToken) =>
        {
            var service = (FlyerImportApplicationService)services.GetService(typeof(FlyerImportApplicationService))!;
            var result = await service.RunReadingAsync(menuKey, holder, progress, cancellationToken);
            var outcome = new Result<object?>();
            outcome.ErrorMessages.AddRange(result.ErrorMessages);
            outcome.Value = result.Value;
            return outcome;
        });
        return Result<string>.Ok(jobId);
    }

    /// <summary>読み取りの処理の本体（バックグラウンドの中で呼ぶ）</summary>
    public async Task<Result<string>> RunReadingAsync(string menuId, List<ClaudeImage> images, IJobProgress progress, CancellationToken cancellationToken)
    {
        FlyerImportSession session;
        try
        {
            var menu = await _menus.GetAsync(menuId);
            if (menu is null)
            {
                return Result<string>.Error("献立が見つかりません。献立一覧から開き直してください");
            }

            var allergens = await _settings.GetAllergensAsync();
            progress.Report("チラシを読み取っています");
            var reading = await _reader.ReadAsync(images, allergens, cancellationToken);
            if (reading.HasError || reading.Value is null)
            {
                var error = new Result<string>();
                error.ErrorMessages.AddRange(reading.ErrorMessages);
                error.ErrorMessages.Add("読み取れない場合は、商品を手で入力することもできます。");
                return error;
            }

            progress.Report("読み取った内容を確かめています");
            session = new FlyerImportSession
            {
                MenuId = menu.Id,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                IsReimport = menu.ListedProducts.Count > 0,
            };
            session.Items.AddRange(reading.Value.Items.Select(read => BuildItem(read, allergens, isManual: false)));
            AddAppIssues(session.Items);

            // 読み取りの検証は、画像を破棄する前に同じ処理の中で行う。失敗しても読み取り結果は出す
            var review = await _reviewer.ReviewAsync(images, reading.Value, cancellationToken);
            if (review.HasError || review.Value is null)
            {
                session.ReviewFailed = true;
            }
            else
            {
                ApplyReview(session, review.Value, allergens);
            }
        }
        finally
        {
            // 読み取りと読み取りの検証が終わったら、すぐに画像を破棄する（保存しない。FR-FLYER-08）
            images.Clear();
        }

        progress.Report("登録済みの商品と比べています");
        var products = await _products.GetAllAsync();
        var calls = 0;
        foreach (var item in session.Items)
        {
            calls += await MatchAsync(item, products, _importOptions.ProductMatchCallLimit - calls, cancellationToken);
        }

        // 読み取りの前に手で入力していた商品（未確定）は、新しい読み取り結果に引き継ぐ（黙って消さない）
        var previousSession = _sessions.Get(menuId);
        if (previousSession is not null)
        {
            lock (previousSession.SyncRoot)
            {
                session.Items.AddRange(previousSession.Items.Where(i => i.IsManual && i.Confirmation is null));
            }

            AddAppIssues(session.Items);
        }

        _sessions.Set(session);
        return Result<string>.Ok(session.Id);
    }

    /// <summary>
    /// 手入力の商品を足す（読み取りの一覧からも、読み取り結果がないときからも。チラシと同じ扱い・同じ照合。D-73・U-36）。
    /// </summary>
    public async Task<Result<string>> AddManualAsync(string? menuId, FlyerManualInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<string>.Error("献立が見つかりません。献立一覧から開き直してください");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Result<string>.Error("商品名を入れてください");
        }

        if (_jobs.FindLatest(JobKind, menu.Id) is { State: BackgroundJobState.Queued or BackgroundJobState.Running })
        {
            return Result<string>.Error("チラシを読み取っている途中です。終わってから手で入力してください");
        }

        var allergens = await _settings.GetAllergensAsync();
        var session = _sessions.Get(menu.Id);
        if (session is null)
        {
            session = new FlyerImportSession { MenuId = menu.Id, CreatedAt = _timeProvider.GetUtcNow().UtcDateTime, IsReimport = menu.ListedProducts.Count > 0 };
            _sessions.Set(session);
        }

        var read = new FlyerReadProduct(
            input.Name.Trim(),
            Blank(input.Maker),
            Blank(input.Volume),
            input.PriceExcludingTax,
            input.PriceIncludingTax,
            Blank(input.OrderNo),
            null,
            Blank(input.AllergenLabelText),
            string.IsNullOrWhiteSpace(input.AllergenLabelText),
            [],
            [],
            null,
            null,
            input.PiecesPerPack,
            input.PiecesPerPack is null ? null : "手で入力しました",
            input.SnackType,
            [],
            []);
        var item = BuildItem(read, allergens, isManual: true);
        var products = await _products.GetAllAsync();
        await MatchAsync(item, products, _importOptions.ProductMatchCallLimit, cancellationToken);
        lock (session.SyncRoot)
        {
            session.Items.Add(item);
            AddAppIssues(session.Items);
        }

        return Result<string>.Ok(item.Id);
    }

    /// <summary>取り込みの状態（読み取りの処理と、確定前の結果）</summary>
    public async Task<Result<FlyerImportView>> GetLatestAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        if (menu is null)
        {
            return Result<FlyerImportView>.Error("献立が見つかりません。献立一覧から開き直してください");
        }

        var job = _jobs.FindLatest(JobKind, menu.Id);
        var session = _sessions.Get(menu.Id);
        if (session is null)
        {
            return Result<FlyerImportView>.Ok(new FlyerImportView(job, null));
        }

        var allergens = await _settings.GetAllergensAsync();
        var avoidedIds = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToList();
        List<string> productIds;
        lock (session.SyncRoot)
        {
            productIds = session.Items
                .SelectMany(i => i.Match.SameProducts.Concat(i.Match.VolumeVariants).Concat(i.Match.Candidates).Select(p => p.ProductId)
                    .Append(i.Confirmation?.ProductId ?? string.Empty))
                .Where(id => id.Length > 0)
                .ToList();
        }
        var products = (await _products.GetManyAsync(productIds)).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var allergenNames = allergens.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);

        List<FlyerItemView> views;
        lock (session.SyncRoot)
        {
            views = session.Items.Select(item => ToView(item, products, allergens, avoidedIds, allergenNames)).ToList();
        }

        return Result<FlyerImportView>.Ok(new FlyerImportView(job, new FlyerSessionView(
            session.Id,
            session.IsReimport,
            session.ReviewFailed,
            session.MissedProducts,
            views,
            views.Count(v => v.Confirmation is null))));
    }

    /// <summary>
    /// 1商品の取り込みを確定する（T3-08）。選び直しもこれで行う（確定のたびに写しへ追記し、選び直したら入れ替える。D-53）。
    /// </summary>
    public async Task<Result> ConfirmItemAsync(FlyerConfirmInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var menu = string.IsNullOrEmpty(input.MenuId) ? null : await _menus.GetAsync(input.MenuId);
        var session = menu is null ? null : _sessions.Get(menu.Id);
        FlyerImportItem? item;
        lock (session?.SyncRoot ?? new object())
        {
            item = session?.Items.FirstOrDefault(i => i.Id == input.ItemId);
        }

        if (menu is null || session is null || item is null)
        {
            return Result.Error("読み取り結果が見つかりません（24時間たったか、サーバーが再起動しました）。チラシを読み取り直してください");
        }

        var result = await ConfirmCoreAsync(menu, session, item, input);
        if (result.HasError)
        {
            return Result.Error(result.ErrorMessages[0]);
        }

        // 保存できてから、取り込みの状態を「確定済み」にする（保存に失敗したら、確定していない扱いのまま）
        await _unitOfWork.SaveChangesAsync();
        result.Value!();
        return new Result();
    }

    /// <summary>
    /// 「同じ商品」（完全一致で候補がちょうど1件）の商品をまとめて確定する（FR-FLYER-06a）。
    /// 対応づけできていない表記の語がある商品・選び直しが要る商品は飛ばす。
    /// </summary>
    public async Task<Result<int>> ConfirmAllSameAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        var session = menu is null ? null : _sessions.Get(menu.Id);
        if (menu is null || session is null)
        {
            return Result<int>.Error("読み取り結果が見つかりません（24時間たったか、サーバーが再起動しました）。チラシを読み取り直してください");
        }

        List<FlyerImportItem> targets;
        lock (session.SyncRoot)
        {
            targets = session.Items
                .Where(i => i.Confirmation is null && i.Match.CanBulkConfirmAsSame && i.UnmappedTerms.Count == 0)
                .ToList();
        }
        var count = 0;
        var allergens = await _settings.GetAllergensAsync();
        var avoidedIds = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToList();
        foreach (var item in targets)
        {
            // パッケージでの確認と食い違う商品は、まとめて確定せず、食い違いを見せてから1件ずつ確定してもらう（SR-20）
            var existing = await _products.GetAsync(item.Match.SameProducts[0].ProductId);
            var preview = FlyerAllergenPlanner.Plan(
                new FlyerAllergenInput(item.LabelAllergenIds, item.Read.AllergenLabelText, KeywordSearchText(item.Read), item.UnmappedTerms),
                allergens,
                avoidedIds,
                existing?.AllergenChecks ?? []);
            if (existing is null || preview.Discrepancies.Count > 0)
            {
                continue;
            }

            var read = item.Read;
            var input = new FlyerConfirmInput(
                menu.Id,
                item.Id,
                FlyerItemChoice.Same,
                item.Match.SameProducts[0].ProductId,
                read.Name,
                read.Maker,
                read.Volume,
                read.PiecesPerPackCandidate,
                false,
                read.SnackType,
                false,
                read.PriceIncludingTax,
                read.PriceExcludingTax,
                read.OrderNo,
                read.TargetAgeLabel,
                [],
                [],
                item.LabelAllergenIds,
                [],
                false);
            var result = await ConfirmCoreAsync(menu, session, item, input);
            if (result.HasError)
            {
                continue;
            }

            // 1件ずつ保存してから「確定済み」にする（途中で失敗しても、保存した分と画面の状態が食い違わない）
            await _unitOfWork.SaveChangesAsync();
            result.Value!();
            count++;
        }

        return Result<int>.Ok(count);
    }

    /// <summary>取り込みをやめる（確定前の結果を捨てる。確定した商品・写しはそのまま）</summary>
    public Result Discard(string? menuId)
    {
        if (!string.IsNullOrEmpty(menuId))
        {
            _sessions.Remove(menuId);
        }

        return new Result();
    }

    /// <summary>
    /// 1商品の確定を DB の変更として組み立てる（保存はしない）。成功したら、保存できたあとで取り込みの状態を変える処理を返す。
    /// </summary>
    private async Task<Result<Action>> ConfirmCoreAsync(Menu menu, FlyerImportSession session, FlyerImportItem item, FlyerConfirmInput input)
    {
        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result<Action>.Error("ログインが必要です。ログインし直してください");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var previous = item.Confirmation;
        var clearFirst = session.IsReimport && !session.HasConfirmedAny;

        if (input.Choice == FlyerItemChoice.Skip)
        {
            if (previous?.ProductId is { } previousId && !clearFirst)
            {
                ListedProductsUpdater.Remove(menu, previousId);
            }

            await DisableCreatedProductAsync(previous, null, now, user);
            Touch(menu, now, user);
            return Result<Action>.Ok(() =>
            {
                lock (session.SyncRoot)
                {
                    item.Confirmation = new FlyerItemConfirmation(FlyerItemChoice.Skip, null, item.Read.Name, false);
                }
            });
        }

        var validation = ValidateInput(input);
        if (validation.HasError)
        {
            var error = new Result<Action>();
            error.ErrorMessages.AddRange(validation.ErrorMessages);
            return error;
        }

        var allergens = await _settings.GetAllergensAsync();
        var avoidedIds = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToList();
        var allergenIds = allergens.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        if (input.LabelAllergenIds.Any(id => !allergenIds.Contains(id)))
        {
            return Result<Action>.Error("アレルゲンの品目が正しくありません。画面を読み込み直してください");
        }

        Product product;
        if (input.Choice == FlyerItemChoice.Same)
        {
            // 「同じ商品」にできるのは、ルールで同じと決まった商品か、AI が「同じ商品」と判定した候補だけ。
            // 内容量違い・味違いの商品を「同じ」にすると、別の商品の確認済みの記録を引き継いでしまう（SR-05）
            if (input.ProductId is null || !SameChoiceProductIds(item).Contains(input.ProductId))
            {
                return Result<Action>.Error("「同じ商品」にできるのは、同じ商品と分かった商品だけです。内容量違い・味違いなら「味違い・内容量違いとして登録」を選んでください");
            }

            var existing = await _products.GetAsync(input.ProductId);
            if (existing is null)
            {
                return Result<Action>.Error("商品が見つかりません。画面を読み込み直してください");
            }

            // 前に「新しい商品」として作った商品は、使わない商品にする（同じ商品に選び直したため）
            await DisableCreatedProductAsync(previous, existing.Id, now, user);

            product = existing;
            ListedProductsUpdater.ApplyListing(product, menu.WeekStart, input.PriceIncludingTax, input.PriceExcludingTax, Blank(input.OrderNo));
            if (input.PiecesEdited || product.PiecesPerPack is null)
            {
                product.PiecesPerPack = input.PiecesPerPack ?? product.PiecesPerPack;
            }

            if (input.SnackTypeEdited && input.SnackType is { } snackType)
            {
                product.SnackType = snackType;
            }

            product.UpdateDateTime = now;
            product.UpdateUserId = user.UserId;
        }
        else
        {
            // 味違い・内容量違い・新しい商品は別の商品として作る（アレルゲン確認を引き継がない。SR-05）。
            // 前に同じ品目で新しく作った商品があれば、それを直す（選び直すたびに商品が増えないように）
            var reused = previous is { Choice: FlyerItemChoice.New or FlyerItemChoice.Variant, ProductId: { } createdId }
                ? await _products.GetAsync(createdId)
                : null;
            product = reused ?? new Product { UpdateDateTime = now };
            product.Name = input.Name!.Trim();
            product.NormalizedName = TextNormalizer.ForProductMatch(product.Name);
            product.Maker = Blank(input.Maker);
            product.Volume = Blank(input.Volume);
            product.PiecesPerPack = input.PiecesPerPack;
            product.SnackType = input.SnackType!.Value;
            product.Price = input.PriceIncludingTax;
            product.PriceExcludingTax = input.PriceExcludingTax;
            product.OrderNo = Blank(input.OrderNo);
            product.TargetAgeLabel = Blank(input.TargetAgeLabel);
            product.ListedWeekStart = menu.WeekStart;
            product.UpdateDateTime = now;
            product.UpdateUserId = user.UserId;
            if (reused is null)
            {
                var tags = AllergenCheckFactory.CreateTags(
                    product.Id,
                    input.CharacterTags.Select(t => new ProductTagInput(ProductTagKind.Character, t))
                        .Concat(input.FlavorTags.Select(t => new ProductTagInput(ProductTagKind.Flavor, t))),
                    ProductTagSource.Claude,
                    now,
                    user.UserId);
                if (tags.HasError)
                {
                    return Result<Action>.Error(tags.ErrorMessages[0]);
                }

                product.Tags.AddRange(tags.Value!);
                _products.Add(product);
            }
        }

        // チラシの品目（表記 ∪ 説明文で当たった避ける品目）から「参考」の記録を足すかを決める（03 §3・SR-20）
        var unresolved = item.UnmappedTerms.Except(input.ResolvedTerms, StringComparer.Ordinal).ToList();
        var plan = FlyerAllergenPlanner.Plan(
            new FlyerAllergenInput(input.LabelAllergenIds, item.Read.AllergenLabelText, KeywordSearchText(item.Read), unresolved),
            allergens,
            avoidedIds,
            product.AllergenChecks);
        if (!plan.CanConfirm)
        {
            return Result<Action>.Error($"対応づけできていないアレルゲンの表記（{string.Join("・", plan.UnresolvedLabelTerms)}）があります。品目を選ぶか「対応する品目なし」を選んでから確定してください");
        }

        var addReference = plan.ShouldAddReference(input.TreatAsFlyerError);
        if (addReference)
        {
            var check = FlyerAllergenPlanner.CreateReferenceCheck(product.Id, plan, now, user.UserName, user.UserId);
            var removedLabels = item.LabelAllergenIds.Except(input.LabelAllergenIds, StringComparer.Ordinal).ToList();
            if (removedLabels.Count > 0)
            {
                // 読み取った表記の品目を職員が外したことを残す（あとで確かめられるように。SR-19）
                check.SourceNote += "／職員が外した品目：" + string.Join("・", removedLabels.Select(id => allergens.First(a => a.Id == id).Name));
            }

            _products.AddCheck(check);
            product.AllergenChecks.Add(check);
        }

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct(product.Id, input.PriceIncludingTax, Blank(input.OrderNo)), clearFirst, previous?.ProductId);
        Touch(menu, now, user);
        var confirmation = new FlyerItemConfirmation(input.Choice, product.Id, product.Name, addReference);
        return Result<Action>.Ok(() =>
        {
            lock (session.SyncRoot)
            {
                session.HasConfirmedAny = true;
                item.Confirmation = confirmation;
            }
        });
    }

    /// <summary>
    /// 選び直しで、前に「新しい商品」「味違い・内容量違い」として作った商品を使わなくなったら、「使わない」にする
    /// （商品一覧に使われない商品が増えないように。削除はしない）。
    /// </summary>
    private async Task DisableCreatedProductAsync(FlyerItemConfirmation? previous, string? keepProductId, DateTime now, CurrentUserInfo user)
    {
        if (previous is not { Choice: FlyerItemChoice.New or FlyerItemChoice.Variant, ProductId: { } createdId } || createdId == keepProductId)
        {
            return;
        }

        var created = await _products.GetAsync(createdId);
        if (created is not null)
        {
            created.IsDisabled = true;
            created.UpdateDateTime = now;
            created.UpdateUserId = user.UserId;
        }
    }

    /// <summary>
    /// 探す言葉を探す文：Claude が読んだ商品名と説明文。取り込みで職員が商品名を直しても、読んだ名前の当たりは
    /// チラシの品目に残る（当たりを取り消す手段は作らない。SR-14）
    /// </summary>
    private static string KeywordSearchText(FlyerReadProduct read) =>
        string.Join("\n", new[] { read.Name, read.Description }.Where(t => !string.IsNullOrWhiteSpace(t)));

    private static void Touch(Menu menu, DateTime now, CurrentUserInfo user)
    {
        // 献立の行を必ず更新して row_version を進める（D-46）
        menu.UpdateDateTime = now;
        menu.UpdateUserId = user.UserId;
    }

    private static Result ValidateInput(FlyerConfirmInput input)
    {
        var result = new Result();
        var isNewProduct = input.Choice is FlyerItemChoice.New or FlyerItemChoice.Variant;
        if (isNewProduct && string.IsNullOrWhiteSpace(input.Name))
        {
            result.ErrorMessages.Add("商品名を入れてください");
        }

        if (isNewProduct && (input.SnackType is null || !Enum.IsDefined(input.SnackType.Value)))
        {
            result.ErrorMessages.Add("おやつの分類を選んでください");
        }

        if (input.SnackType is { } snackType && !Enum.IsDefined(snackType))
        {
            result.ErrorMessages.Add("おやつの分類を選び直してください");
        }

        if (input.PiecesPerPack is not null)
        {
            result.ErrorMessages.AddRange(InputValidator.ValidatePiecesPerPack(input.PiecesPerPack).ErrorMessages);
        }

        if (input.PriceIncludingTax is not null)
        {
            result.ErrorMessages.AddRange(InputValidator.ValidatePrice(input.PriceIncludingTax).ErrorMessages);
        }

        if (input.PriceExcludingTax is < 1)
        {
            result.ErrorMessages.Add("税抜価格は1以上の整数で入れてください");
        }

        return result;
    }

    /// <summary>読み取った1商品を、取り込みの商品にする（アレルゲン表記はアプリでもう一度対応づける）</summary>
    private static FlyerImportItem BuildItem(FlyerReadProduct read, IReadOnlyList<Allergen> allergens, bool isManual)
    {
        var byCode = allergens.ToDictionary(a => a.Code, StringComparer.Ordinal);
        var parsed = FlyerAllergenPlanner.ParseLabel(read.AllergenLabelText, allergens);
        var labelIds = read.AllergenCodes
            .Where(byCode.ContainsKey)
            .Select(code => byCode[code].Id)
            .Concat(parsed.Allergens.Select(a => a.Id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Claude が対応づけた品目も、アプリが対応づけた品目もすべて採る（確認する側に倒す）。対応づけできない語は残す
        var unmapped = parsed.UnmappedTerms
            .Concat(read.UnmappedAllergenTerms)
            .Concat(read.AllergenCodes.Where(code => !byCode.ContainsKey(code)))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new FlyerImportItem
        {
            IsManual = isManual,
            Read = read,
            LabelAllergenIds = labelIds,
            UnmappedTerms = unmapped,
        };
    }

    /// <summary>アプリの検証（T1-15。Claude は呼ばない）の指摘を付け直す</summary>
    private static void AddAppIssues(List<FlyerImportItem> items)
    {
        foreach (var item in items)
        {
            item.Issues.RemoveAll(i => i.Source == "app");
        }

        var issues = FlyerReadingValidator.Validate(items
            .Select(i => new FlyerReadItem(i.Read.Name, i.Read.Volume, i.Read.PriceIncludingTax, i.Read.PriceExcludingTax, i.Read.PiecesPerPackCandidate))
            .ToList());
        foreach (var issue in issues)
        {
            if (issue.Index >= 0 && issue.Index < items.Count)
            {
                items[issue.Index].Issues.Add(new FlyerItemIssue("app", FieldOf(issue.Kind), issue.Message));
            }
        }
    }

    private static string FieldOf(FlyerReadingIssueKind kind) => kind switch
    {
        FlyerReadingIssueKind.MissingName or FlyerReadingIssueKind.Duplicate => "商品名",
        FlyerReadingIssueKind.MissingVolume => "内容量",
        FlyerReadingIssueKind.MissingPrice or FlyerReadingIssueKind.TaxMismatch => "価格",
        _ => "入数",
    };

    /// <summary>
    /// 読み取りの検証の指摘を付ける（書き換えはしない）。アレルゲンは確認を増やす方向の指摘だけ採用する（SR-19）：
    /// 読み落としたアレルゲン表記は、表記の品目に足す（減らす指摘は受け付けない形にしてある）。
    /// </summary>
    private static void ApplyReview(FlyerImportSession session, FlyerReview review, IReadOnlyList<Allergen> allergens)
    {
        foreach (var note in review.Notes)
        {
            if (note.Index >= 0 && note.Index < session.Items.Count)
            {
                session.Items[note.Index].Issues.Add(new FlyerItemIssue("review", note.Field, note.Reason));
            }
        }

        foreach (var missed in review.MissedAllergenLabels)
        {
            if (missed.Index < 0 || missed.Index >= session.Items.Count)
            {
                continue;
            }

            var item = session.Items[missed.Index];
            var parsed = FlyerAllergenPlanner.ParseLabel(missed.LabelText, allergens);
            foreach (var allergen in parsed.Allergens.Where(a => !item.LabelAllergenIds.Contains(a.Id)))
            {
                item.LabelAllergenIds.Add(allergen.Id);
            }

            foreach (var term in parsed.UnmappedTerms.Where(t => !item.UnmappedTerms.Contains(t)))
            {
                item.UnmappedTerms.Add(term);
            }

            item.Issues.Add(new FlyerItemIssue("review", "アレルゲン", $"アレルゲンの表記「{missed.LabelText}」を読み落としている可能性があります（{missed.Reason}）。チラシと見比べてください"));
        }

        session.MissedProducts.AddRange(review.MissedProducts);
    }

    /// <summary>照合する（ルール → 迷うものだけ Haiku）。呼び出した回数を返す</summary>
    private async Task<int> MatchAsync(FlyerImportItem item, IReadOnlyList<Product> products, int remainingCalls, CancellationToken cancellationToken)
    {
        var input = new ProductMatchInput(item.Read.Name, item.Read.Volume, item.Read.Maker);
        var match = ProductMatcher.Match(input, products, _matchOptions);
        item.Match = new FlyerItemMatch
        {
            SameProducts = match.SameProducts.Select(m => ToMatchProduct(m.Product, m.MakerCompared, null)).ToList(),
            VolumeVariants = match.VolumeVariants.Select(m => ToMatchProduct(m.Product, m.MakerCompared, null)).ToList(),
            Candidates = match.Candidates.Select(c => ToMatchProduct(c.Product, true, c.Score)).ToList(),
        };

        if (!match.NeedsJudgement)
        {
            return 0;
        }

        if (remainingCalls <= 0)
        {
            item.Match.JudgementSkipped = true;
            return 0;
        }

        var candidates = match.Candidates.Select(c => c.Product).ToList();
        var judgement = await _judge.JudgeAsync(input, candidates, cancellationToken);
        if (judgement.HasError || judgement.Value is null
            || (judgement.Value.ProductId is not null && !candidates.Any(c => c.Id == judgement.Value.ProductId)))
        {
            // 失敗・候補にない商品を返したときは、判定なしで候補だけを示す（既定は「新しい商品」）
            item.Match.JudgementSkipped = true;
        }
        else
        {
            item.Match.Judgement = judgement.Value;
        }

        return 1;
    }

    /// <summary>
    /// 「同じ商品」として選べる商品：ルールで同じ商品と決まった候補と、AI が「同じ商品」と判定した候補（確定は職員）。
    /// 内容量違い・味違い・似ているだけの商品は含めない（SR-05）。
    /// </summary>
    public static IReadOnlyList<string> SameChoiceProductIds(FlyerImportItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var ids = item.Match.SameProducts.Select(p => p.ProductId).ToList();
        if (item.Match.Judgement is { Verdict: ProductMatchVerdict.Same, ProductId: { } judged }
            && item.Match.Candidates.Any(c => c.ProductId == judged)
            && !ids.Contains(judged))
        {
            ids.Add(judged);
        }

        return ids;
    }

    private static FlyerMatchProduct ToMatchProduct(Product product, bool makerCompared, double? score) =>
        new(product.Id, product.Name, product.Maker, product.Volume, makerCompared, product.IsDisabled, score);

    private static FlyerItemView ToView(
        FlyerImportItem item,
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyList<Allergen> allergens,
        IReadOnlyList<string> avoidedIds,
        IReadOnlyDictionary<string, string> allergenNames)
    {
        // 既定の選択：完全一致の候補がちょうど1件なら「同じ商品」、それ以外は「新しい商品」（迷えば別商品。FR-FLYER-06a）
        var suggestedChoice = item.Match.CanBulkConfirmAsSame ? FlyerItemChoice.Same : FlyerItemChoice.New;
        var suggestedProductId = item.Match.CanBulkConfirmAsSame ? item.Match.SameProducts[0].ProductId : null;

        // チラシの品目と、同じ商品の候補との食い違い（SR-20）を先に示す
        var compareProductId = item.Confirmation?.Choice == FlyerItemChoice.Same ? item.Confirmation.ProductId : suggestedProductId;
        var compareProduct = compareProductId is not null ? products.GetValueOrDefault(compareProductId) : null;
        var plan = FlyerAllergenPlanner.Plan(
            new FlyerAllergenInput(item.LabelAllergenIds, item.Read.AllergenLabelText, KeywordSearchText(item.Read), item.UnmappedTerms),
            allergens,
            avoidedIds,
            compareProduct?.AllergenChecks ?? []);

        // 「同じ商品」として選べる候補ごとの食い違い（AI が同じと判定した候補を選んだときも、確定の前に示す。SR-20）
        var discrepanciesByProduct = SameChoiceProductIds(item)
            .Where(products.ContainsKey)
            .ToDictionary(
                id => id,
                id => FlyerAllergenPlanner.Plan(
                    new FlyerAllergenInput(item.LabelAllergenIds, item.Read.AllergenLabelText, KeywordSearchText(item.Read), item.UnmappedTerms),
                    allergens,
                    avoidedIds,
                    products[id].AllergenChecks),
                StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => new FlyerCandidatePlanView(p.Value.Discrepancies, p.Value.Decision), StringComparer.Ordinal);

        var judgement = item.Match.Judgement is { } j
            ? new FlyerJudgementView(j.Verdict, j.ProductId, j.ProductId is null ? null : products.GetValueOrDefault(j.ProductId)?.Name, j.Reason)
            : null;

        return new FlyerItemView(
            item.Id,
            item.IsManual,
            item.Read,
            item.LabelAllergenIds.Select(id => new AllergenRef(id, allergenNames.GetValueOrDefault(id, id))).ToList(),
            item.UnmappedTerms,
            plan.DescriptionHits.Select(h => h.MatchedText).Distinct(StringComparer.Ordinal).ToList(),
            plan.Discrepancies,
            plan.Decision,
            item.Issues,
            item.Match.SameProducts,
            item.Match.VolumeVariants,
            item.Match.Candidates,
            judgement,
            item.Match.JudgementSkipped,
            item.Match.CanBulkConfirmAsSame && item.UnmappedTerms.Count == 0 && plan.Discrepancies.Count == 0,
            SameChoiceProductIds(item),
            discrepanciesByProduct,
            suggestedChoice,
            suggestedProductId,
            item.Confirmation);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>取り込みの設定（Claude:ProductMatchCallLimitPerImport など）</summary>
public sealed class FlyerImportOptions
{
    /// <summary>1回の取り込みでの照合の判定（Haiku）の呼び出しの上限（U-52）</summary>
    public int ProductMatchCallLimit { get; set; } = 30;

    /// <summary>読み取りの処理全体の期限（呼び出しのタイムアウトと再試行の合計より長くする。U-48）</summary>
    public TimeSpan JobDeadline { get; set; } = TimeSpan.FromMinutes(20);
}

/// <summary>確定の入力</summary>
public sealed record FlyerConfirmInput(
    string? MenuId,
    string? ItemId,
    FlyerItemChoice Choice,
    string? ProductId,
    string? Name,
    string? Maker,
    string? Volume,
    int? PiecesPerPack,
    bool PiecesEdited,
    SnackType? SnackType,
    bool SnackTypeEdited,
    int? PriceIncludingTax,
    int? PriceExcludingTax,
    string? OrderNo,
    string? TargetAgeLabel,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags,
    IReadOnlyList<string> LabelAllergenIds,
    IReadOnlyList<string> ResolvedTerms,
    bool TreatAsFlyerError);

/// <summary>手入力の商品</summary>
public sealed record FlyerManualInput(
    string? Name,
    string? Maker,
    string? Volume,
    int? PriceIncludingTax,
    int? PriceExcludingTax,
    string? OrderNo,
    int? PiecesPerPack,
    SnackType? SnackType,
    string? AllergenLabelText);

/// <summary>取り込みの状態</summary>
public sealed record FlyerImportView(BackgroundJobStatus? Job, FlyerSessionView? Session);

/// <summary>確定前の読み取り結果</summary>
public sealed record FlyerSessionView(
    string Id,
    bool IsReimport,
    bool ReviewFailed,
    IReadOnlyList<FlyerMissedProduct> MissedProducts,
    IReadOnlyList<FlyerItemView> Items,
    int UnconfirmedCount);

/// <summary>取り込みの1商品の表示</summary>
public sealed record FlyerItemView(
    string Id,
    bool IsManual,
    FlyerReadProduct Read,
    IReadOnlyList<AllergenRef> LabelAllergens,
    IReadOnlyList<string> UnmappedTerms,
    IReadOnlyList<string> DescriptionHitWords,
    IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies,
    FlyerReferenceDecision ReferenceDecision,
    IReadOnlyList<FlyerItemIssue> Issues,
    IReadOnlyList<FlyerMatchProduct> SameProducts,
    IReadOnlyList<FlyerMatchProduct> VolumeVariants,
    IReadOnlyList<FlyerMatchProduct> Candidates,
    FlyerJudgementView? Judgement,
    bool JudgementSkipped,
    bool CanBulkConfirm,
    IReadOnlyList<string> SameChoiceProductIds,
    IReadOnlyDictionary<string, FlyerCandidatePlanView> PlansByProduct,
    FlyerItemChoice SuggestedChoice,
    string? SuggestedProductId,
    FlyerItemConfirmation? Confirmation);

/// <summary>「同じ商品」の候補ごとの、チラシの表記との食い違いと「参考」の記録の扱い</summary>
public sealed record FlyerCandidatePlanView(IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies, FlyerReferenceDecision ReferenceDecision);

/// <summary>照合の判定の表示</summary>
public sealed record FlyerJudgementView(ProductMatchVerdict Verdict, string? ProductId, string? ProductName, string Reason);
