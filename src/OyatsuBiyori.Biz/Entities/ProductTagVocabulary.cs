namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// タグ「味・種類」の決まった語（D-61。仮の語彙。実際のチラシを見て見直す）。
/// しょっぱい・あまい・「デザート・飲料」は、タグではなく「おやつの分類」で表す。
/// </summary>
public static class ProductTagVocabulary
{
    /// <summary>味・種類として選べる語</summary>
    public static readonly IReadOnlyList<string> Flavors =
    [
        "スナック",
        "せんべい",
        "焼き菓子",
        "チョコ",
        "キャンディ",
        "グミ",
        "ゼリー",
        "プリン",
        "アイス",
        "ジュース",
        "牛乳",
        "お茶",
        "パン",
        "果物",
    ];

    /// <summary>決まった語か</summary>
    public static bool IsFlavor(string value) => Flavors.Contains(value);
}
