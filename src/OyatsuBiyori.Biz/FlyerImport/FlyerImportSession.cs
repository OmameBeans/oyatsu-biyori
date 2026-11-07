using OyatsuBiyori.Biz.Interfaces;

namespace OyatsuBiyori.Biz.FlyerImport;

/// <summary>
/// チラシの取り込みの途中の状態（確定前の読み取り結果）。サーバーのメモリに献立と結びつけて24時間だけ持つ（FR-FLYER-10・D-36）。
/// 説明文はここにだけ持ち、DB には保存しない（探す言葉を探すためだけに使う。FR-FLYER-03）。
/// 分類（FlyerItemClassifier）は持たず、表示と確定のたびに、今の商品マスタ・避ける品目で求め直す（09 の 3.1・5.2）。
/// </summary>
public sealed class FlyerImportSession
{
    /// <summary>同時に書き換えないための鍵</summary>
    public object SyncRoot { get; } = new();

    /// <summary>
    /// 確定と確定の取り消し（DB に保存する操作）を1つずつ行うための鍵。SyncRoot は await の間は外れるので、
    /// 2つの端末から同じ行を同時に確定して、商品や記録が二重にできないようにする
    /// </summary>
    public SemaphoreSlim WriteGate { get; } = new(1, 1);

    /// <summary>取り込みの ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>献立</summary>
    public string MenuId { get; init; } = string.Empty;

    /// <summary>作った日時（UTC）</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>取り込みを始めたときに、献立に掲載品の写しがあったか（取り込み直し。最初の確定で写しを空にする。T1-18）</summary>
    public bool IsReimport { get; init; }

    /// <summary>この取り込みで、もう1件以上確定したか（取り込み直しで写しを空にしたか）</summary>
    public bool HasConfirmedAny { get; set; }

    /// <summary>読み取りの検証（Claude）ができなかったか（FR-FLYER 受け入れ条件。できなければ、この取り込みの商品はまとめに入れない。09 の 5.2）</summary>
    public bool ReviewFailed { get; set; }

    /// <summary>読み取りの検証が指摘した、読み落とした商品（手入力の候補）</summary>
    public List<FlyerMissedProduct> MissedProducts { get; } = [];

    /// <summary>商品（外した掲載品も含む。IsRemoved で分ける）</summary>
    public List<FlyerImportItem> Items { get; } = [];
}

/// <summary>
/// 取り込みの1商品（二重に読んだ商品は1件にまとめたもの。FR-IMPORT-06）。
/// Claude が読んだまま（OriginalRead）と、職員が直した今の値（Read）を分けて持つ（直した値で読み取りの当たりを消さないため。SR-14）。
/// </summary>
public sealed class FlyerImportItem
{
    /// <summary>ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>手入力した商品か（チラシの読み取りと同じ扱い。まとめには入れない。D-73）</summary>
    public bool IsManual { get; init; }

    /// <summary>Claude が読み取ったまま（二重の読み取りをまとめたあと）。説明文と商品名の探す言葉はここからも探す</summary>
    public FlyerReadProduct OriginalRead { get; init; } = null!;

    /// <summary>今の値（職員が直した値・入れた入数を反映したもの。直していなければ OriginalRead と同じ）</summary>
    public FlyerReadProduct Read { get; set; } = null!;

    /// <summary>職員が直した項目（FlyerItemField の値。値が変わった項目だけ）</summary>
    public HashSet<string> EditedFields { get; } = new(StringComparer.Ordinal);

    /// <summary>「使えない（入数が分からない）」の行に、職員が入数を入れたか（読み取りの検証を通っていないので、まとめに入れない。FR-IMPORT-11）</summary>
    public bool IsPiecesEnteredByStaff { get; set; }

    /// <summary>まとめた元の読み取りの番号（0から。Claude が返した一覧の番号。手入力は空）</summary>
    public IReadOnlyList<int> SourceIndexes { get; init; } = [];

