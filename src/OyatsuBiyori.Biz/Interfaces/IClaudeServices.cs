using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz.Interfaces;

/// <summary>Claude に送る画像（サーバーに保存しない。処理が終わったら破棄する。NFR-SEC-06）</summary>
/// <param name="Data">画像（JPEG など）</param>
/// <param name="MediaType">メディアタイプ（image/jpeg など）</param>
public sealed record ClaudeImage(byte[] Data, string MediaType);

/// <summary>
/// チラシの読み取り（Sonnet・画像。T3-04）。結果は誤りがある前提で扱い、アプリが検証してから使う。
/// </summary>
public interface IFlyerReader
{
    /// <summary>チラシを読み取る</summary>
    /// <param name="images">チラシの画像（10枚まで）</param>
    /// <param name="allergens">マスタの品目（アレルゲン表記を品目に対応づけさせるため）</param>
    /// <param name="cancellationToken">処理全体の期限</param>
    Task<Result<FlyerReading>> ReadAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, CancellationToken cancellationToken);
}

/// <summary>チラシの読み取り結果</summary>
public sealed record FlyerReading(IReadOnlyList<FlyerReadProduct> Items);

/// <summary>
/// チラシから読み取った1商品。
/// </summary>
/// <param name="Name">商品名</param>
/// <param name="Maker">メーカー</param>
/// <param name="Volume">内容量</param>
/// <param name="PriceExcludingTax">税抜価格（まとめ買いは1箱の単位）</param>
/// <param name="PriceIncludingTax">税込価格（括弧内。まとめ買いは1箱の単位）</param>
/// <param name="OrderNo">注文番号</param>
/// <param name="TargetAgeLabel">対象年齢</param>
/// <param name="AllergenLabelText">アレルゲン表記の原文（なければ null）</param>
/// <param name="AllergenLabelAbsent">アレルゲン表記がないと読んだか（「表記なし」）</param>
/// <param name="AllergenCodes">表記をマスタの品目に対応づけたもの（品目コード）</param>
/// <param name="UnmappedAllergenTerms">品目に対応づけられなかった表記の語</param>
/// <param name="Description">説明文（保存しない。探す言葉を探すためだけに使う）</param>
/// <param name="BulkNote">まとめ買いの表記（「箱でお届け」など）</param>
/// <param name="PiecesPerPackCandidate">入数の候補</param>
/// <param name="PiecesPerPackReason">入数の候補の理由</param>
/// <param name="SnackType">おやつの分類の候補</param>
/// <param name="CharacterTags">キャラクターのタグ</param>
/// <param name="FlavorTags">味・種類のタグ（決まった語）</param>
/// <param name="IsNonFood">
/// 食品以外の掲載品（洗剤・日用品・おもちゃなど）か（FR-IMPORT-20）。アプリはこの商品を分類せず「外した掲載品」に入れる（職員は「戻す」で戻せる）。
/// 大袋のお菓子・乳幼児向けの商品・飲み物は食品なので false
/// </param>
/// <param name="NonFoodReason">食品以外とした理由（IsNonFood のときだけ。画面の「外した掲載品」に出す）</param>
public sealed record FlyerReadProduct(
    string Name,
    string? Maker,
    string? Volume,
    int? PriceExcludingTax,
    int? PriceIncludingTax,
    string? OrderNo,
    string? TargetAgeLabel,
    string? AllergenLabelText,
    bool AllergenLabelAbsent,
    IReadOnlyList<string> AllergenCodes,
    IReadOnlyList<string> UnmappedAllergenTerms,
    string? Description,
    string? BulkNote,
    int? PiecesPerPackCandidate,
    string? PiecesPerPackReason,
    SnackType? SnackType,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags,
    bool IsNonFood = false,
    string? NonFoodReason = null);

/// <summary>
/// 読み取りの検証（別の呼び出し。T3-05）。疑わしい箇所・読み落とした商品・読み落としたアレルゲン表記だけを返させる（書き換えさせない）。
/// </summary>
public interface IFlyerReviewer
{
    /// <summary>読み取り結果を検証する</summary>
    Task<Result<FlyerReview>> ReviewAsync(IReadOnlyList<ClaudeImage> images, FlyerReading reading, CancellationToken cancellationToken);
}

/// <summary>読み取りの検証の結果</summary>
public sealed record FlyerReview(
    IReadOnlyList<FlyerReviewNote> Notes,
    IReadOnlyList<FlyerMissedProduct> MissedProducts,
    IReadOnlyList<FlyerMissedAllergenLabel> MissedAllergenLabels);

/// <summary>疑わしい箇所（Index は読み取り結果の番号。0から）</summary>
public sealed record FlyerReviewNote(int Index, string Field, string Reason);

