using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// アレルゲンマスタ（29品目と施設で追加した品目）。品目はコードに直書きせず、このマスタで持つ。
/// </summary>
public sealed class Allergen : EntityBase
{
    /// <summary>識別子（例：egg, milk）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>表示名（例：卵）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>区分</summary>
    public AllergenCategory Category { get; set; }

    /// <summary>表示が確実でない品目か（推奨20品目と、経過措置中のカシューナッツ）。SR-06</summary>
    public bool LabelNotGuaranteed { get; set; }

    /// <summary>表示順</summary>
    public int SortOrder { get; set; }

    /// <summary>原材料名で探す言葉</summary>
    public List<AllergenKeyword> Keywords { get; set; } = [];

    /// <summary>チラシのアレルゲン表記の略記（「麦」など。チラシの表記の対応づけだけに使う。D-96）</summary>
    public List<AllergenLabelAbbreviation> LabelAbbreviations { get; set; } = [];
}
