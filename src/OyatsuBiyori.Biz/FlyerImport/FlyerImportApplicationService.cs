using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// チラシの取り込み（SC-09 手順1。D-100、09-flyer-import.md）。
/// 読み取り（Claude）→ 食品以外を外した掲載品へ（FR-IMPORT-20）→ 二重の読み取りをまとめる（FR-IMPORT-06）→ 読み取りの検証（Claude。まとめる前の一覧を渡す）
/// → 画像を破棄 → 結果をメモリに24時間。分類（FlyerItemClassifier）は保存せず、表示と確定のたびに、名前＋メーカーの完全一致の照合（ProductMatcher）と
/// 今の商品マスタ・避ける品目で求め直す（09 の 3.1・5.2）。Claude は照合に使わない（FR-IMPORT-05）。
/// 確定で、商品・「参考」の記録・掲載品の写しに反映する。「確認済み」にはしない（参考の記録だけを作る。SR-03）。
/// 確認済みの商品とチラシの表記が食い違えば、その取り込みで確定するたびに、食い違いのある全商品の分の「参考」の記録を同じ保存で足す（09 の 3.5）。
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
    IUserContext userContext,
    TimeProvider timeProvider,
    FlyerImportOptions importOptions)
{
    /// <summary>バックグラウンドの処理の種類</summary>
    public const string JobKind = "flyer-reading";

    /// <summary>1回に取り込める画像の数（FR-FLYER-01）</summary>
    public const int MaxImages = 10;

    private const string SessionNotFound = "読み取り結果が見つかりません（24時間たったか、サーバーが再起動しました）。チラシを読み取り直してください";
    private const string ItemNotFound = "その商品が見つかりません。画面を読み込み直してください";

    /// <summary>直している途中の商品なし</summary>
    private static readonly IReadOnlySet<string> NoEditing = new HashSet<string>(StringComparer.Ordinal);

    private readonly IMenuRepository _menus = menuRepository ?? throw new ArgumentNullException(nameof(menuRepository));
    private readonly IProductRepository _products = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IFlyerSessionStore _sessions = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    private readonly IBackgroundJobService _jobs = backgroundJobs ?? throw new ArgumentNullException(nameof(backgroundJobs));
    private readonly IFlyerReader _reader = flyerReader ?? throw new ArgumentNullException(nameof(flyerReader));
    private readonly IFlyerReviewer _reviewer = flyerReviewer ?? throw new ArgumentNullException(nameof(flyerReviewer));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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

    /// <summary>読み取りの処理の本体（バックグラウンドの中で呼ぶ）。取り込みの ID を返す</summary>
    public async Task<Result<string>> RunReadingAsync(string menuId, List<ClaudeImage> images, IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(progress);

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

            var reads = reading.Value.Items;
            var itemsByIndex = BuildReadItems(session, reads, allergens);

            // 読み取りの検証は、画像を破棄する前に同じ処理の中で行う。失敗しても読み取り結果は出す。
            // 検証の指摘は Claude が返した一覧の番号で返るので、まとめる前の一覧（食品以外も含む）を渡し、番号からまとめた商品に付け替える
            var review = await _reviewer.ReviewAsync(images, new FlyerReading(reads), cancellationToken);
            if (review.HasError || review.Value is null)
            {
                session.ReviewFailed = true;
            }
            else
            {
                ApplyReview(session, review.Value, allergens, itemsByIndex);
            }
        }
        finally
        {
            // 読み取りと読み取りの検証が終わったら、すぐに画像を破棄する（保存しない。FR-FLYER-08）
            images.Clear();
        }

        // 読み取りの前に手で入力していた商品（未確定）は、新しい読み取り結果に引き継ぐ（黙って消さない）
        var previousSession = _sessions.Get(menuId);
        if (previousSession is not null)
        {
            lock (previousSession.SyncRoot)
            {
                var carried = previousSession.Items.Where(i => i.IsManual && i.Confirmation is null).ToList();
                foreach (var item in carried)
                {
                    // 表記なしの見比べの印は引き継がない（新しいチラシとは見比べていないため。D-97）
                    item.IsLabelAbsentCompared = false;
                }

                session.Items.AddRange(carried);
            }
        }

        _sessions.Set(session);
        return Result<string>.Ok(session.Id);
    }

    /// <summary>
    /// 手入力の商品を足す（読み取りの一覧からも、読み取り結果がないときからも。チラシと同じ照合・分類。まとめには入れない。D-73）。
    /// 足した商品の ID を返す
    /// </summary>
    public async Task<Result<string>> AddManualAsync(string? menuId, FlyerManualInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        _ = cancellationToken;
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

        var validation = ValidateValues(input.PiecesPerPack, input.PriceIncludingTax, input.PriceExcludingTax, input.SnackType);
        if (validation.HasError)
        {
            return Result<string>.Error(validation.ErrorMessages[0]);
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
        var item = BuildItem(read, allergens, isManual: true, sourceIndexes: [], conflicts: []);
        lock (session.SyncRoot)
        {
            session.Items.Add(item);
        }

        return Result<string>.Ok(item.Id);
    }

    /// <summary>
    /// 取り込みの状態（読み取りの処理と、確定前の結果）。分類はここで求め直す。
    /// </summary>
    /// <param name="menuId">献立</param>
    /// <param name="editingItemIds">画面で行を開いて直している途中の商品（まとめに入れない。09 の 5.2）</param>
    public async Task<Result<FlyerImportView>> GetLatestAsync(string? menuId, IReadOnlyList<string>? editingItemIds = null)
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

        var context = await LoadContextAsync();
        var editing = ToSet(editingItemIds);
        lock (session.SyncRoot)
        {
            return Result<FlyerImportView>.Ok(new FlyerImportView(job, BuildSessionView(session, context, editing)));
        }
    }

    /// <summary>
    /// 行を直す（09 の 5.3・3.6）。input は行のすべての値（フォームの値）で、今の値と違う項目だけを「直した項目」にする。
    /// 直した値は読み取ったままの値と別に持つ。商品名・メーカーを直したら照合し直す（表示のたびに照合するので、次の分類で変わる）。
    /// 直した欄の読み取りの検証の指摘と、その欄の「このままでよい」は消す。アレルゲン表記の品目を直したら、表記なしの見比べの印を消す。
    /// 分類し直した行を返す（結果が「使えない」になることもある）
    /// </summary>
    public async Task<Result<FlyerItemView>> UpdateItemAsync(FlyerItemEditInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (_, session, error) = await FindSessionAsync(input.MenuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var validation = ValidateValues(input.PiecesPerPack, input.PriceIncludingTax, input.PriceExcludingTax, input.SnackType);
        if (validation.HasError)
        {
            var result = new Result<FlyerItemView>();
            result.ErrorMessages.AddRange(validation.ErrorMessages);
            return result;
        }

        var context = await LoadContextAsync();
        if (input.LabelAllergenIds is { } labelIds && labelIds.Any(id => !context.AllergenIds.Contains(id)))
        {
            return Result<FlyerItemView>.Error("アレルゲンの品目が正しくありません。画面を読み込み直してください");
        }

        var tagError = ValidateTags(input.CharacterTags ?? [], input.FlavorTags ?? []);
        if (tagError is not null)
        {
            return Result<FlyerItemView>.Error(tagError);
        }

        lock (session.SyncRoot)
        {
            var (item, itemError) = FindEditableItem(session, input.ItemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(itemError!);
            }

            // 「使えない（入数が分からない）」の行に入数を入れたかを、直す前の分類で見る（FR-IMPORT-11・09 の 5.2）
            var wasPiecesUnknown = Classify(session, item, context, editing: NoEditing)
                .Classification.Reasons.Any(r => r.Code == FlyerItemReasonCode.PiecesUnknown);
            var current = item.Read;
            var changed = new List<string>();
            var name = Blank(input.Name) ?? string.Empty;
            var maker = Blank(input.Maker);
            var volume = Blank(input.Volume);
            var orderNo = Blank(input.OrderNo);
            Track(changed, FlyerItemField.Name, !string.Equals(current.Name, name, StringComparison.Ordinal));
            Track(changed, FlyerItemField.Maker, !string.Equals(current.Maker, maker, StringComparison.Ordinal));
            Track(changed, FlyerItemField.Volume, !string.Equals(current.Volume, volume, StringComparison.Ordinal));
            Track(changed, FlyerItemField.PriceIncludingTax, current.PriceIncludingTax != input.PriceIncludingTax);
            Track(changed, FlyerItemField.PriceExcludingTax, current.PriceExcludingTax != input.PriceExcludingTax);
            Track(changed, FlyerItemField.OrderNo, !string.Equals(current.OrderNo, orderNo, StringComparison.Ordinal));
            Track(changed, FlyerItemField.PiecesPerPack, current.PiecesPerPackCandidate != input.PiecesPerPack);
            var labelChanged = input.LabelAllergenIds is { } newLabels
                && !item.LabelAllergenIds.ToHashSet(StringComparer.Ordinal).SetEquals(newLabels);
            Track(changed, FlyerItemField.AllergenLabel, labelChanged);

            item.Read = current with
            {
                Name = name,
                Maker = maker,
                Volume = volume,
                PriceIncludingTax = input.PriceIncludingTax,
                PriceExcludingTax = input.PriceExcludingTax,
                OrderNo = orderNo,
                PiecesPerPackCandidate = input.PiecesPerPack,
                PiecesPerPackReason = current.PiecesPerPackCandidate != input.PiecesPerPack ? "職員が直しました" : current.PiecesPerPackReason,
                SnackType = input.SnackType,
                TargetAgeLabel = Blank(input.TargetAgeLabel),
                CharacterTags = input.CharacterTags?.ToList() ?? current.CharacterTags,
                FlavorTags = input.FlavorTags?.ToList() ?? current.FlavorTags,
            };
            if (wasPiecesUnknown && current.PiecesPerPackCandidate != input.PiecesPerPack)
            {
                // どの API から入れても、入数を入れて「使えない」から戻した行はまとめに入れない（EnterPiecesAsync と同じ）
                item.IsPiecesEnteredByStaff = true;
            }

            if (labelChanged)
            {
                item.LabelAllergenIds = input.LabelAllergenIds!.Distinct(StringComparer.Ordinal).ToList();
                // 直した表記で見てもらったことにしない（D-97）
                item.IsLabelAbsentCompared = false;
            }

            foreach (var field in changed)
            {
                item.EditedFields.Add(field);
                item.ReviewNotes.RemoveAll(n => ReviewNoteMatchesField(n.Field, field));
            }

            item.AcceptedReasonKeys.RemoveWhere(key => changed.Any(field => AcceptedKeyDependsOn(key, field)));
            return Result<FlyerItemView>.Ok(BuildItemView(session, item, context, editing: NoEditing));
        }
    }

    /// <summary>
    /// 品目の分からない語に品目を選ぶ（allergenIds が空なら「対応する品目なし」。FR-IMPORT-33）。語は消さずに残し、選んだ品目を表記の品目に足す。
    /// 職員が選んだ語を略記として覚えることはしない（FR-IMPORT-34）。分類し直した行を返す
    /// </summary>
    public async Task<Result<FlyerItemView>> ResolveTermAsync(string? menuId, string? itemId, string? term, IReadOnlyList<string>? allergenIds)
    {
        var (_, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var context = await LoadContextAsync();
        var ids = (allergenIds ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Any(id => !context.AllergenIds.Contains(id)))
        {
            return Result<FlyerItemView>.Error("アレルゲンの品目が正しくありません。画面を読み込み直してください");
        }

        lock (session.SyncRoot)
        {
            var (item, itemError) = FindEditableItem(session, itemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(itemError!);
            }

            if (term is null || !item.UnmappedTerms.Contains(term, StringComparer.Ordinal))
            {
                return Result<FlyerItemView>.Error("その表記の語が見つかりません。画面を読み込み直してください");
            }

            item.ResolvedTerms[term] = ids;
            return Result<FlyerItemView>.Ok(BuildItemView(session, item, context, editing: NoEditing));
        }
    }

    /// <summary>
    /// 「使えない（入数が分からない）」の行に入数を入れる（FR-IMPORT-11）。読み取った内容（アレルゲン表記・説明文を含む）のまま分類し直す。
    /// 入れた行は読み取りの検証を通っていないため、まとめて確定・まとめて登録に入れない（1件ずつ確定する）。分類し直した行を返す
    /// </summary>
    public async Task<Result<FlyerItemView>> EnterPiecesAsync(string? menuId, string? itemId, int? piecesPerPack)
    {
        var (_, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        if (piecesPerPack is null)
        {
            return Result<FlyerItemView>.Error("入数（1パックで何人分か）を入れてください");
        }

        var validation = InputValidator.ValidatePiecesPerPack(piecesPerPack);
        if (validation.HasError)
        {
            return Result<FlyerItemView>.Error(validation.ErrorMessages[0]);
        }

        var context = await LoadContextAsync();
        lock (session.SyncRoot)
        {
            var (item, itemError) = FindEditableItem(session, itemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(itemError!);
            }

            var state = Classify(session, item, context, editing: NoEditing);
            if (!state.Classification.Reasons.Any(r => r.Code == FlyerItemReasonCode.PiecesUnknown))
            {
                return Result<FlyerItemView>.Error("この商品は入数が分かっています。入数を直すときは、行を開いて直してください");
            }

            item.Read = item.Read with { PiecesPerPackCandidate = piecesPerPack, PiecesPerPackReason = "職員が入れました" };
            item.IsPiecesEnteredByStaff = true;
            return Result<FlyerItemView>.Ok(BuildItemView(session, item, context, editing: NoEditing));
        }
    }

    /// <summary>
    /// 再入力の理由に「このままでよい」を押す（理由ごと。CanAccept の理由だけ）。取り込みのメモリにだけ持ち、記録には使わない（09 の 5.3）。
    /// 分類し直した行を返す
    /// </summary>
    public async Task<Result<FlyerItemView>> AcceptReasonAsync(string? menuId, string? itemId, string? reasonKey)
    {
        var (_, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var context = await LoadContextAsync();
        lock (session.SyncRoot)
        {
            var (item, itemError) = FindEditableItem(session, itemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(itemError!);
            }

            var state = Classify(session, item, context, editing: NoEditing);
            var reason = state.Classification.Reasons.FirstOrDefault(r => r.Key == reasonKey);
            if (reason is null)
            {
                return Result<FlyerItemView>.Error("その理由はもうありません。画面を読み込み直してください");
            }

            if (!reason.CanAccept)
            {
                return Result<FlyerItemView>.Error("この理由は「このままでよい」にできません。欄を直してください");
            }

            item.AcceptedReasonKeys.Add(reason.Key);
            return Result<FlyerItemView>.Ok(BuildItemView(session, item, context, editing: NoEditing));
        }
    }

    /// <summary>
    /// 外した掲載品に移す（FR-IMPORT-21）。どの分類の商品でも外せる。確定済みの商品は、確定を取り消してから外す
    /// （掲載品の写しから除き、その取り込みで新しく登録した商品は「使わない」にする）。外した掲載品は今週の商品にも商品マスタにも入れない（FR-IMPORT-22）
    /// </summary>
    public async Task<Result> RemoveItemAsync(string? menuId, string? itemId)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result.Error(error!);
        }

        // 確定と同時に走らないようにする（確定の途中の行を取り消して、写しと商品の状態が食い違わないように）
        await session.WriteGate.WaitAsync();
        try
        {
            return await RemoveItemLockedAsync(menu!, session, itemId);
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>RemoveItemAsync の中身（WriteGate を持っている間だけ呼ぶ）</summary>
    private async Task<Result> RemoveItemLockedAsync(Menu menu, FlyerImportSession session, string? itemId)
    {
        FlyerImportItem? item;
        FlyerItemConfirmation? confirmation;
        bool sharedListing;
        lock (session.SyncRoot)
        {
            item = session.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
            {
                return Result.Error(ItemNotFound);
            }

            if (item.IsRemoved)
            {
                return new Result();
            }

            confirmation = item.Confirmation;
            // 同じ商品に確定したほかの行があれば、写しの行は残す
            sharedListing = confirmation is not null
                && session.Items.Any(i => i != item && !i.IsRemoved && i.Confirmation?.ProductId == confirmation.ProductId);
            if (confirmation is null)
            {
                item.IsRemoved = true;
                item.RemovedReason = "職員が外しました";
                return new Result();
            }
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result.Error("ログインが必要です。ログインし直してください");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!sharedListing)
        {
            ListedProductsUpdater.Remove(menu, confirmation.ProductId);
        }

        // ほかの行がまだこの商品で確定しているときは「使わない」にしない（写しに残る商品が候補から外れないように）
        if (!sharedListing
            && confirmation.Kind == FlyerConfirmationKind.CreatedNew
            && await _products.GetAsync(confirmation.ProductId) is { } created)
        {
            // 新しく登録した商品は消さずに「使わない」にする（商品一覧に使われない商品が増えないように）
            created.IsDisabled = true;
            created.UpdateDateTime = now;
            created.UpdateUserId = user.UserId;
        }

        Touch(menu, now, user);
        await _unitOfWork.SaveChangesAsync();
        lock (session.SyncRoot)
        {
            item.Confirmation = null;
            item.IsRemoved = true;
            item.RemovedReason = "職員が外しました";
        }

        return new Result();
    }

    /// <summary>外した掲載品を一覧に戻す（FR-IMPORT-21。戻すと分類し直す）。分類し直した行を返す</summary>
    public async Task<Result<FlyerItemView>> RestoreItemAsync(string? menuId, string? itemId)
    {
        var (_, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var context = await LoadContextAsync();
        lock (session.SyncRoot)
        {
            var item = session.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(ItemNotFound);
            }

            item.IsRemoved = false;
            item.RemovedReason = null;
            return Result<FlyerItemView>.Ok(BuildItemView(session, item, context, editing: NoEditing));
        }
    }

    /// <summary>
    /// 1件を確定する（09 の 5.1）。確定の直前に分類を求め直し、使える＝一致した商品を更新、要確認＝新しい商品として登録する。
    /// 使えない・再入力・外した掲載品・確定済みはエラー（何も保存しない）。確定と同じ保存で、食い違いの「参考」の記録も足す（09 の 3.5）
    /// </summary>
    public async Task<Result<FlyerConfirmResult>> ConfirmItemAsync(string? menuId, string? itemId)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerConfirmResult>.Error(error!);
        }

        return await ConfirmCoreAsync(menu!, session, NoEditing, states =>
        {
            var item = session.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
            {
                return Selection.Fail(ItemNotFound);
            }

            if (item.IsRemoved)
            {
                return Selection.Fail("外した掲載品は確定できません。「戻す」で一覧に戻してから確定してください");
            }

            if (item.Confirmation is not null)
            {
                return Selection.Fail("この商品はもう確定しています");
            }

            var state = states.First(s => s.Item == item);
            return state.Classification.Category switch
            {
                FlyerItemCategory.Unusable => Selection.Fail("「使えない」の商品は確定できません（" + string.Join("／", state.Classification.Reasons.Select(r => r.Message)) + "）"),
                FlyerItemCategory.NeedsInput => Selection.Fail("直すところがあるため確定できません（" + string.Join("／", state.Classification.Reasons.Select(r => r.Message)) + "）"),
                _ => new Selection([state], 0, null, FailOnInvalid: true),
            };
        });
    }

    /// <summary>
    /// 「使える商品をまとめて確定」（09 の 5.1・5.2）。確定の直前に分類と 5.2 の条件を求め直し、当たる「使える」の商品だけを確定する。
    /// </summary>
    /// <param name="menuId">献立</param>
    /// <param name="itemIds">確かめのダイアログに並べた商品（null なら、まとめに入る「使える」の商品すべて）</param>
    /// <param name="editingItemIds">画面で行を開いて直している途中の商品（まとめに入れない）</param>
    /// <returns>確定した件数と確定しなかった件数（itemIds を渡したときはそのうち確定しなかった件数、null なら「使える」でまとめに入らなかった件数）</returns>
    public async Task<Result<FlyerConfirmResult>> ConfirmAllUsableAsync(string? menuId, IReadOnlyList<string>? itemIds = null, IReadOnlyList<string>? editingItemIds = null)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerConfirmResult>.Error(error!);
        }

        return await ConfirmCoreAsync(menu!, session, ToSet(editingItemIds), states => SelectBulk(states, FlyerItemCategory.Usable, itemIds));
    }

    /// <summary>
    /// 「新しい商品をまとめて登録」（09 の 5.1・5.2、D-98 のダイアログ）。職員がダイアログで印を付けた商品のうち、確定の直前に分類と 5.2 の条件を
    /// 求め直して当たる「要確認」の商品だけを、Claude の候補（直していればその値）で新しく登録する。
    /// </summary>
    /// <param name="menuId">献立</param>
    /// <param name="itemIds">登録する商品（必須）</param>
    /// <param name="editingItemIds">画面で行を開いて直している途中の商品（まとめに入れない）</param>
    public async Task<Result<FlyerConfirmResult>> ConfirmAllNewAsync(string? menuId, IReadOnlyList<string>? itemIds, IReadOnlyList<string>? editingItemIds = null)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerConfirmResult>.Error(error!);
        }

        if (itemIds is null || itemIds.Count == 0)
        {
            return Result<FlyerConfirmResult>.Error("登録する商品を選んでください");
        }

        return await ConfirmCoreAsync(menu!, session, ToSet(editingItemIds), states => SelectBulk(states, FlyerItemCategory.New, itemIds));
    }

    /// <summary>
    /// 「表記なし」と読んだ商品を、職員がチラシで見比べたことを残す（まとめての見比べ。D-97・FR-FLYER-04a）。
    /// 取り込みのメモリにだけ持ち、記録（allergen_checks）や確定の内容には使わない。まとめに入れてよいかの判断にだけ使う。
    /// 見比べに並べられない商品（読み取りの検証ができなかった・確定済み・外した・表記のある商品）は飛ばす。印を付けた件数を返す
    /// </summary>
    public async Task<Result<int>> MarkLabelAbsentComparedAsync(string? menuId, IReadOnlyList<string>? itemIds)
    {
        var (_, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<int>.Error(error!);
        }

        if (itemIds is null || itemIds.Count == 0)
        {
            return Result<int>.Error("見比べた商品を選んでください");
        }

        var ids = itemIds.ToHashSet(StringComparer.Ordinal);
        var context = await LoadContextAsync();
        var count = 0;
        lock (session.SyncRoot)
        {
            foreach (var item in session.Items.Where(i => ids.Contains(i.Id) && !i.IsRemoved && i.Confirmation is null))
            {
                var classification = Classify(session, item, context, editing: NoEditing).Classification;
                if (!classification.IsLabelAbsent || !classification.CanMarkLabelAbsentCompared)
                {
                    continue;
                }

                item.IsLabelAbsentCompared = true;
                count++;
            }
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
    /// 確定の本体。分類を求め直して対象を選び（select）、確定の内容を確かめてから、DB の変更（食い違いの記録・商品の更新か登録・確定の記録・写し）を
    /// 組み立てて1回で保存する。保存できてから取り込みのメモリを変える。
    /// 1件の確定（FailOnInvalid）で確かめに失敗したら、何も変えずにエラーを返す（まとめての確定では、その商品を飛ばす）
    /// </summary>
    private async Task<Result<FlyerConfirmResult>> ConfirmCoreAsync(Menu menu, FlyerImportSession session, IReadOnlySet<string> editing, Func<IReadOnlyList<ItemState>, Selection> select)
    {
        await session.WriteGate.WaitAsync();
        try
        {
            return await ConfirmCoreLockedAsync(menu, session, editing, select);
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>ConfirmCoreAsync の中身（WriteGate を持っている間だけ呼ぶ）</summary>
    private async Task<Result<FlyerConfirmResult>> ConfirmCoreLockedAsync(Menu menu, FlyerImportSession session, IReadOnlySet<string> editing, Func<IReadOnlyList<ItemState>, Selection> select)
    {
        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result<FlyerConfirmResult>.Error("ログインが必要です。ログインし直してください");
        }

        var context = await LoadContextAsync();
        List<ItemState> states;
        Selection selection;
        lock (session.SyncRoot)
        {
            states = session.Items
                .Where(i => !i.IsRemoved && i.Confirmation is null)
                .Select(i => Classify(session, i, context, editing))
                .ToList();
            selection = select(states);
        }

        if (selection.Error is not null)
        {
            return Result<FlyerConfirmResult>.Error(selection.Error);
        }

        // ここで、確定の内容の確かめ（エラーを返すもの）をすべて済ませる。DB の変更はこのあとでだけ組み立てる
        // （エラーで返したときに、追跡中の変更が残って次の保存に混ざらないように。09 の 5.2）
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var skipped = selection.SkippedCount;
        var plans = new List<ConfirmPlan>();
        foreach (var state in selection.Targets)
        {
            var (plan, planError) = await PlanConfirmAsync(state, context, now, user);
            if (plan is not null)
            {
                plans.Add(plan);
                continue;
            }

            if (selection.FailOnInvalid)
            {
                return Result<FlyerConfirmResult>.Error(planError!);
            }

            skipped++;
        }

        if (plans.Count == 0)
        {
            return Result<FlyerConfirmResult>.Ok(new FlyerConfirmResult(0, skipped, [], []));
        }

        // ここから下ではエラーを返さない
        // 1. 確認済みとの食い違いの「参考」の記録（使えない・未確定を含む、食い違いのある全商品の分。09 の 3.5）
        var recorded = AddDiscrepancyRecords(states, context, now, user);

        // 2. 確定する商品
        var clearFirst = session.IsReimport && !session.HasConfirmedAny;
        var confirmations = new List<(FlyerImportItem Item, FlyerItemConfirmation Confirmation, string? CreatedProductId)>();
        foreach (var plan in plans)
        {
            var confirmation = ApplyConfirm(menu, plan, context, now, user, clearFirst);
            clearFirst = false;
            confirmations.Add((plan.State.Item, confirmation, plan.Kind == FlyerConfirmationKind.CreatedNew ? plan.Product.Id : null));
        }

        Touch(menu, now, user);
        await _unitOfWork.SaveChangesAsync();

        // 保存できてから、取り込みの状態を変える（保存に失敗したら、確定していない扱いのまま）
        lock (session.SyncRoot)
        {
            session.HasConfirmedAny = true;
            foreach (var (item, confirmation, createdId) in confirmations)
            {
                item.Confirmation = confirmation;
                if (createdId is not null)
                {
                    item.CreatedProductId = createdId;
                }
            }

            foreach (var record in recorded)
            {
                record.Item.RecordedDiscrepancyKeys.Add(record.Key);
                foreach (var allergen in record.Allergens.Where(a => !record.Item.RecordedDiscrepancyAllergens.Contains(a)))
                {
                    record.Item.RecordedDiscrepancyAllergens.Add(allergen);
                }
            }
        }

        var recordedProducts = recorded
            .GroupBy(r => r.Product.Id, StringComparer.Ordinal)
            .Select(g => new FlyerRecordedProductView(g.Key, g.First().Product.Name, g.SelectMany(r => r.Allergens).Distinct().ToList()))
            .ToList();
        return Result<FlyerConfirmResult>.Ok(new FlyerConfirmResult(
            confirmations.Count,
            skipped,
            confirmations.Select(c => c.Item.Id).ToList(),
            recordedProducts));
    }

    /// <summary>まとめての確定の対象を選ぶ（分類が category で、5.2 の条件に当たらないもの）</summary>
    private static Selection SelectBulk(IReadOnlyList<ItemState> states, FlyerItemCategory category, IReadOnlyList<string>? itemIds)
    {
        var candidates = states.Where(s => s.Classification.Category == category).ToList();
        if (itemIds is null)
        {
            var targets = candidates.Where(s => s.Classification.CanBulk).ToList();
            return new Selection(targets, candidates.Count - targets.Count, null, FailOnInvalid: false);
        }

        var ids = itemIds.Distinct(StringComparer.Ordinal).ToList();
        var selected = candidates.Where(s => s.Classification.CanBulk && ids.Contains(s.Item.Id)).ToList();
        return new Selection(selected, ids.Count - selected.Count, null, FailOnInvalid: false);
    }

    /// <summary>1件の確定の内容を確かめて組み立てる（DB は変えない）。確定できなければエラーの文を返す</summary>
    private async Task<(ConfirmPlan? Plan, string? Error)> PlanConfirmAsync(ItemState state, ImportContext context, DateTime now, CurrentUserInfo user)
    {
        var read = state.Item.Read;
        var classification = state.Classification;
        if (classification.PiecesPerPack is not >= 1)
        {
            return (null, "入数（1パックで何人分か）を入れてください");
        }

        if (read.PriceIncludingTax is not >= 1)
        {
            return (null, "税込価格を入れてください");
        }

        if (read.PriceExcludingTax is < 1)
        {
            return (null, "税抜価格は1以上の整数で入れてください");
        }

        var allergenInput = AllergenInputOf(state.Item, context, ignoreUnresolved: false);
        if (allergenInput.UnresolvedLabelTerms.Count > 0)
        {
            return (null, $"対応づけできていないアレルゲンの表記（{string.Join("・", allergenInput.UnresolvedLabelTerms)}）があります。品目を選ぶか「対応する品目なし」を選んでから確定してください");
        }

        if (classification.Category == FlyerItemCategory.Usable)
        {
            return (new ConfirmPlan(state, FlyerConfirmationKind.UpdatedExisting, state.Product!, IsCreated: false, [], allergenInput), null);
        }

        // 要確認：新しい商品として登録する項目がそろっているか
        if (string.IsNullOrWhiteSpace(read.Name))
        {
            return (null, "商品名を入れてください");
        }

        if (read.SnackType is not { } snackType || !Enum.IsDefined(snackType))
        {
            return (null, "おやつの分類を選んでください（行を開いて直してから確定してください）");
        }

        // 前にこの行で新しく登録して、確定を取り消した商品があれば、それを直して使う（選び直すたびに商品が増えないように）
        var reused = state.Item.CreatedProductId is { } createdId ? await _products.GetAsync(createdId) : null;
        var product = reused ?? new Product { UpdateDateTime = now };
        IReadOnlyList<ProductTag> tags = [];
        if (reused is null)
        {
            var created = AllergenCheckFactory.CreateTags(
                product.Id,
                read.CharacterTags.Select(t => new ProductTagInput(ProductTagKind.Character, t))
                    .Concat(read.FlavorTags.Select(t => new ProductTagInput(ProductTagKind.Flavor, t))),
                ProductTagSource.Claude,
                now,
                user.UserId);
            if (created.HasError)
            {
                return (null, created.ErrorMessages[0]);
            }

            tags = created.Value!;
        }

        return (new ConfirmPlan(state, FlyerConfirmationKind.CreatedNew, product, IsCreated: reused is null, tags, allergenInput), null);
    }

    /// <summary>確かめ済みの確定を DB の変更として組み立てる（保存はしない。エラーは返さない）</summary>
    private FlyerItemConfirmation ApplyConfirm(Menu menu, ConfirmPlan plan, ImportContext context, DateTime now, CurrentUserInfo user, bool clearFirst)
    {
        var item = plan.State.Item;
        var read = item.Read;
        var product = plan.Product;
        var pieces = plan.State.Classification.PiecesPerPack!.Value;
        if (plan.Kind == FlyerConfirmationKind.UpdatedExisting)
        {
            // 価格・税抜・注文番号・最後に載った週を上書きする（過去の週の取り込みでは上書きしない。FR-FLYER-07）
            var overwritten = ListedProductsUpdater.ApplyListing(product, menu.WeekStart, read.PriceIncludingTax, read.PriceExcludingTax, Blank(read.OrderNo));
            // 再入力で確かめた内容量・入数を上書きする（FR-IMPORT-12）。おやつの分類・タグはマスタの値のまま（09 の 5.1）
            if ((overwritten || product.Volume is null) && Blank(read.Volume) is { } volume)
            {
                product.Volume = volume;
            }

            if (overwritten || product.PiecesPerPack is null)
            {
                product.PiecesPerPack = pieces;
            }
        }
        else
        {
            // 新しい商品は Claude の候補（直していればその値）で登録する
            product.Name = read.Name.Trim();
            product.NormalizedName = TextNormalizer.ForProductMatch(product.Name);
            product.Maker = Blank(read.Maker);
            product.Volume = Blank(read.Volume);
            product.PiecesPerPack = pieces;
            product.SnackType = read.SnackType!.Value;
            product.Price = read.PriceIncludingTax;
            product.PriceExcludingTax = read.PriceExcludingTax;
            product.OrderNo = Blank(read.OrderNo);
            product.TargetAgeLabel = Blank(read.TargetAgeLabel);
            product.ListedWeekStart = menu.WeekStart;
            product.IsDisabled = false;
            if (plan.IsCreated)
            {
                product.Tags.AddRange(plan.Tags);
                _products.Add(product);
            }
        }

        product.UpdateDateTime = now;
        product.UpdateUserId = user.UserId;

        // チラシの品目から「参考」の記録を足すか（03 §3 の表）。食い違いの記録はもう足してあるので、ここでは重ならない
        var allergenPlan = FlyerAllergenPlanner.Plan(plan.AllergenInput, context.Allergens, context.AvoidedIds, product.AllergenChecks);
        var addReference = allergenPlan.CanConfirm && allergenPlan.ShouldAddReference();
        if (addReference)
        {
            AddReferenceCheck(product, item, allergenPlan, context, now, user, note: null);
        }

        ListedProductsUpdater.ApplyConfirmation(menu, new ListedProduct(product.Id, read.PriceIncludingTax, Blank(read.OrderNo)), clearFirst);
        return new FlyerItemConfirmation(plan.Kind, product.Id, product.Name, addReference);
    }

    /// <summary>
    /// 確認済みの商品とチラシの表記の食い違い（09 の 3.5）の「参考」の記録を足す（保存はしない）。
    /// 使えない・未確定を含む、外していない未確定の全商品が対象。品目の分からない語が残っていても、対応づけできた品目で足す（記録は確認を増やす方向だけ）。
    /// 同じ食い違いで2回足さない（足した記録は有効な記録に入るので、足したあとは食い違いがなくなる。鍵でも確かめる）
    /// </summary>
    private List<RecordedDiscrepancy> AddDiscrepancyRecords(IReadOnlyList<ItemState> states, ImportContext context, DateTime now, CurrentUserInfo user)
    {
        var recorded = new List<RecordedDiscrepancy>();
        // 同じ商品に一致する行が2つ以上あっても、1回の保存で同じ記録を2件足さない
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in states.Where(s => s.Product is not null && s.Classification.Discrepancies.Count > 0))
        {
            var product = state.Product!;
            var plan = FlyerAllergenPlanner.Plan(AllergenInputOf(state.Item, context, ignoreUnresolved: true), context.Allergens, context.AvoidedIds, product.AllergenChecks);
            if (plan.Discrepancies.Count == 0)
            {
                continue;
            }

            var key = product.Id + "|" + string.Join(",", plan.FlyerAllergens.Select(a => a.Id).Order(StringComparer.Ordinal));
            if (state.Item.RecordedDiscrepancyKeys.Contains(key) || !addedKeys.Add(key))
            {
                continue;
            }

            AddReferenceCheck(product, state.Item, plan, context, now, user, note: "パッケージでの確認との食い違い");
            recorded.Add(new RecordedDiscrepancy(state.Item, product, key, plan.Discrepancies.Select(d => d.Allergen).ToList()));
        }

        return recorded;
    }

    /// <summary>チラシの品目を「含む」とした「参考」の記録を足す（保存はしない）。職員が外した表記の品目を補足に残す（SR-19）</summary>
    private void AddReferenceCheck(Product product, FlyerImportItem item, FlyerAllergenPlan plan, ImportContext context, DateTime now, CurrentUserInfo user, string? note)
    {
        var check = FlyerAllergenPlanner.CreateReferenceCheck(product.Id, plan, now, user.UserName, user.UserId);
        if (note is not null)
        {
            check.SourceNote += "／" + note;
        }

        var removedLabels = item.OriginalLabelAllergenIds.Except(item.LabelAllergenIds, StringComparer.Ordinal).ToList();
        if (removedLabels.Count > 0)
        {
            check.SourceNote += "／職員が外した品目：" + string.Join("・", removedLabels.Select(id => context.AllergenName(id)));
        }

        _products.AddCheck(check);
        product.AllergenChecks.Add(check);
    }

    /// <summary>
    /// チラシの品目を求める入力（分類と同じ）：表記の品目 ∪ 職員が選んだ品目、探す文は読んだ商品名・直した商品名・読んだ説明文（直して当たりを消せない。SR-14）。
    /// </summary>
    private static FlyerAllergenInput AllergenInputOf(FlyerImportItem item, ImportContext context, bool ignoreUnresolved)
    {
        var labelIds = item.LabelAllergenIds
            .Concat(item.ResolvedTerms.Values.SelectMany(ids => ids))
            .Where(context.AllergenIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var texts = new[] { item.OriginalRead.Name, item.Read.Name }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .Append(item.OriginalRead.Description ?? string.Empty)
            .Where(t => t.Length > 0);
        IReadOnlyCollection<string> unresolved = ignoreUnresolved
            ? []
            : item.UnmappedTerms.Where(t => !string.IsNullOrWhiteSpace(t) && !item.ResolvedTerms.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
        return new FlyerAllergenInput(labelIds, item.Read.AllergenLabelText, string.Join("\n", texts), unresolved);
    }

    /// <summary>1商品を分類する（照合はここで今の商品名・メーカーで行う）</summary>
    private static ItemState Classify(FlyerImportSession session, FlyerImportItem item, ImportContext context, IReadOnlySet<string> editing)
    {
        // この行で新しく登録して確定を取り消した（使わないにした）商品とは照合しない（自分が作った商品で「使えない」にならないように）
        var products = item.CreatedProductId is { } createdId
            ? context.Products.Where(p => !(p.Id == createdId && p.IsDisabled)).ToList()
            : context.Products;
        var match = ProductMatcher.Match(item.Read.Name, item.Read.Maker, products);
        var product = match.IsMatched ? match.Product : null;
        var judgement = product is null ? null : AllergenJudge.Judge(product, context.Avoided, context.Today, context.RecheckDays);
        var classification = FlyerItemClassifier.Classify(new FlyerClassificationInput
        {
            OriginalRead = item.OriginalRead,
            Read = item.Read,
            EditedFields = item.EditedFields,
            IsPiecesEnteredByStaff = item.IsPiecesEnteredByStaff,
            LabelAllergenIds = item.LabelAllergenIds.Where(context.AllergenIds.Contains).ToList(),
            UnmappedTerms = item.UnmappedTerms,
            ResolvedTerms = item.ResolvedTerms.ToDictionary(
                p => p.Key,
                p => (IReadOnlyCollection<string>)p.Value.Where(context.AllergenIds.Contains).ToList(),
                StringComparer.Ordinal),
            ReviewNotes = item.ReviewNotes.ToList(),
            DuplicateConflicts = item.DuplicateConflicts,
            AcceptedReasonKeys = item.AcceptedReasonKeys,
            Match = match,
            MatchedJudgement = judgement,
            AllAllergens = context.Allergens,
            AvoidedAllergens = context.Avoided,
            IsManual = item.IsManual,
            ReviewFailed = session.ReviewFailed,
            IsLabelAbsentCompared = item.IsLabelAbsentCompared,
            IsEditing = editing.Contains(item.Id),
        });
        return new ItemState(item, match, product, judgement, classification);
    }

    private FlyerSessionView BuildSessionView(FlyerImportSession session, ImportContext context, IReadOnlySet<string> editing)
    {
        var items = session.Items
            .Where(i => !i.IsRemoved)
            .Select((item, index) => (View: BuildItemView(session, item, context, editing), Index: index))
            .OrderBy(x => x.View.Status)
            .ThenBy(x => x.Index)
            .Select(x => x.View)
            .ToList();
        var removed = session.Items
            .Where(i => i.IsRemoved)
            .Select(i => new FlyerRemovedItemView(i.Id, i.Read.Name, i.Read.Maker, i.Read.Volume, i.RemovedReason ?? "外しました", i.OriginalRead.IsNonFood))
            .ToList();

        int CountOf(FlyerItemStatus status) => items.Count(v => v.Status == status);
        var counts = new FlyerCategoryCounts(
            CountOf(FlyerItemStatus.NeedsInput),
            CountOf(FlyerItemStatus.New),
            CountOf(FlyerItemStatus.Usable),
            CountOf(FlyerItemStatus.Unusable),
            CountOf(FlyerItemStatus.Confirmed),
            removed.Count);
        var unconfirmed = counts.New + counts.Usable;
        var notListed = new FlyerNotListedCounts(counts.Unusable + counts.NeedsInput + unconfirmed, counts.Unusable, counts.NeedsInput, unconfirmed);

        return new FlyerSessionView(
            session.Id,
            session.IsReimport,
            session.ReviewFailed,
            session.MissedProducts.ToList(),
            items,
            removed,
            counts,
            BulkSummary(items, FlyerItemStatus.Usable),
            BulkSummary(items, FlyerItemStatus.New),
            notListed,
            counts.NeedsInput + counts.New + counts.Usable + counts.Unusable);
    }

    private static FlyerBulkSummary BulkSummary(IReadOnlyList<FlyerItemView> items, FlyerItemStatus status)
    {
        var candidates = items.Where(v => v.Status == status).ToList();
        var targets = candidates.Where(v => v.CanBulk).Select(v => v.Id).ToList();
        var exclusions = candidates
            .SelectMany(v => v.BulkExclusions)
            .GroupBy(e => e)
            .OrderBy(g => g.Key)
            .Select(g => new FlyerBulkExclusionCount(g.Key, BulkExclusionMessage(g.Key), g.Count()))
            .ToList();
        return new FlyerBulkSummary(targets.Count, targets, candidates.Count - targets.Count, exclusions);
    }

    /// <summary>まとめに入らない理由の文（09 の 5.2）</summary>
    public static string BulkExclusionMessage(FlyerBulkExclusion exclusion) => exclusion switch
    {
        FlyerBulkExclusion.LabelAbsentNotCompared => "アレルゲンの表記なしと読み、まだチラシと見比べていない商品",
        FlyerBulkExclusion.Manual => "手で入力した商品",
        FlyerBulkExclusion.PiecesEnteredByStaff => "入数を入れて戻した商品（読み取りの検証を通っていません）",
        FlyerBulkExclusion.ReviewFailed => "読み取りの検証ができなかった取り込みの商品",
        FlyerBulkExclusion.Editing => "行を開いて直している途中の商品",
        FlyerBulkExclusion.MissingNewProductFields => "おやつの分類か入数の候補がない商品",
        _ => exclusion.ToString(),
    };

    private FlyerItemView BuildItemView(FlyerImportSession session, FlyerImportItem item, ImportContext context, IReadOnlySet<string> editing)
    {
        var labelAllergens = item.LabelAllergenIds
            .Where(context.AllergenIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new AllergenRef(id, context.AllergenName(id)))
            .ToList();
        var resolvedTerms = item.ResolvedTerms
            .Select(p => new FlyerResolvedTermView(p.Key, p.Value.Where(context.AllergenIds.Contains).Select(id => new AllergenRef(id, context.AllergenName(id))).ToList()))
            .ToList();
        var descriptionHits = AllergenKeywordFinder.Find(item.OriginalRead.Description, context.Avoided)
            .Select(h => h.MatchedText)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (item.Confirmation is { } confirmation)
        {
            var confirmed = context.ProductsById.GetValueOrDefault(confirmation.ProductId);
            return new FlyerItemView(
                item.Id,
                FlyerItemStatus.Confirmed,
                null,
                item.IsManual,
                item.IsPiecesEnteredByStaff,
                item.Read,
                item.OriginalRead,
                item.EditedFields.Order(StringComparer.Ordinal).ToList(),
                [],
                [],
                labelAllergens,
                item.AbbreviationHits,
                item.UnmappedTerms,
                resolvedTerms,
                [],
                descriptionHits,
                [],
                [],
                item.RecordedDiscrepancyAllergens.ToList(),
                false,
                false,
                item.IsLabelAbsentCompared,
                confirmed is null ? null : MatchedView(confirmed, context),
                0,
                null,
                confirmed?.PiecesPerPack ?? item.Read.PiecesPerPackCandidate,
                item.DuplicateConflicts,
                Math.Max(1, item.SourceIndexes.Count),
                false,
                [],
                item.Confirmation);
        }

        var state = Classify(session, item, context, editing);
        var classification = state.Classification;
        return new FlyerItemView(
            item.Id,
            StatusOf(classification.Category),
            classification.Category,
            item.IsManual,
            item.IsPiecesEnteredByStaff,
            item.Read,
            item.OriginalRead,
            item.EditedFields.Order(StringComparer.Ordinal).ToList(),
            classification.Reasons,
            classification.Notices,
            labelAllergens,
            item.AbbreviationHits,
            item.UnmappedTerms,
            resolvedTerms,
            classification.UnresolvedTerms,
            descriptionHits,
            classification.FlyerAllergens,
            classification.Discrepancies,
            item.RecordedDiscrepancyAllergens.ToList(),
            classification.IsLabelAbsent,
            classification.CanMarkLabelAbsentCompared,
            item.IsLabelAbsentCompared,
            state.Product is null ? null : MatchedView(state.Product, context, state.Judgement),
            state.Match.OtherMatches.Count,
            state.Match.NotMatchableReason,
            classification.PiecesPerPack,
            item.DuplicateConflicts,
            Math.Max(1, item.SourceIndexes.Count),
            classification.CanBulk,
            classification.BulkExclusions,
            null);
    }

    private static FlyerMatchedProductView MatchedView(Product product, ImportContext context, AllergenJudgement? judgement = null)
    {
        judgement ??= AllergenJudge.Judge(product, context.Avoided, context.Today, context.RecheckDays);
        return new FlyerMatchedProductView(
            product.Id,
            product.Name,
            product.Maker,
            product.Volume,
            product.PiecesPerPack,
            product.IsDisabled,
            product.ListedWeekStart,
            AllergenJudgementView.From(product, judgement, context.Today));
    }

    private static FlyerItemStatus StatusOf(FlyerItemCategory category) => category switch
    {
        FlyerItemCategory.NeedsInput => FlyerItemStatus.NeedsInput,
        FlyerItemCategory.New => FlyerItemStatus.New,
        FlyerItemCategory.Usable => FlyerItemStatus.Usable,
        _ => FlyerItemStatus.Unusable,
    };

    /// <summary>
    /// 読み取った商品を取り込みの商品にする。食品以外は外した掲載品に入れ（FR-IMPORT-20）、食品は二重の読み取りをまとめる（FR-IMPORT-06）。
    /// 元の読み取りの番号 → 取り込みの商品 を返す（読み取りの検証の指摘を付け替えるため）
    /// </summary>
    private static Dictionary<int, FlyerImportItem> BuildReadItems(FlyerImportSession session, IReadOnlyList<FlyerReadProduct> reads, IReadOnlyList<Allergen> allergens)
    {
        var itemsByIndex = new Dictionary<int, FlyerImportItem>();
        var foodIndexes = Enumerable.Range(0, reads.Count).Where(i => !reads[i].IsNonFood).ToList();
        foreach (var merged in FlyerDuplicateMerger.Merge(foodIndexes.Select(i => reads[i]).ToList()))
        {
            var sourceIndexes = merged.SourceIndexes.Select(i => foodIndexes[i]).ToList();
            var item = BuildItem(merged.Read, allergens, isManual: false, sourceIndexes, merged.Conflicts);
            session.Items.Add(item);
            foreach (var index in sourceIndexes)
            {
                itemsByIndex[index] = item;
            }
        }

        foreach (var index in Enumerable.Range(0, reads.Count).Where(i => reads[i].IsNonFood))
        {
            var read = reads[index];
            var item = BuildItem(read, allergens, isManual: false, [index], []);
            item.IsRemoved = true;
            item.RemovedReason = string.IsNullOrWhiteSpace(read.NonFoodReason) ? "食品以外の掲載品です" : "食品以外：" + read.NonFoodReason.Trim();
            session.Items.Add(item);
            itemsByIndex[index] = item;
        }

        return itemsByIndex;
    }

    /// <summary>読み取った1商品を、取り込みの商品にする（アレルゲン表記はアプリでもう一度対応づけ、略記を補正する）</summary>
    private static FlyerImportItem BuildItem(FlyerReadProduct read, IReadOnlyList<Allergen> allergens, bool isManual, IReadOnlyList<int> sourceIndexes, IReadOnlyList<FlyerDuplicateConflict> conflicts)
    {
        var byCode = allergens.ToDictionary(a => a.Code, StringComparer.Ordinal);
        var parsed = FlyerAllergenPlanner.ParseLabel(read.AllergenLabelText, allergens);

        // Claude が対応づけられなかった語も、マスタの略記（「麦」→小麦など）に当てる（D-96）。
        // 当たった品目は足すだけで、Claude の品目は消さない。すべての部分が略記に当たった語だけを未対応から外す
        var claudeUnmapped = new List<string>();
        var abbreviationIds = new List<string>();
        var abbreviationHits = parsed.AbbreviationHits.ToList();
        foreach (var term in read.UnmappedAllergenTerms)
        {
            var match = FlyerAllergenPlanner.MatchAbbreviations(term, allergens);
            abbreviationIds.AddRange(match.Allergens.Select(a => a.Id));
            abbreviationHits.AddRange(match.Hits);
            if (!match.IsFullyMapped)
            {
                claudeUnmapped.Add(term);
            }
        }

        var labelIds = read.AllergenCodes
            .Where(byCode.ContainsKey)
            .Select(code => byCode[code].Id)
            .Concat(parsed.Allergens.Select(a => a.Id))
            .Concat(abbreviationIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Claude が対応づけた品目も、アプリが対応づけた品目もすべて採る（確認する側に倒す）。対応づけできない語は残す
        var unmapped = parsed.UnmappedTerms
            .Concat(claudeUnmapped)
            .Concat(read.AllergenCodes.Where(code => !byCode.ContainsKey(code)))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new FlyerImportItem
        {
            IsManual = isManual,
            OriginalRead = read,
            Read = read,
            SourceIndexes = sourceIndexes,
            DuplicateConflicts = conflicts,
            OriginalLabelAllergenIds = labelIds.ToList(),
            LabelAllergenIds = labelIds,
            UnmappedTerms = unmapped,
            AbbreviationHits = abbreviationHits.Distinct().ToList(),
        };
    }

    /// <summary>
    /// 読み取りの検証の指摘を付ける（書き換えはしない）。番号はまとめる前の一覧の番号なので、まとめた商品に付け替える。
    /// アレルゲンは確認を増やす方向の指摘だけ採用する（SR-19）：読み落としたアレルゲン表記は、表記の品目に足す（減らす指摘は受け付けない形にしてある）。
    /// アプリの検証（FlyerReadingValidator）の指摘は分類が自分で求めるので、ここでは足さない（二重になるため）
    /// </summary>
    private static void ApplyReview(FlyerImportSession session, FlyerReview review, IReadOnlyList<Allergen> allergens, IReadOnlyDictionary<int, FlyerImportItem> itemsByIndex)
    {
        foreach (var note in review.Notes)
        {
            if (itemsByIndex.GetValueOrDefault(note.Index) is { } item)
            {
                AddReviewNote(item, note.Field, note.Reason);
            }
        }

        foreach (var missed in review.MissedAllergenLabels)
        {
            if (itemsByIndex.GetValueOrDefault(missed.Index) is not { } item)
            {
                continue;
            }

            var parsed = FlyerAllergenPlanner.ParseLabel(missed.LabelText, allergens);
            foreach (var allergen in parsed.Allergens.Where(a => !item.LabelAllergenIds.Contains(a.Id)))
            {
                item.LabelAllergenIds.Add(allergen.Id);
                item.OriginalLabelAllergenIds.Add(allergen.Id);
            }

            foreach (var term in parsed.UnmappedTerms.Where(t => !item.UnmappedTerms.Contains(t)))
            {
                item.UnmappedTerms.Add(term);
            }

            item.AbbreviationHits.AddRange(parsed.AbbreviationHits.Where(h => !item.AbbreviationHits.Contains(h)));
            AddReviewNote(item, "アレルゲン", $"アレルゲンの表記「{missed.LabelText}」を読み落としている可能性があります（{missed.Reason}）。チラシと見比べてください");
        }

        // 読み落とした商品は、アプリでは消さない（重なって出ても職員が見比べれば済む方に倒す）
        session.MissedProducts.AddRange(review.MissedProducts);
    }

    private static void AddReviewNote(FlyerImportItem item, string field, string message)
    {
        if (item.ReviewNotes.Any(n => n.Field == field && n.Message == message))
        {
            return;
        }

        item.ReviewNotes.Add(new FlyerReviewNoteInput($"review:{Guid.NewGuid():N}", field, message));
    }

    /// <summary>読み取りの検証の指摘（Claude の項目名）が、直した欄のものか</summary>
    private static bool ReviewNoteMatchesField(string noteField, string field) => field switch
    {
        FlyerItemField.Name => noteField.Contains("商品名", StringComparison.Ordinal),
        FlyerItemField.Maker => noteField.Contains("メーカー", StringComparison.Ordinal),
        FlyerItemField.Volume => noteField.Contains("内容量", StringComparison.Ordinal),
        FlyerItemField.PriceIncludingTax => noteField.Contains("税込", StringComparison.Ordinal)
            || (noteField.Contains("価格", StringComparison.Ordinal) && !noteField.Contains("税抜", StringComparison.Ordinal)),
        FlyerItemField.PriceExcludingTax => noteField.Contains("税抜", StringComparison.Ordinal)
            || (noteField.Contains("価格", StringComparison.Ordinal) && !noteField.Contains("税込", StringComparison.Ordinal)),
        FlyerItemField.OrderNo => noteField.Contains("注文番号", StringComparison.Ordinal),
        FlyerItemField.PiecesPerPack => noteField.Contains("入数", StringComparison.Ordinal),
        FlyerItemField.AllergenLabel => noteField.Contains("アレルゲン", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>「このままでよい」を押した理由が、直した欄に関わるか（直したら押し直してもらう）</summary>
    private static bool AcceptedKeyDependsOn(string key, string field)
    {
        var code = key.Split(':')[0];
        return code switch
        {
            nameof(FlyerItemReasonCode.TaxMismatch) => field is FlyerItemField.PriceIncludingTax or FlyerItemField.PriceExcludingTax,
            nameof(FlyerItemReasonCode.PiecesMismatch) => field is FlyerItemField.PiecesPerPack or FlyerItemField.Volume,
            // 商品名・メーカーを直すと一致する商品が変わることがある
            nameof(FlyerItemReasonCode.MasterVolumeOrPiecesChanged) => field is FlyerItemField.PiecesPerPack or FlyerItemField.Volume or FlyerItemField.Name or FlyerItemField.Maker,
            nameof(FlyerItemReasonCode.LabelAbsentButMasterContains) => field is FlyerItemField.AllergenLabel or FlyerItemField.Name or FlyerItemField.Maker,
            nameof(FlyerItemReasonCode.DuplicateConflict) => key == $"{nameof(FlyerItemReasonCode.DuplicateConflict)}:{field}",
            _ => false,
        };
    }

    private static void Track(List<string> changed, string field, bool isChanged)
    {
        if (isChanged)
        {
            changed.Add(field);
        }
    }

    private async Task<(Menu? Menu, FlyerImportSession? Session, string? Error)> FindSessionAsync(string? menuId)
    {
        var menu = string.IsNullOrEmpty(menuId) ? null : await _menus.GetAsync(menuId);
        var session = menu is null ? null : _sessions.Get(menu.Id);
        return menu is null || session is null ? (menu, null, SessionNotFound) : (menu, session, null);
    }

    /// <summary>直せる商品を探す（外した掲載品・確定済みは直さない）</summary>
    private static (FlyerImportItem? Item, string? Error) FindEditableItem(FlyerImportSession session, string? itemId)
    {
        var item = session.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return (null, ItemNotFound);
        }

        if (item.IsRemoved)
        {
            return (null, "外した掲載品は直せません。「戻す」で一覧に戻してから直してください");
        }

        if (item.Confirmation is not null)
        {
            return (null, "確定済みの商品は直せません。選び直すときは「外す」で確定を取り消し、「戻す」で一覧に戻してください");
        }

        return (item, null);
    }

    private async Task<ImportContext> LoadContextAsync()
    {
        var allergens = await _settings.GetAllergensAsync();
        var avoided = await _settings.GetAvoidedAllergensAsync();
        var setting = await _settings.GetSettingAsync();
        var products = await _products.GetAllAsync();
        return new ImportContext(allergens, avoided, products, JapanDate.Today(_timeProvider), setting.RecheckDays);
    }

    private static Result ValidateValues(int? piecesPerPack, int? priceIncludingTax, int? priceExcludingTax, SnackType? snackType)
    {
        var result = new Result();
        if (piecesPerPack is not null)
        {
            result.ErrorMessages.AddRange(InputValidator.ValidatePiecesPerPack(piecesPerPack).ErrorMessages);
        }

        if (priceIncludingTax is not null)
        {
            result.ErrorMessages.AddRange(InputValidator.ValidatePrice(priceIncludingTax).ErrorMessages);
        }

        if (priceExcludingTax is < 1)
        {
            result.ErrorMessages.Add("税抜価格は1以上の整数で入れてください");
        }

        if (snackType is { } type && !Enum.IsDefined(type))
        {
            result.ErrorMessages.Add("おやつの分類を選び直してください");
        }

        return result;
    }

    private string? ValidateTags(IReadOnlyList<string> characterTags, IReadOnlyList<string> flavorTags)
    {
        var tags = AllergenCheckFactory.CreateTags(
            Guid.NewGuid().ToString(),
            characterTags.Select(t => new ProductTagInput(ProductTagKind.Character, t))
                .Concat(flavorTags.Select(t => new ProductTagInput(ProductTagKind.Flavor, t))),
            ProductTagSource.Claude,
            _timeProvider.GetUtcNow().UtcDateTime,
            null);
        return tags.HasError ? tags.ErrorMessages[0] : null;
    }

    private static IReadOnlySet<string> ToSet(IReadOnlyList<string>? ids) => (ids ?? []).ToHashSet(StringComparer.Ordinal);

    private static void Touch(Menu menu, DateTime now, CurrentUserInfo user)
    {
        // 献立の行を必ず更新して row_version を進める（D-46）
        menu.UpdateDateTime = now;
        menu.UpdateUserId = user.UserId;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>表示・確定に使うマスタと今日</summary>
    private sealed class ImportContext(IReadOnlyList<Allergen> allergens, IReadOnlyList<Allergen> avoided, IReadOnlyList<Product> products, DateOnly today, int recheckDays)
    {
        public IReadOnlyList<Allergen> Allergens { get; } = allergens;

        public IReadOnlyList<Allergen> Avoided { get; } = avoided;

        public IReadOnlyList<string> AvoidedIds { get; } = avoided.Select(a => a.Id).ToList();

        public IReadOnlyList<Product> Products { get; } = products;

        public IReadOnlyDictionary<string, Product> ProductsById { get; } = products.DistinctBy(p => p.Id, StringComparer.Ordinal).ToDictionary(p => p.Id, StringComparer.Ordinal);

        public HashSet<string> AllergenIds { get; } = allergens.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        public DateOnly Today { get; } = today;

        public int RecheckDays { get; } = recheckDays;

        public string AllergenName(string id) => Allergens.FirstOrDefault(a => a.Id == id)?.Name ?? id;
    }

    /// <summary>1商品の分類（照合の結果と一致した商品の判定つき）</summary>
    private sealed record ItemState(FlyerImportItem Item, ProductMatch Match, Product? Product, AllergenJudgement? Judgement, FlyerItemClassification Classification);

    /// <summary>確定の対象（FailOnInvalid：確かめに失敗したらエラーにする＝1件の確定）</summary>
    private sealed record Selection(IReadOnlyList<ItemState> Targets, int SkippedCount, string? Error, bool FailOnInvalid)
    {
        public static Selection Fail(string error) => new([], 0, error, FailOnInvalid: true);
    }

    /// <summary>確かめ済みの1件の確定の内容</summary>
    private sealed record ConfirmPlan(ItemState State, FlyerConfirmationKind Kind, Product Product, bool IsCreated, IReadOnlyList<ProductTag> Tags, FlyerAllergenInput AllergenInput);

    /// <summary>食い違いで足した記録</summary>
    private sealed record RecordedDiscrepancy(FlyerImportItem Item, Product Product, string Key, IReadOnlyList<AllergenRef> Allergens);
}

/// <summary>取り込みの設定</summary>
public sealed class FlyerImportOptions
{
    /// <summary>
    /// 読み取りの処理全体の期限（呼び出しのタイムアウトと再試行の合計より長くする。U-48）。
    /// 読み取り 420秒×3回 ＋ 読み取りの検証 300秒×3回 ＝ 36分に余裕を足した長さ（D-100 で照合の判定（Haiku）はやめた）
    /// </summary>
    public TimeSpan JobDeadline { get; set; } = TimeSpan.FromMinutes(45);
}

/// <summary>
/// 行を直す入力（フォームの行のすべての値。今の値と違う項目だけを「直した項目」にする）。
/// 文字の項目は空なら「値なし」。品目・タグは null なら変えない。
/// </summary>
public sealed record FlyerItemEditInput(
    string? MenuId,
    string? ItemId,
    string? Name,
    string? Maker,
    string? Volume,
    int? PriceIncludingTax,
    int? PriceExcludingTax,
    string? OrderNo,
    int? PiecesPerPack,
    SnackType? SnackType,
    string? TargetAgeLabel,
    IReadOnlyList<string>? CharacterTags = null,
    IReadOnlyList<string>? FlavorTags = null,
    IReadOnlyList<string>? LabelAllergenIds = null);

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

/// <summary>
/// 確定前の読み取り結果（09 の 6）。
/// </summary>
/// <param name="Id">取り込みの ID</param>
/// <param name="IsReimport">取り込み直しか</param>
/// <param name="ReviewFailed">読み取りの検証ができなかったか</param>
/// <param name="MissedProducts">読み落とした商品の指摘</param>
/// <param name="Items">一覧の商品（再入力 → 要確認 → 使える → 使えない → 確定済み の順。同じ分類の中は読み取りの順）</param>
/// <param name="RemovedItems">外した掲載品（FR-IMPORT-20・21）</param>
/// <param name="Counts">分類ごとの件数（タブ）</param>
/// <param name="BulkUsable">「使える商品をまとめて確定」の対象と、入らない件数・理由</param>
/// <param name="BulkNew">「新しい商品をまとめて登録」の対象と、入らない件数・理由</param>
/// <param name="NotListed">今週の商品に入らない件数（FR-FLYER-06b）</param>
/// <param name="UnconfirmedCount">まだ確定していない件数（外した掲載品を除く）</param>
public sealed record FlyerSessionView(
    string Id,
    bool IsReimport,
    bool ReviewFailed,
    IReadOnlyList<FlyerMissedProduct> MissedProducts,
    IReadOnlyList<FlyerItemView> Items,
    IReadOnlyList<FlyerRemovedItemView> RemovedItems,
    FlyerCategoryCounts Counts,
    FlyerBulkSummary BulkUsable,
    FlyerBulkSummary BulkNew,
    FlyerNotListedCounts NotListed,
    int UnconfirmedCount);

/// <summary>一覧での並びと表示の状態（値の順が一覧の並び。JSON でもこの名前）</summary>
public enum FlyerItemStatus
{
    /// <summary>🟠 再入力</summary>
    NeedsInput,

    /// <summary>🔵 要確認（新しい商品）</summary>
    New,

    /// <summary>🟢 使える</summary>
    Usable,

    /// <summary>🔴 使えない</summary>
    Unusable,

    /// <summary>確定済み</summary>
    Confirmed,
}

/// <summary>
/// 取り込みの1商品の表示。
/// </summary>
/// <param name="Id">ID</param>
/// <param name="Status">一覧の状態（確定済みを含む）</param>
/// <param name="Category">取り込みの分類（確定済みなら null）</param>
/// <param name="IsManual">手入力した商品か</param>
/// <param name="IsPiecesEnteredByStaff">「使えない（入数が分からない）」に職員が入数を入れたか</param>
/// <param name="Read">今の値（直した値を反映したもの）</param>
/// <param name="OriginalRead">Claude が読んだまま（二重の読み取りをまとめたあと）</param>
/// <param name="EditedFields">職員が直した項目（FlyerItemField の値）</param>
/// <param name="Reasons">使えない・再入力の理由（アレルゲンに関わる理由が先）</param>
/// <param name="Notices">知らせるだけのこと（確認済みとの食い違い）</param>
/// <param name="LabelAllergens">今の表記の品目</param>
/// <param name="AbbreviationHits">略記で当てた語と品目（「麦→小麦」）</param>
/// <param name="UnmappedTerms">品目に対応づけできなかった表記の語（選んだ語も残る）</param>
/// <param name="ResolvedTerms">職員が品目を選んだ語（品目が空なら「対応する品目なし」）</param>
/// <param name="UnresolvedTerms">まだ品目を選んでいない語</param>
/// <param name="DescriptionHitWords">説明文で当たった避ける品目の探す言葉</param>
/// <param name="FlyerAllergens">チラシの品目（参考の記録に入る品目）</param>
/// <param name="Discrepancies">確認済みとの食い違い（09 の 3.5。確定すると参考の記録を足す）</param>
/// <param name="RecordedDiscrepancyAllergens">食い違いで「参考」の記録を足した品目（確定したあとに「記録を足しました」と知らせる）</param>
/// <param name="IsLabelAbsent">表記なしと読んだか</param>
/// <param name="CanMarkLabelAbsentCompared">表記なしのまとめての見比べに並べてよいか</param>
/// <param name="IsLabelAbsentCompared">見比べの印を付けたか</param>
/// <param name="MatchedProduct">一致した商品（確定済みなら確定に使った商品）</param>
/// <param name="OtherMatchCount">ほかに同じ名前（名前＋メーカー）の商品が何件あるか（FR-IMPORT-04）</param>
/// <param name="NotMatchableReason">照合できない理由（商品名かメーカーが空）</param>
/// <param name="PiecesPerPack">使う入数（読み取りの候補、なければ一致した商品の入数）</param>
/// <param name="DuplicateConflicts">二重の読み取りで食い違った値</param>
/// <param name="SourceCount">まとめた読み取りの数（1なら二重でない）</param>
/// <param name="CanBulk">まとめて確定・まとめて登録に入るか</param>
/// <param name="BulkExclusions">まとめに入らない理由（使える・要確認のときだけ）</param>
/// <param name="Confirmation">確定した内容（未確定なら null）</param>
public sealed record FlyerItemView(
    string Id,
    FlyerItemStatus Status,
    FlyerItemCategory? Category,
    bool IsManual,
    bool IsPiecesEnteredByStaff,
    FlyerReadProduct Read,
    FlyerReadProduct OriginalRead,
    IReadOnlyList<string> EditedFields,
    IReadOnlyList<FlyerItemReason> Reasons,
    IReadOnlyList<FlyerItemReason> Notices,
    IReadOnlyList<AllergenRef> LabelAllergens,
    IReadOnlyList<FlyerAbbreviationHit> AbbreviationHits,
    IReadOnlyList<string> UnmappedTerms,
    IReadOnlyList<FlyerResolvedTermView> ResolvedTerms,
    IReadOnlyList<string> UnresolvedTerms,
    IReadOnlyList<string> DescriptionHitWords,
    IReadOnlyList<AllergenRef> FlyerAllergens,
    IReadOnlyList<FlyerAllergenDiscrepancy> Discrepancies,
    IReadOnlyList<AllergenRef> RecordedDiscrepancyAllergens,
    bool IsLabelAbsent,
    bool CanMarkLabelAbsentCompared,
    bool IsLabelAbsentCompared,
    FlyerMatchedProductView? MatchedProduct,
    int OtherMatchCount,
    ProductNotMatchableReason? NotMatchableReason,
    int? PiecesPerPack,
    IReadOnlyList<FlyerDuplicateConflict> DuplicateConflicts,
    int SourceCount,
    bool CanBulk,
    IReadOnlyList<FlyerBulkExclusion> BulkExclusions,
    FlyerItemConfirmation? Confirmation);

/// <summary>職員が品目を選んだ語（Allergens が空なら「対応する品目なし」）</summary>
public sealed record FlyerResolvedTermView(string Term, IReadOnlyList<AllergenRef> Allergens);

/// <summary>
/// 一致した商品（マスタの値とアレルゲンの表示。03 の 6）。
/// </summary>
/// <param name="Id">商品</param>
/// <param name="Name">商品名</param>
/// <param name="Maker">メーカー</param>
/// <param name="Volume">マスタの内容量（チラシの値は Read.Volume）</param>
/// <param name="PiecesPerPack">マスタの入数（チラシの候補は Read.PiecesPerPackCandidate）</param>
/// <param name="IsDisabled">「使わない」にした商品か</param>
/// <param name="ListedWeekStart">最後にチラシに載った週</param>
/// <param name="Judgement">マスタでのアレルゲンの判定（🔴🟡🟢 と理由）</param>
public sealed record FlyerMatchedProductView(
    string Id,
    string Name,
    string? Maker,
    string? Volume,
    int? PiecesPerPack,
    bool IsDisabled,
    DateOnly? ListedWeekStart,
    AllergenJudgementView Judgement);

/// <summary>外した掲載品（商品名と理由。IsNonFood は Claude が食品以外とした商品）</summary>
public sealed record FlyerRemovedItemView(string Id, string Name, string? Maker, string? Volume, string Reason, bool IsNonFood);

/// <summary>分類ごとの件数</summary>
public sealed record FlyerCategoryCounts(int NeedsInput, int New, int Usable, int Unusable, int Confirmed, int Removed);

/// <summary>まとめての確定・登録の対象（Count・ItemIds）と、その分類でまとめに入らない件数と理由（09 の 5.2）</summary>
public sealed record FlyerBulkSummary(int Count, IReadOnlyList<string> ItemIds, int ExcludedCount, IReadOnlyList<FlyerBulkExclusionCount> Exclusions);

/// <summary>まとめに入らない理由ごとの件数（1商品が複数の理由に数えられることがある）</summary>
public sealed record FlyerBulkExclusionCount(FlyerBulkExclusion Exclusion, string Message, int Count);

/// <summary>今週の商品に入らない件数（Total ＝ 使えない ＋ 再入力 ＋ まだ確定していない。外した掲載品は数えない）</summary>
public sealed record FlyerNotListedCounts(int Total, int Unusable, int NeedsInput, int Unconfirmed);

/// <summary>
/// 確定の結果。
/// </summary>
/// <param name="ConfirmedCount">確定した件数</param>
/// <param name="SkippedCount">確定しなかった件数（まとめての確定で、条件から外れた・見つからない商品）</param>
/// <param name="ConfirmedItemIds">確定した取り込みの商品</param>
/// <param name="RecordedProducts">確認済みとの食い違いで「参考」の記録を足した商品（09 の 3.5）</param>
public sealed record FlyerConfirmResult(int ConfirmedCount, int SkippedCount, IReadOnlyList<string> ConfirmedItemIds, IReadOnlyList<FlyerRecordedProductView> RecordedProducts);

/// <summary>「参考」の記録を足した商品と、食い違った品目</summary>
public sealed record FlyerRecordedProductView(string ProductId, string ProductName, IReadOnlyList<AllergenRef> Allergens);
