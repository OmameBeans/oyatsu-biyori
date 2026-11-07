using System.Globalization;
using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>献立の問題の重さ</summary>
public enum MenuIssueSeverity
{
    /// <summary>エラー（Claude の案なら差し戻す）</summary>
    Error,

    /// <summary>警告（表示だけ。差し戻さない）</summary>
    Warning,
}

/// <summary>献立の問題の種類（FR-MENU-15、02 §5.5 の表）</summary>
public enum MenuIssueKind
{
    /// <summary>判定が「入れられない」（含む・含む可能性あり・不明（避ける品目があるとき））</summary>
    AllergenBlocked,

    /// <summary>存在しない商品</summary>
    ProductNotFound,

    /// <summary>その週の掲載品にない商品</summary>
    NotListed,

    /// <summary>写しの税込価格か、商品の入数がない</summary>
    MissingPriceOrPieces,

    /// <summary>「今週は使わない」にした商品</summary>
    ExcludedThisWeek,

    /// <summary>「使わない」商品</summary>
    ProductDisabled,

    /// <summary>おやつの分類が「対象外」</summary>
    NotSnack,

    /// <summary>献立にない日付（Claude の案）</summary>
    UnknownDate,

    /// <summary>判定が「警告」（参考・要再確認・未確認の品目あり・不明（避ける品目がないとき））</summary>
    AllergenWarning,

    /// <summary>重複ルールの期間内・同じ週の中の重複</summary>
    Duplicate,

    /// <summary>週の予算の超過</summary>
    OverBudget,

    /// <summary>通常の日に商品がない</summary>
    EmptyNormalDay,

    /// <summary>おやつなしの日に商品がある（金額に含まれていない）</summary>
    ItemsOnNonNormalDay,

    /// <summary>パック数が全員分に足りない</summary>
    PackShort,

    /// <summary>エージェントの「何人分買うか」の指定が使えない（その日にない商品・範囲外の人数）</summary>
    InvalidServing,
}

/// <summary>
/// 献立の問題。
/// </summary>
/// <param name="Severity">重さ（検証での扱い）</param>
/// <param name="Kind">種類</param>
/// <param name="Date">日（週全体の問題なら null）</param>
/// <param name="MenuItemId">献立の商品（日・週の問題なら null）</param>
/// <param name="ProductId">商品</param>
/// <param name="ProductName">商品名（存在しない商品なら null）</param>
/// <param name="Message">理由と次にすること（日本語。画面と Claude に返す文面に使う）</param>
public sealed record MenuIssue(
    MenuIssueSeverity Severity,
    MenuIssueKind Kind,
    DateOnly? Date,
    string? MenuItemId,
    string? ProductId,
    string? ProductName,
    string Message)
{
    /// <summary>確定を止めるか（D-78：入れられない・価格か入数がない・掲載品にない。存在しない商品も止める）</summary>
    public bool BlocksConfirmation => Kind is MenuIssueKind.AllergenBlocked
        or MenuIssueKind.ProductNotFound
        or MenuIssueKind.NotListed
        or MenuIssueKind.MissingPriceOrPieces
        or MenuIssueKind.UnknownDate;

    /// <summary>「11/16（月）『さくさくクッキー』：…」の形の1行</summary>
    public string ToLine()
    {
        var prefix = Date is { } date ? MenuIssueText.DateLabel(date) : "週全体";
        var product = ProductName is not null
            ? $"「{ProductName}」"
            : ProductId is not null ? $"商品ID {ProductId}" : null;
        return product is null ? $"{prefix}：{Message}" : $"{prefix} {product}：{Message}";
    }
}

/// <summary>献立の問題の文面</summary>
public static class MenuIssueText
{
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>「11/16（月）」の形</summary>
    public static string DateLabel(DateOnly date) => $"{date.Month}/{date.Day}（{Japanese.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek)}）";

    /// <summary>
    /// 判定が「入れられない」理由（FR-MENU-14：「避ける品目〇〇を含む」「〇〇の探す言葉が当たった」「アレルゲンの情報がない」）。
    /// Claude に返すときも品目名だけを伝え、判定を Claude に委ねない（SR-18）。
    /// </summary>
    public static string BlockedReason(AllergenJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(judgement);

        return judgement.Kind switch
        {
            AllergenJudgementKind.Contains =>
                $"避ける品目（{string.Join("・", judgement.ContainedAllergens.Select(a => a.Name))}）を含むため、献立に入れられません",
            AllergenJudgementKind.MayContain =>
                $"避ける品目（{string.Join("・", judgement.KeywordHits.Select(h => h.Match.AllergenName).Distinct())}）の探す言葉が当たったため、献立に入れられません",
            AllergenJudgementKind.Unknown =>
                "アレルゲンの情報がないため、献立に入れられません。パッケージの原材料表示を撮影して確認してください",
            _ => "アレルゲンの判定により、献立に入れられません",
        };
    }

    /// <summary>判定が「警告」の理由と次にすること（03 §6）</summary>
    public static string WarningReason(AllergenJudgement judgement)
    {
        ArgumentNullException.ThrowIfNull(judgement);

        return judgement.Kind switch
        {
            AllergenJudgementKind.Reference =>
                "アレルゲンの情報が「参考」です。届いたらパッケージの原材料表示を撮影して確認してください",
            AllergenJudgementKind.RecheckNeeded =>
                "前回の確認から再確認期間が過ぎています。パッケージの原材料表示を撮影して確認し直してください",
            AllergenJudgementKind.UnconfirmedItems =>
                $"避ける品目のうち、確認していない品目があります（{string.Join("・", judgement.UnconfirmedAllergens.Select(a => a.Name))}）。パッケージで確認してください",
            AllergenJudgementKind.Unknown =>
                "アレルゲンの情報がありません。パッケージの原材料表示を撮影して確認してください",
            _ => "アレルゲンの確認が必要です",
        };
    }
}
