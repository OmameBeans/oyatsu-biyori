using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;
using OyatsuBiyori.Biz.Menus;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// チラシの取り込み（SC-09 手順1。D-101、09-flyer-import.md）。
/// 読み取り（Claude）→ 食品以外を外した掲載品へ（FR-IMPORT-20）→ 二重の読み取りをまとめる（FR-IMPORT-06）→ 読み取りの検証（Claude。まとめる前の一覧を渡す）
/// → 画像を破棄 → 結果をメモリに24時間。分類（FlyerItemClassifier。使えない / 再入力 / 取り込める）は保存せず、表示と「取り込みを終える」のたびに、
/// 名前＋メーカーの完全一致の照合（ProductMatcher）と今の商品マスタ・避ける品目で求め直す（09 の 3.1・5.1）。Claude は照合に使わない（FR-IMPORT-05）。
/// 「取り込みを終える」（FinishAsync）の1回の操作で、取り込めるの商品をすべて、商品（新しい商品の登録・登録済みの商品の上書き）・「参考」の記録・
/// 掲載品の写し（チラシの表記の原文つき）に反映する。使えない・再入力は決して取り込まない。「確認済み」にはしない（参考の記録だけを作る。SR-03）。
/// 確認済みの商品とチラシの表記が食い違えば、取り込みを終えるたびに、食い違いのある全商品の分の「参考」の記録を同じ保存で足す（09 の 3.5）。
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

            // 登録できない値はここで読めなかった扱いにする（分類では「取り込める」なのに、取り込みを終えるときに失敗しないように）
            var reads = reading.Value.Items.Select(TidyRead).ToList();
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
                session.Items.AddRange(previousSession.Items.Where(i => i.IsManual && i.Confirmation is null));
            }
        }

        _sessions.Set(session);
        return Result<string>.Ok(session.Id);
    }

    /// <summary>
    /// 手入力の商品を足す（読み取りの一覧からも、読み取り結果がないときからも。チラシと同じ照合・分類。注意「読み取りの検証を通っていません」を出す。D-73）。
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

        var context = await LoadContextAsync(menu);
        lock (session.SyncRoot)
        {
            return Result<FlyerImportView>.Ok(new FlyerImportView(job, BuildSessionView(session, context)));
        }
    }

    /// <summary>
    /// 行を直す（09 の 5.3・3.6）。input は行のすべての値（フォームの値）で、今の値と違う項目だけを「直した項目」にする。
    /// 直した値は読み取ったままの値と別に持つ。商品名・メーカーを直したら照合し直す（表示のたびに照合するので、次の分類で変わる）。
    /// 直した欄の読み取りの検証の指摘は消す。分類し直した行を返す（結果が「使えない」「再入力」になることもある）。
    /// 取り込み済みの行も直せる。値が変わるなら、直す前に取り込みを取り消す（掲載品の写しから外し、この取り込みで新しく登録した商品は「使わない」に戻す。
    /// 決めたこと B）。直した内容は、もう一度「取り込みを終える」で取り込み直す
    /// </summary>
    public async Task<Result<FlyerItemView>> UpdateItemAsync(FlyerItemEditInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (menu, session, error) = await FindSessionAsync(input.MenuId);
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

        var context = await LoadContextAsync(menu!);
        if (input.LabelAllergenIds is { } labelIds && labelIds.Any(id => !context.AllergenIds.Contains(id)))
        {
            return Result<FlyerItemView>.Error("アレルゲンの品目が正しくありません。画面を読み込み直してください");
        }

        var tagError = ValidateTags(input.CharacterTags ?? [], input.FlavorTags ?? []);
        if (tagError is not null)
        {
            return Result<FlyerItemView>.Error(tagError);
        }

        // 「取り込みを終える」・外すと同時に走らないようにする（取り込みの途中の行を直して、写しと商品の状態が食い違わないように）
        await session.WriteGate.WaitAsync();
        try
        {
            // 待っている間に取り込みを終えたことがあるので、商品マスタを読み直してから直す
            return await UpdateItemLockedAsync(menu!, session, input, await LoadContextAsync(menu!));
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>UpdateItemAsync の中身（WriteGate を持っている間だけ呼ぶ）</summary>
    private async Task<Result<FlyerItemView>> UpdateItemLockedAsync(Menu menu, FlyerImportSession session, FlyerItemEditInput input, ImportContext context)
    {
        FlyerImportItem? item;
        bool unconfirm;
        lock (session.SyncRoot)
        {
            (item, var itemError) = FindEditableItem(session, input.ItemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(itemError!);
            }

            var anyChange = ChangedFields(item, input).Count > 0 || HasOtherChanges(item.Read, input);
            if (!anyChange)
            {
                // 値が1つも変わらなければ何もしない（取り込み済みの行も取り消さない）
                return Result<FlyerItemView>.Ok(BuildItemView(item, context));
            }

            unconfirm = item.Confirmation is not null;
        }

        if (unconfirm)
        {
            var unconfirmError = await UnconfirmLockedAsync(menu, session, item, afterSave: null);
            if (unconfirmError is not null)
            {
                return Result<FlyerItemView>.Error(unconfirmError);
            }
        }

        lock (session.SyncRoot)
        {
            // 「使えない（入数が分からない）」の行に入数を入れたかを、直す前の分類で見る（FR-IMPORT-11）
            var wasPiecesUnknown = Classify(item, context)
                .Classification.Reasons.Any(r => r.Code == FlyerItemReasonCode.PiecesUnknown);
            var current = item.Read;
            var changed = ChangedFields(item, input);
            var labelChanged = changed.Contains(FlyerItemField.AllergenLabel);

            item.Read = current with
            {
                Name = Blank(input.Name) ?? string.Empty,
                Maker = Blank(input.Maker),
                Volume = Blank(input.Volume),
                PriceIncludingTax = input.PriceIncludingTax,
                PriceExcludingTax = input.PriceExcludingTax,
                OrderNo = Blank(input.OrderNo),
                PiecesPerPackCandidate = input.PiecesPerPack,
                PiecesPerPackReason = current.PiecesPerPackCandidate != input.PiecesPerPack ? "職員が直しました" : current.PiecesPerPackReason,
                SnackType = input.SnackType,
                TargetAgeLabel = Blank(input.TargetAgeLabel),
                CharacterTags = input.CharacterTags?.ToList() ?? current.CharacterTags,
                FlavorTags = input.FlavorTags?.ToList() ?? current.FlavorTags,
            };
            if (wasPiecesUnknown && current.PiecesPerPackCandidate != input.PiecesPerPack)
            {
                // どの API から入れても、入数を入れて「使えない」から戻した行には注意を出す（EnterPiecesAsync と同じ）
                item.IsPiecesEnteredByStaff = true;
            }

            if (labelChanged)
            {
                item.LabelAllergenIds = input.LabelAllergenIds!.Distinct(StringComparer.Ordinal).ToList();
            }

            foreach (var field in changed)
            {
                item.EditedFields.Add(field);
                item.ReviewNotes.RemoveAll(n => ReviewNoteMatchesField(n.Field, field));
            }

            return Result<FlyerItemView>.Ok(BuildItemView(item, context));
        }
    }

    /// <summary>入力のうち、今の値と違う項目（FlyerItemField の値。「直した項目」になるもの）</summary>
    private static List<string> ChangedFields(FlyerImportItem item, FlyerItemEditInput input)
    {
        var current = item.Read;
        var changed = new List<string>();
        Track(changed, FlyerItemField.Name, !string.Equals(current.Name, Blank(input.Name) ?? string.Empty, StringComparison.Ordinal));
        Track(changed, FlyerItemField.Maker, !string.Equals(current.Maker, Blank(input.Maker), StringComparison.Ordinal));
        Track(changed, FlyerItemField.Volume, !string.Equals(current.Volume, Blank(input.Volume), StringComparison.Ordinal));
        Track(changed, FlyerItemField.PriceIncludingTax, current.PriceIncludingTax != input.PriceIncludingTax);
        Track(changed, FlyerItemField.PriceExcludingTax, current.PriceExcludingTax != input.PriceExcludingTax);
        Track(changed, FlyerItemField.OrderNo, !string.Equals(current.OrderNo, Blank(input.OrderNo), StringComparison.Ordinal));
        Track(changed, FlyerItemField.PiecesPerPack, current.PiecesPerPackCandidate != input.PiecesPerPack);
        Track(changed, FlyerItemField.AllergenLabel, input.LabelAllergenIds is { } newLabels
            && !item.LabelAllergenIds.ToHashSet(StringComparer.Ordinal).SetEquals(newLabels));
        return changed;
    }

    /// <summary>「直した項目」に数えない値（おやつの分類・対象年齢・タグ）が変わるか</summary>
    private static bool HasOtherChanges(FlyerReadProduct current, FlyerItemEditInput input) =>
        current.SnackType != input.SnackType
        || !string.Equals(current.TargetAgeLabel, Blank(input.TargetAgeLabel), StringComparison.Ordinal)
        || (input.CharacterTags is { } characterTags && !characterTags.SequenceEqual(current.CharacterTags, StringComparer.Ordinal))
        || (input.FlavorTags is { } flavorTags && !flavorTags.SequenceEqual(current.FlavorTags, StringComparer.Ordinal));

    /// <summary>
    /// 品目の分からない語に品目を選ぶ（allergenIds が空なら「対応する品目なし」。FR-IMPORT-33）。語は消さずに残し、選んだ品目を表記の品目に足す。
    /// 職員が選んだ語を略記として覚えることはしない（FR-IMPORT-34）。分類し直した行を返す
    /// </summary>
    public async Task<Result<FlyerItemView>> ResolveTermAsync(string? menuId, string? itemId, string? term, IReadOnlyList<string>? allergenIds)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var context = await LoadContextAsync(menu!);
        var ids = (allergenIds ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Any(id => !context.AllergenIds.Contains(id)))
        {
            return Result<FlyerItemView>.Error("アレルゲンの品目が正しくありません。画面を読み込み直してください");
        }

        // 「取り込みを終える」と同時に走らないようにする（終える途中の行の品目・入数を変えて、確かめた内容と取り込む内容が食い違わないように）
        await session.WriteGate.WaitAsync();
        try
        {
            // 待っている間に取り込みを終えたことがあるので、商品マスタを読み直す
            context = await LoadContextAsync(menu!);
            lock (session.SyncRoot)
            {
                var (item, itemError) = FindEditableItem(session, itemId);
                if (item is null)
                {
                    return Result<FlyerItemView>.Error(itemError!);
                }

                if (item.Confirmation is not null)
                {
                    // 取り込み済みの行には、品目を決めていない語は残っていない（直すときは行を開いて表記の品目を直す）
                    return Result<FlyerItemView>.Error("取り込み済みの商品です。品目を直すときは、行を開いてアレルゲン表記の品目を直してください");
                }

                if (term is null || !item.UnmappedTerms.Contains(term, StringComparer.Ordinal))
                {
                    return Result<FlyerItemView>.Error("その表記の語が見つかりません。画面を読み込み直してください");
                }

                item.ResolvedTerms[term] = ids;
                return Result<FlyerItemView>.Ok(BuildItemView(item, context));
            }
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>
    /// 「使えない（入数が分からない）」の行に入数を入れる（FR-IMPORT-11）。読み取った内容（アレルゲン表記・説明文を含む）のまま分類し直す。
    /// 入れた行は読み取りの検証を通っていないため、注意「読み取りの検証を通っていません」を出す。分類し直した行を返す
    /// </summary>
    public async Task<Result<FlyerItemView>> EnterPiecesAsync(string? menuId, string? itemId, int? piecesPerPack)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
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

        var context = await LoadContextAsync(menu!);
        // 「取り込みを終える」と同時に走らないようにする（終える途中の行の品目・入数を変えて、確かめた内容と取り込む内容が食い違わないように）
        await session.WriteGate.WaitAsync();
        try
        {
            // 待っている間に取り込みを終えたことがあるので、商品マスタを読み直す
            context = await LoadContextAsync(menu!);
            lock (session.SyncRoot)
            {
                var (item, itemError) = FindEditableItem(session, itemId);
                if (item is null)
                {
                    return Result<FlyerItemView>.Error(itemError!);
                }

                if (item.Confirmation is not null)
                {
                    return Result<FlyerItemView>.Error("取り込み済みの商品です。入数を直すときは、行を開いて直してください");
                }

                var state = Classify(item, context);
                if (!state.Classification.Reasons.Any(r => r.Code == FlyerItemReasonCode.PiecesUnknown))
                {
                    return Result<FlyerItemView>.Error("この商品は入数が分かっています。入数を直すときは、行を開いて直してください");
                }

                item.Read = item.Read with { PiecesPerPackCandidate = piecesPerPack, PiecesPerPackReason = "職員が入れました" };
                item.IsPiecesEnteredByStaff = true;
                return Result<FlyerItemView>.Ok(BuildItemView(item, context));
            }
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>
    /// 外した掲載品に移す（FR-IMPORT-21）。どの分類の商品でも外せる。取り込み済みの商品は、取り込みを取り消してから外す
    /// （掲載品の写しから除き、その取り込みで新しく登録した商品は「使わない」にする）。外した掲載品は今週の商品にも商品マスタにも入れない（FR-IMPORT-22）
    /// </summary>
    public async Task<Result> RemoveItemAsync(string? menuId, string? itemId)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result.Error(error!);
        }

        // 取り込みを終えるのと同時に走らないようにする（取り込みの途中の行を取り消して、写しと商品の状態が食い違わないように）
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

            if (item.Confirmation is null)
            {
                item.IsRemoved = true;
                item.RemovedReason = "職員が外しました";
                return new Result();
            }
        }

        var unconfirmError = await UnconfirmLockedAsync(menu, session, item, afterSave: removed =>
        {
            removed.IsRemoved = true;
            removed.RemovedReason = "職員が外しました";
        });
        return unconfirmError is null ? new Result() : Result.Error(unconfirmError);
    }

    /// <summary>
    /// 取り込み済みの行の取り込みを取り消す（WriteGate を持っている間だけ呼ぶ）。掲載品の写しから外し、この取り込みで新しく登録した商品は「使わない」にして保存する。
    /// 保存できてから行の確定を null に戻し、afterSave を呼ぶ。エラーなら何も変えずにその文を返す
    /// </summary>
    private async Task<string?> UnconfirmLockedAsync(Menu menu, FlyerImportSession session, FlyerImportItem item, Action<FlyerImportItem>? afterSave)
    {
        FlyerItemConfirmation? confirmation;
        bool sharedListing;
        bool createdInSession;
        FlyerProductOverwrite? overwrite;
        lock (session.SyncRoot)
        {
            confirmation = item.Confirmation;
            if (confirmation is null)
            {
                return null;
            }

            // 同じ商品に取り込んだほかの行があれば、写しの行は残す
            sharedListing = session.Items.Any(i => i != item && !i.IsRemoved && i.Confirmation?.ProductId == confirmation.ProductId);
            // この取り込みで新しく登録した商品か（同じ商品の後の行として取り込んだ行を、最後に取り消したときも「使わない」にするため）
            createdInSession = confirmation.Kind == FlyerConfirmationKind.CreatedNew
                || session.Items.Any(i => i.CreatedProductId == confirmation.ProductId);
            overwrite = session.ProductOverwrites.GetValueOrDefault(confirmation.ProductId);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return "ログインが必要です。ログインし直してください";
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!sharedListing)
        {
            ListedProductsUpdater.Remove(menu, confirmation.ProductId);
        }

        // ほかの行がまだこの商品で取り込んでいるときは「使わない」にしない（写しに残る商品が候補から外れないように）
        if (!sharedListing
            && createdInSession
            && await _products.GetAsync(confirmation.ProductId) is { } created)
        {
            // 新しく登録した商品は消さずに「使わない」にする（商品一覧に使われない商品が増えないように）
            created.IsDisabled = true;
            created.UpdateDateTime = now;
            created.UpdateUserId = user.UserId;
        }
        else if (!sharedListing
            && overwrite is not null
            && await _products.GetAsync(confirmation.ProductId) is { } updated
            && RestoreOverwrittenValues(updated, overwrite))
        {
            // 登録済みの商品に上書きした値（価格・内容量・入数など）を、取り込む前の値に戻す（読み違いを商品一覧に残さない。FR-IMPORT-22 と同じ考え方）
            updated.UpdateDateTime = now;
            updated.UpdateUserId = user.UserId;
        }

        Touch(menu, now, user);
        await _unitOfWork.SaveChangesAsync();
        lock (session.SyncRoot)
        {
            item.Confirmation = null;
            if (!sharedListing)
            {
                session.ProductOverwrites.Remove(confirmation.ProductId);
            }

            afterSave?.Invoke(item);
        }

        return null;
    }

    /// <summary>
    /// 取り込みで上書きした項目のうち、まだ上書きした値のままの項目だけを、上書きする前の値に戻す（取り込みのあとに職員が商品一覧で直した値は戻さない）。
    /// 1つでも戻したら true
    /// </summary>
    private static bool RestoreOverwrittenValues(Product product, FlyerProductOverwrite overwrite)
    {
        var (before, after) = (overwrite.Before, overwrite.After);
        var restored = false;
        if (before.Price != after.Price && product.Price == after.Price)
        {
            product.Price = before.Price;
            restored = true;
        }

        if (before.PriceExcludingTax != after.PriceExcludingTax && product.PriceExcludingTax == after.PriceExcludingTax)
        {
            product.PriceExcludingTax = before.PriceExcludingTax;
            restored = true;
        }

        if (before.OrderNo != after.OrderNo && product.OrderNo == after.OrderNo)
        {
            product.OrderNo = before.OrderNo;
            restored = true;
        }

        if (before.ListedWeekStart != after.ListedWeekStart && product.ListedWeekStart == after.ListedWeekStart)
        {
            product.ListedWeekStart = before.ListedWeekStart;
            restored = true;
        }

        if (before.Volume != after.Volume && product.Volume == after.Volume)
        {
            product.Volume = before.Volume;
            restored = true;
        }

        if (before.PiecesPerPack != after.PiecesPerPack && product.PiecesPerPack == after.PiecesPerPack)
        {
            product.PiecesPerPack = before.PiecesPerPack;
            restored = true;
        }

        return restored;
    }

    /// <summary>外した掲載品を一覧に戻す（FR-IMPORT-21。戻すと分類し直す）。分類し直した行を返す</summary>
    public async Task<Result<FlyerItemView>> RestoreItemAsync(string? menuId, string? itemId)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerItemView>.Error(error!);
        }

        var context = await LoadContextAsync(menu!);
        lock (session.SyncRoot)
        {
            var item = session.Items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
            {
                return Result<FlyerItemView>.Error(ItemNotFound);
            }

            item.IsRemoved = false;
            item.RemovedReason = null;
            return Result<FlyerItemView>.Ok(BuildItemView(item, context));
        }
    }

    /// <summary>取り込みをやめる（確定前の結果を捨てる。取り込んだ商品・写しはそのまま）</summary>
    public Result Discard(string? menuId)
    {
        if (!string.IsNullOrEmpty(menuId))
        {
            _sessions.Remove(menuId);
        }

        return new Result();
    }

    /// <summary>
    /// 「取り込みを終える」（09 の 5.1・6。D-101）。分類をサーバーで求め直し、「取り込める」の商品をすべて1回の保存で取り込む
    /// （新しい商品は登録、登録済みの商品は価格・税抜・注文番号・最後に載った週などを上書き、掲載品の写しにチラシの表記の原文つきで入れる）。
    /// 使えない・再入力・外した掲載品は決して取り込まない（画面の値を信用しない。SR-10・SR-21）。
    /// 確認済みとの食い違いの「参考」の記録（09 の 3.5）は、使えない・再入力・外した掲載品も含めて同じ保存で足す（取り込めるが0件でも。決めたこと D）。
    /// 全部か無し：1件でも組み立てに失敗したら、何も保存せずにエラーを返す（決めたこと C）
    /// </summary>
    public async Task<Result<FlyerFinishResult>> FinishAsync(string? menuId)
    {
        var (menu, session, error) = await FindSessionAsync(menuId);
        if (session is null)
        {
            return Result<FlyerFinishResult>.Error(error!);
        }

        await session.WriteGate.WaitAsync();
        try
        {
            return await FinishLockedAsync(menu!, session);
        }
        finally
        {
            session.WriteGate.Release();
        }
    }

    /// <summary>FinishAsync の中身（WriteGate を持っている間だけ呼ぶ）</summary>
    private async Task<Result<FlyerFinishResult>> FinishLockedAsync(Menu menu, FlyerImportSession session)
    {
        var user = await _userContext.GetCurrentUserInfoAsync();
        if (user is null)
        {
            return Result<FlyerFinishResult>.Error("ログインが必要です。ログインし直してください");
        }

        var context = await LoadContextAsync(menu);
        List<ItemState> states;
        List<ItemState> removedStates;
        lock (session.SyncRoot)
        {
            states = session.Items
                .Where(i => !i.IsRemoved && i.Confirmation is null)
                .Select(i => Classify(i, context))
                .ToList();
            // 外した掲載品は取り込まないが、確認済みとの食い違いの記録（09 の 3.5）には入れる
            removedStates = session.Items
                .Where(i => i.IsRemoved && i.Confirmation is null)
                .Select(i => Classify(i, context))
                .ToList();
        }

        // 取り込むのは「取り込める」だけ（使えない・再入力は決して取り込まない）
        var targets = states.Where(s => s.Classification.Category == FlyerItemCategory.Ready).ToList();

        // ここで、取り込みの内容の確かめ（エラーを返すもの）をすべて済ませる。DB の変更はこのあとでだけ組み立てる
        // （エラーで返したときに、追跡中の変更が残って次の保存に混ざらないように。全部か無し）
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var plans = new List<ConfirmPlan>();
        // 同じ取り込みの中で、正規化した名前＋メーカーが同じ行（手入力の行・名前やメーカーを直した行は、二重の読み取りのまとめ（FR-IMPORT-06）を通らない）は、
        // 1件の商品にまとめる。先の行で使う・作る商品を後の行も使い、同じ商品を2件登録しない（2件あると、パッケージで確かめた記録が片方にしか付かない）
        var leadPlans = new Dictionary<ProductMatchKey, ConfirmPlan>();
        foreach (var state in targets)
        {
            var (plan, planError) = await PlanConfirmAsync(state, context, now, user);
            if (plan is null)
            {
                return Result<FlyerFinishResult>.Error($"「{state.Item.Read.Name}」を取り込めませんでした（{planError}）。何も保存していません");
            }

            var key = ProductMatcher.KeyOf(state.Item.Read.Name, state.Item.Read.Maker);
            if (leadPlans.TryGetValue(key, out var lead))
            {
                // 後の行：商品の値は先の行の値を使い（上書きしない）、チラシの品目は「参考」の記録で足す（和集合。確認する側に倒す）
                plan = plan with { Kind = FlyerConfirmationKind.UpdatedExisting, Product = lead.Product, IsCreated = false, Tags = [], FollowsEarlierRow = true };
            }
            else if (key.NotMatchableReason is null)
            {
                leadPlans[key] = plan;
            }

            plans.Add(plan);
        }

        var notListedUnusable = states.Count(s => s.Classification.Category == FlyerItemCategory.Unusable);
        var notListedNeedsInput = states.Count(s => s.Classification.Category == FlyerItemCategory.NeedsInput);

        // ここから下ではエラーを返さない
        // 1. 確認済みとの食い違いの「参考」の記録（使えない・再入力・取り込める、外した掲載品を含む、食い違いのある全商品の分。09 の 3.5）
        // 外した掲載品（職員が外した・Claude が食品以外とした）も、外した理由を問わず対象にする（今週使わなくても、ほかの週の写しや献立に残りうるため）
        var recorded = AddDiscrepancyRecords([.. states, .. removedStates], context, now, user);

        // 2. 取り込む商品（取り込み直しでは、最初に取り込むときだけ写しを空にする）
        var clearFirst = session.IsReimport && !session.HasConfirmedAny;
        var confirmations = new List<(FlyerImportItem Item, FlyerItemConfirmation Confirmation, string? CreatedProductId)>();
        var overwrites = new List<(string ProductId, FlyerProductValues Before, FlyerProductValues After)>();
        foreach (var plan in plans)
        {
            var before = FlyerProductValues.Of(plan.Product);
            var confirmation = ApplyConfirm(menu, plan, context, now, user, clearFirst);
            clearFirst = false;
            confirmations.Add((plan.State.Item, confirmation, plan.Kind == FlyerConfirmationKind.CreatedNew ? plan.Product.Id : null));
            if (plan.Kind == FlyerConfirmationKind.UpdatedExisting && !plan.FollowsEarlierRow)
            {
                overwrites.Add((plan.Product.Id, before, FlyerProductValues.Of(plan.Product)));
            }
        }

        // 3. 同じ商品（名前＋メーカーが同じ）の、今週の商品に入れなかった行（使えない・再入力）のチラシの品目も、取り込んだ商品の「参考」の記録に足す
        // （同じ商品なのに、避ける品目を含むと読んだ行だけが 🔴 になり、取り込んだ商品にその品目が残らないことがないように。確認を増やす方向だけ）
        var sameProductRecords = AddSameProductRecords(menu, states, leadPlans, context, now, user);
        if (confirmations.Count == 0 && recorded.Count == 0 && sameProductRecords == 0)
        {
            return Result<FlyerFinishResult>.Ok(new FlyerFinishResult(0, 0, notListedUnusable, notListedNeedsInput, [], []));
        }

        Touch(menu, now, user);
        await _unitOfWork.SaveChangesAsync();

        // 保存できてから、取り込みの状態を変える（保存に失敗したら、取り込んでいない扱いのまま）
        lock (session.SyncRoot)
        {
            if (confirmations.Count > 0)
            {
                session.HasConfirmedAny = true;
            }

            foreach (var (item, confirmation, createdId) in confirmations)
            {
                item.Confirmation = confirmation;
                if (createdId is not null)
                {
                    item.CreatedProductId = createdId;
                }
            }

            // 上書きする前の値は、この取り込みで最初に上書きしたときの値を残す（取り込みを取り消したら戻す）
            foreach (var (productId, before, after) in overwrites)
            {
                session.ProductOverwrites[productId] = session.ProductOverwrites.TryGetValue(productId, out var earlier)
                    ? earlier with { After = after }
                    : new FlyerProductOverwrite(before, after);
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
        // 件数は商品の数で数える（同じ商品の2行目は数えない）
        var createdIds = confirmations
            .Where(c => c.Confirmation.Kind == FlyerConfirmationKind.CreatedNew)
            .Select(c => c.Confirmation.ProductId)
            .ToHashSet(StringComparer.Ordinal);
        return Result<FlyerFinishResult>.Ok(new FlyerFinishResult(
            createdIds.Count,
            confirmations
                .Where(c => c.Confirmation.Kind == FlyerConfirmationKind.UpdatedExisting && !createdIds.Contains(c.Confirmation.ProductId))
                .Select(c => c.Confirmation.ProductId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            notListedUnusable,
            notListedNeedsInput,
            confirmations.Select(c => c.Item.Id).ToList(),
            recordedProducts));
    }

    /// <summary>1件の取り込みの内容を確かめて組み立てる（DB は変えない）。取り込めなければエラーの文を返す</summary>
    private async Task<(ConfirmPlan? Plan, string? Error)> PlanConfirmAsync(ItemState state, ImportContext context, DateTime now, CurrentUserInfo user)
    {
        var read = state.Item.Read;
        var classification = state.Classification;
        if (classification.Category != FlyerItemCategory.Ready)
        {
            // 呼び出し側で「取り込める」だけを渡すが、念のためここでも確かめる（使えない・再入力を取り込まない）
            return (null, "「取り込める」の商品ではありません");
        }

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
            return (null, $"対応づけできていないアレルゲンの表記（{string.Join("・", allergenInput.UnresolvedLabelTerms)}）があります。品目を選ぶか「対応する品目なし」を選んでください");
        }

        if (!classification.IsNew)
        {
            return (new ConfirmPlan(state, FlyerConfirmationKind.UpdatedExisting, state.Product!, IsCreated: false, [], allergenInput), null);
        }

        // 新しい商品として登録する項目がそろっているか
        if (string.IsNullOrWhiteSpace(read.Name))
        {
            return (null, "商品名を入れてください");
        }

        if (read.SnackType is not { } snackType || !Enum.IsDefined(snackType))
        {
            return (null, "おやつの分類を選んでください");
        }

        // 前にこの行で新しく登録して、取り込みを取り消した商品があれば、それを直して使う（選び直すたびに商品が増えないように）
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

    /// <summary>確かめ済みの取り込みを DB の変更として組み立てる（保存はしない。エラーは返さない）</summary>
    private FlyerItemConfirmation ApplyConfirm(Menu menu, ConfirmPlan plan, ImportContext context, DateTime now, CurrentUserInfo user, bool clearFirst)
    {
        var item = plan.State.Item;
        var read = item.Read;
        var product = plan.Product;
        var pieces = plan.State.Classification.PiecesPerPack!.Value;
        if (plan.FollowsEarlierRow)
        {
            // 同じ取り込みの先の行と同じ商品：商品の値は先の行の値のまま（FR-IMPORT-06 の「先に読んだ値を使う」と同じ）
        }
        else if (plan.Kind == FlyerConfirmationKind.UpdatedExisting)
        {
            // 価格・税抜・注文番号・最後に載った週を上書きする（過去の週の取り込みでは上書きしない。読めなかった値（null）で上書きしない。FR-FLYER-07）
            var overwritten = ListedProductsUpdater.ApplyListing(product, menu.WeekStart, read.PriceIncludingTax, read.PriceExcludingTax, Blank(read.OrderNo));
            // 内容量・入数はチラシの値で上書きする（FR-IMPORT-12。注意で知らせる）。過去の週は空のときだけ埋める。おやつの分類・タグはマスタの値のまま（09 の 5.1）
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

        // チラシの品目から「参考」の記録を足すか（03 §3 の表）。表記なしは品目が空の「参考（表記なし）」で、どの品目も「含まない」にはしない。
        // 食い違いの記録はもう足してあるので、ここでは重ならない。
        // 同じ商品の後の行は、先の行で足した記録を見て、品目が増えたときだけ足す（有効な記録の和集合になる）
        var allergenPlan = FlyerAllergenPlanner.Plan(plan.AllergenInput, context.Allergens, context.AvoidedIds, product.AllergenChecks);
        var addReference = allergenPlan.CanConfirm && allergenPlan.ShouldAddReference();
        if (addReference)
        {
            AddReferenceCheck(product, item, allergenPlan, context, now, user, note: plan.FollowsEarlierRow ? "同じ商品の別の行のチラシの表記" : null);
        }

        // 写しにはチラシの表記の原文を入れる（献立の確定で見比べるための表示だけ。D-101・FR-IMPORT-40）
        var labelText = ListingLabelTextOf(item, context);
        var listed = plan.FollowsEarlierRow ? menu.ListedProducts.FirstOrDefault(l => l.ProductId == product.Id) : null;
        if (listed is not null)
        {
            // 同じ商品の後の行：価格・注文番号は先の行のまま。表記の原文は違えば書き足す（どちらの表記も見比べられるように）
            if (listed.LabelText != labelText)
            {
                ListedProductsUpdater.ApplyConfirmation(
                    menu,
                    listed with { LabelText = string.IsNullOrEmpty(listed.LabelText) ? labelText : listed.LabelText + "／" + labelText },
                    clearFirst: false);
            }
        }
        else
        {
            ListedProductsUpdater.ApplyConfirmation(
                menu,
                new ListedProduct(product.Id, read.PriceIncludingTax, Blank(read.OrderNo), labelText),
                clearFirst);
        }

        return new FlyerItemConfirmation(plan.Kind, product.Id, product.Name, addReference);
    }

    /// <summary>
    /// 同じ商品（名前＋メーカーが同じ）の、今週の商品に入れなかった行（使えない・再入力）のチラシの品目を、
    /// 今週の商品に入っている（この保存で取り込む、または写しにある）その商品の「参考」の記録に足す（保存はしない）。足した件数を返す。
    /// 記録がないか「参考」だけの商品で、チラシの品目が増えるときだけ足す（確認済みの商品の食い違いは 3.5 で足してある）。
    /// 品目の分からない語が残っていても、対応づけできた品目で足す（記録は確認を増やす方向だけ）
    /// </summary>
    private int AddSameProductRecords(Menu menu, IReadOnlyList<ItemState> states, IReadOnlyDictionary<ProductMatchKey, ConfirmPlan> leadPlans, ImportContext context, DateTime now, CurrentUserInfo user)
    {
        var count = 0;
        foreach (var state in states.Where(s => s.Classification.Category != FlyerItemCategory.Ready))
        {
            Product? product;
            if (state.Product is { } matched)
            {
                product = menu.ListedProducts.Any(l => l.ProductId == matched.Id) ? matched : null;
            }
            else
            {
                var key = ProductMatcher.KeyOf(state.Item.Read.Name, state.Item.Read.Maker);
                product = key.NotMatchableReason is null && leadPlans.TryGetValue(key, out var lead) ? lead.Product : null;
            }

            if (product is null)
            {
                continue;
            }

            var plan = FlyerAllergenPlanner.Plan(AllergenInputOf(state.Item, context, ignoreUnresolved: true), context.Allergens, context.AvoidedIds, product.AllergenChecks);
            if (plan.Decision != FlyerReferenceDecision.Add || plan.FlyerAllergens.Count == 0)
            {
                continue;
            }

            AddReferenceCheck(product, state.Item, plan, context, now, user, note: "同じ商品の、今週の商品に入れなかった行のチラシの表記");
            count++;
        }

        return count;
    }

    /// <summary>掲載品の写しに入れる表記の原文（決めたこと F。行の表示の listingLabelText も同じ）</summary>
    private static string ListingLabelTextOf(FlyerImportItem item, ImportContext context)
    {
        IReadOnlyList<string>? editedNames = item.EditedFields.Contains(FlyerItemField.AllergenLabel)
            ? item.LabelAllergenIds
                .Concat(item.ResolvedTerms.Values.SelectMany(ids => ids))
                .Where(context.AllergenIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .Select(id => context.Allergens.First(a => a.Id == id))
                .OrderBy(a => a.SortOrder)
                .Select(a => a.Name)
                .ToList()
            : null;
        return FlyerAllergenPlanner.ListingLabelText(item.Read.AllergenLabelText, item.FoundLabelTexts, editedNames);
    }

    /// <summary>
    /// 確認済みの商品とチラシの表記の食い違い（09 の 3.5）の「参考」の記録を足す（保存はしない）。
    /// 使えない・再入力を含む、未確定の全商品（外した掲載品も）が対象。品目の分からない語が残っていても、対応づけできた品目で足す（記録は確認を増やす方向だけ）。
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
    private static ItemState Classify(FlyerImportItem item, ImportContext context)
    {
        // この行で新しく登録して取り込みを取り消した（使わないにした）商品とは照合しない（自分が作った商品で「使わない」の「使えない」にならないように）。
        // ただし、その商品の記録で避ける品目を含めば「使えない」にする（決めたこと H。パッケージで確かめた 🔴 を取りこぼさない）
        var ownCreated = item.CreatedProductId is { } createdId
            ? context.ProductsById.GetValueOrDefault(createdId) is { IsDisabled: true } own ? own : null
            : null;
        var products = ownCreated is null
            ? context.Products
            : context.Products.Where(p => p.Id != ownCreated.Id).ToList();
        var match = ProductMatcher.Match(item.Read.Name, item.Read.Maker, products);
        var product = match.IsMatched ? match.Product : null;
        var judgement = product is null ? null : AllergenJudge.Judge(product, context.Avoided, context.Today, context.RecheckDays);
        var ownJudgement = ownCreated is null ? null : AllergenJudge.Judge(ownCreated, context.Avoided, context.Today, context.RecheckDays);
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
            Match = match,
            MatchedJudgement = judgement,
            OwnCreatedJudgement = ownJudgement,
            AllAllergens = context.Allergens,
            AvoidedAllergens = context.Avoided,
            IsManual = item.IsManual,
            OverwritesMaster = product is null || ListedProductsUpdater.ShouldOverwriteProduct(product, context.MenuWeekStart),
        });
        return new ItemState(item, match, product, judgement, classification);
    }

    private FlyerSessionView BuildSessionView(FlyerImportSession session, ImportContext context)
    {
        var items = session.Items
            .Where(i => !i.IsRemoved)
            .Select((item, index) => (View: BuildItemView(item, context), Index: index))
            .OrderBy(x => x.View.Status)
            .ThenBy(x => x.Index)
            .Select(x => x.View)
            .ToList();
        var removed = session.Items
            .Where(i => i.IsRemoved)
            .Select(i => new FlyerRemovedItemView(
                i.Id, i.Read.Name, i.Read.Maker, i.Read.Volume, i.RemovedReason ?? "外しました", i.OriginalRead.IsNonFood,
                // 確認済みとの食い違い（取り込みを終えると参考の記録を足す。まだ足していないものだけ）
                i.Confirmation is null
                    ? Classify(i, context).Classification.Discrepancies
                        .Select(d => d.Allergen.Name)
                        .Where(name => i.RecordedDiscrepancyAllergens.All(a => a.Name != name))
                        .Distinct()
                        .ToList()
                    : []))
            .ToList();

        int CountOf(FlyerItemStatus status) => items.Count(v => v.Status == status);
        var counts = new FlyerCategoryCounts(
            CountOf(FlyerItemStatus.NeedsInput),
            CountOf(FlyerItemStatus.Ready),
            CountOf(FlyerItemStatus.Unusable),
            CountOf(FlyerItemStatus.Confirmed),
            removed.Count);
        var ready = items.Where(v => v.Status == FlyerItemStatus.Ready).ToList();
        // 新しい商品・登録済みの商品の件数は商品の数で数える（名前＋メーカーが同じ行は、取り込みを終えると1件の商品にまとめるため）
        var finish = new FlyerFinishSummary(
            ready.Count,
            ready.Where(v => v.IsNew).Select(v => ProductMatcher.KeyOf(v.Read.Name, v.Read.Maker)).Distinct().Count(),
            ready.Where(v => !v.IsNew).Select(v => v.MatchedProduct?.Id).Distinct().Count());
        var notListed = new FlyerNotListedCounts(counts.Unusable + counts.NeedsInput, counts.Unusable, counts.NeedsInput);

        return new FlyerSessionView(
            session.Id,
            session.IsReimport,
            session.ReviewFailed,
            session.MissedProducts.ToList(),
            items,
            removed,
            counts,
            finish,
            notListed,
            counts.NeedsInput + counts.Ready + counts.Unusable);
    }

    private FlyerItemView BuildItemView(FlyerImportItem item, ImportContext context)
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
        var listingLabelText = ListingLabelTextOf(item, context);

        if (item.Confirmation is { } confirmation)
        {
            var confirmed = context.ProductsById.GetValueOrDefault(confirmation.ProductId);
            var labelIds = item.LabelAllergenIds
                .Concat(item.ResolvedTerms.Values.SelectMany(ids => ids))
                .Where(context.AllergenIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new FlyerItemView(
                item.Id,
                FlyerItemStatus.Confirmed,
                null,
                confirmation.Kind == FlyerConfirmationKind.CreatedNew,
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
                FlyerItemClassifier.IsLabelAbsent(labelIds, []),
                listingLabelText,
                confirmed is null ? null : MatchedView(confirmed, context),
                0,
                null,
                confirmed?.PiecesPerPack ?? item.Read.PiecesPerPackCandidate,
                item.DuplicateConflicts,
                Math.Max(1, item.SourceIndexes.Count),
                item.Confirmation);
        }

        var state = Classify(item, context);
        var classification = state.Classification;
        return new FlyerItemView(
            item.Id,
            StatusOf(classification.Category),
            classification.Category,
            classification.IsNew,
            item.IsManual,
            item.IsPiecesEnteredByStaff,
            item.Read,
            item.OriginalRead,
            item.EditedFields.Order(StringComparer.Ordinal).ToList(),
            classification.Reasons,
            classification.Notes,
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
            listingLabelText,
            state.Product is null ? null : MatchedView(state.Product, context, state.Judgement),
            state.Match.OtherMatches.Count,
            state.Match.NotMatchableReason,
            classification.PiecesPerPack,
            item.DuplicateConflicts,
            Math.Max(1, item.SourceIndexes.Count),
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
        FlyerItemCategory.Ready => FlyerItemStatus.Ready,
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

    /// <summary>
    /// Claude の読み取りのうち、商品に登録できない値を整える（Claude の出力は信用しない）。
    /// 税抜価格が1未満なら読めなかった扱い（null）にし、空のタグと選べる語にない味・種類のタグを除く。
    /// アレルゲン表記・説明文・商品名は変えない（探す言葉の当たりを消さない）
    /// </summary>
    private static FlyerReadProduct TidyRead(FlyerReadProduct read) => read with
    {
        PriceExcludingTax = read.PriceExcludingTax is >= 1 ? read.PriceExcludingTax : null,
        CharacterTags = read.CharacterTags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList(),
        FlavorTags = read.FlavorTags
            .Where(t => t is not null)
            .Select(t => t.Trim())
            .Where(ProductTagVocabulary.IsFlavor)
            .Distinct(StringComparer.Ordinal)
            .ToList(),
    };

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
            // 見つけた表記の原文は、掲載品の写しの表記に書き添える（決めたこと F）
            if (!string.IsNullOrWhiteSpace(missed.LabelText) && !item.FoundLabelTexts.Contains(missed.LabelText.Trim(), StringComparer.Ordinal))
            {
                item.FoundLabelTexts.Add(missed.LabelText.Trim());
            }

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

    /// <summary>直せる商品を探す（外した掲載品は直さない。取り込み済みの行は直せる。決めたこと B）</summary>
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

        return (item, null);
    }

    private async Task<ImportContext> LoadContextAsync(Menu menu)
    {
        var allergens = await _settings.GetAllergensAsync();
        var avoided = await _settings.GetAvoidedAllergensAsync();
        var setting = await _settings.GetSettingAsync();
        var products = await _products.GetAllAsync();
        return new ImportContext(allergens, avoided, products, JapanDate.Today(_timeProvider), setting.RecheckDays, menu.WeekStart);
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

    private static void Touch(Menu menu, DateTime now, CurrentUserInfo user)
    {
        // 献立の行を必ず更新して row_version を進める（D-46）
        menu.UpdateDateTime = now;
        menu.UpdateUserId = user.UserId;
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>表示・取り込みに使うマスタと今日・献立の週</summary>
    private sealed class ImportContext(IReadOnlyList<Allergen> allergens, IReadOnlyList<Allergen> avoided, IReadOnlyList<Product> products, DateOnly today, int recheckDays, DateOnly menuWeekStart)
    {
        public IReadOnlyList<Allergen> Allergens { get; } = allergens;

        public IReadOnlyList<Allergen> Avoided { get; } = avoided;

        public IReadOnlyList<string> AvoidedIds { get; } = avoided.Select(a => a.Id).ToList();

        public IReadOnlyList<Product> Products { get; } = products;

        public IReadOnlyDictionary<string, Product> ProductsById { get; } = products.DistinctBy(p => p.Id, StringComparer.Ordinal).ToDictionary(p => p.Id, StringComparer.Ordinal);

        public HashSet<string> AllergenIds { get; } = allergens.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        public DateOnly Today { get; } = today;

        public int RecheckDays { get; } = recheckDays;

        /// <summary>献立の週（一致した商品を上書きする週か。ListedProductsUpdater.ShouldOverwriteProduct）</summary>
        public DateOnly MenuWeekStart { get; } = menuWeekStart;

        public string AllergenName(string id) => Allergens.FirstOrDefault(a => a.Id == id)?.Name ?? id;
    }

    /// <summary>1商品の分類（照合の結果と一致した商品の判定つき）</summary>
    private sealed record ItemState(FlyerImportItem Item, ProductMatch Match, Product? Product, AllergenJudgement? Judgement, FlyerItemClassification Classification);

    /// <summary>
    /// 確かめ済みの1件の取り込みの内容。FollowsEarlierRow は、同じ取り込みの先の行と名前＋メーカーが同じで、先の行の商品を使う行
    /// （商品の値は上書きせず、チラシの品目の「参考」の記録と写しの表記の原文だけを足す）
    /// </summary>
    private sealed record ConfirmPlan(ItemState State, FlyerConfirmationKind Kind, Product Product, bool IsCreated, IReadOnlyList<ProductTag> Tags, FlyerAllergenInput AllergenInput, bool FollowsEarlierRow = false);

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
/// <param name="ReviewFailed">読み取りの検証ができなかったか（一覧の上に知らせるだけ）</param>
/// <param name="MissedProducts">読み落とした商品の指摘</param>
/// <param name="Items">一覧の商品（再入力 → 取り込める → 使えない → 確定済み の順。同じ分類の中は読み取りの順）</param>
/// <param name="RemovedItems">外した掲載品（FR-IMPORT-20・21）</param>
/// <param name="Counts">分類ごとの件数（タブ）</param>
/// <param name="Finish">「取り込みを終える」で取り込む件数と内訳</param>
/// <param name="NotListed">今週の商品に入らない件数（FR-FLYER-06b）</param>
/// <param name="UnconfirmedCount">まだ取り込んでいない件数（取り込み済み・外した掲載品を除く）</param>
public sealed record FlyerSessionView(
    string Id,
    bool IsReimport,
    bool ReviewFailed,
    IReadOnlyList<FlyerMissedProduct> MissedProducts,
    IReadOnlyList<FlyerItemView> Items,
    IReadOnlyList<FlyerRemovedItemView> RemovedItems,
    FlyerCategoryCounts Counts,
    FlyerFinishSummary Finish,
    FlyerNotListedCounts NotListed,
    int UnconfirmedCount);

/// <summary>一覧での並びと表示の状態（値の順が一覧の並び。JSON でもこの名前）</summary>
public enum FlyerItemStatus
{
    /// <summary>🟠 再入力</summary>
    NeedsInput,

    /// <summary>🟢 取り込める</summary>
    Ready,

    /// <summary>🔴 使えない</summary>
    Unusable,

    /// <summary>確定済み（取り込み済み）</summary>
    Confirmed,
}

/// <summary>
/// 取り込みの1商品の表示。
/// </summary>
/// <param name="Id">ID</param>
/// <param name="Status">一覧の状態（確定済みを含む）</param>
/// <param name="Category">取り込みの分類（確定済みなら null）</param>
/// <param name="IsNew">新しい商品の印（未確定：一致する商品がない。確定済み：新しい商品として登録した）</param>
/// <param name="IsManual">手入力した商品か</param>
/// <param name="IsPiecesEnteredByStaff">「使えない（入数が分からない）」に職員が入数を入れたか</param>
/// <param name="Read">今の値（直した値を反映したもの）</param>
/// <param name="OriginalRead">Claude が読んだまま（二重の読み取りをまとめたあと）</param>
/// <param name="EditedFields">職員が直した項目（FlyerItemField の値）</param>
/// <param name="Reasons">使えない・再入力の理由（アレルゲンに関わる理由が先）</param>
/// <param name="Notes">注意（分類は変えない。アレルゲンに関わるものが先。09 の 3.8）</param>
/// <param name="LabelAllergens">今の表記の品目</param>
/// <param name="AbbreviationHits">略記で当てた語と品目（「麦→小麦」）</param>
/// <param name="UnmappedTerms">品目に対応づけできなかった表記の語（選んだ語も残る）</param>
/// <param name="ResolvedTerms">職員が品目を選んだ語（品目が空なら「対応する品目なし」）</param>
/// <param name="UnresolvedTerms">まだ品目を選んでいない語</param>
/// <param name="DescriptionHitWords">説明文で当たった避ける品目の探す言葉</param>
/// <param name="FlyerAllergens">チラシの品目（参考の記録に入る品目）</param>
/// <param name="Discrepancies">確認済みとの食い違い（09 の 3.5。取り込みを終えると参考の記録を足す）</param>
/// <param name="RecordedDiscrepancyAllergens">食い違いで「参考」の記録を足した品目（「記録を足しました」と知らせる）</param>
/// <param name="IsLabelAbsent">アレルゲンの表記がないか（表記の品目も品目の分からない語もない。決めたこと E）</param>
/// <param name="ListingLabelText">取り込みを終えると掲載品の写しに入る表記の原文（献立の確定で見比べる表記。決めたこと F）</param>
/// <param name="MatchedProduct">一致した商品（確定済みなら取り込みに使った商品）</param>
/// <param name="OtherMatchCount">ほかに同じ名前（名前＋メーカー）の商品が何件あるか（FR-IMPORT-04）</param>
/// <param name="NotMatchableReason">照合できない理由（商品名かメーカーが空）</param>
/// <param name="PiecesPerPack">使う入数（読み取りの候補、なければ一致した商品の入数）</param>
/// <param name="DuplicateConflicts">二重の読み取りで食い違った値</param>
/// <param name="SourceCount">まとめた読み取りの数（1なら二重でない）</param>
/// <param name="Confirmation">取り込んだ内容（未確定なら null）</param>
public sealed record FlyerItemView(
    string Id,
    FlyerItemStatus Status,
    FlyerItemCategory? Category,
    bool IsNew,
    bool IsManual,
    bool IsPiecesEnteredByStaff,
    FlyerReadProduct Read,
    FlyerReadProduct OriginalRead,
    IReadOnlyList<string> EditedFields,
    IReadOnlyList<FlyerItemReason> Reasons,
    IReadOnlyList<FlyerItemReason> Notes,
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
    string ListingLabelText,
    FlyerMatchedProductView? MatchedProduct,
    int OtherMatchCount,
    ProductNotMatchableReason? NotMatchableReason,
    int? PiecesPerPack,
    IReadOnlyList<FlyerDuplicateConflict> DuplicateConflicts,
    int SourceCount,
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
/// <param name="DiscrepancyAllergens">確認済みの商品との食い違いの品目の名前（取り込みを終えると「参考」の記録を足す。09 の 3.5）</param>
public sealed record FlyerRemovedItemView(string Id, string Name, string? Maker, string? Volume, string Reason, bool IsNonFood, IReadOnlyList<string> DiscrepancyAllergens);

/// <summary>分類ごとの件数</summary>
public sealed record FlyerCategoryCounts(int NeedsInput, int Ready, int Unusable, int Confirmed, int Removed);

/// <summary>「取り込みを終える」で取り込む件数（ReadyCount ＝ 取り込める。NewCount ＝ そのうち新しい商品、ExistingCount ＝ 登録済みの商品）</summary>
public sealed record FlyerFinishSummary(int ReadyCount, int NewCount, int ExistingCount);

/// <summary>今週の商品に入らない件数（Total ＝ 使えない ＋ 再入力。外した掲載品は数えない）</summary>
public sealed record FlyerNotListedCounts(int Total, int Unusable, int NeedsInput);

/// <summary>
/// 「取り込みを終える」の結果。
/// </summary>
/// <param name="CreatedCount">新しく登録した件数</param>
/// <param name="UpdatedCount">登録済みの商品を上書きした件数</param>
/// <param name="NotListedUnusableCount">使えない（今週の商品に入らない）件数</param>
/// <param name="NotListedNeedsInputCount">再入力（今週の商品に入らない）件数</param>
/// <param name="ConfirmedItemIds">取り込んだ取り込みの商品</param>
/// <param name="RecordedProducts">確認済みとの食い違いで「参考」の記録を足した商品（09 の 3.5）</param>
public sealed record FlyerFinishResult(
    int CreatedCount,
    int UpdatedCount,
    int NotListedUnusableCount,
    int NotListedNeedsInputCount,
    IReadOnlyList<string> ConfirmedItemIds,
    IReadOnlyList<FlyerRecordedProductView> RecordedProducts);

/// <summary>「参考」の記録を足した商品と、食い違った品目</summary>
public sealed record FlyerRecordedProductView(string ProductId, string ProductName, IReadOnlyList<AllergenRef> Allergens);
