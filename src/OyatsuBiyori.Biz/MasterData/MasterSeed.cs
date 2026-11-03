using OyatsuBiyori.Biz;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.MasterData;

/// <summary>
/// マスタの初期データ（29品目・施設の追加品目・探す言葉・グループ・施設の条件）。
/// 品目は 2026年4月の改正後の29品目（03 §2.1）。制度が変わったら、ここ（とマイグレーション）でマスタを直し、判定のコードは変えない。
/// </summary>
public static class MasterSeed
{
    /// <summary>品目の定義（code, 表示名, 区分, 表示が確実でないか, 探す言葉）</summary>
    private static readonly (string Code, string Name, AllergenCategory Category, bool LabelNotGuaranteed, string[] Keywords)[] AllergenDefinitions =
    [
        // 特定原材料（表示義務・9品目）。カシューナッツは 2028-03-31 まで経過措置中のため「表示が確実でない」
        ("shrimp", "えび", AllergenCategory.Specified, false, []),
        ("crab", "かに", AllergenCategory.Specified, false, []),
        ("walnut", "くるみ", AllergenCategory.Specified, false, ["くるみ", "胡桃", "ウォールナッツ"]),
        ("wheat", "小麦", AllergenCategory.Specified, false, []),
        ("buckwheat", "そば", AllergenCategory.Specified, false, []),
        ("egg", "卵", AllergenCategory.Specified, false, []),
        ("milk", "乳", AllergenCategory.Specified, false, []),
        ("peanut", "落花生（ピーナッツ）", AllergenCategory.Specified, false, ["落花生", "ピーナッツ", "ピーナツ", "南京豆"]),
        ("cashew", "カシューナッツ", AllergenCategory.Specified, true, ["カシューナッツ", "カシュー"]),

        // 特定原材料に準ずるもの（表示推奨・20品目）。すべて「表示が確実でない」
        ("almond", "アーモンド", AllergenCategory.Recommended, true, ["アーモンド", "マジパン", "アーモンドプードル", "プラリネ", "マカロン", "フロランタン"]),
        ("abalone", "あわび", AllergenCategory.Recommended, true, []),
        ("squid", "いか", AllergenCategory.Recommended, true, []),
        ("salmon_roe", "いくら", AllergenCategory.Recommended, true, []),
        ("orange", "オレンジ", AllergenCategory.Recommended, true, []),
        ("kiwifruit", "キウイフルーツ", AllergenCategory.Recommended, true, []),
        ("beef", "牛肉", AllergenCategory.Recommended, true, []),
        ("sesame", "ごま", AllergenCategory.Recommended, true, []),
        ("salmon", "さけ", AllergenCategory.Recommended, true, []),
        ("mackerel", "さば", AllergenCategory.Recommended, true, []),
        ("gelatin", "ゼラチン", AllergenCategory.Recommended, true, []),
        ("soybean", "大豆", AllergenCategory.Recommended, true, []),
        ("chicken", "鶏肉", AllergenCategory.Recommended, true, []),
        ("banana", "バナナ", AllergenCategory.Recommended, true, []),
        ("pistachio", "ピスタチオ", AllergenCategory.Recommended, true, ["ピスタチオ"]),
        ("pork", "豚肉", AllergenCategory.Recommended, true, []),
        ("macadamia", "マカダミアナッツ", AllergenCategory.Recommended, true, ["マカダミア", "マカデミア"]),
        ("peach", "もも", AllergenCategory.Recommended, true, []),
        ("yam", "やまいも", AllergenCategory.Recommended, true, []),
        ("apple", "りんご", AllergenCategory.Recommended, true, []),

        // 施設で追加した品目（29品目にない。原材料の全文から探す。03 §2.2）
        ("hazelnut", "ヘーゼルナッツ", AllergenCategory.FacilityAdded, false, ["ヘーゼルナッツ", "ヘーゼル", "ヘイゼル", "ジャンドゥーヤ", "ジャンドゥ", "ノワゼット", "ノアゼット", "プラリネ"]),
        ("pecan", "ペカンナッツ", AllergenCategory.FacilityAdded, false, ["ペカン", "ピーカン"]),
        ("brazil_nut", "ブラジルナッツ", AllergenCategory.FacilityAdded, false, ["ブラジルナッツ"]),
        ("coconut", "ココナッツ", AllergenCategory.FacilityAdded, false, ["ココナッツ", "ココナツ", "ナタデココ"]),
        ("pine_nut", "松の実", AllergenCategory.FacilityAdded, false, ["松の実", "松のみ", "パインナッツ"]),
    ];

    /// <summary>グループの定義（03 §2.3）</summary>
    private static readonly (string Key, string Name, string[] Members)[] GroupDefinitions =
    [
        ("nuts", "ナッツ類（すべて）", ["walnut", "almond", "cashew", "macadamia", "hazelnut", "pistachio", "pecan", "brazil_nut", "pine_nut", "coconut", "peanut"]),
        ("crustacean", "甲殻類", ["shrimp", "crab"]),
    ];

    /// <summary>品目の ID</summary>
    public static string AllergenId(string code) => SeedIds.For("allergen:" + code);

    /// <summary>施設の条件の ID（1件だけ）</summary>
    public static string SettingId { get; } = SeedIds.For("setting");

    /// <summary>品目</summary>
    public static IEnumerable<Allergen> Allergens() => AllergenDefinitions.Select((d, i) => new Allergen
    {
        Id = AllergenId(d.Code),
        Code = d.Code,
        Name = d.Name,
        Category = d.Category,
        LabelNotGuaranteed = d.LabelNotGuaranteed,
        SortOrder = (i + 1) * 10,
        UpdateDateTime = SeedIds.SeededAt,
    });

    /// <summary>探す言葉（正規化は検索と同じ関数で作る）</summary>
    public static IEnumerable<AllergenKeyword> Keywords() => AllergenDefinitions.SelectMany(d => d.Keywords.Select(keyword => new AllergenKeyword
    {
        Id = SeedIds.For("keyword:" + d.Code + ":" + keyword),
        AllergenId = AllergenId(d.Code),
        Keyword = keyword,
        NormalizedKeyword = TextNormalizer.ForKeywordSearch(keyword),
        IsSeed = true,
        UpdateDateTime = SeedIds.SeededAt,
    }));

    /// <summary>グループ</summary>
    public static IEnumerable<AllergenGroup> Groups() => GroupDefinitions.Select((g, i) => new AllergenGroup
    {
        Id = SeedIds.For("group:" + g.Key),
        Name = g.Name,
        SortOrder = (i + 1) * 10,
        UpdateDateTime = SeedIds.SeededAt,
    });

    /// <summary>グループに含まれる品目</summary>
    public static IEnumerable<AllergenGroupMember> GroupMembers() => GroupDefinitions.SelectMany(g => g.Members.Select(code => new AllergenGroupMember
    {
        Id = SeedIds.For("group-member:" + g.Key + ":" + code),
        AllergenGroupId = SeedIds.For("group:" + g.Key),
        AllergenId = AllergenId(code),
        UpdateDateTime = SeedIds.SeededAt,
    }));

    /// <summary>施設の条件（月〜金・再確認90日・しょっぱい / あまい30日。人数と予算は未設定）</summary>
    public static Setting Setting() => new()
    {
        Id = SettingId,
        ServingWeekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        RecheckDays = 90,
        DuplicateAvoidDays = [new DuplicateAvoidRule(SnackType.Salty, 30), new DuplicateAvoidRule(SnackType.Sweet, 30)],
        UpdateDateTime = SeedIds.SeededAt,
    };
}