/// <summary>読み落とした商品</summary>
public sealed record FlyerMissedProduct(string Name, string Reason);

/// <summary>読み落としたアレルゲン表記（確認を増やす方向の指摘だけ採用する。SR-19）</summary>
public sealed record FlyerMissedAllergenLabel(int Index, string LabelText, string Reason);

/// <summary>
/// パッケージの原材料表示からの読み取り（Sonnet・4枚まで。T5-01）。結果は候補で、「確認済み」にするのは職員だけ（SR-03）。
/// </summary>
public interface IAllergenExtractor
{
    /// <summary>パッケージの画像から原材料の全文と、含む品目の候補を読み取る</summary>
    /// <param name="images">パッケージの画像（4枚まで）</param>
    /// <param name="allergens">候補にしてよい品目（29品目と、避ける品目にある施設の追加品目）</param>
    /// <param name="productName">商品名（読み取りの手がかり）</param>
    /// <param name="cancellationToken">処理全体の期限</param>
    Task<Result<AllergenExtraction>> ExtractAsync(IReadOnlyList<ClaudeImage> images, IReadOnlyList<Allergen> allergens, string productName, CancellationToken cancellationToken);
}

/// <summary>
/// 原材料表示の読み取り結果（候補）。
/// </summary>
/// <param name="RawIngredients">原材料の全文（読めなければ空）</param>
/// <param name="ContainsAllergenCodes">「含む」の候補の品目コード（書かれていない品目は入れない）</param>
/// <param name="ContaminationNote">同一工場・コンタミの注意書き（なければ null）</param>
/// <param name="CharacterTags">キャラクターのタグの候補</param>
/// <param name="FlavorTags">味・種類のタグの候補（決まった語）</param>
public sealed record AllergenExtraction(
    string RawIngredients,
    IReadOnlyList<string> ContainsAllergenCodes,
    string? ContaminationNote,
    IReadOnlyList<string> CharacterTags,
    IReadOnlyList<string> FlavorTags);

/// <summary>
/// 献立づくりのエージェント（Sonnet。T4-04・T4-08、D-82）。道具（IMenuAgentTools）を使わせ、submit_menu が Biz の検証を通したら止める。
/// チャットでは、質問なら submit_menu を使わずに文章で答えさせ、直す依頼なら submit_menu のあとに何を変えたかを文章で書かせる（D-91）。
/// </summary>
public interface IMenuPlannerAgent
{
    /// <summary>献立案を作る（チャットでの組み直し・質問への返答も同じ）</summary>
    /// <param name="request">渡す条件</param>
    /// <param name="tools">道具（判断のロジックは Biz）</param>
    /// <param name="progress">段階名と、チャットの返答の文章（届いた順）を報告する</param>
    /// <param name="cancellationToken">処理全体の期限</param>
    Task<Result<MenuAgentRunResult>> RunAsync(MenuAgentRequest request, IMenuAgentTools tools, IJobProgress progress, CancellationToken cancellationToken);
}

/// <summary>
/// 使い終わった献立づくりのセッションを、Anthropic 側から消す（Managed Agents。D-94）。
/// 職員のチャットの文章・週の要望が、使わなくなったセッションに残り続けないようにする。失敗しても例外にせず、ログだけ残す
/// </summary>
public interface IMenuAgentSessionCleaner
{
    /// <summary>セッションを消す</summary>
    Task ForgetAsync(string sessionId);
}

/// <summary>何もしない後片付け（Managed Agents を使わないとき・テスト）</summary>
public sealed class NoMenuAgentSessionCleaner : IMenuAgentSessionCleaner
{
    /// <inheritdoc />
    public Task ForgetAsync(string sessionId) => Task.CompletedTask;
}

/// <summary>エージェントの動かし方</summary>
public enum MenuAgentMode
{
    /// <summary>献立案の作成（要望の読み取りを含む）</summary>
    Create,

    /// <summary>チャットでの組み直し</summary>
    Chat,
}

/// <summary>エージェントに渡す条件</summary>
/// <param name="Mode">動かし方</param>
/// <param name="ConditionsText">固定条件・日ごとの値・直近の確定献立・候補などをまとめた文章（Biz が作る）</param>
/// <param name="RequestText">週の要望（作成）か、チャットの依頼</param>
/// <param name="History">チャットの履歴（Claude 側の説明文と職員の依頼）</param>
/// <param name="WeekRequestText">チャットのとき、作成時の週の要望と読み取った方針（守ったまま組み直させる。FR-MENU-22）。作成のときは null</param>
/// <param name="SessionId">続けて使うエージェントのセッション（Managed Agents 版のチャットだけ。D-94）。なければ新しく始める</param>
public sealed record MenuAgentRequest(MenuAgentMode Mode, string ConditionsText, string? RequestText, IReadOnlyList<MenuMessage> History, string? WeekRequestText = null, string? SessionId = null);

