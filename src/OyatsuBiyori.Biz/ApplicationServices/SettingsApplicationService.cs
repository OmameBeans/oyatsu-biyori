using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.ApplicationServices;

/// <summary>
/// 条件設定（SC-03）・避ける品目（SC-04）の操作（FR-COND、FR-AVOID、T2-06・T2-07）。
/// 避ける品目・探す言葉・再確認期間の変更は、すぐに判定に効く（判定は保存しないため）。変更は safety_setting_logs に残す。
/// </summary>
public sealed class SettingsApplicationService(
    ISettingRepository settingRepository,
    IUnitOfWork unitOfWork,
    IUserContext userContext,
    TimeProvider timeProvider)
{
    private readonly ISettingRepository _settings = settingRepository ?? throw new ArgumentNullException(nameof(settingRepository));
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IUserContext _userContext = userContext ?? throw new ArgumentNullException(nameof(userContext));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>条件設定を取得する</summary>
    public async Task<Result<SettingsView>> GetSettingsAsync()
    {
        var setting = await _settings.GetSettingAsync();
        return Result<SettingsView>.Ok(SettingsView.From(setting));
    }

    /// <summary>
    /// 条件設定を保存する。変更は次に作る献立から効く（作成済みの下書きは変えない。U-37）。
    /// 再確認期間の変更は safety_setting_logs に残す（FR-AVOID-06）。
    /// </summary>
    public async Task<Result<SettingsView>> UpdateSettingsAsync(SettingsUpdateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rules = input.DuplicateAvoidDays ?? [];
        var validation = InputValidator.ValidateSettings(new SettingsInput(
            input.DefaultChildrenCount,
            input.DefaultWeeklyBudget,
            input.ServingWeekdays ?? [],
            input.RecheckDays,
            rules));
        if (validation.HasError)
        {
            var error = new Result<SettingsView>();
            error.ErrorMessages.AddRange(validation.ErrorMessages);
            return error;
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var setting = await _settings.GetSettingAsync();

        if (setting.RecheckDays != input.RecheckDays!.Value)
        {
            _settings.AddSafetyLog(new SafetySettingLog
            {
                OperatedAt = now,
                OperatedBy = user?.UserName,
                Kind = SafetySettingLogKind.RecheckDaysChanged,
                Detail = $"再確認期間を {setting.RecheckDays}日 から {input.RecheckDays.Value}日 に変更",
                UpdateDateTime = now,
                UpdateUserId = user?.UserId,
            });
        }

        setting.DefaultChildrenCount = input.DefaultChildrenCount;
        setting.DefaultWeeklyBudget = input.DefaultWeeklyBudget;
        setting.ServingWeekdays = input.ServingWeekdays!.OrderBy(d => ((int)d + 6) % 7).ToList();
        setting.RecheckDays = input.RecheckDays.Value;
        setting.DuplicateAvoidDays = rules.ToList();
        setting.DuplicateRuleNote = string.IsNullOrWhiteSpace(input.DuplicateRuleNote) ? null : input.DuplicateRuleNote.Trim();
        setting.UpdateDateTime = now;
        setting.UpdateUserId = user?.UserId;

        await _unitOfWork.SaveChangesAsync();
        return Result<SettingsView>.Ok(SettingsView.From(setting));
    }

    /// <summary>品目・グループ・避ける品目・探す言葉の一覧（フロントは品目を定数にせず、これを使う。D-80）</summary>
    public async Task<Result<AllergenMasterView>> GetAllergenMasterAsync()
    {
        var allergens = await _settings.GetAllergensAsync();
        var groups = await _settings.GetGroupsAsync();
        var avoided = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToHashSet(StringComparer.Ordinal);
        var groupMemberIds = groups.SelectMany(g => g.Members).Select(m => m.AllergenId).ToHashSet(StringComparer.Ordinal);

        var views = new List<AllergenView>();
        foreach (var allergen in allergens)
        {
            var canDelete = allergen.Category == AllergenCategory.FacilityAdded
                && !avoided.Contains(allergen.Id)
                && !groupMemberIds.Contains(allergen.Id)
                && !await _settings.IsUsedByChecksAsync(allergen.Id);
            views.Add(new AllergenView(
                allergen.Id,
                allergen.Code,
                allergen.Name,
                allergen.Category,
                allergen.LabelNotGuaranteed,
                allergen.SortOrder,
                avoided.Contains(allergen.Id),
                canDelete,
                allergen.Keywords
                    .OrderBy(k => k.IsSeed ? 0 : 1)
                    .ThenBy(k => k.UpdateDateTime)
                    .Select(k => new AllergenKeywordView(k.Id, k.Keyword, k.IsSeed))
                    .ToList()));
        }

        return Result<AllergenMasterView>.Ok(new AllergenMasterView(
            views,
            groups.Select(g => new AllergenGroupView(g.Id, g.Name, g.Members.Select(m => m.AllergenId).ToList())).ToList()));
    }

    /// <summary>避ける品目を足す（FR-AVOID-01）</summary>
    public async Task<Result> AddAvoidedAsync(string? allergenId)
    {
        if (string.IsNullOrEmpty(allergenId))
        {
            return Result.Error("品目が選ばれていません");
        }

        var allergen = await _settings.GetAllergenAsync(allergenId);
        if (allergen is null)
        {
            return Result.Error("品目が見つかりません。画面を読み込み直してください");
        }

        var avoided = await _settings.GetAvoidedAsync();
        if (avoided.Any(a => a.AllergenId == allergenId))
        {
            return new Result();
        }

        await AddAvoidedCoreAsync(allergen);
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>グループの品目をまとめて避ける品目に足す（あとで個別に外せる。FR-AVOID-02）</summary>
    public async Task<Result> AddGroupAsync(string? groupId)
    {
        var group = (await _settings.GetGroupsAsync()).FirstOrDefault(g => g.Id == groupId);
        if (group is null)
        {
            return Result.Error("グループが見つかりません。画面を読み込み直してください");
        }

        var avoided = (await _settings.GetAvoidedAsync()).Select(a => a.AllergenId).ToHashSet(StringComparer.Ordinal);
        foreach (var member in group.Members.Where(m => !avoided.Contains(m.AllergenId)))
        {
            var allergen = await _settings.GetAllergenAsync(member.AllergenId);
            if (allergen is not null)
            {
                await AddAvoidedCoreAsync(allergen);
            }
        }

        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>避ける品目を外す（画面で「この品目を避ける子がもういないことを確認しましたか？」を確かめてから呼ぶ。FR-AVOID-05）</summary>
    public async Task<Result> RemoveAvoidedAsync(string? allergenId)
    {
        var avoided = (await _settings.GetAvoidedAsync()).FirstOrDefault(a => a.AllergenId == allergenId);
        if (avoided is null)
        {
            return new Result();
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _settings.RemoveAvoided(avoided);
        _settings.AddSafetyLog(new SafetySettingLog
        {
            OperatedAt = now,
            OperatedBy = user?.UserName,
            Kind = SafetySettingLogKind.AvoidedAllergenRemoved,
            AllergenId = avoided.AllergenId,
            Detail = $"避ける品目から「{avoided.Allergen.Name}」を外した",
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>探す言葉を足す（FR-AVOID-07）</summary>
    public async Task<Result> AddKeywordAsync(string? allergenId, string? keyword)
    {
        var allergen = string.IsNullOrEmpty(allergenId) ? null : await _settings.GetAllergenAsync(allergenId);
        if (allergen is null)
        {
            return Result.Error("品目が見つかりません。画面を読み込み直してください");
        }

        var created = AllergenSettingRules.CreateKeyword(allergen, keyword);
        if (created.HasError)
        {
            return Result.Error(created.ErrorMessages[0]);
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var entity = created.Value!;
        entity.UpdateDateTime = now;
        entity.UpdateUserId = user?.UserId;
        _settings.AddKeyword(entity);
        _settings.AddSafetyLog(new SafetySettingLog
        {
            OperatedAt = now,
            OperatedBy = user?.UserName,
            Kind = SafetySettingLogKind.KeywordAdded,
            AllergenId = allergen.Id,
            Detail = $"「{allergen.Name}」に探す言葉「{entity.Keyword}」を追加",
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>探す言葉を消す（初期データの言葉・施設の追加品目の最後の1語は消せない。D-50）</summary>
    public async Task<Result> RemoveKeywordAsync(string? keywordId)
    {
        var keyword = string.IsNullOrEmpty(keywordId) ? null : await _settings.GetKeywordAsync(keywordId);
        if (keyword is null)
        {
            return Result.Error("探す言葉が見つかりません。画面を読み込み直してください");
        }

        var allergen = await _settings.GetAllergenAsync(keyword.AllergenId);
        if (allergen is null)
        {
            return Result.Error("品目が見つかりません。画面を読み込み直してください");
        }

        var rule = AllergenSettingRules.CanDeleteKeyword(keyword, allergen);
        if (rule.HasError)
        {
            return rule;
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _settings.RemoveKeyword(keyword);
        _settings.AddSafetyLog(new SafetySettingLog
        {
            OperatedAt = now,
            OperatedBy = user?.UserName,
            Kind = SafetySettingLogKind.KeywordRemoved,
            AllergenId = allergen.Id,
            Detail = $"「{allergen.Name}」の探す言葉「{keyword.Keyword}」を削除",
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    /// <summary>
    /// 施設の追加品目を登録する（品目名を探す言葉として自動で入れる。FR-AVOID-03）。
    /// avoid が true なら、そのまま避ける品目にも足す（登録と同時に避けるのがふつうのため）。
    /// </summary>
    public async Task<Result<string>> CreateFacilityAllergenAsync(string? name, IReadOnlyList<string>? extraKeywords, bool avoid)
    {
        var allergens = await _settings.GetAllergensAsync();
        var created = AllergenSettingRules.CreateFacilityAllergen(name, extraKeywords ?? [], allergens);
        if (created.HasError)
        {
            var error = new Result<string>();
            error.ErrorMessages.AddRange(created.ErrorMessages);
            return error;
        }

        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var allergen = created.Value!;
        allergen.UpdateDateTime = now;
        allergen.UpdateUserId = user?.UserId;
        foreach (var keyword in allergen.Keywords)
        {
            keyword.UpdateDateTime = now;
            keyword.UpdateUserId = user?.UserId;
        }

        _settings.AddAllergen(allergen);
        if (avoid)
        {
            await AddAvoidedCoreAsync(allergen);
        }

        await _unitOfWork.SaveChangesAsync();
        return Result<string>.Ok(allergen.Id);
    }

    /// <summary>施設の追加品目を削除する（避ける品目・グループ・確認の記録で使われていなければ。D-56）</summary>
    public async Task<Result> DeleteFacilityAllergenAsync(string? allergenId)
    {
        var allergen = string.IsNullOrEmpty(allergenId) ? null : await _settings.GetAllergenAsync(allergenId);
        if (allergen is null)
        {
            return Result.Error("品目が見つかりません。画面を読み込み直してください");
        }

        var isAvoided = (await _settings.GetAvoidedAsync()).Any(a => a.AllergenId == allergen.Id);
        var rule = AllergenSettingRules.CanDeleteAllergen(
            allergen,
            isAvoided,
            await _settings.IsInGroupAsync(allergen.Id),
            await _settings.IsUsedByChecksAsync(allergen.Id));
        if (rule.HasError)
        {
            return rule;
        }

        _settings.RemoveAllergen(allergen);
        await _unitOfWork.SaveChangesAsync();
        return new Result();
    }

    private async Task AddAvoidedCoreAsync(Allergen allergen)
    {
        var user = await _userContext.GetCurrentUserInfoAsync();
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _settings.AddAvoided(new AvoidedAllergen
        {
            AllergenId = allergen.Id,
            AddedAt = now,
            AddedBy = user?.UserName,
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
        _settings.AddSafetyLog(new SafetySettingLog
        {
            OperatedAt = now,
            OperatedBy = user?.UserName,
            Kind = SafetySettingLogKind.AvoidedAllergenAdded,
            AllergenId = allergen.Id,
            Detail = $"避ける品目に「{allergen.Name}」を追加",
            UpdateDateTime = now,
            UpdateUserId = user?.UserId,
        });
    }
}

/// <summary>条件設定の保存の入力</summary>
public sealed record SettingsUpdateInput(
    int? DefaultChildrenCount,
    int? DefaultWeeklyBudget,
    IReadOnlyList<DayOfWeek>? ServingWeekdays,
    int? RecheckDays,
    IReadOnlyList<DuplicateAvoidRule>? DuplicateAvoidDays,
    string? DuplicateRuleNote);

/// <summary>条件設定の表示</summary>
public sealed record SettingsView(
    int? DefaultChildrenCount,
    int? DefaultWeeklyBudget,
    IReadOnlyList<DayOfWeek> ServingWeekdays,
    int RecheckDays,
    IReadOnlyList<DuplicateAvoidRule> DuplicateAvoidDays,
    string? DuplicateRuleNote)
{
    /// <summary>献立を作れるか（通常の人数と既定の週の予算が設定済み。FR-COND-02・03）</summary>
    public bool IsReadyForMenu => DefaultChildrenCount is > 0 && DefaultWeeklyBudget is > 0;

    /// <summary>エンティティから作る</summary>
    public static SettingsView From(Setting setting) => new(
        setting.DefaultChildrenCount,
        setting.DefaultWeeklyBudget,
        setting.ServingWeekdays,
        setting.RecheckDays,
        setting.DuplicateAvoidDays,
        setting.DuplicateRuleNote);
}

/// <summary>品目のマスタ</summary>
public sealed record AllergenMasterView(IReadOnlyList<AllergenView> Allergens, IReadOnlyList<AllergenGroupView> Groups);

/// <summary>品目</summary>
public sealed record AllergenView(
    string Id,
    string Code,
    string Name,
    AllergenCategory Category,
    bool LabelNotGuaranteed,
    int SortOrder,
    bool IsAvoided,
    bool CanDelete,
    IReadOnlyList<AllergenKeywordView> Keywords);

/// <summary>探す言葉</summary>
public sealed record AllergenKeywordView(string Id, string Keyword, bool IsSeed);

/// <summary>グループ</summary>
public sealed record AllergenGroupView(string Id, string Name, IReadOnlyList<string> AllergenIds);
