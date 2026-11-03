using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;
using OyatsuBiyori.Biz.MasterData;

namespace OyatsuBiyori.Biz.Tests;

/// <summary>
/// テスト用のデータ（マスタの初期データと、架空の商品・確認の記録）。
/// </summary>
internal static class TestData
{
    /// <summary>テストの「今日」（日本時間）</summary>
    public static readonly DateOnly Today = new(2026, 11, 3);

    /// <summary>再確認期間（既定）</summary>
    public const int RecheckDays = 90;

    private static readonly Dictionary<string, Allergen> AllergensByCode = BuildAllergens();

    /// <summary>マスタの品目（探す言葉つき）</summary>
    public static Allergen Allergen(string code) => AllergensByCode[code];

    /// <summary>マスタのすべての品目（探す言葉つき）</summary>
    public static IReadOnlyList<Allergen> AllAllergens => AllergensByCode.Values.OrderBy(a => a.SortOrder).ToList();

    /// <summary>ナッツ類（すべて）のグループの品目</summary>
    public static IReadOnlyList<Allergen> Nuts()
    {
        var group = MasterSeed.Groups().Single(g => g.Name == "ナッツ類（すべて）");
        var ids = MasterSeed.GroupMembers().Where(m => m.AllergenGroupId == group.Id).Select(m => m.AllergenId).ToHashSet();
        return AllAllergens.Where(a => ids.Contains(a.Id)).ToList();
    }

    /// <summary>品目を選ぶ</summary>
    public static IReadOnlyList<Allergen> Avoid(params string[] codes) => codes.Select(Allergen).ToList();

    /// <summary>
    /// 確認済みの記録（パッケージ）。contains は「含む」、notContains は「含まない」と確定した品目。
    /// </summary>
    public static AllergenCheck Confirmed(DateOnly checkedOn, string[] contains, string[] notContains, string? rawIngredients = "砂糖、小麦粉", string? rawIngredientsOcr = null)
    {
        var check = NewCheck(AllergenCheckStatus.Confirmed, AllergenCheckSource.Package, checkedOn);
        check.RawIngredients = rawIngredients;
        check.RawIngredientsOcr = rawIngredientsOcr ?? rawIngredients;
        check.Items = Items(check, contains, true).Concat(Items(check, notContains, false)).ToList();
        return check;
    }

    /// <summary>確認済みの記録で、指定以外のすべての品目を「含まない」にしたもの</summary>
    public static AllergenCheck ConfirmedAll(DateOnly checkedOn, string[] contains, string? rawIngredients = "砂糖、小麦粉", string? rawIngredientsOcr = null)
    {
        var notContains = AllAllergens.Select(a => a.Code).Except(contains).ToArray();
        return Confirmed(checkedOn, contains, notContains, rawIngredients, rawIngredientsOcr);
    }

    /// <summary>参考の記録（チラシ）。「含む」の品目だけを持つ</summary>
    public static AllergenCheck Reference(DateOnly checkedOn, params string[] contains)
    {
        var check = NewCheck(AllergenCheckStatus.Reference, AllergenCheckSource.Flyer, checkedOn);
        check.SourceNote = contains.Length == 0 ? "表記なし" : "表記：" + string.Join("・", contains.Select(c => Allergen(c).Name));
        check.Items = Items(check, contains, true).ToList();
        return check;
    }

    /// <summary>架空の商品</summary>
    public static Product Product(string name, params AllergenCheck[] checks)
    {
        var product = new Product { Name = name, NormalizedName = TextNormalizer.ForProductMatch(name) };
        foreach (var check in checks)
        {
            check.ProductId = product.Id;
            product.AllergenChecks.Add(check);
        }

        return product;
    }

    /// <summary>日本時間のその日の正午（UTC）</summary>
    public static DateTime NoonUtc(DateOnly date) => JapanDate.StartOfDayUtc(date).AddHours(12);

    private static AllergenCheck NewCheck(AllergenCheckStatus status, AllergenCheckSource source, DateOnly checkedOn) => new()
    {
        Status = status,
        Source = source,
        CheckedAt = NoonUtc(checkedOn),
        CheckedBy = "test-user",
    };

    private static IEnumerable<AllergenCheckItem> Items(AllergenCheck check, string[] codes, bool contains) =>
        codes.Select(code => new AllergenCheckItem { AllergenCheckId = check.Id, AllergenId = Allergen(code).Id, Contains = contains });

    private static Dictionary<string, Allergen> BuildAllergens()
    {
        var allergens = MasterSeed.Allergens().ToDictionary(a => a.Code);
        var byId = allergens.Values.ToDictionary(a => a.Id);
        foreach (var keyword in MasterSeed.Keywords())
        {
            byId[keyword.AllergenId].Keywords.Add(keyword);
        }

        return allergens;
    }
}
