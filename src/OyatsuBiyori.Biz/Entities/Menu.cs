using OyatsuBiyori.Biz.Abstractions;

namespace OyatsuBiyori.Biz.Entities;

/// <summary>
/// 週の献立（1つの週に1つだけ）。
/// </summary>
public sealed class Menu : EntityBase
{
    /// <summary>対象週の初日（月曜・日本時間の日付。UNIQUE）</summary>
    public DateOnly WeekStart { get; set; }

    /// <summary>週のメモ</summary>
    public string? Note { get; set; }

    /// <summary>その週の予算（円・税込）。作るときに既定の週の予算を写し、職員が上書きできる</summary>
    public int WeeklyBudget { get; set; }

    /// <summary>その週の掲載品の写し（献立の候補・価格はこれで決める。FR-FLYER-07）</summary>
    public List<ListedProduct> ListedProducts { get; set; } = [];

    /// <summary>「今週は使わない」と指定した商品の ID</summary>
    public List<string> ExcludedProductIds { get; set; } = [];

    /// <summary>職員が入力した週の要望（原文）</summary>
    public string? RequestText { get; set; }

    /// <summary>Claude が読み取った要望（「こう理解しました」の内容。D-45）</summary>
    public InterpretedRequests? InterpretedRequests { get; set; }

    /// <summary>状態</summary>
    public MenuStatus Status { get; set; }

    /// <summary>確定日時（UTC）</summary>
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>確定した職員</summary>
    public string? ConfirmedBy { get; set; }

    /// <summary>
    /// チャットで続けて使う献立づくりのエージェントのセッション（Managed Agents。D-94）。
    /// 献立案を作り直したとき・チャットの履歴を消したときは新しいセッションにする。中身（会話）は Anthropic 側にあり、ここには ID だけを持つ
    /// </summary>
    public string? AgentSessionId { get; set; }

    /// <summary>
    /// セッションを始めたときの条件の指紋（避ける品目と、候補とその要確認の印。D-94）。
    /// 避ける品目・確認の記録・掲載品が変わったら、前のセッション（古い候補やツールの結果を覚えている）は続けない
    /// </summary>
    public string? AgentSessionFingerprint { get; set; }

    /// <summary>同時に保存したときの衝突を見つける値（SQLite のトリガーで更新する。D-46）</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>日ごとの献立</summary>
    public List<MenuDay> Days { get; set; } = [];

    /// <summary>チャットの履歴</summary>
    public List<MenuMessage> Messages { get; set; } = [];

    /// <summary>注文リストで「注文した」とチェックした行（FR-MENU-35。商品・注文番号ごとに1件）</summary>
    public List<OrderCheck> OrderChecks { get; set; } = [];
}

/// <summary>注文リストの「注文した」のチェック</summary>
/// <param name="ProductId">商品</param>
/// <param name="OrderNo">注文番号（なければ null）</param>
/// <param name="PackCount">チェックしたときのパック数（あとで献立のパック数が変わったら知らせる）</param>
/// <param name="CheckedAt">チェックした日時（UTC）</param>
/// <param name="CheckedBy">チェックした職員</param>
public sealed record OrderCheck(string ProductId, string? OrderNo, int PackCount, DateTime CheckedAt, string? CheckedBy);

/// <summary>掲載品の写し</summary>
/// <param name="ProductId">商品</param>
/// <param name="Price">税込価格（読めなかったら NULL）</param>
/// <param name="OrderNo">注文番号</param>
/// <param name="LabelText">
/// 今週のチラシのアレルゲン表記の原文（表記なしなら「表記なし」。D-101 より前の写しは null）。
/// 献立の確定で見比べるための表示だけに使い、判定・確定の条件・指紋には使わない（SR-18・FR-IMPORT-42）
/// </param>
public sealed record ListedProduct(string ProductId, int? Price, string? OrderNo, string? LabelText = null);

/// <summary>Claude が読み取った要望（D-45）</summary>
public sealed class InterpretedRequests
{
    /// <summary>日ごとの値（構造化できるもの）</summary>
    public List<InterpretedDay> Days { get; set; } = [];

    /// <summary>方針として扱うもの</summary>
    public List<InterpretedPolicy> Policies { get; set; } = [];

    /// <summary>反映しなかった要望（アレルギーの条件を緩める・出欠など）</summary>
    public List<RejectedRequest> Rejected { get; set; } = [];
}

/// <summary>日ごとに読み取った値</summary>
public sealed record InterpretedDay(DateOnly Date, int? ChildrenCount, DayType? DayType, string? Label);

/// <summary>方針として扱う要望</summary>
public sealed record InterpretedPolicy(string Text, List<DateOnly>? Days);

/// <summary>反映しなかった要望と理由</summary>
public sealed record RejectedRequest(string Text, string Reason);
