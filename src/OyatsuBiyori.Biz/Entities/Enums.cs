namespace OyatsuBiyori.Biz.Entities;

/// <summary>アレルゲンの区分</summary>
public enum AllergenCategory
{
    /// <summary>特定原材料（表示義務）</summary>
    Specified,

    /// <summary>特定原材料に準ずるもの（表示推奨）</summary>
    Recommended,

    /// <summary>施設で追加した品目</summary>
    FacilityAdded,
}

/// <summary>アレルゲン確認の記録のステータス（「不明」の記録は作らない。記録がない商品が「不明」）</summary>
public enum AllergenCheckStatus
{
    /// <summary>確認済み（職員がパッケージで確認した）</summary>
    Confirmed,

    /// <summary>参考（パッケージ以外の情報）</summary>
    Reference,
}

/// <summary>アレルゲン確認の情報源</summary>
public enum AllergenCheckSource
{
    /// <summary>パッケージ（「確認済み」にできるのはこれだけ）</summary>
    Package,

    /// <summary>コープのサイト</summary>
    CoopSite,

    /// <summary>チラシ</summary>
    Flyer,

    /// <summary>その他</summary>
    Other,
}

/// <summary>商品の現在のステータス（表示・絞り込み用。保存しない）</summary>
public enum ProductAllergenStatus
{
    /// <summary>確認済み（期限内）</summary>
    Confirmed,

    /// <summary>要再確認（確認済みだが再確認期間を過ぎた）</summary>
    RecheckNeeded,

    /// <summary>参考</summary>
    Reference,

    /// <summary>不明（記録がない）</summary>
    Unknown,
}

/// <summary>アレルゲンの判定の区分（03 §5）</summary>
public enum AllergenJudgementCategory
{
    /// <summary>入れられない（エラー。確定できない）</summary>
    Blocked,

    /// <summary>警告（確認すれば確定できる）</summary>
    Warning,

    /// <summary>安全</summary>
    Safe,
}

/// <summary>アレルゲンの細かい判定の種類（確定時に確認した判定として保存し、変わったら確認し直す）</summary>
public enum AllergenJudgementKind
{
    /// <summary>避ける品目を含む（入れられない）</summary>
    Contains,

    /// <summary>探す言葉が当たった（含む可能性あり。入れられない）</summary>
    MayContain,

    /// <summary>不明（避ける品目があれば入れられない。なければ警告）</summary>
    Unknown,

    /// <summary>参考（警告）</summary>
    Reference,

    /// <summary>要再確認（警告）</summary>
    RecheckNeeded,

    /// <summary>未確認の品目あり（警告）</summary>
    UnconfirmedItems,

    /// <summary>安全</summary>
    Safe,
}

/// <summary>探す言葉が当たった場所（判定の理由の表示用。保存しない）</summary>
public enum KeywordHitSource
{
    /// <summary>商品名</summary>
    ProductName,

    /// <summary>Claude が読み取った原材料の全文</summary>
    RawIngredientsOcr,

    /// <summary>職員が確かめた原材料の全文</summary>
    RawIngredients,
}

/// <summary>おやつの分類（アプリの固定値）</summary>
public enum SnackType
{
    /// <summary>しょっぱいもの</summary>
    Salty,

    /// <summary>あまいもの</summary>
    Sweet,

    /// <summary>デザート・飲料</summary>
    Smooth,

    /// <summary>対象外（献立の候補にしない）</summary>
    NotSnack,
}

/// <summary>商品のタグの種類</summary>
public enum ProductTagKind
{
    /// <summary>キャラクター（自由入力）</summary>
    Character,

    /// <summary>味・種類（決まった語から選ぶ。ProductTagVocabulary）</summary>
    Flavor,
}

/// <summary>商品のタグの出どころ</summary>
public enum ProductTagSource
{
    /// <summary>Claude の読み取り（職員が選んだもの）</summary>
    Claude,

    /// <summary>職員が入力</summary>
    Staff,
}

/// <summary>献立の状態</summary>
public enum MenuStatus
{
    /// <summary>下書き</summary>
    Draft,

    /// <summary>確定</summary>
    Confirmed,
}

/// <summary>日の種類（画面では「種別」）。手作りの日は扱わない（Q-11）</summary>
public enum DayType
{
    /// <summary>通常</summary>
    Normal,

    /// <summary>おやつなし（祝日・休み・店で買うおやつの日も含む）</summary>
    NoSnack,
}

/// <summary>その日の人数の出どころ</summary>
public enum ChildrenCountSource
{
    /// <summary>通常の人数</summary>
    Default,

    /// <summary>要望から読み取り</summary>
    FromRequest,

    /// <summary>職員が修正</summary>
    EditedByStaff,
}

/// <summary>チャットの発言者</summary>
public enum MenuMessageRole
{
    /// <summary>職員</summary>
    Staff,

    /// <summary>Claude</summary>
    Claude,
}

/// <summary>安全に関わる設定の変更の種類</summary>
public enum SafetySettingLogKind
{
    /// <summary>避ける品目の追加</summary>
    AvoidedAllergenAdded,

    /// <summary>避ける品目の削除</summary>
    AvoidedAllergenRemoved,

    /// <summary>探す言葉の追加</summary>
    KeywordAdded,

    /// <summary>探す言葉の削除</summary>
    KeywordRemoved,

    /// <summary>再確認期間の変更</summary>
    RecheckDaysChanged,
}

/// <summary>Claude API の用途（api_usage_logs・モデルの設定に使う）</summary>
public enum ClaudePurpose
{
    /// <summary>チラシの読み取り</summary>
    FlyerReading,

    /// <summary>チラシの読み取りの検証</summary>
    FlyerReview,

    /// <summary>商品照合の判定</summary>
    ProductMatch,

    /// <summary>アレルゲン抽出</summary>
    AllergenExtraction,

    /// <summary>献立の提案（要望の読み取りを含む）</summary>
    MenuPlanning,

    /// <summary>チャットでの組み直し</summary>
    MenuChat,

    /// <summary>予算に合わせる案の選択</summary>
    BudgetFit,

    /// <summary>届いた後の代わりの提案</summary>
    Replacement,
}