    /// <summary>二重に読んで食い違った値（FR-IMPORT-06）</summary>
    public IReadOnlyList<FlyerDuplicateConflict> DuplicateConflicts { get; init; } = [];

    /// <summary>読み取ったときの表記の品目（Claude・アプリの対応づけ・略記の補正・読み落とし表記の追加。職員が外した品目を記録に残すため）</summary>
    public List<string> OriginalLabelAllergenIds { get; init; } = [];

    /// <summary>今の表記の品目（品目 ID。職員が直したらその値）</summary>
    public List<string> LabelAllergenIds { get; set; } = [];

    /// <summary>対応づけできなかった表記の語（職員が品目を選んでも消さずに残す）</summary>
    public List<string> UnmappedTerms { get; init; } = [];

    /// <summary>品目の分からない語に職員が選んだ品目（語 → 品目 ID。空なら「対応する品目なし」）</summary>
    public Dictionary<string, List<string>> ResolvedTerms { get; } = new(StringComparer.Ordinal);

    /// <summary>略記で当てた語と品目（画面で「麦→小麦」と示す）</summary>
    public List<FlyerAbbreviationHit> AbbreviationHits { get; init; } = [];

    /// <summary>読み取りの検証（FR-FLYER-03g）の指摘のうち、まだ直していないもの（Key は商品の中で一意。review:0 など）</summary>
    public List<FlyerReviewNoteInput> ReviewNotes { get; } = [];

    /// <summary>再入力の理由のうち、職員が「このままでよい」を押したものの鍵（メモリにだけ持ち、記録には使わない。09 の 5.3）</summary>
    public HashSet<string> AcceptedReasonKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 「表記なし」と読んだ商品を、職員がチラシで見比べたか（まとめての見比べ。D-97）。このメモリにだけ持ち、
    /// 記録（allergen_checks）や確定の内容には使わない。まとめて確定に入れてよいかの判断にだけ使う。表記を直したら消す
    /// </summary>
    public bool IsLabelAbsentCompared { get; set; }

    /// <summary>外した掲載品か（FR-IMPORT-20・21。分類せず、今週の商品にも商品マスタにも入れない）</summary>
    public bool IsRemoved { get; set; }

    /// <summary>外した理由（Claude が食品以外とした理由か、職員が外したこと）</summary>
    public string? RemovedReason { get; set; }

    /// <summary>確定した内容（未確定なら null）</summary>
    public FlyerItemConfirmation? Confirmation { get; set; }

    /// <summary>
    /// この取り込みで新しく登録した商品（確定を取り消したら「使わない」にし、選び直したらその商品を直して使う。
    /// 選び直すたびに商品が増えないように）
    /// </summary>
    public string? CreatedProductId { get; set; }

    /// <summary>
    /// 確認済みとの食い違い（09 の 3.5）で「参考」の記録を足した商品と品目の鍵（商品 ID｜品目 ID の並び）。
    /// 同じ食い違いで2回足さないため。直して食い違いが変われば、次の確定でまた足す
    /// </summary>
    public HashSet<string> RecordedDiscrepancyKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>食い違いで「参考」の記録を足した品目（画面で「記録を足しました」と知らせる）</summary>
    public List<AllergenRef> RecordedDiscrepancyAllergens { get; } = [];
}

/// <summary>確定の種類</summary>
public enum FlyerConfirmationKind
{
    /// <summary>使える：一致した商品を更新した</summary>
    UpdatedExisting,

    /// <summary>要確認：新しい商品として登録した</summary>
    CreatedNew,
}

/// <summary>確定した内容</summary>
/// <param name="Kind">確定の種類</param>
/// <param name="ProductId">使った・作った商品</param>
/// <param name="ProductName">商品名</param>
/// <param name="AddedReference">この商品の確定で「参考」の記録を足したか（食い違いの記録は RecordedDiscrepancyAllergens）</param>
public sealed record FlyerItemConfirmation(FlyerConfirmationKind Kind, string ProductId, string ProductName, bool AddedReference);
