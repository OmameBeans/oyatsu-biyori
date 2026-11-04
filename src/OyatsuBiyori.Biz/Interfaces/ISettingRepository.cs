using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>
/// 施設の条件・避ける品目・アレルゲンのマスタ（取得したものは追跡され、IUnitOfWork で保存する）。
/// </summary>
public interface ISettingRepository
{
    /// <summary>施設の条件（1件。初期データで必ずある）</summary>
    Task<Setting> GetSettingAsync();

    /// <summary>すべての品目（探す言葉つき・表示順）</summary>
    Task<IReadOnlyList<Allergen>> GetAllergensAsync();

    /// <summary>品目（探す言葉つき。なければ null）</summary>
    Task<Allergen?> GetAllergenAsync(string allergenId);

    /// <summary>グループ（品目つき・表示順）</summary>
    Task<IReadOnlyList<AllergenGroup>> GetGroupsAsync();

    /// <summary>施設の避ける品目（品目と探す言葉つき）</summary>
    Task<IReadOnlyList<AvoidedAllergen>> GetAvoidedAsync();

    /// <summary>避ける品目の品目だけ（判定に渡す形。探す言葉つき）</summary>
    Task<IReadOnlyList<Allergen>> GetAvoidedAllergensAsync();

    /// <summary>探す言葉（なければ null）</summary>
    Task<AllergenKeyword?> GetKeywordAsync(string keywordId);

    /// <summary>品目が確認の記録で使われているか</summary>
    Task<bool> IsUsedByChecksAsync(string allergenId);

    /// <summary>品目がグループに入っているか</summary>
    Task<bool> IsInGroupAsync(string allergenId);

    /// <summary>避ける品目を足す</summary>
    void AddAvoided(AvoidedAllergen avoided);

    /// <summary>避ける品目を外す</summary>
    void RemoveAvoided(AvoidedAllergen avoided);

    /// <summary>品目を足す（施設の追加品目）</summary>
    void AddAllergen(Allergen allergen);

    /// <summary>品目を消す（施設の追加品目）</summary>
    void RemoveAllergen(Allergen allergen);

    /// <summary>探す言葉を足す</summary>
    void AddKeyword(AllergenKeyword keyword);

    /// <summary>探す言葉を消す</summary>
    void RemoveKeyword(AllergenKeyword keyword);

    /// <summary>安全に関わる設定の変更の記録を足す（追加のみ）</summary>
    void AddSafetyLog(SafetySettingLog log);
}
