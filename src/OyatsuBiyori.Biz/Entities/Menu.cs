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

    /// <summary>同時に保存したときの衝突を見つける値（SQLite のトリガーで更新する。D-46）</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>日ごとの献立</summary>
    public List<MenuDay> Days { get; set; } = [];

    /// <summary>チャットの履歴</summary>
    public List<MenuMessage> Messages { get; set; } = [];
}

/// <summary>掲載品の写し</summary>
/// <param name="ProductId">商品</param>
/// <param name="Price">税込価格（読めなかったら NULL）</param>
/// <param name="OrderNo">注文番号</param>
public sealed record ListedProduct(string ProductId, int? Price, string? OrderNo);

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