/// <summary>エージェントの結果（受け付けた案は tools 側が持つ）</summary>
/// <param name="Accepted">submit_menu で案を受け付けたか</param>
/// <param name="Iterations">ループの回数</param>
/// <param name="ReplyText">チャットの返答の文章（Claude が書いた Markdown。質問への返答か、何を変えたかの説明。なければ空）</param>
/// <param name="SessionId">次のチャットで続けて使えるセッション（Managed Agents 版だけ。D-94）。続けられないときは null</param>
public sealed record MenuAgentRunResult(bool Accepted, int Iterations, string ReplyText = "", string? SessionId = null);

/// <summary>
/// 献立づくりの道具（判断のロジックは Biz。NFR-MNT-05）。入出力は文字列（JSON）で、Claude の道具の定義は Claude プロジェクトに置く。
/// </summary>
public interface IMenuAgentTools
{
    /// <summary>候補を探す（候補の絞り込み T1-11 を通ったものだけ返す）</summary>
    string SearchProducts(string? keyword, SnackType? snackType);

    /// <summary>案の金額と週の予算を確かめる</summary>
    string CheckBudget(MenuAgentProposal proposal);

    /// <summary>案の重複を確かめる</summary>
    string CheckDuplicates(MenuAgentProposal proposal);

    /// <summary>案を検証する（エラーと警告）</summary>
    string ValidateMenu(MenuAgentProposal proposal);

    /// <summary>案を出す。エラーがあれば受け付けず、問題を返す。受け付けたら IsAccepted が true になる</summary>
    string SubmitMenu(MenuAgentSubmission submission);

    /// <summary>案を受け付けたか（受け付けたらループを止める）</summary>
    bool IsAccepted { get; }
}

/// <summary>エージェントの献立案（日ごとの商品 ID と理由だけ。個数・金額は Biz が計算する）</summary>
/// <param name="Days">日ごとの商品</param>
/// <param name="DayValues">試す日ごとの値（人数・日の種類・見出し。確かめるときだけ使い、出すときは submit_menu の読み取り結果を使う）</param>
public sealed record MenuAgentProposal(IReadOnlyList<MenuAgentProposalDay> Days, IReadOnlyList<InterpretedDay> DayValues);

/// <summary>日ごとの案</summary>
/// <param name="Date">日付</param>
/// <param name="ProductIds">商品 ID</param>
/// <param name="Reason">理由</param>
/// <param name="Servings">商品ごとに何人分買うか（在庫で補うときなど、その日の人数と違う数にする商品だけ。パック数はアプリが計算する）</param>
public sealed record MenuAgentProposalDay(DateOnly Date, IReadOnlyList<string> ProductIds, string? Reason, IReadOnlyList<MenuAgentServing>? Servings = null);

/// <summary>商品を何人分買うか（エージェントの指定。パック数・金額はアプリが計算する）</summary>
/// <param name="ProductId">商品 ID（その日の ProductIds に入っているもの）</param>
/// <param name="ChildrenCount">何人分（1以上）</param>
public sealed record MenuAgentServing(string ProductId, int ChildrenCount);

/// <summary>
/// submit_menu で出す内容。
/// </summary>
/// <param name="Proposal">献立案</param>
/// <param name="Interpreted">要望の読み取り結果（作成）か、チャットで頼まれた日ごとの値</param>
/// <param name="ExplicitDates">チャットで、職員が直した値も変えてよい日（依頼がはっきり変えるもの）</param>
/// <param name="Explanation">説明（作成：全体の考え方。チャット：何を変えたか・満たせない条件）</param>
/// <param name="IsBudgetFitRequest">チャットの依頼が「予算に合わせて」か</param>
public sealed record MenuAgentSubmission(
    MenuAgentProposal Proposal,
    InterpretedRequests Interpreted,
    IReadOnlyList<DateOnly> ExplicitDates,
    string Explanation,
    bool IsBudgetFitRequest);

/// <summary>
/// 予算に合わせる案の説明（Sonnet。T4-09）。アプリが計算した候補の番号だけを選ばせ、説明させる。
/// </summary>
public interface IBudgetFitExplainer
{
    /// <summary>候補から選んで説明する</summary>
    /// <param name="optionsText">番号つきの候補（Biz が作る）</param>
    /// <param name="requestText">週の要望</param>
    /// <param name="cancellationToken">処理全体の期限</param>
    Task<Result<IReadOnlyList<BudgetFitChoice>>> ChooseAsync(string optionsText, string? requestText, CancellationToken cancellationToken);
}

/// <summary>選んだ候補の番号と説明</summary>
public sealed record BudgetFitChoice(int Index, string Explanation);
