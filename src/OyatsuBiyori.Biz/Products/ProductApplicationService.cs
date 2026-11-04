using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;

namespace OyatsuBiyori.Biz.Products;

/// <summary>
/// 商品一覧（SC-06）・商品詳細と編集（SC-07）・アレルゲン確認（SC-08）（T5-01〜06、T6-01〜02）。
/// 判定はすべて Biz で求め、画面には材料（AllergenJudgementView）だけを返す。確認の記録は追加のみ（SR-07）。
/// Claude の読み取りは候補で、「確認済み」にするのは職員の操作だけ（SR-03）。
/// </summary>
public sealed class ProductApplicationService(
    IProductRepository productRepository,
    IMenuRepository menuRepository,
    ISettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    MenuContextFactory contextFactory,
    IBackgroundJobService backgroundJobs,
    IAllergenExtractor allergenExtractor,
    IUserContext userContext,
    TimeProvider timeProvider)
{
    /// <summary>原材料表示の読み取りの処理の種類</summary>
    public const string ExtractionJobKind = "allergen-extraction";

    /// <summary>1回に読み取れるパッケージの画像の数（FR-PROD-05）</summary>
    public const int MaxImages = 4;

    private const string NotFound = "商品が見つかりません。商品一覧から開き直してください";

    private readonly IProductRepository _products = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly MenuContextFactory _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly IBackgroundJobService _jobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
    private readonly IAllergenExtractor _extractor = allergenExtractor ?? throw new ArgumentNullException(nameof(allergenExtractor));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>
    /// 商品一覧（T6-01）。検索は正規化名の部分一致（FR-PROD-02）。絞り込みはアレルゲンの表示で行い、ステータスでは絞り込まない（D-83）。
    /// 「使わない」商品は既定で隠す。ただし「今日以降の献立で使う、パッケージで確認する商品」は「使わない」でも出す（SC-10 の件数と合わせる）。
    /// </summary>
    public async Task<Result<ProductListView>> ListAsync(string? query, ProductListFilter filter, bool includeDisabled)
    {
        var (context, products) = await _contextFactory.ForAllProductsAsync();
        var today = context.Today;
        var futureIds = (await _menus.GetFromDateAsync(today))
            .SelectMany(m => m.Days.Where(d => d.Date >= today).SelectMany(d => d.Items))
            .Select(i => i.ProductId)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedQuery = TextNormalizer.ForProductMatch(query);

        var items = new List<ProductListItemView>();
        foreach (var product in products)
        {
            if (normalizedQuery.Length > 0 && !TextNormalizer.ForProductMatch(product.Name).Contains(normalizedQuery, StringComparison.Ordinal))
            {
                continue;
            }

            var judgement = context.Judge(product);
            var needsPackage = MenuApplicationService.NeedsPackageCheck(judgement);
            var inFuture = futureIds.Contains(product.Id);
            var matches = filter switch
            {
                ProductListFilter.Blocked => judgement.Category == AllergenJudgementCategory.Blocked,
                ProductListFilter.Warning => judgement.Category == AllergenJudgementCategory.Warning,
                ProductListFilter.Safe => judgement.Category == AllergenJudgementCategory.Safe,
                ProductListFilter.Package => needsPackage,
                ProductListFilter.OtherPackage => needsPackage && !inFuture && !product.IsDisabled,
                ProductListFilter.FuturePackage => needsPackage && inFuture,
                _ => true,
            };
            if (!matches)
            {
                continue;
            }

            if (product.IsDisabled && !includeDisabled && filter != ProductListFilter.FuturePackage)
            {
                continue;
            }

            var history = AllergenCheckHistory.From(product.AllergenChecks);
            items.Add(new ProductListItemView(
                product.Id,
                product.Name,
                product.Maker,
                product.Volume,
                product.SnackType,
                product.Tags.Select(t => new TagView(t.Kind, t.Value)).ToList(),
                AllergenJudgementView.From(product, judgement, today),
                history.LatestDate,
                product.IsDisabled,
                inFuture));
        }

        return Result<ProductListView>.Ok(new ProductListView(
            items.OrderBy(i => i.Judgement.Category).ThenBy(i => i.Name, StringComparer.Ordinal).ToList(),
            context.AvoidedAllergens.Count == 0));
    }

    /// <summary>商品詳細（T6-02）。確認の履歴（ステータスの名前を出すのはここだけ）・今日以降にこの商品を使う献立を含む</summary>
    public async Task<Result<ProductDetailView>> GetAsync(string? productId)
    {
        var product = string.IsNullOrEmpty(productId) ? null : await _products.GetAsync(productId);
        if (product is null)
        {
            return Result<ProductDetailView>.Error(NotFound);
        }

        return Result<ProductDetailView>.Ok(await BuildDetailAsync(product));
    }

    /// <summary>
    /// 基本情報を直す（FR-PROD-04・08・09）。商品名を変えたら正規化名を作り直す。入数・1人あたりの個数・価格は1以上の整数。
    /// JAN は保存前に重複を調べる（空欄は NULL）。税抜・注文番号・対象年齢・最後に載った週は読むだけ（ここでは変えない）。
    /// </summary>
    public async Task<Result<ProductDetailView>> UpdateAsync(ProductUpdateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var product = string.IsNullOrEmpty(input.ProductId) ? null : await _products.GetAsync(input.ProductId);
        if (product is null)
        {
            return Result<ProductDetailView>.Error(NotFound);
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors.Add("商品名を入れてください");
        }

        if (input.PiecesPerPack is not null)
        {
            errors.AddRange(InputValidator.ValidatePiecesPerPack(input.PiecesPerPack).ErrorMessages);
        }

        errors.AddRange(InputValidator.ValidatePerChildCount(input.DefaultPerChildCount).ErrorMessages);
        if (input.Price is not null)
        {
            errors.AddRange(InputValidator.ValidatePrice(input.Price).ErrorMessages);
        }

        if (!Enum.IsDefined(input.SnackType))
        {
            errors.Add("おやつの分類を選んでください");
        }

        // 商品名で当たっていた避ける品目の探す言葉を、名前を変えて消せないようにする（当たりを取り消す手段は作らない。SR-14）
        if (!string.IsNullOrWhiteSpace(input.Name))
        {
            var avoided = await _settings.GetAvoidedAllergensAsync();
            var before = AllergenKeywordFinder.Find(product.Name, avoided).Select(h => h.AllergenId).ToHashSet(StringComparer.Ordinal);
            var after = AllergenKeywordFinder.Find(input.Name, avoided).Select(h => h.AllergenId).ToHashSet(StringComparer.Ordinal);
            var lost = avoided.Where(a => before.Contains(a.Id) && !after.Contains(a.Id)).Select(a => a.Name).ToList();
            if (lost.Count > 0)
            {
                errors.Add($"商品名に避ける品目（{string.Join("・", lost)}）の言葉があるため、その言葉を消す名前には変えられません。パッケージの原材料表示で確認してください");
            }
        }

        var jan = string.IsNullOrWhiteSpace(input.JanCode) ? null : input.JanCode.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        if (jan is not null)
        {
            if (!jan.All(char.IsAsciiDigit) || jan.Length is not (8 or 13))
            {
                errors.Add("JAN コードは8けたか13けたの数字で入れてください");
            }
            else if (await _products.FindByJanAsync(jan) is { } other && other.Id != product.Id)
            {
                errors.Add($"この JAN コードは「{other.Name}」で使われています。商品を確かめてください");
            }
        }

        if (input.Memo is { Length: > 500 })
        {
            errors.Add("メモは500文字までにしてください");
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var tags = AllergenCheckFactory.CreateTags(
            product.Id,
            input.CharacterTags.Select(t => new ProductTagInput(ProductTagKind.Character, t))
                .Concat(input.FlavorTags.Select(t => new ProductTagInput(ProductTagKind.Flavor, t))),
            ProductTagSource.Staff,
            now,
            user?.UserId);
        errors.AddRange(tags.ErrorMessages);
        if (errors.Count > 0)
        {
            var error = new Result<ProductDetailView>();
            error.ErrorMessages.AddRange(errors);
            return error;
        }

        product.Name = input.Name!.Trim();
        product.NormalizedName = TextNormalizer.ForProductMatch(product.Name);
        product.Maker = Blank(input.Maker);
        product.Volume = Blank(input.Volume);
        product.PiecesPerPack = input.PiecesPerPack;
        product.DefaultPerChildCount = input.DefaultPerChildCount!.Value;
        product.Price = input.Price;
        product.JanCode = jan;
        product.SnackType = input.SnackType;
        product.Memo = Blank(input.Memo);
        product.IsDisabled = input.IsDisabled;
        product.UpdateDateTime = now;
        product.UpdateUserId = user?.UserId;
        ReplaceTags(product, tags.Value!);

        await _unitOfWork.SaveChangesAsync();
        return Result<ProductDetailView>.Ok(await BuildDetailAsync(product));
    }

    /// <summary>
    /// 編集中の原材料の全文で、避ける品目の探す言葉に当たった言葉と範囲（T5-03。強調表示のため。フロントで正規化を作り直さない）。
    /// </summary>
    public async Task<Result<IReadOnlyList<KeywordRangeView>>> FindKeywordHitsAsync(string? text)
    {
        var avoided = await _settings.GetAvoidedAllergensAsync();
        var hits = AllergenKeywordFinder.Find(text, avoided)
            .Select(h => new KeywordRangeView(h.AllergenId, h.AllergenName, h.Keyword, h.Start, h.Length, h.MatchedText))
            .ToList();
        return Result<IReadOnlyList<KeywordRangeView>>.Ok(hits);
    }

    /// <summary>
    /// 原材料表示の読み取りを受け付ける（バックグラウンド。T5-02）。画像は読み取りが終わったらすぐ破棄する（NFR-SEC-06）。
    /// </summary>
    public async Task<Result<string>> StartExtractionAsync(string? productId, IReadOnlyList<ClaudeImage> images)
    {
        var product = string.IsNullOrEmpty(productId) ? null : await _products.GetAsync(productId);
        if (product is null)
        {
            return Result<string>.Error(NotFound);
        }

        if (images.Count == 0)
        {
            return Result<string>.Error("パッケージの原材料表示の写真を選んでください");
        }

        if (images.Count > MaxImages)
        {
            return Result<string>.Error($"1回に読み取れるのは{MaxImages}枚までです。いらない写真を外してください");
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var key = product.Id;
        var holder = images.ToList();
        var jobId = _jobs.Enqueue(ExtractionJobKind, key, user, TimeSpan.FromMinutes(8), async (services, progress, cancellationToken) =>
        {
            var service = (ProductApplicationService)services.GetService(typeof(ProductApplicationService))!;
            var result = await service.RunExtractionAsync(key, holder, progress, cancellationToken);
            var outcome = new Result<object?>();
            outcome.ErrorMessages.AddRange(result.ErrorMessages);
            outcome.Value = result.Value;
            return outcome;
        });
        return Result<string>.Ok(jobId);
    }

    /// <summary>原材料表示の読み取りの本体（バックグラウンドの中で呼ぶ）。候補は対象の品目だけに絞る</summary>
    public async Task<Result<AllergenExtractionView>> RunExtractionAsync(string productId, List<ClaudeImage> images, IJobProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            var product = await _products.GetAsync(productId);
            if (product is null)
            {
                return Result<AllergenExtractionView>.Error(NotFound);
            }

            var targets = await CheckTargetsAsync();
            progress.Report("原材料表示を読み取っています");
            var extracted = await _extractor.ExtractAsync(images, targets, product.Name, cancellationToken);
            if (extracted.HasError || extracted.Value is null)
            {
                var error = new Result<AllergenExtractionView>();
                error.ErrorMessages.AddRange(extracted.ErrorMessages);
                error.ErrorMessages.Add("読み取れない場合は、原材料の全文を手で入れてください。");
                return error;
            }

            var byCode = targets.ToDictionary(a => a.Code, StringComparer.Ordinal);
            var value = extracted.Value;
            return Result<AllergenExtractionView>.Ok(new AllergenExtractionView(
                value.RawIngredients,
                value.ContainsAllergenCodes.Where(byCode.ContainsKey).Select(c => byCode[c].Id).Distinct(StringComparer.Ordinal).ToList(),
                string.IsNullOrWhiteSpace(value.ContaminationNote) ? null : value.ContaminationNote.Trim(),
                value.CharacterTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct().ToList(),
                value.FlavorTags.Where(ProductTagVocabulary.IsFlavor).Distinct().ToList()));
        }
        finally
        {
            // 読み取りが終わったら、すぐに画像を破棄する（保存しない）
            images.Clear();
        }
    }

    /// <summary>
    /// アレルゲン確認の記録を足す（T5-05。追加のみ）。受け付けの規則は T1-19（AllergenCheckFactory）：
    /// パッケージなら「確認済み」（全文と、29品目と避ける追加品目のすべての「含む / 含まない」が要る）、それ以外は「参考」（「含む」だけ）。
    /// 確認者はログイン中のアカウント（FR-AUTH-04）。記録したあとの判定と、この商品を使う今日以降の献立を返す（T5-06）。
    /// </summary>
    public async Task<Result<AllergenCheckRecordedView>> RecordCheckAsync(AllergenCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var product = string.IsNullOrEmpty(input.ProductId) ? null : await _products.GetAsync(input.ProductId);
        if (product is null)
        {
            return Result<AllergenCheckRecordedView>.Error(NotFound);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result<AllergenCheckRecordedView>.Error("ログインが必要です。ログインし直してください");
        }

        var allergens = await _settings.GetAllergensAsync();
        var avoidedIds = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToList();
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Claude が読み取った全文は画面から受け取らず、サーバーが持つ読み取り結果から入れる。職員が全文から言葉を消しても、
        // 読み取った全文に避ける品目の言葉があれば「含む可能性あり」のままにするため（SR-14 の二重の守り）
        // 読み取りの途中で記録すると、読み取った全文が付かずに「確認済み」になりうるので止める
        if (_jobs.FindLatest(ExtractionJobKind, product.Id) is { State: BackgroundJobState.Queued or BackgroundJobState.Running })
        {
            return Result<AllergenCheckRecordedView>.Error("原材料表示を読み取っている途中です。読み取りが終わってから記録してください");
        }

        // あとの読み取りが失敗しても、前に終わった読み取りの全文は使う（読み取り直しの失敗で言葉を消せないように）
        var ocrText = _jobs.FindLatestSucceeded(ExtractionJobKind, product.Id) is { Result: AllergenExtractionView extraction }
            && !string.IsNullOrWhiteSpace(extraction.RawIngredients)
            ? extraction.RawIngredients
            : null;
        var created = AllergenCheckFactory.Create(
            new AllergenCheckRequest(input.Source, input.SourceNote, ocrText, input.RawIngredients, input.ContaminationNote, input.Items),
            product.Id,
            allergens,
            avoidedIds,
            now,
            user.UserName);
        if (created.HasError)
        {
            var error = new Result<AllergenCheckRecordedView>();
            error.ErrorMessages.AddRange(created.ErrorMessages);
            return error;
        }

        var tags = AllergenCheckFactory.CreateTags(
            product.Id,
            input.CharacterTags.Select(t => new ProductTagInput(ProductTagKind.Character, t))
                .Concat(input.FlavorTags.Select(t => new ProductTagInput(ProductTagKind.Flavor, t))),
            ProductTagSource.Claude,
            now,
            user.UserId);
        if (tags.HasError)
        {
            return Result<AllergenCheckRecordedView>.Error(tags.ErrorMessages[0]);
        }

        var check = created.Value!;
        check.UpdateUserId = user.UserId;
        foreach (var item in check.Items)
        {
            item.UpdateUserId = user.UserId;
        }
        _products.AddCheck(check);
        product.AllergenChecks.Add(check);
        foreach (var tag in tags.Value!.Where(t => !product.Tags.Any(x => x.Kind == t.Kind && x.Value == t.Value)))
        {
            _products.AddTag(tag);
            product.Tags.Add(tag);
        }

        await _unitOfWork.SaveChangesAsync();

        var detail = await BuildDetailAsync(product);
        return Result<AllergenCheckRecordedView>.Ok(new AllergenCheckRecordedView(detail.Judgement, detail.Usages));
    }

    /// <summary>
    /// 確認の対象の品目（29品目と、避ける品目にある施設の追加品目。SR-15）。探す言葉つき。
    /// </summary>
    private async Task<IReadOnlyList<Allergen>> CheckTargetsAsync()
    {
        var allergens = await _settings.GetAllergensAsync();
        var avoided = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToHashSet(StringComparer.Ordinal);
        return allergens.Where(a => a.Category != AllergenCategory.FacilityAdded || avoided.Contains(a.Id)).OrderBy(a => a.SortOrder).ToList();
    }

    private async Task<ProductDetailView> BuildDetailAsync(Product product)
    {
        var context = await _contextFactory.ForProductsAsync([product]);
        var today = context.Today;
        var judgement = context.Judge(product);
        var history = AllergenCheckHistory.From(product.AllergenChecks);
        var status = history.GetStatus(today, context.RecheckDays);
        var allergens = await _settings.GetAllergensAsync();
        var names = allergens.ToDictionary(a => a.Id, a => a.Name, StringComparer.Ordinal);
        var avoided = context.AvoidedAllergens.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var targets = await CheckTargetsAsync();

        var menus = await _menus.GetFromDateAsync(today);
        var usages = MenuImpactAnalyzer.FindUsages(product.Id, menus, today)
            .Select(u => new ProductUsageView(u.MenuId, u.WeekStart, u.Date, u.Status))
            .ToList();

        var checks = history.Checks
            .OrderByDescending(c => c.CheckedAt)
            .Select(c => new AllergenCheckHistoryView(
                c.Id,
                c.CheckedAt,
                c.CheckedBy,
                c.Source,
                c.Status,
                c.Items.Where(i => i.Contains).Select(i => names.GetValueOrDefault(i.AllergenId, i.AllergenId)).ToList(),
                c.ContaminationNote,
                c.SourceNote,
                c.RawIngredients))
            .ToList();

        return new ProductDetailView(
            product.Id,
            product.Name,
            product.Maker,
            product.Volume,
            product.PiecesPerPack,
            product.DefaultPerChildCount,
            product.Price,
            product.PriceExcludingTax,
            product.OrderNo,
            product.TargetAgeLabel,
            product.ListedWeekStart,
            product.JanCode,
            product.SnackType,
            product.Memo,
            product.IsDisabled,
            product.Tags.Select(t => new TagView(t.Kind, t.Value)).ToList(),
            AllergenJudgementView.From(product, judgement, today),
            status,
            history.LatestDate,
            checks,
            usages,
            targets.Select(a => new CheckTargetView(a.Id, a.Name, a.Category, avoided.Contains(a.Id), a.LabelNotGuaranteed)).ToList(),
            ProductTagVocabulary.Flavors,
            context.AvoidedAllergens.Count == 0);
    }

    private void ReplaceTags(Product product, IReadOnlyList<ProductTag> tags)
    {
        foreach (var old in product.Tags.Where(t => !tags.Any(n => n.Kind == t.Kind && n.Value == t.Value)).ToList())
        {
            product.Tags.Remove(old);
            _products.RemoveTag(old);
        }

        foreach (var tag in tags.Where(n => !product.Tags.Any(t => t.Kind == n.Kind && t.Value == n.Value)))
        {
            _products.AddTag(tag);
            product.Tags.Add(tag);
        }
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>商品一覧の絞り込み（アレルゲンの表示で。ステータスでは絞り込まない。D-83）</summary>
public enum ProductListFilter
{
    /// <summary>すべて</summary>
    All,

    /// <summary>🔴 使えません</summary>
    Blocked,

    /// <summary>🟡 要確認</summary>
    Warning,

    /// <summary>🟢 使えます</summary>
    Safe,

    /// <summary>パッケージで確認する商品（🟡 と、情報がないため 🔴）</summary>
    Package,

    /// <summary>そのほかの、パッケージで確認する商品（今日以降の献立で使っていないもの）</summary>
    OtherPackage,

    /// <summary>今日以降の献立で使う、パッケージで確認する商品（「使わない」でも出す）</summary>
    FuturePackage,
}

/// <summary>商品一覧</summary>
public sealed record ProductListView(IReadOnlyList<ProductListItemView> Items, bool IsAvoidedEmpty);

/// <summary>商品一覧の1件</summary>
public sealed record ProductListItemView(
    string Id,
    string Name,
    string? Maker,
    string? Volume,
    SnackType SnackType,
    IReadOnlyList<TagView> Tags,
    AllergenJudgementView Judgement,
    DateOnly? LastCheckedDate,
    bool IsDisabled,
    bool IsUsedInFutureMenus);

/// <summary>商品詳細</summary>
public sealed record ProductDetailView(
    string Id,
    string Name,
    string? Maker,
    string? Volume,
    int? PiecesPerPack,
    int DefaultPerChildCount,
    int? Price,
    int? PriceExcludingTax,
    string? OrderNo,
    string? TargetAgeLabel,
    DateOnly? ListedWeekStart,
    string? JanCode,
    SnackType SnackType,
    string? Memo,
    bool IsDisabled,
    IReadOnlyList<TagView> Tags,
    AllergenJudgementView Judgement,
    ProductAllergenStatus Status,
    DateOnly? LastCheckedDate,
    IReadOnlyList<AllergenCheckHistoryView> Checks,
    IReadOnlyList<ProductUsageView> Usages,
    IReadOnlyList<CheckTargetView> CheckTargets,
    IReadOnlyList<string> FlavorVocabulary,
    bool IsAvoidedEmpty);

/// <summary>確認の履歴の1件（ステータスの名前を画面に出すのは、この履歴だけ）</summary>
public sealed record AllergenCheckHistoryView(
    string Id,
    DateTime CheckedAt,
    string CheckedBy,
    AllergenCheckSource Source,
    AllergenCheckStatus Status,
    IReadOnlyList<string> ContainedAllergens,
    string? ContaminationNote,
    string? SourceNote,
    string? RawIngredients);

/// <summary>この商品を使う今日以降の献立の日</summary>
public sealed record ProductUsageView(string MenuId, DateOnly WeekStart, DateOnly Date, MenuStatus Status);

/// <summary>確認の対象の品目</summary>
public sealed record CheckTargetView(string Id, string Name, AllergenCategory Category, bool IsAvoided, bool LabelNotGuaranteed);

/// <summary>探す言葉の当たった範囲（元の全文での位置）</summary>
public sealed record KeywordRangeView(string AllergenId, string AllergenName, string Keyword, int Start, int Length, string MatchedText);

/// <summary>原材料表示の読み取り結果（候補）</summary>
public sealed record AllergenExtractionView(
    string RawIngredients,
    IReadOnlyList<string> ContainsAllergenIds,
    string? ContaminationNote,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags);

/// <summary>記録したあとの結果（判定と、この商品を使う今日以降の献立）</summary>
public sealed record AllergenCheckRecordedView(AllergenJudgementView Judgement, IReadOnlyList<ProductUsageView> Usages);

/// <summary>商品の基本情報の入力</summary>
public sealed record ProductUpdateInput(
    string? ProductId,
    string? Name,
    string? Maker,
    string? Volume,
    int? PiecesPerPack,
    int? DefaultPerChildCount,
    int? Price,
    string? JanCode,
    SnackType SnackType,
    string? Memo,
    bool IsDisabled,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags);

/// <summary>アレルゲン確認の記録の入力（職員が確定した内容。Claude の読み取った全文は受け取らず、サーバーの読み取り結果を使う）</summary>
public sealed record AllergenCheckInput(
    string? ProductId,
    AllergenCheckSource Source,
    string? SourceNote,
    string? RawIngredients,
    string? ContaminationNote,
    IReadOnlyCollection<AllergenItemDecision> Items,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags);
