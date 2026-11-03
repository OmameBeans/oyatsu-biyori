using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 避ける品目・施設の追加品目・探す言葉の規則（FR-AVOID-03・07・08、SR-13・SR-14、D-50・D-56、T1-20）。
/// 探す言葉を消すと見落としが増えるため、消せるものを絞る。
/// </summary>
public static class AllergenSettingRules
{
    /// <summary>
    /// 探す言葉を削除できるか：初期データの言葉は削除できない（D-50）。施設の追加品目の最後の1語は削除できない
    /// （言葉が0件になると、原材料の全文で探せなくなる。SR-13）。
    /// </summary>
    /// <param name="keyword">削除する言葉</param>
    /// <param name="allergen">言葉の品目（Keywords を読み込んでおくこと）</param>
    public static Result CanDeleteKeyword(AllergenKeyword keyword, Allergen allergen)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(allergen);

        if (keyword.AllergenId != allergen.Id || !allergen.Keywords.Any(k => k.Id == keyword.Id))
        {
            throw new ArgumentException("言葉と品目が対応していません。品目は Keywords を含めて読み込んでください。", nameof(allergen));
        }

        if (keyword.IsSeed)
        {
            return Result.Error($"「{keyword.Keyword}」は最初から入っている言葉のため削除できません");
        }

        if (allergen.Category == AllergenCategory.FacilityAdded && allergen.Keywords.Count <= 1)
        {
            return Result.Error($"「{allergen.Name}」の探す言葉が1つもなくなるため削除できません。先に別の言葉を足してください");
        }

        return new Result();
    }

    /// <summary>
    /// 探す言葉を足す。正規化して空にならず、同じ品目で重複しないこと。正規化した言葉は必ず ForKeywordSearch で作る。
    /// </summary>
    /// <param name="allergen">言葉を足す品目（Keywords を読み込んでおくこと）</param>
    /// <param name="keyword">足す言葉</param>
    public static Result<AllergenKeyword> CreateKeyword(Allergen allergen, string? keyword)
    {
        ArgumentNullException.ThrowIfNull(allergen);

        var validation = InputValidator.ValidateKeyword(keyword);
        if (validation.HasError)
        {
            return Result<AllergenKeyword>.Error(validation.ErrorMessages[0]);
        }

        var text = keyword!.Trim();
        var normalized = TextNormalizer.ForKeywordSearch(text);
        if (allergen.Keywords.Any(k => TextNormalizer.ForKeywordSearch(k.Keyword) == normalized))
        {
            return Result<AllergenKeyword>.Error($"「{text}」は「{allergen.Name}」の探す言葉にもう入っています");
        }

        return Result<AllergenKeyword>.Ok(new AllergenKeyword
        {
            AllergenId = allergen.Id,
            Keyword = text,
            NormalizedKeyword = normalized,
            IsSeed = false,
        });
    }

    /// <summary>
    /// 施設の追加品目を登録する（FR-AVOID-03）。品目名を探す言葉として自動で入れ、ほかの言葉も足す。
    /// 品目名は正規化して空にならず、既存の品目（29品目を含む）と重ならないこと。
    /// </summary>
    /// <param name="name">品目名（例：ヘーゼルナッツ）</param>
    /// <param name="extraKeywords">品目名のほかに探す言葉</param>
    /// <param name="existingAllergens">マスタのすべての品目</param>
    public static Result<Allergen> CreateFacilityAllergen(string? name, IEnumerable<string> extraKeywords, IEnumerable<Allergen> existingAllergens)
    {
        ArgumentNullException.ThrowIfNull(extraKeywords);
        ArgumentNullException.ThrowIfNull(existingAllergens);

        var normalizedName = TextNormalizer.ForKeywordSearch(name);
        if (normalizedName.Length == 0)
        {
            return Result<Allergen>.Error("品目名を入れてください");
        }

        var existing = existingAllergens.ToList();
        var duplicate = existing.FirstOrDefault(a => TextNormalizer.ForKeywordSearch(a.Name) == normalizedName);
        if (duplicate is not null)
        {
            return Result<Allergen>.Error($"「{duplicate.Name}」はもう品目にあります。一覧から選んでください");
        }

        var allergen = new Allergen
        {
            Code = "facility-" + Guid.NewGuid().ToString("N"),
            Name = name!.Trim(),
            Category = AllergenCategory.FacilityAdded,
            LabelNotGuaranteed = false,
            SortOrder = existing.Count == 0 ? 1 : existing.Max(a => a.SortOrder) + 1,
        };

        var result = new Result<Allergen>();
        foreach (var keyword in new[] { allergen.Name }.Concat(extraKeywords))
        {
            var created = CreateKeyword(allergen, keyword);
            if (created.HasError)
            {
                // 品目名と同じ言葉が重なっただけなら黙って飛ばす。それ以外はエラーにする
                if (TextNormalizer.ForKeywordSearch(keyword) == normalizedName)
                {
                    continue;
                }

                result.ErrorMessages.AddRange(created.ErrorMessages);
                continue;
            }

            allergen.Keywords.Add(created.Value!);
        }

        if (result.HasError)
        {
            return result;
        }

        result.Value = allergen;
        return result;
    }

    /// <summary>
    /// 品目を削除できるか：29品目は削除できない（FR-AVOID-08）。施設の追加品目は、避ける品目・グループ・確認の記録の
    /// どれからも使われていなければ削除できる（D-56。safety_setting_logs からの参照は数えない）。
    /// </summary>
    public static Result CanDeleteAllergen(Allergen allergen, bool isAvoided, bool isInGroup, bool isUsedByChecks)
    {
        ArgumentNullException.ThrowIfNull(allergen);

        if (allergen.Category != AllergenCategory.FacilityAdded)
        {
            return Result.Error($"「{allergen.Name}」は法令の品目のため削除できません");
        }

        var result = new Result();
        if (isAvoided)
        {
            result.ErrorMessages.Add($"「{allergen.Name}」は避ける品目に入っているため削除できません。先に避ける品目から外してください");
        }

        if (isInGroup)
        {
            result.ErrorMessages.Add($"「{allergen.Name}」はグループに入っているため削除できません");
        }

        if (isUsedByChecks)
        {
            result.ErrorMessages.Add($"「{allergen.Name}」はアレルゲン確認の記録で使われているため削除できません");
        }

        return result;
    }

    /// <summary>29品目の名前は変えられない（FR-AVOID-08）。施設の追加品目は変えられる</summary>
    public static Result CanRenameAllergen(Allergen allergen)
    {
        ArgumentNullException.ThrowIfNull(allergen);

        return allergen.Category == AllergenCategory.FacilityAdded
            ? new Result()
            : Result.Error($"「{allergen.Name}」は法令の品目のため名前を変えられません");
    }
}
