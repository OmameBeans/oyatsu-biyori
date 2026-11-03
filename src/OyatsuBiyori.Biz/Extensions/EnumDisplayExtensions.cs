using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Extensions;

/// <summary>
/// enum の日本語の表示名。
/// </summary>
public static class EnumDisplayExtensions
{
    /// <summary>アレルゲンの区分の表示名</summary>
    public static string ToDisplayString(this AllergenCategory value) => value switch
    {
        AllergenCategory.Specified => "特定原材料",
        AllergenCategory.Recommended => "特定原材料に準ずるもの",
        AllergenCategory.FacilityAdded => "施設で追加",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>確認の記録のステータスの表示名</summary>
    public static string ToDisplayString(this AllergenCheckStatus value) => value switch
    {
        AllergenCheckStatus.Confirmed => "確認済み",
        AllergenCheckStatus.Reference => "参考",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>情報源の表示名</summary>
    public static string ToDisplayString(this AllergenCheckSource value) => value switch
    {
        AllergenCheckSource.Package => "パッケージ",
        AllergenCheckSource.CoopSite => "コープのサイト",
        AllergenCheckSource.Flyer => "チラシ",
        AllergenCheckSource.Other => "その他",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>探す言葉が当たった場所の表示名</summary>
    public static string ToDisplayString(this KeywordHitSource value) => value switch
    {
        KeywordHitSource.ProductName => "商品名",
        KeywordHitSource.RawIngredientsOcr => "原材料の全文（読み取り）",
        KeywordHitSource.RawIngredients => "原材料の全文（職員が確認）",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>商品の現在のステータスの表示名</summary>
    public static string ToDisplayString(this ProductAllergenStatus value) => value switch
    {
        ProductAllergenStatus.Confirmed => "確認済み",
        ProductAllergenStatus.RecheckNeeded => "要再確認",
        ProductAllergenStatus.Reference => "参考",
        ProductAllergenStatus.Unknown => "不明",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>判定の区分の表示名</summary>
    public static string ToDisplayString(this AllergenJudgementCategory value) => value switch
    {
        AllergenJudgementCategory.Blocked => "入れられない",
        AllergenJudgementCategory.Warning => "要確認",
        AllergenJudgementCategory.Safe => "安全",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>細かい判定の種類の表示名</summary>
    public static string ToDisplayString(this AllergenJudgementKind value) => value switch
    {
        AllergenJudgementKind.Contains => "含む",
        AllergenJudgementKind.MayContain => "含む可能性あり",
        AllergenJudgementKind.Unknown => "不明",
        AllergenJudgementKind.Reference => "参考",
        AllergenJudgementKind.RecheckNeeded => "要再確認",
        AllergenJudgementKind.UnconfirmedItems => "未確認の品目あり",
        AllergenJudgementKind.Safe => "安全",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>おやつの分類の表示名</summary>
    public static string ToDisplayString(this SnackType value) => value switch
    {
        SnackType.Salty => "しょっぱいもの",
        SnackType.Sweet => "あまいもの",
        SnackType.Smooth => "のどごしのいいもの",
        SnackType.NotSnack => "対象外",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>献立の状態の表示名</summary>
    public static string ToDisplayString(this MenuStatus value) => value switch
    {
        MenuStatus.Draft => "下書き",
        MenuStatus.Confirmed => "確定",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>日の種類の表示名</summary>
    public static string ToDisplayString(this DayType value) => value switch
    {
        DayType.Normal => "通常",
        DayType.Handmade => "手作り",
        DayType.NoSnack => "おやつなし",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>人数の出どころの表示名</summary>
    public static string ToDisplayString(this ChildrenCountSource value) => value switch
    {
        ChildrenCountSource.Default => "通常",
        ChildrenCountSource.FromRequest => "要望から読み取り",
        ChildrenCountSource.EditedByStaff => "職員が修正",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>安全に関わる設定の変更の種類の表示名</summary>
    public static string ToDisplayString(this SafetySettingLogKind value) => value switch
    {
        SafetySettingLogKind.AvoidedAllergenAdded => "避ける品目の追加",
        SafetySettingLogKind.AvoidedAllergenRemoved => "避ける品目の削除",
        SafetySettingLogKind.KeywordAdded => "探す言葉の追加",
        SafetySettingLogKind.KeywordRemoved => "探す言葉の削除",
        SafetySettingLogKind.RecheckDaysChanged => "再確認期間の変更",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
