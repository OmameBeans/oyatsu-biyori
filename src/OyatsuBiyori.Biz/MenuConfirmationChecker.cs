using OyatsuBiyori.Biz.Entities;

namespace OyatsuBiyori.Biz;

/// <summary>
/// 献立を確定できるかの判定（FR-MENU-32、SR-04・SR-21・SR-22、D-78、T1-16）。
/// - 確定を止める：入れられない商品・価格か入数がない商品・掲載品にない商品（存在しない商品も）。職員の確認でも確定できない
/// - 確認が要る：判定が「警告」の商品。確認したときの判定の種類と中身（指紋）が今と同じときだけ確認済みとみなす
/// - 表示だけ：候補から外した商品・重複・予算超過・0品の日・手作り / おやつなしの日の商品・足りないパック数
/// 判定はこの関数を呼ぶたびに求め直す（確定ボタンを押した時点でも呼び直すこと）。
/// </summary>
public static class MenuConfirmationChecker
{
    /// <summary>確定できるかを調べる</summary>
    public static MenuConfirmationResult Check(Menu menu, MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(context);

        var validation = MenuValidator.Validate(menu, context);
        var blocking = validation.Issues.Where(i => i.BlocksConfirmation).ToList();
        var display = validation.Issues
            .Where(i => !i.BlocksConfirmation && i.Kind != MenuIssueKind.AllergenWarning)
            .Select(i => i with { Severity = MenuIssueSeverity.Warning })
            .ToList();

        var acknowledgements = new List<AcknowledgementStatus>();
        foreach (var day in menu.Days.OrderBy(d => d.Date))
        {
            foreach (var item in day.Items)
            {
                var product = context.FindProduct(item.ProductId);
                if (product is null)
                {
                    continue;
                }

                var judgement = context.Judge(product);
                if (judgement.Category != AllergenJudgementCategory.Warning)
                {
                    continue;
                }

                var fingerprint = context.Fingerprint(product);
                acknowledgements.Add(new AcknowledgementStatus(day.Date, item, product, judgement, fingerprint, IsAcknowledged(item, judgement, fingerprint)));
            }
        }

        return new MenuConfirmationResult(blocking, acknowledgements, display, validation.Week);
    }

    /// <summary>
    /// 確認したときの判定と今の判定が同じか：確認日時があり、種類と指紋の両方が一致するときだけ true（SR-22）。
    /// </summary>
    public static bool IsAcknowledged(MenuItem item, AllergenJudgement judgement, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(judgement);

        return item.AcknowledgedAt is not null
            && item.AcknowledgedJudgement == judgement.Kind
            && string.Equals(item.AcknowledgedJudgementFingerprint, fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// 職員が警告の商品を確認したことを記録する。判定は画面から受け取らず、ここで今の判定を求めて記録する
    /// （画面を開いてから確認するまでに判定が変わっても、古い判定を確認済みにしない）。
    /// 判定が「入れられない」の商品は確認できない（SR-21）。「安全」の商品は確認が要らない。
    /// </summary>
    public static Result Acknowledge(MenuItem item, MenuContext context, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("日時は UTC で渡してください", nameof(nowUtc));
        }

        var product = context.FindProduct(item.ProductId);
        if (product is null)
        {
            return Result.Error("商品が見つかりません。献立を読み込み直してください");
        }

        var judgement = context.Judge(product);
        switch (judgement.Category)
        {
            case AllergenJudgementCategory.Blocked:
                return Result.Error($"「{product.Name}」は{MenuIssueText.BlockedReason(judgement)}。確認しても確定できません。献立から外してください");
            case AllergenJudgementCategory.Safe:
                return Result.Error($"「{product.Name}」は確認の要らない商品です。画面を読み込み直してください");
        }

        item.AcknowledgedAt = nowUtc;
        item.AcknowledgedJudgement = judgement.Kind;
        item.AcknowledgedJudgementFingerprint = context.Fingerprint(product);
        return new Result();
    }
}

/// <summary>警告の商品の確認の状態</summary>
/// <param name="Date">日</param>
/// <param name="Item">献立の商品</param>
/// <param name="Product">商品</param>
/// <param name="Judgement">今の判定</param>
/// <param name="Fingerprint">今の判定の指紋</param>
/// <param name="IsAcknowledged">今の判定のまま確認済みか</param>
public sealed record AcknowledgementStatus(DateOnly Date, MenuItem Item, Product Product, AllergenJudgement Judgement, string Fingerprint, bool IsAcknowledged)
{
    /// <summary>理由と次にすること</summary>
    public string Message => MenuIssueText.WarningReason(Judgement);
}

/// <summary>
/// 確定できるかの結果。
/// </summary>
public sealed class MenuConfirmationResult(
    IReadOnlyList<MenuIssue> blockingIssues,
    IReadOnlyList<AcknowledgementStatus> acknowledgements,
    IReadOnlyList<MenuIssue> displayWarnings,
    WeekAmount week)
{
    /// <summary>確定を止めるエラー（職員の確認でも確定できない）</summary>
    public IReadOnlyList<MenuIssue> BlockingIssues { get; } = blockingIssues;

    /// <summary>確認が要る警告の商品（1件ずつチェック）</summary>
    public IReadOnlyList<AcknowledgementStatus> Acknowledgements { get; } = acknowledgements;

    /// <summary>表示だけの警告（チェックは要らない）</summary>
    public IReadOnlyList<MenuIssue> DisplayWarnings { get; } = displayWarnings;

    /// <summary>週の金額</summary>
    public WeekAmount Week { get; } = week;

    /// <summary>まだ確認していない警告の商品</summary>
    public IReadOnlyList<AcknowledgementStatus> PendingAcknowledgements => Acknowledgements.Where(a => !a.IsAcknowledged).ToList();

    /// <summary>確定できるか：止めるエラーがなく、警告の商品をすべて今の判定のまま確認済み</summary>
    public bool CanConfirm => BlockingIssues.Count == 0 && Acknowledgements.All(a => a.IsAcknowledged);
}
